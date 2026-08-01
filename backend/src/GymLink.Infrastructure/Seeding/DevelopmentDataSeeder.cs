using GymLink.Application.Abstractions;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Recommendations;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using GymLink.Infrastructure.Identity;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Seeding;

internal sealed class DevelopmentDataSeeder(
    GymLinkDbContext dbContext,
    UserManager<GymLinkIdentityUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ITenantMutationScope tenantMutationScope,
    IOptions<DevelopmentSeedOptions> options)
{
    private static readonly DateTime MembershipRequestedAtUtc = Utc(2026, 7, 10, 10);
    private static readonly DateTime MembershipActivatedAtUtc = Utc(2026, 7, 15, 10);
    private static readonly DateTime ReservationConfirmedAtUtc = Utc(2026, 7, 20, 10);

    private static readonly DateOnly[] CompletedDates =
    [
        new(2026, 8, 3), new(2026, 8, 4), new(2026, 8, 5),
        new(2026, 8, 6), new(2026, 8, 7), new(2026, 8, 10),
        new(2026, 8, 11), new(2026, 8, 12), new(2026, 8, 13),
        new(2026, 8, 14),
    ];

    private static readonly DateOnly[] UpcomingDates =
    [
        new(2026, 8, 24), new(2026, 8, 25), new(2026, 8, 26),
        new(2026, 8, 27), new(2026, 8, 28), new(2026, 8, 31),
        new(2026, 9, 1), new(2026, 9, 2), new(2026, 9, 3),
        new(2026, 9, 4), new(2026, 9, 7), new(2026, 9, 8),
        new(2026, 9, 9), new(2026, 9, 10), new(2026, 9, 11),
        new(2026, 9, 14), new(2026, 9, 15), new(2026, 9, 16),
        new(2026, 9, 17), new(2026, 9, 18), new(2026, 9, 21),
        new(2026, 9, 22), new(2026, 9, 23), new(2026, 9, 25),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await EnsureRolesAsync();

        var users = new Dictionary<string, GymLinkIdentityUser>(StringComparer.Ordinal);
        foreach (var account in DevelopmentSeedCatalog.Accounts)
        {
            users[account.Username] = await EnsureAccountAsync(
                account,
                options.Value.DefaultPassword,
                cancellationToken);
        }

        var country = await EnsureCountryAsync(cancellationToken);
        var cities = new Dictionary<string, City>(StringComparer.Ordinal);
        foreach (var name in DevelopmentSeedCatalog.Gyms.Select(x => x.City).Distinct())
        {
            cities[name] = await EnsureCityAsync(country.Id, name, cancellationToken);
        }

        var equipment = new Dictionary<string, Equipment>(StringComparer.Ordinal);
        foreach (var definition in DevelopmentSeedCatalog.Equipment)
        {
            equipment[definition.Name] = await EnsureEquipmentAsync(
                definition.Name,
                cancellationToken);
        }

        var trainingTypes = new Dictionary<string, TrainingType>(StringComparer.Ordinal);
        foreach (var definition in DevelopmentSeedCatalog.TrainingTypes)
        {
            trainingTypes[definition.Name] = await EnsureTrainingTypeAsync(
                definition,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var tenants = new Dictionary<string, Tenant>(StringComparer.Ordinal);
        foreach (var definition in DevelopmentSeedCatalog.Gyms)
        {
            tenants[definition.Slug] = await EnsureTenantAsync(definition, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        using var tenantWrite = tenantMutationScope.Begin(tenants.Values.Select(x => x.Id).ToArray());
        var gyms = new Dictionary<string, SeededGym>(StringComparer.Ordinal);
        foreach (var definition in DevelopmentSeedCatalog.Gyms)
        {
            var tenant = tenants[definition.Slug];
            var gym = await EnsureGymAsync(
                definition,
                tenant.Id,
                cities[definition.City].Id,
                cancellationToken);
            var admin = users[definition.AdminUsername];
            await EnsureAssignmentAsync(
                admin.Id,
                tenant.Id,
                RoleNames.GymAdmin,
                MembershipActivatedAtUtc,
                cancellationToken);
            var quarterlyPlan = await EnsureGymCatalogAsync(
                definition,
                gym,
                equipment,
                trainingTypes,
                cancellationToken);

            var trainers = new List<SeededTrainer>();
            foreach (var trainerDefinition in definition.Trainers)
            {
                var trainerUser = users[trainerDefinition.Username];
                await EnsureAssignmentAsync(
                    trainerUser.Id,
                    tenant.Id,
                    RoleNames.Trainer,
                    MembershipActivatedAtUtc,
                    cancellationToken);
                trainers.Add(await EnsureTrainerAsync(
                    trainerDefinition,
                    trainerUser,
                    tenant.Id,
                    trainingTypes,
                    cancellationToken));
            }

            gyms[definition.Slug] = new SeededGym(
                definition,
                tenant,
                gym,
                admin,
                quarterlyPlan,
                trainers);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var memberships = new Dictionary<(string Member, string Gym), SeededMembership>();
        foreach (var definition in DevelopmentSeedCatalog.Memberships)
        {
            var seededGym = gyms[definition.GymSlug];
            var member = users[definition.MemberUsername];
            memberships[(definition.MemberUsername, definition.GymSlug)] =
                await EnsureMembershipAsync(
                    member,
                    seededGym,
                    cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var random = new Random(230038);
        var reservations = new List<SeededReservation>();
        for (var gymIndex = 0; gymIndex < DevelopmentSeedCatalog.Gyms.Length; gymIndex++)
        {
            var gymDefinition = DevelopmentSeedCatalog.Gyms[gymIndex];
            var seededGym = gyms[gymDefinition.Slug];
            var memberDefinitions = DevelopmentSeedCatalog.Memberships
                .Where(x => x.GymSlug == gymDefinition.Slug)
                .ToArray();

            for (var trainerIndex = 0; trainerIndex < seededGym.Trainers.Count; trainerIndex++)
            {
                var trainer = seededGym.Trainers[trainerIndex];
                for (var memberIndex = 0; memberIndex < memberDefinitions.Length; memberIndex++)
                {
                    var membershipDefinition = memberDefinitions[memberIndex];
                    var membership = memberships[(
                        membershipDefinition.MemberUsername,
                        membershipDefinition.GymSlug)];
                    var completedDate = CompletedDates[(gymIndex * 2 + memberIndex) % CompletedDates.Length];
                    var completedStart = SarajevoUtc(
                        completedDate,
                        new TimeOnly(trainerIndex == 0 ? 9 : 11, 0));
                    reservations.Add(await EnsureReservationAsync(
                        membership,
                        trainer,
                        completedStart,
                        completed: true,
                        random.Next(3, 6),
                        cancellationToken));

                    var upcomingIndex = (gymIndex * 4) + (trainerIndex * 2) + memberIndex;
                    var upcomingStart = SarajevoUtc(
                        UpcomingDates[upcomingIndex],
                        new TimeOnly(trainerIndex == 0 ? 16 : 18, 0));
                    reservations.Add(await EnsureReservationAsync(
                        membership,
                        trainer,
                        upcomingStart,
                        completed: false,
                        rating: null,
                        cancellationToken));
                }
            }

            foreach (var memberDefinition in memberDefinitions)
            {
                await EnsureGymReviewAsync(
                    memberships[(memberDefinition.MemberUsername, memberDefinition.GymSlug)],
                    random.Next(3, 6),
                    cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var preferences = await EnsurePreferencesAsync(
            users,
            cities,
            trainingTypes,
            cancellationToken);
        await EnsureActivityHistoryAsync(
            users,
            gyms,
            memberships.Values,
            reservations,
            preferences,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Ensure(await roleManager.CreateAsync(new IdentityRole<Guid>(role)));
            }
        }
    }

    private async Task<GymLinkIdentityUser> EnsureAccountAsync(
        SeedAccount account,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(account.Username);
        var renamed = false;
        if (user is null && account.LegacyUsername is not null)
        {
            user = await userManager.FindByNameAsync(account.LegacyUsername);
            renamed = user is not null;
        }

        if (user is null)
        {
            user = new GymLinkIdentityUser
            {
                UserName = account.Username,
                Email = $"{account.Username}@gymlink.local",
                EmailConfirmed = true,
            };
            Ensure(await userManager.CreateAsync(user, password));
        }
        else
        {
            if (!string.Equals(user.UserName, account.Username, StringComparison.Ordinal))
            {
                Ensure(await userManager.SetUserNameAsync(user, account.Username));
                renamed = true;
            }

            var email = $"{account.Username}@gymlink.local";
            if (!string.Equals(user.Email, email, StringComparison.Ordinal))
            {
                Ensure(await userManager.SetEmailAsync(user, email));
                user.EmailConfirmed = true;
                Ensure(await userManager.UpdateAsync(user));
            }

            if (!await userManager.CheckPasswordAsync(user, password))
            {
                if (await userManager.HasPasswordAsync(user))
                {
                    Ensure(await userManager.RemovePasswordAsync(user));
                }

                Ensure(await userManager.AddPasswordAsync(user, password));
            }
        }

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(
            x => x.Id == user.Id,
            cancellationToken);
        if (profile is null)
        {
            profile = new UserProfile(user.Id, account.DisplayName);
            dbContext.UserProfiles.Add(profile);
        }

        profile.DisplayName = account.DisplayName;
        profile.PhoneNumber = account.PhoneNumber;
        profile.IsActive = true;
        if (renamed)
        {
            profile.TokenVersion++;
            var now = DateTime.UtcNow;
            var sessions = await dbContext.RefreshTokenSessions
                .Where(x => x.UserId == user.Id && x.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = now;
                session.RevocationReason = "Development seed account renamed.";
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || !string.Equals(roles[0], account.Role, StringComparison.Ordinal))
        {
            if (roles.Count > 0)
            {
                Ensure(await userManager.RemoveFromRolesAsync(user, roles));
            }

            Ensure(await userManager.AddToRoleAsync(user, account.Role));
        }

        return user;
    }

    private async Task<Country> EnsureCountryAsync(CancellationToken cancellationToken)
    {
        var country = await dbContext.Countries.SingleOrDefaultAsync(
            x => x.Code == "BIH",
            cancellationToken);
        if (country is not null)
        {
            country.Name = "Bosna i Hercegovina";
            country.IsActive = true;
            return country;
        }

        country = new Country { Code = "BIH", Name = "Bosna i Hercegovina" };
        dbContext.Countries.Add(country);
        return country;
    }

    private async Task<City> EnsureCityAsync(
        Guid countryId,
        string name,
        CancellationToken cancellationToken)
    {
        var city = await dbContext.Cities.SingleOrDefaultAsync(
            x => x.CountryId == countryId && x.Name == name,
            cancellationToken);
        if (city is not null)
        {
            city.IsActive = true;
            return city;
        }

        city = new City { CountryId = countryId, Name = name };
        dbContext.Cities.Add(city);
        return city;
    }

    private async Task<Equipment> EnsureEquipmentAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Equipment.SingleOrDefaultAsync(
            x => x.Name == name,
            cancellationToken);
        if (entity is not null)
        {
            entity.IsActive = true;
            return entity;
        }

        entity = new Equipment { Name = name };
        dbContext.Equipment.Add(entity);
        return entity;
    }

    private async Task<TrainingType> EnsureTrainingTypeAsync(
        SeedTrainingType definition,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TrainingTypes.SingleOrDefaultAsync(
            x => x.Name == definition.Name,
            cancellationToken);
        if (entity is null)
        {
            entity = new TrainingType { Name = definition.Name };
            dbContext.TrainingTypes.Add(entity);
        }

        entity.Description = definition.Description;
        entity.IsActive = true;
        return entity;
    }

    private async Task<Tenant> EnsureTenantAsync(
        SeedGym definition,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
            x => x.Name == definition.Name,
            cancellationToken);
        if (tenant is null && definition.LegacyName is not null)
        {
            tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                x => x.Name == definition.LegacyName,
                cancellationToken);
        }

        if (tenant is null)
        {
            tenant = new Tenant(Guid.NewGuid(), definition.Name);
            dbContext.Tenants.Add(tenant);
        }

        tenant.Name = definition.Name;
        tenant.Status = TenantStatus.Active;
        return tenant;
    }

    private async Task<Gym> EnsureGymAsync(
        SeedGym definition,
        Guid tenantId,
        Guid cityId,
        CancellationToken cancellationToken)
    {
        var gym = await dbContext.Gyms.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (gym is null)
        {
            gym = new Gym { TenantId = tenantId };
            dbContext.Gyms.Add(gym);
        }

        gym.Name = definition.Name;
        gym.Description = definition.Description;
        gym.Address = definition.Address;
        gym.CityId = cityId;
        gym.Latitude = definition.Latitude;
        gym.Longitude = definition.Longitude;
        gym.PhoneNumber = definition.PhoneNumber;
        gym.IsPubliclyVisible = true;

        var image = await dbContext.GymImages.IgnoreQueryFilters()
            .Where(x => x.GymId == gym.Id && x.StorageKey.StartsWith("seed/"))
            .OrderBy(x => x.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (image is null && !await dbContext.GymImages.IgnoreQueryFilters()
                .AnyAsync(x => x.GymId == gym.Id, cancellationToken))
        {
            image = new GymImage
            {
                TenantId = tenantId,
                GymId = gym.Id,
                StorageKey = $"seed/{definition.Slug}/primary",
                SortOrder = 0,
                IsPrimary = true,
            };
            dbContext.GymImages.Add(image);
        }

        if (image is not null)
        {
            image.PublicUrl = definition.ImageUrl;
            image.AltText = definition.Name;
            image.SortOrder = 0;
            image.IsPrimary = true;
        }

        return gym;
    }

    private async Task EnsureAssignmentAsync(
        Guid userId,
        Guid tenantId,
        string role,
        DateTime startsAtUtc,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.TenantId == tenantId && x.Role == role,
                cancellationToken);
        if (assignment is null)
        {
            assignment = new UserGymAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                Role = role,
                Reason = "Development evaluation seed",
            };
            dbContext.UserGymAssignments.Add(assignment);
        }

        assignment.Status = AssignmentStatus.Active;
        assignment.StartsAtUtc = startsAtUtc;
        assignment.EndsAtUtc = null;
    }

    private async Task<MembershipPlan> EnsureGymCatalogAsync(
        SeedGym definition,
        Gym gym,
        Dictionary<string, Equipment> equipment,
        Dictionary<string, TrainingType> trainingTypes,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.Equipment.Length; index++)
        {
            var equipmentEntity = equipment[definition.Equipment[index]];
            var join = await dbContext.GymEquipment.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.GymId == gym.Id && x.EquipmentId == equipmentEntity.Id,
                    cancellationToken);
            if (join is null)
            {
                join = new GymEquipment
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    EquipmentId = equipmentEntity.Id,
                };
                dbContext.GymEquipment.Add(join);
            }

            join.Quantity = 2 + ((index + definition.Slug.Length) % 7);
            join.Notes = "Oprema dostupna članovima u razvojnom seed skupu.";
        }

        foreach (var name in definition.TrainingTypes)
        {
            var trainingType = trainingTypes[name];
            if (!await dbContext.GymTrainingTypes.IgnoreQueryFilters().AnyAsync(
                    x => x.GymId == gym.Id && x.TrainingTypeId == trainingType.Id,
                    cancellationToken))
            {
                dbContext.GymTrainingTypes.Add(new GymTrainingType
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    TrainingTypeId = trainingType.Id,
                });
            }
        }

        foreach (var (day, hours) in definition.Hours)
        {
            var entity = await dbContext.GymWorkingHours.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.GymId == gym.Id && x.DayOfWeek == day,
                    cancellationToken);
            if (entity is null)
            {
                entity = new GymWorkingHours
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    DayOfWeek = day,
                };
                dbContext.GymWorkingHours.Add(entity);
            }

            entity.OpensAt = hours.OpensAt;
            entity.ClosesAt = hours.ClosesAt;
            entity.IsClosed = hours.IsClosed;
        }

        await EnsureMembershipPlanAsync(
            gym,
            "Mjesečna članarina",
            30,
            definition.MonthlyPrice,
            cancellationToken);
        return await EnsureMembershipPlanAsync(
            gym,
            "Tromjesečna članarina",
            90,
            definition.QuarterlyPrice,
            cancellationToken);
    }

    private async Task<MembershipPlan> EnsureMembershipPlanAsync(
        Gym gym,
        string name,
        int durationDays,
        decimal price,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.MembershipPlans.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.GymId == gym.Id && x.Name == name,
                cancellationToken);
        if (plan is null)
        {
            plan = new MembershipPlan
            {
                TenantId = gym.TenantId,
                GymId = gym.Id,
                Name = name,
            };
            dbContext.MembershipPlans.Add(plan);
        }

        plan.DurationDays = durationDays;
        plan.Price = price;
        plan.Currency = "BAM";
        plan.IsActive = true;
        return plan;
    }

    private async Task<SeededTrainer> EnsureTrainerAsync(
        SeedTrainer definition,
        GymLinkIdentityUser user,
        Guid tenantId,
        Dictionary<string, TrainingType> trainingTypes,
        CancellationToken cancellationToken)
    {
        var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == user.Id && x.TenantId == tenantId,
                cancellationToken);
        if (trainer is null)
        {
            trainer = new TrainerProfile { TenantId = tenantId, UserId = user.Id };
            dbContext.TrainerProfiles.Add(trainer);
        }

        trainer.Biography = definition.Biography;
        trainer.Credentials = "GymLink demo certifikat za stručno vođenje treninga";
        trainer.IsActive = true;

        var personal = trainingTypes["Personalni trening"];
        var specialty = trainingTypes[definition.SpecialtyTrainingType];
        foreach (var trainingType in new[] { personal, specialty })
        {
            if (!await dbContext.TrainerTrainingTypes.IgnoreQueryFilters().AnyAsync(
                    x => x.TrainerProfileId == trainer.Id &&
                        x.TrainingTypeId == trainingType.Id,
                    cancellationToken))
            {
                dbContext.TrainerTrainingTypes.Add(new TrainerTrainingType
                {
                    TenantId = tenantId,
                    TrainerProfileId = trainer.Id,
                    TrainingTypeId = trainingType.Id,
                });
            }
        }

        var personal60 = await FindOrCreateOfferingAsync(
            trainer,
            personal,
            "Personalni trening 60 min",
            60,
            35m,
            "Individualni trening",
            cancellationToken);
        await FindOrCreateOfferingAsync(
            trainer,
            personal,
            "Personalni trening 90 min",
            90,
            50m,
            legacyName: null,
            cancellationToken);
        await FindOrCreateOfferingAsync(
            trainer,
            specialty,
            definition.SpecialtyOfferingName,
            60,
            40m,
            legacyName: null,
            cancellationToken);
        await EnsureTrainerScheduleAsync(trainer, cancellationToken);
        return new SeededTrainer(definition, user, trainer, personal60);
    }

    private async Task<TrainerServiceOffering> FindOrCreateOfferingAsync(
        TrainerProfile trainer,
        TrainingType trainingType,
        string name,
        int durationMinutes,
        decimal price,
        string? legacyName,
        CancellationToken cancellationToken)
    {
        var offering = await dbContext.TrainerServiceOfferings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TrainerProfileId == trainer.Id && x.Name == name,
                cancellationToken);
        if (offering is null && legacyName is not null)
        {
            offering = await dbContext.TrainerServiceOfferings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.TrainerProfileId == trainer.Id && x.Name == legacyName,
                    cancellationToken);
        }

        if (offering is null)
        {
            offering = new TrainerServiceOffering(
                trainer.TenantId,
                trainer.Id,
                trainingType.Id,
                name,
                durationMinutes,
                price,
                "BAM");
            dbContext.TrainerServiceOfferings.Add(offering);
        }
        else
        {
            offering.UpdateDetails(
                trainingType.Id,
                name,
                durationMinutes,
                price,
                "BAM",
                isActive: true);
        }

        return offering;
    }

    private async Task EnsureTrainerScheduleAsync(
        TrainerProfile trainer,
        CancellationToken cancellationToken)
    {
        var schedule = await dbContext.TrainerAvailabilitySchedules.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TrainerProfileId == trainer.Id,
                cancellationToken);
        if (schedule is null)
        {
            schedule = new TrainerAvailabilitySchedule(trainer.TenantId, trainer.Id);
            dbContext.TrainerAvailabilitySchedules.Add(schedule);
        }

        for (var day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
        {
            foreach (var period in new[] { TrainerShiftPeriod.Morning, TrainerShiftPeriod.Evening })
            {
                var shift = await dbContext.TrainerWeeklyShifts.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        x => x.TrainerProfileId == trainer.Id &&
                            x.DayOfWeek == day &&
                            x.Period == period,
                        cancellationToken);
                if (shift is null)
                {
                    shift = new TrainerWeeklyShift(
                        trainer.TenantId,
                        schedule.Id,
                        trainer.Id,
                        day,
                        period);
                    dbContext.TrainerWeeklyShifts.Add(shift);
                }

                shift.Activate();
            }
        }
    }

    private async Task<SeededMembership> EnsureMembershipAsync(
        GymLinkIdentityUser member,
        SeededGym seededGym,
        CancellationToken cancellationToken)
    {
        var membership = await dbContext.Memberships.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.MemberUserId == member.Id && x.GymId == seededGym.Gym.Id &&
                    (x.Status == MembershipStatus.Active ||
                     x.Status == MembershipStatus.Suspended ||
                     x.Status == MembershipStatus.PendingPayment),
                cancellationToken);
        MembershipRequest request;
        if (membership is null)
        {
            request = new MembershipRequest
            {
                TenantId = seededGym.Tenant.Id,
                MemberUserId = member.Id,
                GymId = seededGym.Gym.Id,
                MembershipPlanId = seededGym.QuarterlyPlan.Id,
                RequestedAtUtc = MembershipRequestedAtUtc,
            };
            request.Approve(seededGym.Admin.Id, MembershipActivatedAtUtc);
            dbContext.MembershipRequests.Add(request);
            membership = new Membership(
                seededGym.Tenant.Id,
                member.Id,
                seededGym.Gym.Id,
                seededGym.QuarterlyPlan.Id,
                request.Id,
                seededGym.QuarterlyPlan.Name,
                seededGym.QuarterlyPlan.DurationDays,
                seededGym.QuarterlyPlan.Price,
                seededGym.QuarterlyPlan.Currency,
                seededGym.Admin.Id,
                MembershipActivatedAtUtc);
            dbContext.Memberships.Add(membership);
        }
        else
        {
            request = await dbContext.MembershipRequests.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == membership.MembershipRequestId, cancellationToken);
        }

        await EnsureAssignmentAsync(
            member.Id,
            seededGym.Tenant.Id,
            RoleNames.Member,
            MembershipActivatedAtUtc,
            cancellationToken);
        return new SeededMembership(member, seededGym, request, membership);
    }

    private async Task<SeededReservation> EnsureReservationAsync(
        SeededMembership seededMembership,
        SeededTrainer trainer,
        DateTime startsAtUtc,
        bool completed,
        int? rating,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.MemberUserId == seededMembership.Member.Id &&
                    x.TrainerServiceOfferingId == trainer.Personal60.Id &&
                    x.StartsAtUtc == startsAtUtc,
                cancellationToken);
        if (reservation is null)
        {
            reservation = new AppointmentReservation(
                seededMembership.Gym.Tenant.Id,
                seededMembership.Member.Id,
                trainer.Profile.Id,
                trainer.Personal60.Id,
                availabilitySlotId: null,
                seededMembership.Membership.Id,
                startsAtUtc,
                trainer.Personal60.DurationMinutes,
                trainer.Personal60.Price,
                trainer.Personal60.Currency);
            reservation.ConfirmForPayInPerson(
                seededMembership.Member.Id,
                ReservationConfirmedAtUtc);
            if (completed)
            {
                reservation.Complete(trainer.User.Id, reservation.EndsAtUtc.AddMinutes(10));
            }

            dbContext.AppointmentReservations.Add(reservation);
        }
        else if (completed && reservation.Status == ReservationStatus.Confirmed)
        {
            reservation.Complete(trainer.User.Id, reservation.EndsAtUtc.AddMinutes(10));
        }

        Review? review = null;
        if (completed && rating.HasValue)
        {
            review = await dbContext.Reviews.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ReservationId == reservation.Id, cancellationToken);
            if (review is null)
            {
                review = new Review(
                    seededMembership.Gym.Tenant.Id,
                    reservation.Id,
                    seededMembership.Member.Id,
                    trainer.Profile.Id,
                    rating.Value,
                    "Demo ocjena nakon završenog treninga.");
                dbContext.Reviews.Add(review);
                trainer.Profile.AddReview(rating.Value);
            }
        }

        return new SeededReservation(seededMembership, trainer, reservation, review);
    }

    private async Task EnsureGymReviewAsync(
        SeededMembership seededMembership,
        int rating,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GymReviews.IgnoreQueryFilters().AnyAsync(
                x => x.GymId == seededMembership.Gym.Gym.Id &&
                    x.ReviewerUserId == seededMembership.Member.Id,
                cancellationToken))
        {
            return;
        }

        dbContext.GymReviews.Add(new GymReview(
            seededMembership.Gym.Tenant.Id,
            seededMembership.Gym.Gym.Id,
            seededMembership.Member.Id,
            rating,
            "Demo ocjena teretane za evaluacijski skup podataka."));
        seededMembership.Gym.Gym.AddReview(rating);
    }

    private async Task<IReadOnlyList<SeededPreference>> EnsurePreferencesAsync(
        Dictionary<string, GymLinkIdentityUser> users,
        Dictionary<string, City> cities,
        Dictionary<string, TrainingType> trainingTypes,
        CancellationToken cancellationToken)
    {
        var result = new List<SeededPreference>();
        foreach (var definition in DevelopmentSeedCatalog.Preferences)
        {
            var user = users[definition.MemberUsername];
            var city = cities[definition.City];
            var trainingType = trainingTypes[definition.TrainingType];
            var preference = await dbContext.UserPreferences.SingleOrDefaultAsync(
                x => x.UserId == user.Id &&
                    x.PreferredCityId == city.Id &&
                    x.PreferredTrainingTypeId == trainingType.Id,
                cancellationToken);
            if (preference is null)
            {
                preference = new UserPreference
                {
                    UserId = user.Id,
                    PreferredCityId = city.Id,
                    PreferredTrainingTypeId = trainingType.Id,
                };
                dbContext.UserPreferences.Add(preference);
            }

            preference.Weight = definition.Weight;
            result.Add(new SeededPreference(definition, user, preference));
        }

        return result;
    }

    private async Task EnsureActivityHistoryAsync(
        Dictionary<string, GymLinkIdentityUser> users,
        IReadOnlyDictionary<string, SeededGym> gyms,
        IEnumerable<SeededMembership> memberships,
        IEnumerable<SeededReservation> reservations,
        IReadOnlyList<SeededPreference> preferences,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ActivityHistory.ToListAsync(cancellationToken);
        for (var memberIndex = 0; memberIndex < 4; memberIndex++)
        {
            var user = users[$"mobile{memberIndex + 1}"];
            EnsureActivity(existing, user.Id, null, null, null, ActivityEventType.Search,
                Utc(2026, 7, 1, 8, memberIndex));
            EnsureActivity(existing, user.Id, null, null, null, ActivityEventType.Filter,
                Utc(2026, 7, 1, 9, memberIndex));
        }

        for (var index = 0; index < preferences.Count; index++)
        {
            var preference = preferences[index];
            EnsureActivity(
                existing,
                preference.User.Id,
                null,
                null,
                null,
                ActivityEventType.PreferredTrainingTypeChange,
                Utc(2026, 7, 2, 10, index));
        }

        foreach (var membership in memberships)
        {
            var userId = membership.Member.Id;
            var gym = membership.Gym;
            EnsureActivity(existing, userId, gym.Tenant.Id, RecommendationTargetType.Gym,
                gym.Gym.Id, ActivityEventType.GymView, Utc(2026, 7, 5, 10));
            foreach (var trainer in gym.Trainers)
            {
                EnsureActivity(existing, userId, gym.Tenant.Id, RecommendationTargetType.Trainer,
                    trainer.Profile.Id, ActivityEventType.TrainerView, Utc(2026, 7, 6, 10));
            }

            EnsureActivity(existing, userId, gym.Tenant.Id, RecommendationTargetType.Gym,
                gym.Gym.Id, ActivityEventType.MembershipRequest, MembershipRequestedAtUtc);
            EnsureActivity(existing, userId, gym.Tenant.Id, RecommendationTargetType.Gym,
                gym.Gym.Id, ActivityEventType.MembershipActivation, MembershipActivatedAtUtc);
            EnsureActivity(existing, userId, gym.Tenant.Id, RecommendationTargetType.Gym,
                gym.Gym.Id, ActivityEventType.ReviewCreation, Utc(2026, 8, 15, 12));
        }

        foreach (var seededReservation in reservations)
        {
            var reservation = seededReservation.Reservation;
            var tenantId = seededReservation.Membership.Gym.Tenant.Id;
            var trainerId = seededReservation.Trainer.Profile.Id;
            EnsureActivity(existing, reservation.MemberUserId, tenantId,
                RecommendationTargetType.Trainer, trainerId,
                ActivityEventType.ReservationCreation, reservation.StartsAtUtc.AddDays(-14));
            if (reservation.Status == ReservationStatus.Completed)
            {
                EnsureActivity(existing, reservation.MemberUserId, tenantId,
                    RecommendationTargetType.Trainer, trainerId,
                    ActivityEventType.ReservationCompletion,
                    reservation.CompletedAtUtc!.Value);
                EnsureActivity(existing, reservation.MemberUserId, tenantId,
                    RecommendationTargetType.Trainer, trainerId,
                    ActivityEventType.ReviewCreation,
                    reservation.CompletedAtUtc.Value.AddMinutes(5));
            }
        }
    }

    private void EnsureActivity(
        List<ActivityHistory> existing,
        Guid userId,
        Guid? tenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        ActivityEventType eventType,
        DateTime occurredAtUtc)
    {
        if (existing.Any(x => x.UserId == userId &&
                x.TargetTenantId == tenantId &&
                x.TargetType == targetType &&
                x.TargetId == targetId &&
                x.EventType == eventType &&
                x.OccurredAtUtc == occurredAtUtc))
        {
            return;
        }

        var activity = new ActivityHistory
        {
            UserId = userId,
            TargetTenantId = tenantId,
            TargetType = targetType,
            TargetId = targetId,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc,
            MetadataVersion = 1,
        };
        dbContext.ActivityHistory.Add(activity);
        existing.Add(activity);
    }

    private static DateTime SarajevoUtc(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            TrainerAvailabilitySchedule.SarajevoTimeZoneId);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Development seeding failed: {string.Join(" ", result.Errors.Select(x => x.Description))}");
        }
    }

    private sealed record SeededGym(
        SeedGym Definition,
        Tenant Tenant,
        Gym Gym,
        GymLinkIdentityUser Admin,
        MembershipPlan QuarterlyPlan,
        IReadOnlyList<SeededTrainer> Trainers);

    private sealed record SeededTrainer(
        SeedTrainer Definition,
        GymLinkIdentityUser User,
        TrainerProfile Profile,
        TrainerServiceOffering Personal60);

    private sealed record SeededMembership(
        GymLinkIdentityUser Member,
        SeededGym Gym,
        MembershipRequest Request,
        Membership Membership);

    private sealed record SeededReservation(
        SeededMembership Membership,
        SeededTrainer Trainer,
        AppointmentReservation Reservation,
        Review? Review);

    private sealed record SeededPreference(
        SeedPreference Definition,
        GymLinkIdentityUser User,
        UserPreference Preference);
}
