using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudKB.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CloudKbDbContext>
{
    public CloudKbDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CloudKbDbContext>();
        
        // We use a dummy connection string during design-time migration creation.
        // Aspire will override this connection string at runtime.
        optionsBuilder.UseNpgsql("Host=localhost;Database=cloudkb;Username=postgres;Password=postgres");

        return new CloudKbDbContext(optionsBuilder.Options);
    }
}
