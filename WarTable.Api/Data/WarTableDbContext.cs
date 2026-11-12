using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WarTable.Api.Models;

namespace WarTable.Api.Data;

public class WarTableDbContext : DbContext
{
	public WarTableDbContext(DbContextOptions<WarTableDbContext> options) : base(options) { }

	public DbSet<Campaign> Campaigns => Set<Campaign>();
	public DbSet<Objective> Objectives => Set<Objective>();
	public DbSet<Step> Steps => Set<Step>();
	public DbSet<StepDependency> StepDependencies => Set<StepDependency>();
	public DbSet<StepUpdate> StepUpdates => Set<StepUpdate>();
	public DbSet<Evidence> Evidence => Set<Evidence>();
	public DbSet<BunkerNote> BunkerNotes => Set<BunkerNote>();
	public DbSet<BunkerNoteLink> BunkerNoteLinks => Set<BunkerNoteLink>();
	public DbSet<Preference> Preferences => Set<Preference>();
	public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Campaign>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
			entity.Property(e => e.Intent).HasMaxLength(20000);
			entity.Property(e => e.Charter).HasMaxLength(40000);
			entity.Property(e => e.Tags).HasMaxLength(1000);
			entity.Property(e => e.NextAliasNumber).HasDefaultValue(1);
			entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(Guid.Empty);

			entity.HasMany(e => e.Objectives)
				  .WithOne(o => o.Campaign)
				  .HasForeignKey(o => o.CampaignId)
				  .OnDelete(DeleteBehavior.Cascade);

			entity.HasMany(e => e.BunkerNotes)
				  .WithOne(n => n.Campaign)
				  .HasForeignKey(n => n.CampaignId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<Objective>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
			entity.Property(e => e.Description).HasMaxLength(20000);
			entity.Property(e => e.PlanState).HasConversion<string>().HasMaxLength(20);
			entity.Property(e => e.Status).HasMaxLength(20);
			entity.Property(e => e.NextStepNumber).HasDefaultValue(1);
			entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(Guid.Empty);

			// Aliases are unique within a campaign and shared with Bunker notes.
			entity.HasIndex(e => new { e.CampaignId, e.Alias }).IsUnique();

			// Soft-deleted objectives disappear from normal queries, but
			// IgnoreQueryFilters() still resolves them for old #N references.
			entity.HasQueryFilter(e => e.DeletedAt == null);

			entity.HasMany(e => e.Steps)
				  .WithOne(s => s.Objective)
				  .HasForeignKey(s => s.ObjectiveId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<Step>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
			entity.Property(e => e.Context).HasMaxLength(20000);
			entity.Property(e => e.Assignee).HasMaxLength(100);
			entity.Property(e => e.Assigner).HasMaxLength(100);
			entity.Property(e => e.Status).HasMaxLength(20);
			entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(Guid.Empty);

			// Step numbers are unique within an objective and never reused.
			entity.HasIndex(e => new { e.ObjectiveId, e.StepNumber }).IsUnique();
			entity.HasIndex(e => new { e.ObjectiveId, e.SortOrder });

			entity.HasQueryFilter(e => e.DeletedAt == null);

			entity.HasMany(e => e.Updates)
				  .WithOne(u => u.Step)
				  .HasForeignKey(u => u.StepId)
				  .OnDelete(DeleteBehavior.Cascade);

			entity.HasMany(e => e.Evidence)
				  .WithOne(ev => ev.Step)
				  .HasForeignKey(ev => ev.StepId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<StepDependency>(entity =>
		{
			// Composite key: an edge is identified by both endpoints.
			entity.HasKey(e => new { e.StepId, e.DependsOnStepId });

			// Restrict both sides to avoid multiple cascade paths. The service
			// removes edges explicitly before hard-deleting a campaign.
			entity.HasOne(e => e.Step)
				  .WithMany(s => s.Dependencies)
				  .HasForeignKey(e => e.StepId)
				  .OnDelete(DeleteBehavior.Restrict);

			entity.HasOne(e => e.DependsOnStep)
				  .WithMany(s => s.Dependents)
				  .HasForeignKey(e => e.DependsOnStepId)
				  .OnDelete(DeleteBehavior.Restrict);

			entity.HasIndex(e => e.DependsOnStepId);
		});

		modelBuilder.Entity<StepUpdate>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Actor).HasMaxLength(100);
			entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
			entity.Property(e => e.Body).HasMaxLength(20000);

			entity.HasIndex(e => e.StepId);
		});

		modelBuilder.Entity<Evidence>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Label).HasMaxLength(300);
			entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
			entity.Property(e => e.Value).HasMaxLength(4000);
			entity.Property(e => e.CreatedBy).HasMaxLength(100);

			entity.HasIndex(e => e.StepId);
		});

		modelBuilder.Entity<BunkerNote>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
			entity.Property(e => e.Body).HasMaxLength(40000);
			entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
			// Status is not mapped — it is derived on read (Superseded when another
			// non-deleted note points at this one via SupersedesId).
			entity.Ignore(e => e.Status);
			entity.Property(e => e.Tags).HasMaxLength(1000);
			entity.Property(e => e.CreatedBy).HasMaxLength(100);
			entity.Property(e => e.Basis).HasMaxLength(500);
			entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(Guid.Empty);

			entity.HasIndex(e => new { e.CampaignId, e.Alias }).IsUnique();
			entity.HasIndex(e => new { e.CampaignId, e.Kind });

			entity.HasQueryFilter(e => e.DeletedAt == null);

			entity.HasMany(e => e.Links)
				  .WithOne(l => l.Note)
				  .HasForeignKey(l => l.NoteId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<BunkerNoteLink>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.HasIndex(e => e.NoteId);
			entity.HasIndex(e => e.ObjectiveId);
			entity.HasIndex(e => e.StepId);
		});

		modelBuilder.Entity<Preference>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Text).IsRequired().HasMaxLength(2000);
			entity.Property(e => e.Rationale).HasMaxLength(5000);
			entity.Property(e => e.Source).HasMaxLength(100);
			entity.Property(e => e.Tags).HasMaxLength(1000);
			entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(Guid.Empty);

			// No query filter: Active is a field and retired preferences stay visible on demand.
		});

		modelBuilder.Entity<ActivityEvent>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Actor).HasMaxLength(100);
			entity.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(20);
			entity.Property(e => e.Action).HasMaxLength(100);
			entity.Property(e => e.Summary).HasMaxLength(2000);
			entity.Property(e => e.Metadata).HasDefaultValue("{}");

			entity.HasIndex(e => e.Sequence);
			entity.HasIndex(e => new { e.CampaignId, e.Sequence });

			// Deliberately no FK to Campaign: events survive a hard-deleted campaign.
		});
	}

	/// <summary>
	/// Regenerates <see cref="IConcurrencyGuarded.RowVersion"/> on every entity
	/// that implements <see cref="IConcurrencyGuarded"/> and is in the
	/// <c>Added</c> or <c>Modified</c> state.
	/// </summary>
	public override int SaveChanges()
	{
		RegenerateRowVersions();
		return base.SaveChanges();
	}

	/// <inheritdoc cref="SaveChanges()"/>
	public override int SaveChanges(bool acceptAllChangesOnSuccess)
	{
		RegenerateRowVersions();
		return base.SaveChanges(acceptAllChangesOnSuccess);
	}

	/// <inheritdoc cref="SaveChanges()"/>
	public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		RegenerateRowVersions();
		return base.SaveChangesAsync(cancellationToken);
	}

	/// <inheritdoc cref="SaveChangesAsync(CancellationToken)"/>
	public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
	{
		RegenerateRowVersions();
		return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
	}

	private void RegenerateRowVersions()
	{
		foreach (var entry in ChangeTracker.Entries())
		{
			if (entry.Entity is IConcurrencyGuarded guarded &&
				(entry.State == EntityState.Added || entry.State == EntityState.Modified))
			{
				guarded.RowVersion = Guid.NewGuid();
			}
		}
	}
}
