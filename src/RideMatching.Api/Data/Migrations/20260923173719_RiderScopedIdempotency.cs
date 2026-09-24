using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RideMatching.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RiderScopedIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rides_IdempotencyKey",
                table: "Rides");

            migrationBuilder.CreateIndex(
                name: "IX_Rides_RiderId_IdempotencyKey",
                table: "Rides",
                columns: new[] { "RiderId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rides_RiderId_IdempotencyKey",
                table: "Rides");

            migrationBuilder.CreateIndex(
                name: "IX_Rides_IdempotencyKey",
                table: "Rides",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }
    }
}
