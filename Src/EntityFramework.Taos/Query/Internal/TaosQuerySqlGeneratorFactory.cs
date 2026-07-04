using Microsoft.EntityFrameworkCore.Query;

namespace EntityFramework.Taos.Query.Internal;

public sealed class TaosQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    public TaosQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public QuerySqlGenerator Create()
        => new TaosQuerySqlGenerator(_dependencies);
}
