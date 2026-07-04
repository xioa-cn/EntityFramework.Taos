using Microsoft.EntityFrameworkCore.Update;

namespace EntityFramework.Taos.Update.Internal;

public sealed class TaosModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private readonly ModificationCommandBatchFactoryDependencies _dependencies;

    public TaosModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies)
        => _dependencies = dependencies;

    public ModificationCommandBatch Create()
        => new TaosModificationCommandBatch(_dependencies);
}
