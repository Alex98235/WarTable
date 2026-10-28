using Microsoft.EntityFrameworkCore;
using WarTable.Api.Models;

namespace WarTable.Api.Data;

public class WarTableDbContext : DbContext
{
    public WarTableDbContext(DbContextOptions<WarTableDbContext> options) : base(options) { }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<Intel> IntelEntries => Set<Intel>();
    public DbSet<SitRep> SitReps => Set<SitRep>();
    public DbSet<SitRepObjective> SitRepObjectives => Set<SitRepObjective>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(10000);
            entity.Property(e => e.Tags).HasMaxLength(1000);
            entity.Property(e => e.NextAliasNumber).HasDefaultValue(1);
            entity.HasMany(e => e.Objectives)
                  .WithOne(o => o.Campaign)
                  .HasForeignKey(o => o.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.IntelEntries)
                  .WithOne(i => i.Campaign)
                  .HasForeignKey(i => i.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.SitReps)
                  .WithOne(s => s.Campaign)
                  .HasForeignKey(s => s.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Objective>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.Lane).HasMaxLength(50);
            entity.Property(e => e.Dependencies).HasMaxLength(2000);

            // Aliases are unique within a campaign and shared across kinds.
            entity.HasIndex(e => new { e.CampaignId, e.Alias }).IsUnique();

            // Soft-deleted objectives disappear from normal queries, but
            // IgnoreQueryFilters() can still resolve them for old #N references.
            entity.HasQueryFilter(e => e.DeletedAt == null);
        });

        modelBuilder.Entity<Intel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Body).HasMaxLength(20000);
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Tags).HasMaxLength(1000);

            entity.HasIndex(e => new { e.CampaignId, e.Alias }).IsUnique();
            entity.HasQueryFilter(e => e.DeletedAt == null);
        });

        modelBuilder.Entity<SitRep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Summary).HasMaxLength(20000);
            entity.Property(e => e.Source).HasMaxLength(100).HasDefaultValue("human");

            entity.HasIndex(e => new { e.CampaignId, e.Alias }).IsUnique();

            entity.HasMany(e => e.Objectives)
                  .WithOne(o => o.SitRep)
                  .HasForeignKey(o => o.SitRepId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SitRepObjective>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TitleSnapshot).HasMaxLength(300);
            entity.Property(e => e.LaneAtWrite).HasMaxLength(50);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => e.SitRepId);
            entity.HasIndex(e => e.ObjectiveAlias);
        });
    }
}
