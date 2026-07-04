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
            // EF 默认修改命令包含关系型 update/delete 行为，
            // 这和 TDengine 的只追加写入模型不匹配。这里把每个 entry 包装成
            // TaosModificationCommandBatch 需要的最小命令形态。
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
            // 这里生成的 TDengine INSERT 不返回服务端生成值。
        }

        public void PropagateOutputParameters(DbParameterCollection parameterCollection, int baseParameterIndex)
        {
            // provider 写入时不使用存储过程或输出参数。
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
