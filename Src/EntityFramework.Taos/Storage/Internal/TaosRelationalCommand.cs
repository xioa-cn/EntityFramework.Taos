using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalCommand : RelationalCommand
{
    private static readonly Regex LimitOffsetParameterPattern =
        new(@"\b(?<keyword>LIMIT|OFFSET)\s+(?<parameter>@[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

#if NET10_0_OR_GREATER
    public TaosRelationalCommand(
        RelationalCommandBuilderDependencies dependencies,
        string commandText,
        string logCommandText,
        IReadOnlyList<IRelationalParameter> parameters)
        : base(dependencies, commandText, logCommandText, parameters)
    {
    }
#else
    public TaosRelationalCommand(
        RelationalCommandBuilderDependencies dependencies,
        string commandText,
        IReadOnlyList<IRelationalParameter> parameters)
        : base(dependencies, commandText, parameters)
    {
    }
#endif

    protected override RelationalDataReader CreateRelationalDataReader()
        => new TaosRelationalDataReader();

    public override DbCommand CreateDbCommand(
        RelationalCommandParameterObject parameterObject,
        Guid commandId,
        DbCommandMethod commandMethod)
    {
        var command = base.CreateDbCommand(parameterObject, commandId, commandMethod);
        if (parameterObject.ParameterValues is not null)
        {
            InlineLimitOffsetParameters(command, parameterObject.ParameterValues);
        }

        return command;
    }

    private static void InlineLimitOffsetParameters(
        DbCommand command,
        IReadOnlyDictionary<string, object?> parameterValues)
    {
        var inlinedParameterNames = new HashSet<string>(StringComparer.Ordinal);

        // TDengine 不接受 LIMIT/OFFSET 位置上的预处理参数。
        // 这里只内联这些数值分页参数，普通查询参数保持不变。
        command.CommandText = LimitOffsetParameterPattern.Replace(
            command.CommandText,
            match =>
            {
                var parameterName = match.Groups["parameter"].Value;
                var valueName = parameterName[1..];
                if (!parameterValues.TryGetValue(valueName, out var value))
                {
                    return match.Value;
                }

                inlinedParameterNames.Add(parameterName);

                return string.Concat(
                    match.Groups["keyword"].Value,
                    " ",
                    Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            });

        for (var i = command.Parameters.Count - 1; i >= 0; i--)
        {
            if (command.Parameters[i] is DbParameter parameter
                && inlinedParameterNames.Contains(parameter.ParameterName))
            {
                command.Parameters.RemoveAt(i);
            }
        }
    }
}
