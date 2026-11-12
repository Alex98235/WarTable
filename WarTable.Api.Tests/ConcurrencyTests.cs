using Microsoft.EntityFrameworkCore;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// The concurrency token has to reach the UPDATE statement, not just the check
/// before it. A stale-write test that runs after the check exercises the
/// comparison alone; these use two change trackers so the second write is
/// decided by the database.
/// </summary>
public class ConcurrencyTests
{
	private const string Human = "human";

	[Fact]
	public async Task SecondWriter_WithTheVersionItRead_FailsAtTheDatabase()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);

		await using var other = ctx.CreateSecondContext();

		// Both clients read the row before either writes.
		var first = await ctx.Db.Campaigns.SingleAsync(c => c.Id == campaign.Id);
		var second = await other.Campaigns.SingleAsync(c => c.Id == campaign.Id);
		Assert.Equal(first.RowVersion, second.RowVersion);

		// Each writes with the version it read, exactly as the service does.
		first.Title = "first";
		ctx.Db.Entry(first).Property(c => c.RowVersion).OriginalValue = first.RowVersion;
		await ctx.Db.SaveChangesAsync();

		second.Title = "second";
		other.Entry(second).Property(c => c.RowVersion).OriginalValue = second.RowVersion;

		// If the token never reached the WHERE clause this save would succeed and
		// silently overwrite the first write.
		await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
	}

	[Fact]
	public async Task StaleVersion_IsRejectedBeforeTheRowIsWritten()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var stale = (await ctx.Service.GetCampaignAsync(campaign.Id))!.RowVersion;

		await ctx.Service.UpdateCampaignAsync(
			campaign.Id, new CampaignPatch("renamed", "", null), Human, stale);

		var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			ctx.Service.UpdateCampaignAsync(
				campaign.Id, new CampaignPatch("clobbered", "", null), Human, stale));

		var current = Assert.IsType<Models.Campaign>(conflict.Current);
		Assert.Equal("renamed", current.Title);
		Assert.NotEqual(stale, current.RowVersion);
	}

	[Fact]
	public async Task AWrite_ChangesTheVersion_SoTheClientMustReRead()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);
		var before = (await ctx.Service.GetCampaignAsync(campaign.Id))!.RowVersion;

		await ctx.Service.UpdateCampaignAsync(
			campaign.Id, new CampaignPatch("renamed", "", null), Human, before);

		var after = (await ctx.Service.GetCampaignAsync(campaign.Id))!.RowVersion;
		Assert.NotEqual(before, after);
		Assert.NotEqual(Guid.Empty, after);
	}

	[Fact]
	public async Task ANewRow_IsBornWithAVersion()
	{
		await using var ctx = await TestContext.CreateAsync();
		var campaign = await ctx.Service.CreateCampaignAsync("C", "", "", Human);

		Assert.NotEqual(Guid.Empty, campaign.RowVersion);
	}
}
