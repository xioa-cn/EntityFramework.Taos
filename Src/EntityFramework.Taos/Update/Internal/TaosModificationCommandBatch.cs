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
        // TDengine 插入会生成完整文本命令，并包含超级表/tag 路由。
        // 每个批次只保留一个 EF 修改，避免在同一条命令中混入不同子表。
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
        var isStable = entityType.FindAnnotation(TaosAnnotationNames.IsStable)?.Value as bool? == true;
        var tagProperties = properties.Where(IsTag).ToArray();
        if (!isStable && tagProperties.Length > 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.DisplayName()}' defines TDengine tag properties but is not mapped with ToStable().");
        }

        if (isStable && tagProperties.Length == 0)
        {
            throw new InvalidOperationException(
                $"TDengine stable '{modificationCommand.TableName}' must define at least one tag property.");
        }

        // 标签用于定位或创建子表；只有非标签属性会作为值列写入。
        // 时间戳排在第一列，以满足 TDengine 时序表要求。
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
            // INSERT INTO subtable USING stable TAGS (...) 会按需创建子表，
            // 并把数据路由到对应 tag 的 TDengine 子表。
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

        // TDengine WebSocket 预处理语句对 STABLE/TAGS 这种插入形态不稳定，
        // 所以这里为生成的 INSERT SQL 输出转义后的字面量。
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
        // TDengine 子表名必须能由一组 tag 稳定推导。
        // 使用紧凑的 FNV-1a 哈希，避免把很长的 tag 值泄露到标识符里。
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
