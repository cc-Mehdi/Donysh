using Donysh.Life.Domain;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Data;

public sealed class LifeDbContext(DbContextOptions<LifeDbContext> options) : DbContext(options)
{
    public DbSet<LifeProfile> Profiles => Set<LifeProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<LifeProfile>();
        profile.ToTable("LifeProfiles");
        profile.HasKey(item => item.Id);
        profile.Property(item => item.UserSubject).HasMaxLength(450).IsRequired();
        profile.HasIndex(item => item.UserSubject).IsUnique();
        profile.Property(item => item.TimeZoneId).HasMaxLength(100).IsRequired();
    }
}
