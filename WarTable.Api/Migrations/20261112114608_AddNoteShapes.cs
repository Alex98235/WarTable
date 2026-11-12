using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class AddNoteShapes : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<string>(
				name: "Charter",
				table: "Campaigns",
				type: "TEXT",
				maxLength: 40000,
				nullable: true);

			migrationBuilder.AddColumn<DateTime>(
				name: "ApprovedAt",
				table: "BunkerNotes",
				type: "TEXT",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "Basis",
				table: "BunkerNotes",
				type: "TEXT",
				maxLength: 500,
				nullable: true);

			// 1. Backfill Campaigns.Charter from the most recent non-deleted Charter note per campaign.
			migrationBuilder.Sql(
				"""
                UPDATE "Campaigns"
                SET "Charter" = (
                    SELECT "Body" FROM "BunkerNotes"
                    WHERE "CampaignId" = "Campaigns"."Id"
                      AND "Kind" = 'Charter'
                      AND "DeletedAt" IS NULL
                    ORDER BY "UpdatedAt" DESC
                    LIMIT 1
                )
                WHERE EXISTS (
                    SELECT 1 FROM "BunkerNotes"
                    WHERE "CampaignId" = "Campaigns"."Id"
                      AND "Kind" = 'Charter'
                      AND "DeletedAt" IS NULL
                );
                """);

			// 2. Question -> Context, appending tag 'migrated-question'.
			migrationBuilder.Sql(
				"""
                UPDATE "BunkerNotes"
                SET "Kind" = 'Context',
                    "Tags" = CASE
                        WHEN "Tags" IS NULL OR "Tags" = '' THEN 'migrated-question'
                        WHEN "Tags" LIKE '%migrated-question%' THEN "Tags"
                        ELSE "Tags" || ',migrated-question'
                    END
                WHERE "Kind" = 'Question';
                """);

			// 3. DesignDoc -> Context, appending tag 'migrated-designdoc'.
			migrationBuilder.Sql(
				"""
                UPDATE "BunkerNotes"
                SET "Kind" = 'Context',
                    "Tags" = CASE
                        WHEN "Tags" IS NULL OR "Tags" = '' THEN 'migrated-designdoc'
                        WHEN "Tags" LIKE '%migrated-designdoc%' THEN "Tags"
                        ELSE "Tags" || ',migrated-designdoc'
                    END
                WHERE "Kind" = 'DesignDoc';
                """);

			// 4. Delete BunkerNoteLinks rows whose note is a Charter note, then delete those BunkerNotes rows.
			migrationBuilder.Sql(
				"""
                DELETE FROM "BunkerNoteLinks"
                WHERE "NoteId" IN (SELECT "Id" FROM "BunkerNotes" WHERE "Kind" = 'Charter');
                """);
			migrationBuilder.Sql(
				"""
                DELETE FROM "BunkerNotes"
                WHERE "Kind" = 'Charter';
                """);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			// WarTable never migrates down (see CONTRIBUTING.md §Schema and data).
			// Rolling back across a schema change means restoring the cold backup
			// that was taken before the migration ran.
			throw new NotSupportedException(
				"WarTable never migrates down; restore the cold backup instead.");
		}
	}
}
