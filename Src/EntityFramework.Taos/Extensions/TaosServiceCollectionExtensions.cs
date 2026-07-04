using EntityFramework.Taos.Diagnostics.Internal;
using EntityFramework.Taos.Infrastructure.Internal;
using EntityFramework.Taos.Metadata.Conventions;
using EntityFramework.Taos.Migrations;
using EntityFramework.Taos.Query.Internal;
using EntityFramework.Taos.Storage.Internal;
using EntityFramework.Taos.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFramework.Taos.Extensions;

public static class TaosServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkTaos(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // This provider is intentionally built on EF Core's relational pipeline.
        // Each service below replaces only the TDengine-specific part: model conventions,
        // SQL dialect, connection/database lifecycle, and append-only write batching.
        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, TaosLoggingDefinitions>()
            .TryAdd<IProviderConventionSetBuilder, TaosConventionSetBuilder>()
            .TryAdd<IModelRuntimeInitializer, RelationalModelRuntimeInitializer>()
            .TryAdd<IRelationalTypeMappingSource, TaosTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, TaosSqlGenerationHelper>()
            .TryAdd<IRelationalCommandBuilderFactory, TaosRelationalCommandBuilderFactory>()
            .TryAdd<IQuerySqlGeneratorFactory, TaosQuerySqlGeneratorFactory>()
            .TryAdd<IRelationalConnection, TaosRelationalConnection>()
            .TryAdd<IUpdateSqlGenerator, TaosUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, TaosModificationCommandBatchFactory>()
            .TryAdd<ICommandBatchPreparer, TaosCommandBatchPreparer>()
            .TryAdd<IBatchExecutor, TaosBatchExecutor>()
            .TryAdd<IMigrationsSqlGenerator, TaosMigrationsSqlGenerator>()
            .TryAdd<IRelationalDatabaseCreator, TaosDatabaseCreator>();

        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDatabaseProvider, TaosDatabaseProvider>());

        builder.TryAddCoreServices();

        return serviceCollection;
    }
}
