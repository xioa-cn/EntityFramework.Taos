using System.Globalization;
using System.Text;
using EntityFramework.Taos.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFramework.Taos.Update.Internal;

public sealed class TaosUpdateSqlGenerator : UpdateSqlGenerator
{
    public TaosUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    public override ResultSetMapping AppendInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        requiresTransaction = false;

        var writeOperations = command.ColumnModifications.Where(o => o.IsWrite).ToArray();
        var tagOperations = writeOperations
            .Where(IsTag)
            .ToArray();
        var valueOperations = writeOperations
            .Where(o => !IsTag(o))
            .OrderBy(o => IsTimestamp(o) ? 0 : 1)
            .ToArray();

        if (valueOperations.Length == 0)
        {
            return ResultSetMapping.NoResults;
        }

        var targetTableName = tagOperations.Length == 0
            ? command.TableName
            : CreateSubTableName(command.TableName, tagOperations);

        commandStringBuilder
            .Append("INSERT INTO ")
            .Append(SqlGenerationHelper.DelimitIdentifier(targetTableName));

        if (tagOperations.Length > 0)
        {
            commandStringBuilder
                .Append(" USING ")
                .Append(SqlGenerationHelper.DelimitIdentifier(command.TableName))
                .Append(" TAGS (");

            for (var i = 0; i < tagOperations.Length; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append(SqlGenerationHelper.GenerateParameterNamePlaceholder(tagOperations[i].ParameterName!));
            }

            commandStringBuilder.Append(")");
        }

        commandStringBuilder
            .Append(" (");

        for (var i = 0; i < valueOperations.Length; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(SqlGenerationHelper.DelimitIdentifier(valueOperations[i].ColumnName));
        }

        commandStringBuilder.AppendLine(")");
        commandStringBuilder.Append("VALUES (");

        for (var i = 0; i < valueOperations.Length; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(SqlGenerationHelper.GenerateParameterNamePlaceholder(valueOperations[i].ParameterName!));
        }

        commandStringBuilder
            .Append(")")
            .Append(SqlGenerationHelper.StatementTerminator)
            .AppendLine();

        return ResultSetMapping.NoResults;
    }

    private static string CreateSubTableName(string stableName, IReadOnlyList<IColumnModification> tagOperations)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var tagOperation in tagOperations)
        {
            AddHashText(ref hash, tagOperation.ColumnName, prime);
            AddHashText(ref hash, "=", prime);
            AddHashText(ref hash, Convert.ToString(tagOperation.Value, CultureInfo.InvariantCulture) ?? "<null>", prime);
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

    private static bool IsTag(IColumnModification modification)
        => modification.Property?.FindAnnotation(TaosAnnotationNames.IsTag)?.Value as bool? == true;

    private static bool IsTimestamp(IColumnModification modification)
        => modification.Property?.FindAnnotation(TaosAnnotationNames.IsTimestamp)?.Value as bool? == true;

    public override ResultSetMapping AppendUpdateOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
        => throw new NotSupportedException("TDengine EF provider supports append-only inserts. Updates are not supported.");

    public override ResultSetMapping AppendDeleteOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
        => throw new NotSupportedException("TDengine EF provider supports append-only inserts. Deletes are not supported.");
}
