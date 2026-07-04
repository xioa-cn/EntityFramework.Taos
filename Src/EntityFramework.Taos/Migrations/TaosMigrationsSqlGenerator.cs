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
            .ToArray();

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
            ColumnDefinition(operation.Schema, operation.Name, columns[i].Name, columns[i], model, builder);
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

                ColumnDefinition(operation.Schema, operation.Name, tags[i].Name, tags[i], model, builder);
            }

            builder.Append(")");
        }

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
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
