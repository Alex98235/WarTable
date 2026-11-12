using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class DropReferenceKind : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// Convert any existing Reference notes to Context, appending
			// tag 'migrated-reference' with the same NULL/empty/duplicate
			// handling as AddNoteShapes.
			migrationBuilder.Sql(
				"""
                UPDATE "BunkerNotes"
                SET "Kind" = 'Context',
                    "Tags" = CASE
                        WHEN "Tags" IS NULL OR "Tags" = '' THEN 'migrated-reference'
                        WHEN "Tags" LIKE '%migrated-reference%' THEN "Tags"
                        ELSE "Tags" || ',migrated-reference'
                    END
                WHERE "Kind" = 'Reference';
                """);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			throw new NotSupportedException(
				"WarTable never migrates down; restore the cold backup instead.");
		}
	}
}
