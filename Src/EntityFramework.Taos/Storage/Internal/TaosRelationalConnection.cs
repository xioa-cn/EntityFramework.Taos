using System.Data.Common;
using EntityFramework.Taos.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TDengine.Data.Client;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosRelationalConnection : RelationalConnection
{
    private readonly string? _databaseName;
    private readonly string? _connectionString;

    public TaosRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
        var extension = dependencies.ContextOptions.Extensions.OfType<TaosOptionsExtension>().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(extension?.ConnectionString))
        {
            // Keep the database name separately because database create/drop must connect
            // to the server first, while normal EF operations should switch into that DB.
            _databaseName = new TDengineConnectionStringBuilder(extension.ConnectionString).Database;
            _connectionString = NormalizeConnectionString(extension.ConnectionString);
        }
    }

    public string? DatabaseName
        => _databaseName;

    protected override DbConnection CreateDbConnection()
        => new TDengineConnection(_connectionString ?? NormalizeConnectionString(GetValidatedConnectionString()));

    public override bool Open(bool errorsExpected = false)
    {
        var opened = base.Open(errorsExpected);
        ChangeDatabase();

        return opened;
    }

    public override async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        var opened = await base.OpenAsync(cancellationToken, errorsExpected).ConfigureAwait(false);
        ChangeDatabase();

        return opened;
    }

    private void ChangeDatabase()
    {
        if (!string.IsNullOrWhiteSpace(_databaseName))
        {
            // TDengine WebSocket connections may open without an active database even when
            // the connection string contains db=, so switch explicitly after each open.
            DbConnection.ChangeDatabase(_databaseName);
        }
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        var builder = new TDengineConnectionStringBuilder(connectionString);
        var normalized = builder.ConnectionString;

        if (!string.IsNullOrWhiteSpace(builder.Database)
            && !normalized.Contains("db=", StringComparison.OrdinalIgnoreCase))
        {
            // TDengineConnectionStringBuilder can normalize the database key away.
            // Add db= back so lower layers and diagnostics keep the selected database.
            normalized += $";db={builder.Database}";
        }

        return normalized;
    }
}
