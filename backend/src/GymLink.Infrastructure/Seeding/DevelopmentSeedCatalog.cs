using GymLink.Domain.Common;

namespace GymLink.Infrastructure.Seeding;

internal static class DevelopmentSeedCatalog
{
    public static readonly SeedAccount[] Accounts =
    [
        new("centraladmin", null, "Central Administrator", RoleNames.CentralAdmin, "+387 61 000 001"),
        new("admin.arena", "gymadmin", "Marina Kordić", RoleNames.GymAdmin, "+387 61 000 101"),
        new("admin.perfectfit", null, "Josip Marić", RoleNames.GymAdmin, "+387 61 000 102"),
        new("admin.respect", "desktop", "Amina Hadžić", RoleNames.GymAdmin, "+387 61 000 103"),
        new("admin.oxide", null, "Emir Bećirović", RoleNames.GymAdmin, "+387 61 000 104"),
        new("admin.fitfactory", null, "Selma Alagić", RoleNames.GymAdmin, "+387 61 000 105"),
        new("admin.iskra", null, "Adnan Husić", RoleNames.GymAdmin, "+387 61 000 106"),
        new("arenatrainer1", "trainer2", "Marko Dogan", RoleNames.Trainer, "+387 61 001 101"),
        new("arenatrainer2", null, "Ana Marić", RoleNames.Trainer, "+387 61 001 102"),
        new("perfectfittrainer1", null, "Ivan Kraljević", RoleNames.Trainer, "+387 61 001 103"),
        new("perfectfittrainer2", null, "Petra Bošnjak", RoleNames.Trainer, "+387 61 001 104"),
        new("respecttrainer1", "trainer", "Emir Hadžić", RoleNames.Trainer, "+387 61 001 105"),
        new("respecttrainer2", null, "Lejla Bećirović", RoleNames.Trainer, "+387 61 001 106"),
        new("oxidetrainer1", null, "Amar Kovačević", RoleNames.Trainer, "+387 61 001 107"),
        new("oxidetrainer2", null, "Selma Delić", RoleNames.Trainer, "+387 61 001 108"),
        new("fitfactorytrainer1", null, "Adnan Mujić", RoleNames.Trainer, "+387 61 001 109"),
        new("fitfactorytrainer2", null, "Emina Alagić", RoleNames.Trainer, "+387 61 001 110"),
        new("iskratrainer1", null, "Haris Mehić", RoleNames.Trainer, "+387 61 001 111"),
        new("iskratrainer2", null, "Ivana Vuković", RoleNames.Trainer, "+387 61 001 112"),
        new("mobile1", "mobile", "Sara Kovač", RoleNames.Member, "+387 61 002 001"),
        new("mobile2", "member", "Dino Alić", RoleNames.Member, "+387 61 002 002"),
        new("mobile3", null, "Lamija Softić", RoleNames.Member, "+387 61 002 003"),
        new("mobile4", null, "Luka Perić", RoleNames.Member, "+387 61 002 004"),
    ];

    public static readonly SeedEquipment[] Equipment =
    [
        new("Traka za trčanje"),
        new("Sobni bicikl"),
        new("Eliptični trenažer"),
        new("Slobodni utezi"),
        new("Smith mašina"),
        new("Squat rack"),
        new("Cable crossover"),
        new("Leg press"),
        new("Kettlebell set"),
        new("Vreća za boks"),
    ];

    public static readonly SeedTrainingType[] TrainingTypes =
    [
        new("Personalni trening", "Individualni rad prilagođen ciljevima člana."),
        new("Funkcionalni trening", "Trening snage, mobilnosti i kondicije."),
        new("Trening snage", "Razvoj snage uz slobodne utege i sprave."),
        new("Kondicioni trening", "Razvoj izdržljivosti, brzine i opće kondicije."),
        new("Pilates", "Kontrolirani trening stabilnosti, posture i pokretljivosti."),
        new("Kickboxing", "Kondicioni i tehnički trening elemenata kickboxinga."),
        new("Mobilnost", "Vođeni trening pokretljivosti i oporavka."),
    ];

    public static readonly SeedGym[] Gyms =
    [
        new(
            "arena",
            "Arena Sport Centar",
            "GymLink Mostar",
            "Mostar",
            "Mile Budaka bb, 88000 Mostar",
            43.3438m,
            17.8078m,
            "+387 36 323 333",
            "Sportsko-rekreacijski centar sa savremenom fitness opremom, individualnim i grupnim programima.",
            "https://images.unsplash.com/photo-1534438327276-14e5300c3a48",
            "admin.arena",
            65m,
            170m,
            ["Traka za trčanje", "Sobni bicikl", "Eliptični trenažer", "Slobodni utezi", "Smith mašina", "Squat rack", "Cable crossover", "Leg press", "Kettlebell set"],
            ["Personalni trening", "Funkcionalni trening", "Trening snage", "Kondicioni trening"],
            [
                new("arenatrainer1", "Trening snage", "Trening snage 60 min", "Certificirani trener snage i kondicione pripreme.", "arenatrainer1.png"),
                new("arenatrainer2", "Kondicioni trening", "Kondicioni trening 60 min", "Trenerica usmjerena na kondiciju i siguran individualni napredak.", "arenatrainer2.png"),
            ],
            SeedGymHours.Arena),
        new(
            "perfectfit",
            "Perfect Fit",
            null,
            "Mostar",
            "Opine bb, 88000 Mostar",
            43.3186m,
            17.8610m,
            "+387 61 748 894",
            "Fitness i wellness centar sa programima snage, funkcionalnog treninga, pilatesa i mobilnosti.",
            "https://images.unsplash.com/photo-1571902943202-507ec2618e8f",
            "admin.perfectfit",
            60m,
            155m,
            ["Traka za trčanje", "Sobni bicikl", "Slobodni utezi", "Smith mašina", "Cable crossover", "Leg press", "Kettlebell set"],
            ["Personalni trening", "Funkcionalni trening", "Pilates", "Mobilnost"],
            [
                new("perfectfittrainer1", "Funkcionalni trening", "Funkcionalni trening 60 min", "Trener funkcionalnog treninga i individualne pripreme.", "perfectfittrainer1.png"),
                new("perfectfittrainer2", "Pilates", "Pilates 60 min", "Trenerica pilatesa, posture i kontroliranog pokreta.", "perfectfittrainer2.png"),
            ],
            SeedGymHours.PerfectFit),
        new(
            "respect",
            "Sportska Akademija Respect",
            "GymLink Sarajevo",
            "Sarajevo",
            "Olimpijska dvorana Juan Antonio Samaranch, 71000 Sarajevo",
            43.8717m,
            18.4085m,
            "+387 61 923 504",
            "Sportska akademija sa individualnim, funkcionalnim, kondicionim i borilačkim treninzima.",
            "https://images.unsplash.com/photo-1517836357463-d25dfeac3438",
            "admin.respect",
            60m,
            160m,
            ["Traka za trčanje", "Sobni bicikl", "Slobodni utezi", "Squat rack", "Cable crossover", "Kettlebell set", "Vreća za boks"],
            ["Personalni trening", "Funkcionalni trening", "Kondicioni trening", "Kickboxing"],
            [
                new("respecttrainer1", "Funkcionalni trening", "Funkcionalni trening 60 min", "Trener funkcionalne pripreme i rada jedan-na-jedan.", "respecttrainer1.png"),
                new("respecttrainer2", "Kickboxing", "Kickboxing 60 min", "Trenerica kondicione pripreme i osnova kickboxinga.", "respecttrainer2.png"),
            ],
            SeedGymHours.Respect),
        new(
            "oxide",
            "Oxide Gym",
            null,
            "Sarajevo",
            "Nikole Šope 13, 71000 Sarajevo",
            43.8427m,
            18.3307m,
            "+387 60 306 7047",
            "Prostran fitness centar sa cjelodnevnim pristupom, spravama za snagu i grupnim treninzima.",
            "https://images.unsplash.com/photo-1581009146145-b5ef050c2e1e",
            "admin.oxide",
            70m,
            180m,
            ["Traka za trčanje", "Sobni bicikl", "Eliptični trenažer", "Slobodni utezi", "Smith mašina", "Squat rack", "Cable crossover", "Leg press", "Kettlebell set"],
            ["Personalni trening", "Funkcionalni trening", "Trening snage", "Kondicioni trening"],
            [
                new("oxidetrainer1", "Trening snage", "Trening snage 60 min", "Trener hipertrofije, snage i pravilne tehnike izvođenja vježbi.", "oxidetrainer1.png"),
                new("oxidetrainer2", "Kondicioni trening", "Kondicioni trening 60 min", "Trenerica izdržljivosti i individualnog fitness programa.", "oxidetrainer2.png"),
            ],
            SeedGymHours.Oxide),
        new(
            "fitfactory",
            "Fit Factory",
            null,
            "Bihać",
            "Reisa Mustafe Omerovića, 77000 Bihać",
            44.8169m,
            15.8708m,
            "+387 60 327 9165",
            "Fitness studio usmjeren na individualni rad, funkcionalni trening, mobilnost i oporavak.",
            "https://images.unsplash.com/photo-1540497077202-7c8a3999166f",
            "admin.fitfactory",
            55m,
            140m,
            ["Traka za trčanje", "Sobni bicikl", "Slobodni utezi", "Cable crossover", "Leg press", "Kettlebell set"],
            ["Personalni trening", "Funkcionalni trening", "Kondicioni trening", "Mobilnost"],
            [
                new("fitfactorytrainer1", "Funkcionalni trening", "Funkcionalni trening 60 min", "Trener funkcionalne pripreme i razvoja opće kondicije.", "fitfactorytrainer1.png"),
                new("fitfactorytrainer2", "Mobilnost", "Mobilnost 60 min", "Trenerica mobilnosti, oporavka i pravilnih obrazaca pokreta.", "fitfactorytrainer2.png"),
            ],
            SeedGymHours.FitFactory),
        new(
            "iskra",
            "Fitness Club Iskra",
            null,
            "Bugojno",
            "Slobode, 70230 Bugojno",
            44.0572m,
            17.4500m,
            "+387 61 457 924",
            "Lokalni fitness klub sa opremom za trening snage, funkcionalni rad i borilačku kondiciju.",
            "https://images.unsplash.com/photo-1534258936925-c58bed479fcb",
            "admin.iskra",
            50m,
            135m,
            ["Traka za trčanje", "Slobodni utezi", "Smith mašina", "Squat rack", "Kettlebell set", "Vreća za boks"],
            ["Personalni trening", "Funkcionalni trening", "Trening snage", "Kickboxing"],
            [
                new("iskratrainer1", "Trening snage", "Trening snage 60 min", "Trener razvoja snage i individualne sportske pripreme.", "iskratrainer1.png"),
                new("iskratrainer2", "Kickboxing", "Kickboxing 60 min", "Trenerica kondicije i tehničkih osnova kickboxinga.", "iskratrainer2.png"),
            ],
            SeedGymHours.Iskra),
    ];

    public static readonly SeedMembership[] Memberships =
    [
        new("mobile1", "arena"), new("mobile2", "arena"),
        new("mobile3", "perfectfit"), new("mobile4", "perfectfit"),
        new("mobile1", "respect"), new("mobile3", "respect"),
        new("mobile2", "oxide"), new("mobile4", "oxide"),
        new("mobile1", "fitfactory"), new("mobile4", "fitfactory"),
        new("mobile2", "iskra"), new("mobile3", "iskra"),
    ];

    public static readonly SeedPreference[] Preferences =
    [
        new("mobile1", "Mostar", "Personalni trening", 1.0000m),
        new("mobile1", "Bihać", "Funkcionalni trening", 0.7000m),
        new("mobile2", "Sarajevo", "Trening snage", 1.0000m),
        new("mobile2", "Mostar", "Kickboxing", 0.6000m),
        new("mobile3", "Sarajevo", "Funkcionalni trening", 1.0000m),
        new("mobile3", "Bugojno", "Trening snage", 0.7000m),
        new("mobile4", "Bihać", "Mobilnost", 1.0000m),
        new("mobile4", "Mostar", "Pilates", 0.7000m),
    ];
}

internal sealed record SeedAccount(
    string Username,
    string? LegacyUsername,
    string DisplayName,
    string Role,
    string PhoneNumber);

internal sealed record SeedEquipment(string Name);

internal sealed record SeedTrainingType(string Name, string Description);

internal sealed record SeedTrainer(
    string Username,
    string SpecialtyTrainingType,
    string SpecialtyOfferingName,
    string Biography,
    string ImageAssetName);

internal sealed record SeedGym(
    string Slug,
    string Name,
    string? LegacyName,
    string City,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string PhoneNumber,
    string Description,
    string ImageUrl,
    string AdminUsername,
    decimal MonthlyPrice,
    decimal QuarterlyPrice,
    string[] Equipment,
    string[] TrainingTypes,
    SeedTrainer[] Trainers,
    IReadOnlyDictionary<DayOfWeek, SeedHours> Hours);

internal sealed record SeedMembership(string MemberUsername, string GymSlug);

internal sealed record SeedPreference(
    string MemberUsername,
    string City,
    string TrainingType,
    decimal Weight);

internal sealed record SeedHours(TimeOnly? OpensAt, TimeOnly? ClosesAt)
{
    public bool IsClosed => OpensAt is null || ClosesAt is null;
}

internal static class SeedGymHours
{
    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> Arena = Week(
        new(6, 0), new(23, 0), new(6, 0), new(23, 0), closedSunday: true);

    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> PerfectFit = Week(
        new(8, 0), new(22, 0), null, null, new(15, 0), new(21, 0));

    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> Respect = Week(
        new(8, 0), new(22, 0), new(9, 0), new(21, 0), new(10, 0), new(20, 0));

    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> Oxide = Week(
        new(0, 0), new(23, 59), new(0, 0), new(23, 59), new(0, 0), new(23, 59));

    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> FitFactory = Week(
        new(7, 0), new(22, 0), new(8, 0), new(22, 0), new(9, 0), new(22, 0));

    public static readonly IReadOnlyDictionary<DayOfWeek, SeedHours> Iskra = Week(
        new(7, 0), new(22, 0), new(8, 0), new(20, 0), closedSunday: true);

    private static Dictionary<DayOfWeek, SeedHours> Week(
        TimeOnly weekdayOpen,
        TimeOnly weekdayClose,
        TimeOnly? saturdayOpen,
        TimeOnly? saturdayClose,
        TimeOnly? sundayOpen = null,
        TimeOnly? sundayClose = null,
        bool closedSunday = false)
    {
        var values = new Dictionary<DayOfWeek, SeedHours>();
        for (var day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
        {
            values[day] = new SeedHours(weekdayOpen, weekdayClose);
        }

        values[DayOfWeek.Saturday] = new SeedHours(saturdayOpen, saturdayClose);
        values[DayOfWeek.Sunday] = closedSunday
            ? new SeedHours(null, null)
            : new SeedHours(sundayOpen, sundayClose);
        return values;
    }
}
