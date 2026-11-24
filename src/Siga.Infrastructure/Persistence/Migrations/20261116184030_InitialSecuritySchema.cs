using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siga.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSecuritySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "seguridad");

            migrationBuilder.CreateTable(
                name: "BitacoraAccesos",
                schema: "seguridad",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Usuario = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FechaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Evento = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Exitoso = table.Column<bool>(type: "bit", nullable: false),
                    Ip = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    DispositivoId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitacoraAccesos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "seguridad",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Usuario = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    FamiliaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispositivoId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FechaCreacionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiraUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevocadoUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MotivoRevocacion = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ReemplazadoPorHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    IpCreacion = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Usuarios",
                        column: x => x.Usuario,
                        principalSchema: "dbo",
                        principalTable: "Usuarios",
                        principalColumn: "Usuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsuariosApp",
                schema: "seguridad",
                columns: table => new
                {
                    Usuario = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccesoAppHabilitado = table.Column<bool>(type: "bit", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    BloqueadoHastaUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UltimoAccesoUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PasswordActualizadoUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FechaCreacionUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosApp", x => x.Usuario);
                    table.ForeignKey(
                        name: "FK_UsuariosApp_Usuarios",
                        column: x => x.Usuario,
                        principalSchema: "dbo",
                        principalTable: "Usuarios",
                        principalColumn: "Usuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BitacoraAccesos_Usuario_FechaUtc",
                schema: "seguridad",
                table: "BitacoraAccesos",
                columns: new[] { "Usuario", "FechaUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamiliaId",
                schema: "seguridad",
                table: "RefreshTokens",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Usuario",
                schema: "seguridad",
                table: "RefreshTokens",
                column: "Usuario");

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_TokenHash",
                schema: "seguridad",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BitacoraAccesos",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "UsuariosApp",
                schema: "seguridad");
        }
    }
}
