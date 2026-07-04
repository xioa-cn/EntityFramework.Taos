using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalDataReader : RelationalDataReader
{
    public override void Initialize(
        IRelationalConnection relationalConnection,
        DbCommand command,
        DbDataReader reader,
        Guid commandId,
        IRelationalCommandDiagnosticsLogger? logger)
        => base.Initialize(
            relationalConnection,
            command,
            new TaosDbDataReader(reader),
            commandId,
            logger);
}
