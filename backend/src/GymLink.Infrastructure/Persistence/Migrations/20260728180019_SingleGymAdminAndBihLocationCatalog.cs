using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymLink.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleGymAdminAndBihLocationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [UserGymAssignments]
                    WHERE [Role] = N'GymAdmin' AND [Status] = N'Active'
                    GROUP BY [TenantId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51000, 'Resolve duplicate active GymAdmin assignments before applying this migration.', 1;
                END;

                DECLARE @CountryId uniqueidentifier =
                    (SELECT TOP (1) [Id] FROM [Countries] WHERE [Code] = N'BIH');

                IF @CountryId IS NULL
                BEGIN
                    SET @CountryId = NEWID();
                    INSERT INTO [Countries]
                        ([Id], [Code], [Name], [IsActive], [CreatedAtUtc], [CreatedByUserId],
                         [UpdatedAtUtc], [UpdatedByUserId])
                    VALUES
                        (@CountryId, N'BIH', N'Bosna i Hercegovina', 1, SYSUTCDATETIME(), NULL,
                         NULL, NULL);
                END;

                DECLARE @Locations table ([Name] nvarchar(160) NOT NULL);
                INSERT INTO @Locations ([Name])
                VALUES
                    (N'Banovići'),
                    (N'Banja Luka'),
                    (N'Berkovići'),
                    (N'Bihać'),
                    (N'Bijeljina'),
                    (N'Bileća'),
                    (N'Bosanska Krupa'),
                    (N'Bosanski Petrovac'),
                    (N'Bosansko Grahovo'),
                    (N'Bratunac'),
                    (N'Brčko'),
                    (N'Breza'),
                    (N'Brod'),
                    (N'Bugojno'),
                    (N'Busovača'),
                    (N'Bužim'),
                    (N'Cazin'),
                    (N'Centar Sarajevo'),
                    (N'Čajniče'),
                    (N'Čapljina'),
                    (N'Čelić'),
                    (N'Čelinac'),
                    (N'Čitluk'),
                    (N'Derventa'),
                    (N'Doboj'),
                    (N'Doboj Istok'),
                    (N'Doboj Jug'),
                    (N'Dobretići'),
                    (N'Domaljevac-Šamac'),
                    (N'Donji Vakuf'),
                    (N'Donji Žabar'),
                    (N'Drvar'),
                    (N'Foča'),
                    (N'Foča-Ustikolina'),
                    (N'Fojnica'),
                    (N'Gacko'),
                    (N'Glamoč'),
                    (N'Goražde'),
                    (N'Gornji Vakuf-Uskoplje'),
                    (N'Gračanica'),
                    (N'Gradačac'),
                    (N'Gradiška'),
                    (N'Grude'),
                    (N'Hadžići'),
                    (N'Han Pijesak'),
                    (N'Ilidža'),
                    (N'Ilijaš'),
                    (N'Istočna Ilidža'),
                    (N'Istočni Drvar'),
                    (N'Istočni Mostar'),
                    (N'Istočni Stari Grad'),
                    (N'Istočno Novo Sarajevo'),
                    (N'Istočno Sarajevo'),
                    (N'Jablanica'),
                    (N'Jajce'),
                    (N'Jezero'),
                    (N'Kakanj'),
                    (N'Kalesija'),
                    (N'Kalinovik'),
                    (N'Kiseljak'),
                    (N'Ključ'),
                    (N'Kladanj'),
                    (N'Kneževo'),
                    (N'Konjic'),
                    (N'Kostajnica'),
                    (N'Kotor Varoš'),
                    (N'Kozarska Dubica'),
                    (N'Kreševo'),
                    (N'Krupa na Uni'),
                    (N'Kupres (Federacija BiH)'),
                    (N'Kupres (Republika Srpska)'),
                    (N'Laktaši'),
                    (N'Livno'),
                    (N'Ljubinje'),
                    (N'Ljubuški'),
                    (N'Lopare'),
                    (N'Lukavac'),
                    (N'Maglaj'),
                    (N'Milići'),
                    (N'Modriča'),
                    (N'Mostar'),
                    (N'Mrkonjić Grad'),
                    (N'Neum'),
                    (N'Nevesinje'),
                    (N'Novi Grad'),
                    (N'Novi Grad Sarajevo'),
                    (N'Novi Travnik'),
                    (N'Novo Goražde'),
                    (N'Novo Sarajevo'),
                    (N'Odžak'),
                    (N'Olovo'),
                    (N'Orašje'),
                    (N'Osmaci'),
                    (N'Oštra Luka'),
                    (N'Pale'),
                    (N'Pale-Prača'),
                    (N'Pelagićevo'),
                    (N'Petrovac'),
                    (N'Petrovo'),
                    (N'Posušje'),
                    (N'Prijedor'),
                    (N'Prozor-Rama'),
                    (N'Ravno'),
                    (N'Ribnik'),
                    (N'Rogatica'),
                    (N'Rudo'),
                    (N'Sanski Most'),
                    (N'Sapna'),
                    (N'Sarajevo'),
                    (N'Šamac'),
                    (N'Šekovići'),
                    (N'Šipovo'),
                    (N'Široki Brijeg'),
                    (N'Sokolac'),
                    (N'Srbac'),
                    (N'Srebrenica'),
                    (N'Srebrenik'),
                    (N'Stari Grad Sarajevo'),
                    (N'Stanari'),
                    (N'Stolac'),
                    (N'Teočak'),
                    (N'Teslić'),
                    (N'Tešanj'),
                    (N'Tomislavgrad'),
                    (N'Travnik'),
                    (N'Trebinje'),
                    (N'Trnovo (Federacija BiH)'),
                    (N'Trnovo (Republika Srpska)'),
                    (N'Tuzla'),
                    (N'Ugljevik'),
                    (N'Usora'),
                    (N'Vareš'),
                    (N'Velika Kladuša'),
                    (N'Visoko'),
                    (N'Višegrad'),
                    (N'Vitez'),
                    (N'Vlasenica'),
                    (N'Vogošća'),
                    (N'Vukosavlje'),
                    (N'Zavidovići'),
                    (N'Zenica'),
                    (N'Zvornik'),
                    (N'Žepče'),
                    (N'Živinice');

                INSERT INTO [Cities]
                    ([Id], [CountryId], [Name], [IsActive], [CreatedAtUtc], [CreatedByUserId],
                     [UpdatedAtUtc], [UpdatedByUserId])
                SELECT
                    NEWID(), @CountryId, source.[Name], 1, SYSUTCDATETIME(), NULL, NULL, NULL
                FROM @Locations source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [Cities] existing
                    WHERE existing.[CountryId] = @CountryId
                      AND existing.[Name] = source.[Name]);
                """);

            migrationBuilder.DropIndex(
                name: "IX_UserGymAssignments_TenantId",
                table: "UserGymAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_UserGymAssignments_TenantId_ActiveGymAdmin",
                table: "UserGymAssignments",
                column: "TenantId",
                unique: true,
                filter: "[Status] = 'Active' AND [Role] = 'GymAdmin'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserGymAssignments_TenantId_ActiveGymAdmin",
                table: "UserGymAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_UserGymAssignments_TenantId",
                table: "UserGymAssignments",
                column: "TenantId");
        }
    }
}
