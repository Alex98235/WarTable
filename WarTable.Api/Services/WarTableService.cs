using Microsoft.EntityFrameworkCore;
using WarTable.Api.Data;
using WarTable.Api.Models;

namespace WarTable.Api.Services;

/// <summary>
/// Partial update payload for an objective. A null member means "leave unchanged";
/// an empty string means "clear". This exists because the previous implementation
/// copied every field, so partial PUTs (e.g. a lane move) silently blanked the rest.
/// </summary>
public record ObjectivePatch(
    string? Title = null,
    string? Description = null,
    string? Lane = null,
    int? Order = null,
    string? Dependencies = null);

/// <summary>Partial update payload for a campaign. Same semantics as <see cref="ObjectivePatch"/>.</summary>
public record CampaignPatch(
    string? Title = null,
    string? Description = null,
    CampaignStatus? Status = null,
    string? Tags = null);

/// <summary>Raised when a sitrep references objective aliases that do not exist in the campaign.</summary>
public class UnknownAliasException : Exception
{
    public IReadOnlyList<int> Aliases { get; }

    public UnknownAliasException(IReadOnlyList<int> aliases)
        : base($"Unknown objective aliases: {string.Join(", ", aliases.Select(a => $"#{a}"))}")
    {
        Aliases = aliases;
    }
}

/// <summary>Result of resolving a campaign-scoped <c>#N</c> alias to a concrete object.</summary>
public record AliasResolution(
    string Kind,
    Guid Id,
    string Title,
    string? Lane,
    bool Deleted,
    Guid CampaignId,
    int Alias);

/// <summary>
/// Business logic layer over the WarTable EF Core context.
/// </summary>
public class WarTableService
{
    private readonly WarTableDbContext _db;

    public WarTableService(WarTableDbContext db)
    {
        _db = db;
    }

    // ---- Alias allocation ----

    /// <summary>
    /// Claims the next alias number for a campaign. The counter only increments, so
    /// aliases are never reused even after the object is deleted. The caller must save
    /// in the same transaction as the insert that consumes this number.
    /// </summary>
    private async Task<int> NextAliasAsync(Guid campaignId)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId)
            ?? throw new InvalidOperationException($"Campaign not found: {campaignId}");
        return campaign.NextAliasNumber++;
    }

    // ---- Campaigns ----

    public async Task<List<Campaign>> GetCampaignsAsync(string? search = null, CampaignStatus? status = null)
    {
        var query = _db.Campaigns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLowerInvariant();
            query = query.Where(c =>
                c.Title.ToLower().Contains(lower) ||
                c.Description.ToLower().Contains(lower) ||
                c.Tags.ToLower().Contains(lower));
        }

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        return await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Campaign?> GetCampaignAsync(Guid id)
    {
        return await _db.Campaigns
            .Include(c => c.Objectives)
            .Include(c => c.IntelEntries)
            .Include(c => c.SitReps)
                .ThenInclude(s => s.Objectives)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Campaign> CreateCampaignAsync(Campaign campaign)
    {
        if (campaign.Id == Guid.Empty)
            campaign.Id = Guid.NewGuid();
        campaign.CreatedAt = DateTime.UtcNow;
        campaign.UpdatedAt = DateTime.UtcNow;
        campaign.NextAliasNumber = 1;

        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();
        return campaign;
    }

    public async Task<Campaign?> UpdateCampaignAsync(Guid id, CampaignPatch patch)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
            return null;

        if (patch.Title is not null) campaign.Title = patch.Title;
        if (patch.Description is not null) campaign.Description = patch.Description;
        if (patch.Status is not null) campaign.Status = patch.Status.Value;
        if (patch.Tags is not null) campaign.Tags = patch.Tags;
        campaign.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return campaign;
    }

    public async Task<bool> DeleteCampaignAsync(Guid id)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
            return false;

        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync();
        return true;
    }

    // ---- Objectives ----

    public async Task<List<Objective>> GetObjectivesAsync(Guid? campaignId = null, string? lane = null)
    {
        var query = _db.Objectives.AsNoTracking().AsQueryable();

        if (campaignId.HasValue)
            query = query.Where(o => o.CampaignId == campaignId.Value);

        if (!string.IsNullOrWhiteSpace(lane))
        {
            var normalized = NormalizeLane(lane);
            query = query.Where(o => o.Lane == normalized);
        }

        return await query
            .OrderBy(o => o.Lane)
            .ThenBy(o => o.Order)
            .ThenBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Objective?> GetObjectiveAsync(Guid id)
    {
        return await _db.Objectives.FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Objective> CreateObjectiveAsync(Objective objective)
    {
        if (objective.Id == Guid.Empty)
            objective.Id = Guid.NewGuid();
        objective.Lane = NormalizeLane(objective.Lane);
        objective.CreatedAt = DateTime.UtcNow;
        objective.UpdatedAt = DateTime.UtcNow;
        objective.Alias = await NextAliasAsync(objective.CampaignId);

        _db.Objectives.Add(objective);
        await _db.SaveChangesAsync();
        return objective;
    }

    public async Task<Objective?> UpdateObjectiveAsync(Guid id, ObjectivePatch patch)
    {
        var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == id);
        if (objective is null)
            return null;

        if (patch.Title is not null) objective.Title = patch.Title;
        if (patch.Description is not null) objective.Description = patch.Description;
        if (patch.Lane is not null) objective.Lane = NormalizeLane(patch.Lane);
        if (patch.Order is not null) objective.Order = patch.Order.Value;
        if (patch.Dependencies is not null) objective.Dependencies = patch.Dependencies;
        objective.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return objective;
    }

    /// <summary>
    /// Validates and canonicalises a lane name. Accepts case and separator variations
    /// ("In Progress", "InProgress", "in_progress") so agents and the UI agree on one
    /// spelled form: <see cref="Objective.ObjectiveLanes"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The lane is not one of the known lanes.</exception>
    public static string NormalizeLane(string? lane)
    {
        if (string.IsNullOrWhiteSpace(lane))
            return "backlog";

        var key = lane.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
        foreach (var known in Objective.ObjectiveLanes)
        {
            if (known == key)
                return known;
        }

        throw new InvalidOperationException(
            $"Unknown lane '{lane}'. Valid lanes: {string.Join(", ", Objective.ObjectiveLanes)}.");
    }

    /// <summary>
    /// Soft delete. The row is retained so that old <c>#N</c> references in sitreps and
    /// intel remain resolvable.
    /// </summary>
    public async Task<bool> DeleteObjectiveAsync(Guid id)
    {
        var objective = await _db.Objectives.FirstOrDefaultAsync(o => o.Id == id);
        if (objective is null)
            return false;

        objective.DeletedAt = DateTime.UtcNow;
        objective.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderObjectivesAsync(Guid campaignId, Dictionary<Guid, int> orders)
    {
        if (orders is null || orders.Count == 0)
            return false;

        var ids = orders.Keys.ToList();
        var objectives = await _db.Objectives
            .Where(o => o.CampaignId == campaignId && ids.Contains(o.Id))
            .ToListAsync();

        foreach (var objective in objectives)
        {
            if (orders.TryGetValue(objective.Id, out var order))
            {
                objective.Order = order;
                objective.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Objectives in a campaign matching the given aliases. Soft-deleted objectives are
    /// included: a sitrep written after the work happened must still be able to name an
    /// objective that has since been removed — the journal outlives the board.
    /// </summary>
    public async Task<List<Objective>> GetObjectivesByAliasesAsync(Guid campaignId, IEnumerable<int> aliases)
    {
        var wanted = aliases.Distinct().ToList();
        if (wanted.Count == 0)
            return new List<Objective>();

        return await _db.Objectives.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(o => o.CampaignId == campaignId && wanted.Contains(o.Alias))
            .ToListAsync();
    }

    // ---- Intel ----

    public async Task<List<Intel>> GetIntelAsync(string? search = null, IntelCategory? category = null, Guid? campaignId = null)
    {
        var query = _db.IntelEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLowerInvariant();
            query = query.Where(i =>
                i.Title.ToLower().Contains(lower) ||
                i.Body.ToLower().Contains(lower) ||
                i.Tags.ToLower().Contains(lower));
        }

        if (category.HasValue)
            query = query.Where(i => i.Category == category.Value);

        if (campaignId.HasValue)
            query = query.Where(i => i.CampaignId == campaignId.Value);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<Intel?> GetIntelAsync(Guid id)
    {
        return await _db.IntelEntries.FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<Intel> CreateIntelAsync(Intel intel)
    {
        if (intel.CampaignId == Guid.Empty)
            throw new InvalidOperationException("campaignId is required.");
        if (intel.Id == Guid.Empty)
            intel.Id = Guid.NewGuid();
        intel.CreatedAt = DateTime.UtcNow;
        intel.Alias = await NextAliasAsync(intel.CampaignId);

        _db.IntelEntries.Add(intel);
        await _db.SaveChangesAsync();
        return intel;
    }

    /// <summary>Soft delete; see <see cref="DeleteObjectiveAsync"/> for the rationale.</summary>
    public async Task<bool> DeleteIntelAsync(Guid id)
    {
        var intel = await _db.IntelEntries.FirstOrDefaultAsync(i => i.Id == id);
        if (intel is null)
            return false;

        intel.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ---- SitReps ----

    public async Task<List<SitRep>> GetSitRepsAsync(Guid campaignId)
    {
        var sitreps = await _db.SitReps.AsNoTracking()
            .Include(s => s.Objectives)
            .Where(s => s.CampaignId == campaignId)
            .OrderByDescending(s => s.Alias)
            .ToListAsync();

        // Deterministic reference order for agents reading the journal as text.
        foreach (var sitrep in sitreps)
        {
            sitrep.Objectives = sitrep.Objectives
                .OrderBy(o => o.Kind)
                .ThenBy(o => o.ObjectiveAlias)
                .ToList();
        }

        return sitreps;
    }

    /// <summary>
    /// Appends a sitrep to the campaign journal. Objectives are referenced by alias;
    /// title and lane are snapshotted so the timeline survives later renames/deletes.
    /// Throws <see cref="UnknownAliasException"/> rather than persisting dangling refs.
    /// </summary>
    public async Task<SitRep?> CreateSitRepAsync(
        Guid campaignId,
        string summary,
        string? source,
        IEnumerable<int> completed,
        IEnumerable<int> blocked,
        IEnumerable<int> next)
    {
        var campaignExists = await _db.Campaigns.AnyAsync(c => c.Id == campaignId);
        if (!campaignExists)
            return null;

        var completedList = completed.Distinct().ToList();
        var blockedList = blocked.Distinct().ToList();
        var nextList = next.Distinct().ToList();

        var allAliases = completedList.Concat(blockedList).Concat(nextList).Distinct().ToList();
        var resolved = await GetObjectivesByAliasesAsync(campaignId, allAliases);
        var resolvedAliases = resolved.Select(o => o.Alias).ToHashSet();

        var unknown = allAliases.Where(a => !resolvedAliases.Contains(a)).ToList();
        if (unknown.Count > 0)
            throw new UnknownAliasException(unknown);

        var byAlias = resolved.ToDictionary(o => o.Alias);

        var sitrep = new SitRep
        {
            CampaignId = campaignId,
            Summary = summary,
            Source = string.IsNullOrWhiteSpace(source) ? "human" : source!,
            CreatedAt = DateTime.UtcNow,
            Alias = await NextAliasAsync(campaignId),
        };

        void Add(IEnumerable<int> aliases, SitRepObjectiveKind kind)
        {
            foreach (var alias in aliases)
            {
                var objective = byAlias[alias];
                sitrep.Objectives.Add(new SitRepObjective
                {
                    ObjectiveAlias = alias,
                    TitleSnapshot = objective.Title,
                    LaneAtWrite = objective.Lane,
                    Kind = kind,
                });
            }
        }

        Add(completedList, SitRepObjectiveKind.Completed);
        Add(blockedList, SitRepObjectiveKind.Blocked);
        Add(nextList, SitRepObjectiveKind.Next);

        _db.SitReps.Add(sitrep);
        await _db.SaveChangesAsync();
        return sitrep;
    }

    // ---- Alias resolution ----

    /// <summary>
    /// Resolves a <c>#N</c> alias within a campaign. Soft-deleted objectives and intel
    /// still resolve (with <c>Deleted = true</c>) so old references never become dead links.
    /// </summary>
    public async Task<AliasResolution?> ResolveAliasAsync(Guid campaignId, int alias)
    {
        var objective = await _db.Objectives
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.CampaignId == campaignId && o.Alias == alias);
        if (objective is not null)
        {
            return new AliasResolution(
                "Objective", objective.Id, objective.Title, objective.Lane,
                objective.DeletedAt is not null, campaignId, alias);
        }

        var intel = await _db.IntelEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.CampaignId == campaignId && i.Alias == alias);
        if (intel is not null)
        {
            return new AliasResolution(
                "Intel", intel.Id, intel.Title, intel.Category.ToString(),
                intel.DeletedAt is not null, campaignId, alias);
        }

        var sitrep = await _db.SitReps
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CampaignId == campaignId && s.Alias == alias);
        if (sitrep is not null)
        {
            var title = sitrep.Summary.Length > 120 ? sitrep.Summary[..120] + "…" : sitrep.Summary;
            return new AliasResolution("SitRep", sitrep.Id, title, null, false, campaignId, alias);
        }

        return null;
    }
}
