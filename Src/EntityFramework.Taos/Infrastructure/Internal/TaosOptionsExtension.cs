using System.Data.Common;
using EntityFramework.Taos.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Taos.Infrastructure.Internal;

public sealed class TaosOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public TaosOptionsExtension()
    {
    }

    private TaosOptionsExtension(TaosOptionsExtension copyFrom)
        : base(copyFrom)
    {
    }

    public override DbContextOptionsExtensionInfo Info
        => _info ??= new ExtensionInfo(this);

    public override void ApplyServices(IServiceCollection services)
        => services.AddEntityFrameworkTaos();

    protected override RelationalOptionsExtension Clone()
        => new TaosOptionsExtension(this);

    // Return the concrete extension type so UseTaos can fluently update relational options
    // without exposing EF Core's base RelationalOptionsExtension to callers.
    public new TaosOptionsExtension WithConnectionString(string connectionString)
        => (TaosOptionsExtension)base.WithConnectionString(connectionString);

    public new TaosOptionsExtension WithConnection(DbConnection connection, bool owned)
        => (TaosOptionsExtension)base.WithConnection(connection, owned);

    private sealed class ExtensionInfo : RelationalExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override string LogFragment
            => "using Taos ";

        public override bool IsDatabaseProvider
            => true;

        public override int GetServiceProviderHashCode()
            => 0;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Taos:" + nameof(TaosOptionsExtension)] = "1";
    }
}
