using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class DropResolvedStatus : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// `Resolved` meant "the question was answered" or "the gotcha was fixed".
			// Questions are gone, and a gotcha linked only to finished work goes dormant on
			// its own, so nothing produces this status. Superseded is the closest honest
			// state: it keeps the note out of context, which is what resolving did.
			migrationBuilder.Sql(
				"""
				UPDATE "BunkerNotes" SET "Status" = 'Superseded' WHERE "Status" = 'Resolved';
				""");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			throw new NotSupportedException("WarTable never migrates down; restore the cold backup instead.");
		}
	}
}
