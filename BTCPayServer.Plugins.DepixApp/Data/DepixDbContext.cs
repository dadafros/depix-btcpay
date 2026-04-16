using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.DepixApp.Data;

public class DepixDbContext(DbContextOptions<DepixDbContext> options, bool designTime = false)
    : DbContext(options)

{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.DepixApp";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultPluginSchema);
    }
}

