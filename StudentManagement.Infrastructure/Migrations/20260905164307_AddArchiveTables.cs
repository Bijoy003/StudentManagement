using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrollmentsArchive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchiveReason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentsArchive", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentsArchive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateOfEnroll = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchiveReason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentsArchive", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentsArchive");

            migrationBuilder.DropTable(
                name: "StudentsArchive");
        }
    }
}
