using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace EntityFramework.Taos.Update.Internal;

public sealed class TaosCommandBatchPreparer : ICommandBatchPreparer
{
    private readonly IModificationCommandBatchFactory _batchFactory;

    public TaosCommandBatchPreparer(IModificationCommandBatchFactory batchFactory)
        => _batchFactory = batchFactory;

    public IEnumerable<ModificationCommandBatch> BatchCommands(
        IList<IUpdateEntry> entries,
        IUpdateAdapter updateAdapter)
        => CreateCommandBatches(
            // EF's default modification command contains relational update/delete behavior
            // that does not match TDengine's append-only write model. Wrap each entry in the
            // minimal command shape consumed by TaosModificationCommandBatch.
            entries.Select(entry => new TaosModificationCommand(entry)),
            moreCommandSets: false);

    public IEnumerable<ModificationCommandBatch> CreateCommandBatches(
        IEnumerable<IReadOnlyModificationCommand> commandSet,
        bool moreCommandSets)
    {
        var batch = _batchFactory.Create();
        foreach (var command in commandSet)
        {
            if (!batch.TryAddCommand(command))
            {
                batch.Complete(moreBatchesExpected: true);
                yield return batch;

                batch = _batchFactory.Create();
                if (!batch.TryAddCommand(command))
                {
                    throw new InvalidOperationException("Unable to add TDengine modification command to an empty batch.");
                }
            }
        }

        batch.Complete(moreCommandSets);
        yield return batch;
    }

    private sealed class TaosModificationCommand : IReadOnlyModificationCommand
    {
        public TaosModificationCommand(IUpdateEntry entry)
        {
            Entries = [entry];
            EntityState = entry.EntityState;
            TableName = entry.EntityType.GetTableName()
                ?? throw new InvalidOperationException($"Entity '{entry.EntityType.DisplayName()}' is not mapped to a TDengine table.");
            Schema = entry.EntityType.GetSchema();
        }

        public string TableName { get; }

        public string? Schema { get; }

        public IReadOnlyList<IColumnModification> ColumnModifications
            => [];

        public IReadOnlyList<IUpdateEntry> Entries { get; }

        public EntityState EntityState { get; }

        public void PropagateResults(RelationalDataReader relationalReader)
        {
            // TDengine INSERTs generated here do not return server-generated values.
        }

        public void PropagateOutputParameters(DbParameterCollection parameterCollection, int baseParameterIndex)
        {
            // The provider does not use stored procedures or output parameters for writes.
        }

#if NET10_0_OR_GREATER
        public ITable? Table
            => null;

        public IStoreStoredProcedure? StoreStoredProcedure
            => null;

        public IColumnBase? RowsAffectedColumn
            => null;
#else
        public ITable? Table
            => null;

        public IStoreStoredProcedure? StoreStoredProcedure
            => null;

        public IColumnBase? RowsAffectedColumn
            => null;
#endif
    }

    public IReadOnlyList<List<IReadOnlyModificationCommand>> TopologicalSort(
        IEnumerable<IReadOnlyModificationCommand> commands)
        => [commands.ToList()];

#if NET10_0_OR_GREATER
    public void ResetState()
    {
    }

    public Task ResetStateAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
#endif
}
