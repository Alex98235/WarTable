using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarTable.Api.Migrations
{
	/// <inheritdoc />
	public partial class InitialCreate : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateTable(
				name: "ActivityEvents",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					Sequence = table.Column<long>(type: "INTEGER", nullable: false),
					CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
					Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					EntityType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
					Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
					Metadata = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_ActivityEvents", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "Campaigns",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
					Intent = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
					Tags = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
					NextAliasNumber = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Campaigns", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "Preferences",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
					Rationale = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
					Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					Active = table.Column<bool>(type: "INTEGER", nullable: false),
					Tags = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Preferences", x => x.Id);
				});

			migrationBuilder.CreateTable(
				name: "BunkerNotes",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
					Alias = table.Column<int>(type: "INTEGER", nullable: false),
					Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
					Body = table.Column<string>(type: "TEXT", maxLength: 40000, nullable: false),
					Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					Pinned = table.Column<bool>(type: "INTEGER", nullable: false),
					Tags = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
					CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
					DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_BunkerNotes", x => x.Id);
					table.ForeignKey(
						name: "FK_BunkerNotes_Campaigns_CampaignId",
						column: x => x.CampaignId,
						principalTable: "Campaigns",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "Objectives",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
					Alias = table.Column<int>(type: "INTEGER", nullable: false),
					Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
					Description = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
					PlanState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
					NextStepNumber = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Objectives", x => x.Id);
					table.ForeignKey(
						name: "FK_Objectives_Campaigns_CampaignId",
						column: x => x.CampaignId,
						principalTable: "Campaigns",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "BunkerNoteLinks",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					NoteId = table.Column<Guid>(type: "TEXT", nullable: false),
					ObjectiveId = table.Column<Guid>(type: "TEXT", nullable: true),
					StepId = table.Column<Guid>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_BunkerNoteLinks", x => x.Id);
					table.ForeignKey(
						name: "FK_BunkerNoteLinks_BunkerNotes_NoteId",
						column: x => x.NoteId,
						principalTable: "BunkerNotes",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "Steps",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					ObjectiveId = table.Column<Guid>(type: "TEXT", nullable: false),
					StepNumber = table.Column<int>(type: "INTEGER", nullable: false),
					Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
					Context = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
					Assignee = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					Assigner = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Steps", x => x.Id);
					table.ForeignKey(
						name: "FK_Steps_Objectives_ObjectiveId",
						column: x => x.ObjectiveId,
						principalTable: "Objectives",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "Evidence",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					StepId = table.Column<Guid>(type: "TEXT", nullable: false),
					Label = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
					Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
					CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_Evidence", x => x.Id);
					table.ForeignKey(
						name: "FK_Evidence_Steps_StepId",
						column: x => x.StepId,
						principalTable: "Steps",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateTable(
				name: "StepDependencies",
				columns: table => new
				{
					StepId = table.Column<Guid>(type: "TEXT", nullable: false),
					DependsOnStepId = table.Column<Guid>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_StepDependencies", x => new { x.StepId, x.DependsOnStepId });
					table.ForeignKey(
						name: "FK_StepDependencies_Steps_DependsOnStepId",
						column: x => x.DependsOnStepId,
						principalTable: "Steps",
						principalColumn: "Id",
						onDelete: ReferentialAction.Restrict);
					table.ForeignKey(
						name: "FK_StepDependencies_Steps_StepId",
						column: x => x.StepId,
						principalTable: "Steps",
						principalColumn: "Id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateTable(
				name: "StepUpdates",
				columns: table => new
				{
					Id = table.Column<Guid>(type: "TEXT", nullable: false),
					StepId = table.Column<Guid>(type: "TEXT", nullable: false),
					Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
					Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
					Body = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_StepUpdates", x => x.Id);
					table.ForeignKey(
						name: "FK_StepUpdates_Steps_StepId",
						column: x => x.StepId,
						principalTable: "Steps",
						principalColumn: "Id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateIndex(
				name: "IX_ActivityEvents_CampaignId_Sequence",
				table: "ActivityEvents",
				columns: new[] { "CampaignId", "Sequence" });

			migrationBuilder.CreateIndex(
				name: "IX_ActivityEvents_Sequence",
				table: "ActivityEvents",
				column: "Sequence");

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNoteLinks_NoteId",
				table: "BunkerNoteLinks",
				column: "NoteId");

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNoteLinks_ObjectiveId",
				table: "BunkerNoteLinks",
				column: "ObjectiveId");

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNoteLinks_StepId",
				table: "BunkerNoteLinks",
				column: "StepId");

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNotes_CampaignId_Alias",
				table: "BunkerNotes",
				columns: new[] { "CampaignId", "Alias" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_BunkerNotes_CampaignId_Kind_Status",
				table: "BunkerNotes",
				columns: new[] { "CampaignId", "Kind", "Status" });

			migrationBuilder.CreateIndex(
				name: "IX_Evidence_StepId",
				table: "Evidence",
				column: "StepId");

			migrationBuilder.CreateIndex(
				name: "IX_Objectives_CampaignId_Alias",
				table: "Objectives",
				columns: new[] { "CampaignId", "Alias" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_StepDependencies_DependsOnStepId",
				table: "StepDependencies",
				column: "DependsOnStepId");

			migrationBuilder.CreateIndex(
				name: "IX_Steps_ObjectiveId_SortOrder",
				table: "Steps",
				columns: new[] { "ObjectiveId", "SortOrder" });

			migrationBuilder.CreateIndex(
				name: "IX_Steps_ObjectiveId_StepNumber",
				table: "Steps",
				columns: new[] { "ObjectiveId", "StepNumber" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_StepUpdates_StepId",
				table: "StepUpdates",
				column: "StepId");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "ActivityEvents");

			migrationBuilder.DropTable(
				name: "BunkerNoteLinks");

			migrationBuilder.DropTable(
				name: "Evidence");

			migrationBuilder.DropTable(
				name: "Preferences");

			migrationBuilder.DropTable(
				name: "StepDependencies");

			migrationBuilder.DropTable(
				name: "StepUpdates");

			migrationBuilder.DropTable(
				name: "BunkerNotes");

			migrationBuilder.DropTable(
				name: "Steps");

			migrationBuilder.DropTable(
				name: "Objectives");

			migrationBuilder.DropTable(
				name: "Campaigns");
		}
	}
}
