using EntityFramework.Taos.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFramework.Taos.Storage.Internal;

public sealed class TaosDatabaseProvider : IDatabaseProvider
{
    public string Name
        => "EntityFramework.Taos";

    public string? Version
        => typeof(TaosDatabaseProvider).Assembly.GetName().Version?.ToString();

    public bool IsConfigured(IDbContextOptions options)
        => options.Extensions.OfType<TaosOptionsExtension>().Any();
}
