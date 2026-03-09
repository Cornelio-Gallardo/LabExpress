using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dx7Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPhilhealthNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhilhealthNo",
                table: "Patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhilhealthNo",
                table: "Patients");
        }
    }
}
