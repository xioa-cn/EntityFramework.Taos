using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalCommandBuilderFactory : IRelationalCommandBuilderFactory
{
    private readonly RelationalCommandBuilderDependencies _dependencies;

    public TaosRelationalCommandBuilderFactory(RelationalCommandBuilderDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public IRelationalCommandBuilder Create()
        => new TaosRelationalCommandBuilder(_dependencies);
}
