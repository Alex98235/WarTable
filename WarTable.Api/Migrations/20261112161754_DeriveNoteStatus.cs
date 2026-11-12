using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class DeriveNoteStatus : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// Orphan notes: rows with Status='Superseded' but no SupersedesId have no
			// successor to derive from, so mark them as retired with a fixed timestamp
			// and an appended tag so the data is not lost. The Status column was created
			// by prior migrations and exists in every production database.
			migrationBuilder.Sql(
				"""
                UPDATE "BunkerNotes"
                SET "DeletedAt" = '2026-11-12T12:00:00Z',
                    "Tags" = CASE
                        WHEN "Tags" IS NULL OR "Tags" = '' THEN 'retired-without-successor'
                        ELSE "Tags" || ',retired-without-successor'
                    END
                WHERE "Status" = 'Superseded' AND "SupersedesId" IS NULL;
                """);

			migrationBuilder.DropIndex(
				name: "IX_BunkerNotes_CampaignId_Kind_Status",
				table: "BunkerNotes");

			migrationBuilder.DropColumn(
				name: "Status",
				table: "BunkerNotes");

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNotes_CampaignId_Kind",
				table: "BunkerNotes",
				columns: new[] { "CampaignId", "Kind" });
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			throw new NotSupportedException("WarTable never migrates down; restore the cold backup instead.");
		}

	}
}
