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
            // 单独保存数据库名，因为创建/删除数据库必须先连接 server，
            // 普通 EF 操作则需要切换到目标数据库。
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
            // 即使连接字符串包含 db=，TDengine WebSocket 连接打开后也可能没有活动数据库，
            // 所以每次打开后都显式切换一次。
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
            // TDengineConnectionStringBuilder 可能在规范化时移除数据库键。
            // 补回 db=，让底层组件和诊断信息保留当前选择的数据库。
            normalized += $";db={builder.Database}";
        }

        return normalized;
    }
}
