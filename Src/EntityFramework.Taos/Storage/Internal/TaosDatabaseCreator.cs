using System.Text;
using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using TDengine.Data.Client;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosDatabaseCreator : RelationalDatabaseCreator
{
    private readonly IRelationalConnection _connection;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public TaosDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        ISqlGenerationHelper sqlGenerationHelper,
        IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
    {
        _connection = dependencies.Connection;
        _sqlGenerationHelper = sqlGenerationHelper;
        _typeMappingSource = typeMappingSource;
    }

    public override bool Exists()
    {
        var database = GetDatabaseName();
        return string.IsNullOrWhiteSpace(database) || DatabaseExists(database);
    }

    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        var database = GetDatabaseName();
        return string.IsNullOrWhiteSpace(database) || await DatabaseExistsAsync(database, cancellationToken).ConfigureAwait(false);
    }

    public override void Create()
    {
        var database = GetDatabaseName();
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        ExecuteOnServer($"CREATE DATABASE IF NOT EXISTS `{Escape(database)}`");
    }

    public override async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        var database = GetDatabaseName();
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        await ExecuteOnServerAsync($"CREATE DATABASE IF NOT EXISTS `{Escape(database)}`", cancellationToken).ConfigureAwait(false);
    }

    public override void Delete()
    {
        var database = GetDatabaseName();
        if (!string.IsNullOrWhiteSpace(database))
        {
            ExecuteOnServer($"DROP DATABASE IF EXISTS `{Escape(database)}`");
        }
    }

    public override async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        var database = GetDatabaseName();
        if (!string.IsNullOrWhiteSpace(database))
        {
            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS `{Escape(database)}`", cancellationToken).ConfigureAwait(false);
        }
    }

    public override bool HasTables()
    {
        _connection.Open();
        try
        {
            UseDatabase();
            using var command = _connection.DbConnection.CreateCommand();
            command.CommandText = "SHOW TABLES";
            using var reader = command.ExecuteReader();
            return reader.Read();
        }
        finally
        {
            _connection.Close();
        }
    }

    public override async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await UseDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await using var command = _connection.DbConnection.CreateCommand();
            command.CommandText = "SHOW TABLES";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    public override void CreateTables()
    {
        _connection.Open();
        try
        {
            UseDatabase();
            foreach (var sql in GetCreateTableSql())
            {
                using var command = _connection.DbConnection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
        finally
        {
            _connection.Close();
        }
    }

    public override async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await UseDatabaseAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sql in GetCreateTableSql())
            {
                await using var command = _connection.DbConnection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    public override string GenerateCreateScript()
    {
        var builder = new StringBuilder();
        foreach (var sql in GetCreateTableSql())
        {
            builder.AppendLine(sql);
        }

        return builder.ToString();
    }

    private IReadOnlyList<string> GetCreateTableSql()
    {
        var model = Dependencies.CurrentContext.Context.Model;
        var commands = new List<string>();

        foreach (var entityType in model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            // EF 表元数据同时复用于 TDengine 普通表和超级表；
            // provider 注解决定最终 DDL 是 CREATE TABLE 还是 CREATE STABLE。
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var isStable = entityType.FindAnnotation(TaosAnnotationNames.IsStable)?.Value as bool? == true;
            var properties = entityType.GetProperties().ToArray();
            var tagProperties = properties
                .Where(IsTag)
                .ToArray();
            if (!isStable && tagProperties.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.DisplayName()}' defines TDengine tag properties but is not mapped with ToStable().");
            }

            if (isStable && tagProperties.Length == 0)
            {
                throw new InvalidOperationException(
                    $"TDengine stable '{tableName}' must define at least one tag property.");
            }

            // TDengine 时间戳应位于列列表第一位。标签会单独输出到 TAGS 子句，
            // 不属于普通值列列表。
            var valueProperties = properties
                .Where(p => !IsTag(p))
                .OrderBy(p => IsTimestamp(p) ? 0 : 1)
                .ToArray();
            if (valueProperties.Length == 0)
            {
                throw new InvalidOperationException(
                    $"TDengine table '{tableName}' must define at least one value property.");
            }

            var builder = new StringBuilder()
                .Append("CREATE ")
                .Append(isStable ? "STABLE IF NOT EXISTS " : "TABLE IF NOT EXISTS ")
                .Append(_sqlGenerationHelper.DelimitIdentifier(tableName))
                .Append(" (");

            AppendColumns(builder, valueProperties, storeObject);
            builder.Append(")");

            if (isStable && tagProperties.Length > 0)
            {
                builder.Append(" TAGS (");
                AppendColumns(builder, tagProperties, storeObject);
                builder.Append(")");
            }

            builder.Append(_sqlGenerationHelper.StatementTerminator);
            commands.Add(builder.ToString());
        }

        return commands;
    }

    private void AppendColumns(StringBuilder builder, IReadOnlyList<IProperty> properties, StoreObjectIdentifier storeObject)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            var property = properties[i];
            var columnName = property.GetColumnName(storeObject) ?? property.Name;
            var typeMapping = _typeMappingSource.FindMapping(property)
                ?? throw new NotSupportedException($"No TDengine type mapping exists for property '{property.DeclaringType.DisplayName()}.{property.Name}'.");
            var columnType = ResolveColumnType(property, typeMapping);

            builder
                .Append(_sqlGenerationHelper.DelimitIdentifier(columnName))
                .Append(' ')
                .Append(columnType);
        }
    }

    private static string ResolveColumnType(IProperty property, RelationalTypeMapping typeMapping)
    {
        var columnType = property.GetColumnType();
        var maxLength = property.GetMaxLength();
        if (maxLength is > 0)
        {
            var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (clrType == typeof(string) && ShouldApplyMaxLength(columnType, "nchar", "varchar"))
            {
                return $"nchar({maxLength.Value})";
            }

            if (clrType == typeof(byte[]) && ShouldApplyMaxLength(columnType, "binary", "varbinary"))
            {
                return $"varbinary({maxLength.Value})";
            }
        }

        return columnType ?? typeMapping.StoreType;
    }

    private static bool ShouldApplyMaxLength(string? columnType, string firstStoreType, string secondStoreType)
    {
        if (string.IsNullOrWhiteSpace(columnType))
        {
            return true;
        }

        var storeType = UnwrapStoreType(columnType);
        return string.Equals(storeType, firstStoreType, StringComparison.OrdinalIgnoreCase)
               || string.Equals(storeType, secondStoreType, StringComparison.OrdinalIgnoreCase);
    }

    private static string UnwrapStoreType(string storeType)
    {
        var parenIndex = storeType.IndexOf('(');
        return (parenIndex < 0 ? storeType : storeType[..parenIndex]).Trim();
    }

    private string? GetDatabaseName()
    {
        if (_connection is TaosRelationalConnection taosConnection)
        {
            return taosConnection.DatabaseName;
        }

        if (string.IsNullOrWhiteSpace(_connection.ConnectionString))
        {
            return null;
        }

        var builder = new TDengineConnectionStringBuilder(_connection.ConnectionString);

        return builder.Database;
    }

    private bool DatabaseExists(string database)
    {
        using var connection = CreateServerConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SHOW DATABASES";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(0), database, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = CreateServerConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW DATABASES";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(0), database, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ExecuteOnServer(string sql)
    {
        using var connection = CreateServerConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task ExecuteOnServerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = CreateServerConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private TDengineConnection CreateServerConnection()
    {
        var builder = new TDengineConnectionStringBuilder(_connection.ConnectionString);
        // CREATE/DROP DATABASE 和 SHOW DATABASES 必须在 server 作用域执行，
        // 不能进入那个可能还不存在的数据库。
        builder.Database = string.Empty;

        return new TDengineConnection(builder.ConnectionString);
    }

    private void UseDatabase()
    {
        var database = GetDatabaseName();
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        using var command = _connection.DbConnection.CreateCommand();
        command.CommandText = $"USE `{Escape(database)}`";
        command.ExecuteNonQuery();
    }

    private async Task UseDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = GetDatabaseName();
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        await using var command = _connection.DbConnection.CreateCommand();
        command.CommandText = $"USE `{Escape(database)}`";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string identifier)
        => identifier.Replace("`", "``", StringComparison.Ordinal);

    private static bool IsTag(IProperty property)
        => property.FindAnnotation(TaosAnnotationNames.IsTag)?.Value as bool? == true;

    private static bool IsTimestamp(IProperty property)
        => property.FindAnnotation(TaosAnnotationNames.IsTimestamp)?.Value as bool? == true;
}
