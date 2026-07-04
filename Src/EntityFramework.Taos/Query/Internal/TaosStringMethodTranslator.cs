using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Query.Internal;

public sealed class TaosStringMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo StartsWithMethod =
        typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static readonly MethodInfo ContainsMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo IsNullOrEmptyMethod =
        typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public TaosStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (Equals(method, IsNullOrEmptyMethod))
        {
            var value = arguments[0];
            return _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.IsNull(value),
                _sqlExpressionFactory.Equal(value, _sqlExpressionFactory.Constant(string.Empty, value.TypeMapping)));
        }

        if (instance is null)
        {
            return null;
        }

        if (Equals(method, StartsWithMethod))
        {
            return TranslateLike(instance, arguments[0], PatternMode.StartsWith);
        }

        if (Equals(method, ContainsMethod))
        {
            return TranslateLike(instance, arguments[0], PatternMode.Contains);
        }

        return null;
    }

    private SqlExpression TranslateLike(
        SqlExpression match,
        SqlExpression pattern,
        PatternMode mode)
        => _sqlExpressionFactory.Like(match, CreateLikePattern(pattern, mode));

    private SqlExpression CreateLikePattern(SqlExpression pattern, PatternMode mode)
    {
        if (pattern is SqlConstantExpression { Value: string value })
        {
            return _sqlExpressionFactory.Constant(
                mode == PatternMode.StartsWith ? value + "%" : "%" + value + "%",
                pattern.TypeMapping);
        }

        var percent = _sqlExpressionFactory.Constant("%", pattern.TypeMapping);

        return mode == PatternMode.StartsWith
            ? Concat(pattern, percent, pattern.TypeMapping)
            : Concat(percent, pattern, percent, pattern.TypeMapping);
    }

    private SqlExpression Concat(
        SqlExpression left,
        SqlExpression right,
        RelationalTypeMapping? typeMapping)
        => _sqlExpressionFactory.Function(
            "CONCAT",
            [left, right],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            typeof(string),
            typeMapping);

    private SqlExpression Concat(
        SqlExpression left,
        SqlExpression middle,
        SqlExpression right,
        RelationalTypeMapping? typeMapping)
        => _sqlExpressionFactory.Function(
            "CONCAT",
            [left, middle, right],
            nullable: true,
            argumentsPropagateNullability: [true, true, true],
            typeof(string),
            typeMapping);

    private enum PatternMode
    {
        StartsWith,
        Contains
    }
}
