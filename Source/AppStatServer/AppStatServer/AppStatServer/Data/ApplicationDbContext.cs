using AppStatServer.Common;
using AppStatServer.Sentry;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppStatServer.Data;


public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AppEvent> Events { get; set; } = null!;
    protected sealed override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion(typeof(DateTimeToDateTimeUtc));
    }
}