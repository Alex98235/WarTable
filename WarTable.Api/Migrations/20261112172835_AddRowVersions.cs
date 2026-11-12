using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class AddRowVersions : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<Guid>(
				name: "RowVersion",
				table: "Steps",
				type: "TEXT",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

			migrationBuilder.AddColumn<Guid>(
				name: "RowVersion",
				table: "Preferences",
				type: "TEXT",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

			migrationBuilder.AddColumn<Guid>(
				name: "RowVersion",
				table: "Objectives",
				type: "TEXT",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

			migrationBuilder.AddColumn<Guid>(
				name: "RowVersion",
				table: "Campaigns",
				type: "TEXT",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

			migrationBuilder.AddColumn<Guid>(
				name: "RowVersion",
				table: "BunkerNotes",
				type: "TEXT",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			throw new NotSupportedException("WarTable never migrates down; restore the cold backup instead.");
		}
	}
}
