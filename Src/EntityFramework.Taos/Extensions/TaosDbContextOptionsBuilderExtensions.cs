using System.Data.Common;
using EntityFramework.Taos.Infrastructure;
using EntityFramework.Taos.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFramework.Taos.Extensions;

public static class TaosDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseTaos(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        Action<TaosDbContextOptionsBuilder>? taosOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connectionString);

        var extension = GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        ConfigureWarnings(optionsBuilder);
        taosOptionsAction?.Invoke(new TaosDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    public static DbContextOptionsBuilder UseTaos(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection = false,
        Action<TaosDbContextOptionsBuilder>? taosOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        var extension = GetOrCreateExtension(optionsBuilder).WithConnection(connection, contextOwnsConnection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        ConfigureWarnings(optionsBuilder);
        taosOptionsAction?.Invoke(new TaosDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> UseTaos<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        Action<TaosDbContextOptionsBuilder>? taosOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseTaos(
            (DbContextOptionsBuilder)optionsBuilder,
            connectionString,
            taosOptionsAction);

    public static DbContextOptionsBuilder<TContext> UseTaos<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection = false,
        Action<TaosDbContextOptionsBuilder>? taosOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseTaos(
            (DbContextOptionsBuilder)optionsBuilder,
            connection,
            contextOwnsConnection,
            taosOptionsAction);

    private static TaosOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.Options.FindExtension<TaosOptionsExtension>() ?? new TaosOptionsExtension();

    private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.ConfigureWarnings(warnings => warnings.Default(WarningBehavior.Log));
}
