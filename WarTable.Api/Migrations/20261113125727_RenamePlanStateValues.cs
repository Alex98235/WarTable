using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class RenamePlanStateValues : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// PlanState is stored as text, so the rename is a value remap rather
			// than an ordinal shift: no row can be silently reinterpreted.
			migrationBuilder.Sql("UPDATE Objectives SET PlanState = 'Backlog' WHERE PlanState = 'Draft';");
			migrationBuilder.Sql("UPDATE Objectives SET PlanState = 'Ready' WHERE PlanState = 'Active';");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("UPDATE Objectives SET PlanState = 'Active' WHERE PlanState = 'Ready';");
			migrationBuilder.Sql("UPDATE Objectives SET PlanState = 'Draft' WHERE PlanState = 'Backlog';");
		}
	}
}
