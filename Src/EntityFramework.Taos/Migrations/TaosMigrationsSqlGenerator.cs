using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFramework.Taos.Migrations;

public sealed class TaosMigrationsSqlGenerator : MigrationsSqlGenerator
{
    public TaosMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        var isStable = operation[TaosAnnotationNames.IsStable] as bool? == true;
        var tags = operation.Columns
            .Where(c => c[TaosAnnotationNames.IsTag] as bool? == true)
            .ToArray();
        var columns = operation.Columns
            .Where(c => c[TaosAnnotationNames.IsTag] as bool? != true)
            .OrderBy(c => c[TaosAnnotationNames.IsTimestamp] as bool? == true ? 0 : 1)
            .ToArray();

        if (isStable && tags.Length == 0)
        {
            throw new InvalidOperationException(
                $"TDengine stable '{operation.Name}' must define at least one tag column.");
        }

        if (columns.Length == 0)
        {
            throw new InvalidOperationException(
                $"TDengine table '{operation.Name}' must define at least one value column.");
        }

        builder
            .Append("CREATE ")
            .Append(isStable ? "STABLE " : "TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .AppendLine(" (");

        for (var i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine(",");
            }

            builder.Append("    ");
            ColumnDefinitionWithMaxLength(operation.Schema, operation.Name, columns[i], model, builder);
        }

        builder.AppendLine();
        builder.Append(")");

        if (isStable && tags.Length > 0)
        {
            builder.AppendLine();
            builder.Append("TAGS (");
            for (var i = 0; i < tags.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                ColumnDefinitionWithMaxLength(operation.Schema, operation.Name, tags[i], model, builder);
            }

            builder.Append(")");
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    private void ColumnDefinitionWithMaxLength(
        string? schema,
        string table,
        AddColumnOperation column,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        var originalColumnType = column.ColumnType;
        var resolvedColumnType = ResolveColumnType(column);
        if (resolvedColumnType is not null)
        {
            column.ColumnType = resolvedColumnType;
        }

        try
        {
            ColumnDefinition(schema, table, column.Name, column, model, builder);
        }
        finally
        {
            column.ColumnType = originalColumnType;
        }
    }

    private static string? ResolveColumnType(AddColumnOperation column)
    {
        if (column.MaxLength is not > 0)
        {
            return null;
        }

        var clrType = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        if (clrType == typeof(string) && ShouldApplyMaxLength(column.ColumnType, "nchar", "varchar"))
        {
            return $"nchar({column.MaxLength.Value})";
        }

        if (clrType == typeof(byte[]) && ShouldApplyMaxLength(column.ColumnType, "binary", "varbinary"))
        {
            return $"varbinary({column.MaxLength.Value})";
        }

        return null;
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

    protected override void Generate(
        AddPrimaryKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (terminate)
        {
            EndStatement(builder);
        }
    }

    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (terminate)
        {
            EndStatement(builder);
        }
    }
}
