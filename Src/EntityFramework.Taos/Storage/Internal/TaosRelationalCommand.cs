using System.Data.Common;
using System.Globalization;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalCommand : RelationalCommand
{
    private static readonly Regex LimitOffsetParameterPattern =
        new(@"\b(?<keyword>LIMIT|OFFSET)\s+(?<parameter>@[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ParameterPattern =
        new(@"(?<![A-Za-z0-9_])(?<parameter>@[A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LiteralConcatPattern =
        new(@"CONCAT\(\s*(?<first>'(?:''|\\.|[^'])*')\s*,\s*(?<second>'(?:''|\\.|[^'])*')\s*(?:,\s*(?<third>'(?:''|\\.|[^'])*')\s*)?\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NestedInListPattern =
        new(@"\bIN\s+\(\s*(?<list>\((?:'[^']*'|[^()])+\))\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExistsSelectPattern =
        new(@"^\s*SELECT\s+EXISTS\s*\(\s*SELECT\s+1\s+(?<body>FROM\s+[\s\S]*?)\s*\)\s*$",
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
            InlineUnsupportedParameters(command, parameterObject.ParameterValues);
        }

        return command;
    }

    private static void InlineUnsupportedParameters(
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

        // TDengine 的文本执行路径不识别 WHERE 中的 @time 这类参数。
        // EF 仍负责参数取值，这里把标量参数转成转义后的 SQL 字面量。
        command.CommandText = ParameterPattern.Replace(
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
                return FormatLiteral(value);
            });

        // 字符串 StartsWith/Contains 会生成 LIKE CONCAT(@p, '%')。
        // 参数内联后把全字面量 CONCAT 折叠掉，避免依赖 TDengine 的字符串拼接函数。
        command.CommandText = LiteralConcatPattern.Replace(
            command.CommandText,
            match => FoldStringLiteralConcat(match.Groups["first"].Value, match.Groups["second"].Value, match.Groups["third"].Value));

        command.CommandText = NestedInListPattern.Replace(
            command.CommandText,
            match => "IN " + match.Groups["list"].Value);

        // EF Core 会把 Any() 翻译成 SELECT EXISTS (SELECT 1 ...)，
        // TDengine 不支持子查询作为 SELECT 表达式，改写成聚合判断。
        command.CommandText = ExistsSelectPattern.Replace(
            command.CommandText,
            match => "SELECT CASE WHEN COUNT(*) > 0 THEN true ELSE false END " + match.Groups["body"].Value);

        for (var i = command.Parameters.Count - 1; i >= 0; i--)
        {
            if (command.Parameters[i] is DbParameter parameter
                && IsInlinedParameter(parameter.ParameterName, inlinedParameterNames))
            {
                command.Parameters.RemoveAt(i);
            }
        }
    }

    private static string FormatLiteral(object? value)
    {
        if (value is null || value is DBNull)
        {
            return "NULL";
        }

        return value switch
        {
            string text => $"'{EscapeString(text)}'",
            char character => $"'{EscapeString(character.ToString())}'",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            Enum enumValue => FormatEnumLiteral(enumValue),
            bool boolean => boolean ? "true" : "false",
            byte[] bytes => $"'{Convert.ToHexString(bytes)}'",
            IEnumerable values => FormatEnumerableLiteral(values),
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

    private static string FormatEnumLiteral(Enum value)
    {
        var underlyingType = Enum.GetUnderlyingType(value.GetType());
        var underlyingValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);

        return Convert.ToString(underlyingValue, CultureInfo.InvariantCulture) ?? "0";
    }

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);

    private static string FormatEnumerableLiteral(IEnumerable values)
    {
        var builder = new StringBuilder().Append('(');
        var index = 0;
        foreach (var value in values)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(FormatLiteral(value));
            index++;
        }

        if (index == 0)
        {
            builder.Append("NULL");
        }

        return builder.Append(')').ToString();
    }

    private static string FoldStringLiteralConcat(
        string first,
        string second,
        string third)
    {
        var builder = new StringBuilder()
            .Append('\'')
            .Append(UnwrapStringLiteral(first))
            .Append(UnwrapStringLiteral(second));

        if (!string.IsNullOrEmpty(third))
        {
            builder.Append(UnwrapStringLiteral(third));
        }

        return builder.Append('\'').ToString();
    }

    private static string UnwrapStringLiteral(string value)
        => value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1]
            : value;

    private static bool IsInlinedParameter(
        string parameterName,
        HashSet<string> inlinedParameterNames)
    {
        if (inlinedParameterNames.Contains(parameterName))
        {
            return true;
        }

        return parameterName.Length > 0
            && parameterName[0] != '@'
            && inlinedParameterNames.Contains("@" + parameterName);
    }
}
