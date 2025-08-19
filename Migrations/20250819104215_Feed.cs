using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineAssessment.Web.Migrations
{
    /// <inheritdoc />
    public partial class Feed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feedbacks_Users_UserSapId",
                table: "Feedbacks");

            migrationBuilder.RenameColumn(
                name: "UserSapId",
                table: "Feedbacks",
                newName: "Username");

            migrationBuilder.RenameIndex(
                name: "IX_Feedbacks_UserSapId",
                table: "Feedbacks",
                newName: "IX_Feedbacks_Username");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_Username",
                table: "Users",
                column: "Username");

            migrationBuilder.AddForeignKey(
                name: "FK_Feedbacks_Users_Username",
                table: "Feedbacks",
                column: "Username",
                principalTable: "Users",
                principalColumn: "Username",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feedbacks_Users_Username",
                table: "Feedbacks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_Username",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "Username",
                table: "Feedbacks",
                newName: "UserSapId");

            migrationBuilder.RenameIndex(
                name: "IX_Feedbacks_Username",
                table: "Feedbacks",
                newName: "IX_Feedbacks_UserSapId");

            migrationBuilder.AddForeignKey(
                name: "FK_Feedbacks_Users_UserSapId",
                table: "Feedbacks",
                column: "UserSapId",
                principalTable: "Users",
                principalColumn: "SapId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
