using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpendGovernor.Infrastructure.Persistence;

public sealed class SpendGovernorDbContextFactory : IDesignTimeDbContextFactory<SpendGovernorDbContext>
{
    public SpendGovernorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SpendGovernorDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true");
        return new SpendGovernorDbContext(optionsBuilder.Options);
    }
}
