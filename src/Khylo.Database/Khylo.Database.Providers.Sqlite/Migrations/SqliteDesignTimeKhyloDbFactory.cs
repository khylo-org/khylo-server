using Khylo.Database.Implementations;
using Khylo.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Khylo.Database.Providers.Sqlite.Migrations
{
    /// <summary>
    /// The design time factory for <see cref="KhyloDbContext"/>.
    /// This is only used for the creation of migrations and not during runtime.
    /// </summary>
    internal sealed class SqliteDesignTimeKhyloDbFactory : IDesignTimeDbContextFactory<KhyloDbContext>
    {
        public KhyloDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<KhyloDbContext>();
            optionsBuilder.UseSqlite("Data Source=khylo.db", f => f.MigrationsAssembly(GetType().Assembly));

            return new KhyloDbContext(
                optionsBuilder.Options,
                NullLogger<KhyloDbContext>.Instance,
                new SqliteDatabaseProvider(null!, NullLogger<SqliteDatabaseProvider>.Instance),
                new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
        }
    }
}
