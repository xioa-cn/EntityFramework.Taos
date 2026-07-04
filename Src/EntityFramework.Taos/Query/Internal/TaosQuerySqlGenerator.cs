using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFramework.Taos.Query.Internal;

public sealed class TaosQuerySqlGenerator : QuerySqlGenerator
{
    public TaosQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        if (selectExpression.Limit is null && selectExpression.Offset is null)
        {
            return;
        }

        // 关系型基类生成 SQL 标准的 FETCH FIRST 语法；TDengine 需要 LIMIT/OFFSET。
        Sql.AppendLine()
            .Append("LIMIT ");
        if (selectExpression.Limit is null)
        {
            Sql.Append("2147483647");
        }
        else
        {
            Visit(selectExpression.Limit);
        }

        if (selectExpression.Offset is not null)
        {
            Sql.Append(" OFFSET ");
            Visit(selectExpression.Offset);
        }
    }
}
