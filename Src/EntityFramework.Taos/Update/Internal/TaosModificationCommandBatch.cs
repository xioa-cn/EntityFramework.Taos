using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFramework.Taos.Update.Internal;

public sealed class TaosModificationCommandBatch : ModificationCommandBatch
{
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly List<IReadOnlyModificationCommand> _modificationCommands = [];
    private bool _areMoreBatchesExpected;

    public TaosModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies)
        => _sqlGenerationHelper = dependencies.SqlGenerationHelper;

    public override IReadOnlyList<IReadOnlyModificationCommand> ModificationCommands
        => _modificationCommands;

    public override bool RequiresTransaction
        => false;

    public override bool AreMoreBatchesExpected
        => _areMoreBatchesExpected;

    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        // TDengine inserts are generated as complete text commands with stable/tag routing.
        // Keep one EF modification per batch to avoid mixing different subtables in one command.
        if (_modificationCommands.Count > 0)
        {
            return false;
        }

        _modificationCommands.Add(modificationCommand);
        return true;
    }

    public override void Complete(bool moreBatchesExpected)
        => _areMoreBatchesExpected = moreBatchesExpected;

    public override void Execute(IRelationalConnection connection)
    {
        connection.Open();
        try
        {
            foreach (var modificationCommand in _modificationCommands)
            {
                using var command = CreateDbCommand(connection.DbConnection, modificationCommand);
                command.ExecuteNonQuery();
            }
        }
        finally
        {
            connection.Close();
        }
    }

    public override async Task ExecuteAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var modificationCommand in _modificationCommands)
            {
                await using var command = CreateDbCommand(connection.DbConnection, modificationCommand);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private DbCommand CreateDbCommand(DbConnection connection, IReadOnlyModificationCommand modificationCommand)
    {
        var command = connection.CreateCommand();
        command.CommandText = GenerateInsertSql(modificationCommand);
        command.CommandType = CommandType.Text;

        return command;
    }

    private string GenerateInsertSql(IReadOnlyModificationCommand modificationCommand)
    {
        var entry = modificationCommand.Entries.Single();
        var entityType = entry.EntityType;
        var storeObject = StoreObjectIdentifier.Table(modificationCommand.TableName, modificationCommand.Schema);
        var properties = entityType.GetProperties().ToArray();
        var tagProperties = properties.Where(IsTag).ToArray();
        // Tags identify or create the subtable; only non-tag properties are inserted as values.
        // Timestamp is ordered first to satisfy TDengine's time-series table requirements.
        var valueProperties = properties
            .Where(p => !IsTag(p))
            .OrderBy(p => IsTimestamp(p) ? 0 : 1)
            .ToArray();

        if (valueProperties.Length == 0)
        {
            throw new InvalidOperationException($"Entity '{entityType.DisplayName()}' has no TDengine value columns.");
        }

        var stableName = modificationCommand.TableName;
        var targetTableName = tagProperties.Length == 0
            ? stableName
            : CreateSubTableName(stableName, tagProperties, entry);

        var builder = new StringBuilder()
            .Append("INSERT INTO ")
            .Append(_sqlGenerationHelper.DelimitIdentifier(targetTableName));

        if (tagProperties.Length > 0)
        {
            // INSERT INTO subtable USING stable TAGS (...) creates the subtable on demand
            // and routes the row to the tag-specific TDengine child table.
            builder
                .Append(" USING ")
                .Append(_sqlGenerationHelper.DelimitIdentifier(stableName))
                .Append(" TAGS (");

            AppendLiteralList(builder, tagProperties, entry);
            builder.Append(')');
        }

        builder.Append(" (");
        AppendColumnList(builder, valueProperties, storeObject);
        builder.Append(") VALUES (");
        AppendLiteralList(builder, valueProperties, entry);
        builder
            .Append(')')
            .Append(_sqlGenerationHelper.StatementTerminator);

        return builder.ToString();
    }

    private void AppendColumnList(
        StringBuilder builder,
        IReadOnlyList<IProperty> properties,
        StoreObjectIdentifier storeObject)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            var columnName = properties[i].GetColumnName(storeObject) ?? properties[i].Name;
            builder.Append(_sqlGenerationHelper.DelimitIdentifier(columnName));
        }
    }

    private static void AppendLiteralList(
        StringBuilder builder,
        IReadOnlyList<IProperty> properties,
        IUpdateEntry entry)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(GenerateLiteral(entry.GetCurrentValue(properties[i])));
        }
    }

    private static string GenerateLiteral(object? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        // TDengine's WebSocket prepared statements are not reliable for the STABLE/TAGS
        // insert shape, so this provider emits escaped literals for generated INSERT SQL.
        return value switch
        {
            string text => $"'{EscapeString(text)}'",
            char character => $"'{EscapeString(character.ToString())}'",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            bool boolean => boolean ? "true" : "false",
            byte[] bytes => $"'{Convert.ToHexString(bytes)}'",
            float number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            _ => $"'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}'"
        };
    }

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);

    private static string CreateSubTableName(
        string stableName,
        IReadOnlyList<IProperty> tagProperties,
        IUpdateEntry entry)
    {
        // TDengine child table names must be deterministic for a tag set.
        // A compact FNV-1a hash avoids leaking long tag values into identifiers.
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var tagProperty in tagProperties)
        {
            AddHashText(ref hash, tagProperty.Name, prime);
            AddHashText(ref hash, "=", prime);
            AddHashText(ref hash, Convert.ToString(entry.GetCurrentValue(tagProperty), CultureInfo.InvariantCulture) ?? "<null>", prime);
            AddHashText(ref hash, ";", prime);
        }

        return $"{stableName}_{hash:x16}";
    }

    private static void AddHashText(ref ulong hash, string value, ulong prime)
    {
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
    }

    private static bool IsTag(IProperty property)
        => property.FindAnnotation(TaosAnnotationNames.IsTag)?.Value as bool? == true;

    private static bool IsTimestamp(IProperty property)
        => property.FindAnnotation(TaosAnnotationNames.IsTimestamp)?.Value as bool? == true;
}
