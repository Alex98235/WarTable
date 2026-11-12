using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class ReduceEvidenceKinds : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql(
				"""
				UPDATE "Evidence"
				SET "Kind" = 'Link'
				WHERE "Kind" IN ('File', 'Test', 'Screenshot', 'Other')
				  AND ("Value" LIKE 'http://%' OR "Value" LIKE 'https://%');

				UPDATE "Evidence"
				SET "Kind" = 'Commit'
				WHERE "Kind" IN ('File', 'Test', 'Screenshot', 'Other')
				  AND length("Value") BETWEEN 7 AND 40
				  AND "Value" NOT GLOB '*[^0-9a-fA-F]*';

				UPDATE "Evidence"
				SET "Kind" = 'Link'
				WHERE "Kind" IN ('File', 'Test', 'Screenshot', 'Other');
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
