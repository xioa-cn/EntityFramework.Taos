using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace EntityFramework.Taos.Update.Internal;

public sealed class TaosBatchExecutor : IBatchExecutor
{
    public int Execute(
        IEnumerable<ModificationCommandBatch> commandBatches,
        IRelationalConnection connection)
    {
        var rowsAffected = 0;
        foreach (var commandBatch in commandBatches)
        {
            commandBatch.Execute(connection);
            rowsAffected += commandBatch.ModificationCommands.Count;
        }

        return rowsAffected;
    }

    public async Task<int> ExecuteAsync(
        IEnumerable<ModificationCommandBatch> commandBatches,
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        var rowsAffected = 0;
        foreach (var commandBatch in commandBatches)
        {
            await commandBatch.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
            rowsAffected += commandBatch.ModificationCommands.Count;
        }

        return rowsAffected;
    }
}
