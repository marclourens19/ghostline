using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghostline.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimedScore = table.Column<int>(type: "integer", nullable: false),
                    VerifiedScore = table.Column<int>(type: "integer", nullable: false),
                    TotalTicks = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerificationError = table.Column<string>(type: "text", nullable: true),
                    ReplayData = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_IsVerified_VerifiedScore_CreatedAtUtc",
                table: "Runs",
                columns: new[] { "IsVerified", "VerifiedScore", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
