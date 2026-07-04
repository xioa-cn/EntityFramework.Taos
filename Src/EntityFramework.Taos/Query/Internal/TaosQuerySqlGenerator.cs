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
        if (selectExpression.Limit is null)
        {
            return;
        }

        // The base relational generator emits SQL-standard FETCH FIRST syntax; TDengine expects LIMIT/OFFSET.
        Sql.AppendLine()
            .Append("LIMIT ");
        Visit(selectExpression.Limit);

        if (selectExpression.Offset is not null)
        {
            Sql.Append(" OFFSET ");
            Visit(selectExpression.Offset);
        }
    }
}
