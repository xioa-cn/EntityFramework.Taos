using EntityFramework.Taos.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFramework.Taos.Infrastructure;

public sealed class TaosDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<TaosDbContextOptionsBuilder, TaosOptionsExtension>
{
    public TaosDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }
}
