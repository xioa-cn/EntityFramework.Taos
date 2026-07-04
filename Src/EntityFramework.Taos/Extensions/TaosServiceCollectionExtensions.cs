using EntityFramework.Taos.Diagnostics.Internal;
using EntityFramework.Taos.Infrastructure.Internal;
using EntityFramework.Taos.Metadata.Internal;
using EntityFramework.Taos.Metadata.Conventions;
using EntityFramework.Taos.Migrations;
using EntityFramework.Taos.Query.Internal;
using EntityFramework.Taos.Storage.Internal;
using EntityFramework.Taos.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
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

        // 这个 provider 基于 EF Core 的关系型管线实现。
        // 下面每个服务只替换 TDengine 特有的部分：模型约定、
        // SQL 方言、连接/数据库生命周期，以及只追加写入的批处理。
        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, TaosLoggingDefinitions>()
            .TryAdd<IProviderConventionSetBuilder, TaosConventionSetBuilder>()
            .TryAdd<IModelRuntimeInitializer, RelationalModelRuntimeInitializer>()
            .TryAdd<IRelationalAnnotationProvider, TaosAnnotationProvider>()
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
