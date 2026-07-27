using GymLink.Application.Abstractions;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.ReferenceData;
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
    private static readonly SeedAccount[] Accounts =
    [
        new("desktop", "Desktop Gym Administrator", RoleNames.GymAdmin),
        new("mobile", "Mobile Test Member", RoleNames.Member),
        new("centraladmin", "Central Administrator", RoleNames.CentralAdmin),
        new("gymadmin", "Mostar Gym Administrator", RoleNames.GymAdmin),
        new("trainer", "Sarajevo Trainer", RoleNames.Trainer),
        new("member", "Role Test Member", RoleNames.Member),
        new("trainer2", "Mostar Trainer", RoleNames.Trainer),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Ensure(await roleManager.CreateAsync(new IdentityRole<Guid>(role)));
            }
        }

        var users = new Dictionary<string, GymLinkIdentityUser>(StringComparer.Ordinal);
        foreach (var account in Accounts)
        {
            users[account.Username] =
                await EnsureAccountAsync(account, options.Value.DefaultPassword, cancellationToken);
        }

        var country = await dbContext.Countries.SingleOrDefaultAsync(
            x => x.Code == "BIH",
            cancellationToken);
        if (country is null)
        {
            country = new Country { Code = "BIH", Name = "Bosna i Hercegovina" };
            dbContext.Countries.Add(country);
        }

        var sarajevo = await EnsureCityAsync(country.Id, "Sarajevo", cancellationToken);
        var mostar = await EnsureCityAsync(country.Id, "Mostar", cancellationToken);
        var treadmill = await EnsureEquipmentAsync("Traka za trčanje", cancellationToken);
        var weights = await EnsureEquipmentAsync("Slobodni utezi", cancellationToken);
        var functional = await EnsureTrainingTypeAsync(
            "Funkcionalni trening",
            "Trening snage, mobilnosti i kondicije.",
            cancellationToken);
        var personal = await EnsureTrainingTypeAsync(
            "Personalni trening",
            "Individualni rad sa trenerom.",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var sarajevoTenant = await EnsureTenantAsync("GymLink Sarajevo", cancellationToken);
        var mostarTenant = await EnsureTenantAsync("GymLink Mostar", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        using var tenantWrite = tenantMutationScope.Begin(sarajevoTenant.Id, mostarTenant.Id);
        var sarajevoGym = await EnsureGymAsync(
            sarajevoTenant.Id,
            "GymLink Sarajevo",
            "Moderan fitness centar u Sarajevu sa grupnim i individualnim treninzima.",
            "Zmaja od Bosne 7",
            sarajevo.Id,
            43.8563m,
            18.4131m,
            "+387 33 555 100",
            "https://images.unsplash.com/photo-1534438327276-14e5300c3a48",
            cancellationToken);
        var mostarGym = await EnsureGymAsync(
            mostarTenant.Id,
            "GymLink Mostar",
            "Fitness centar u Mostaru sa savremenom opremom i stručnim trenerima.",
            "Kneza Mihajla Viševića Humskog 4",
            mostar.Id,
            43.3438m,
            17.8078m,
            "+387 36 555 200",
            "https://images.unsplash.com/photo-1571902943202-507ec2618e8f",
            cancellationToken);

        await EnsureAssignmentAsync(users["desktop"].Id, sarajevoTenant.Id, RoleNames.GymAdmin, cancellationToken);
        await EnsureAssignmentAsync(users["gymadmin"].Id, mostarTenant.Id, RoleNames.GymAdmin, cancellationToken);
        await EnsureAssignmentAsync(users["trainer"].Id, sarajevoTenant.Id, RoleNames.Trainer, cancellationToken);
        await EnsureAssignmentAsync(users["trainer2"].Id, mostarTenant.Id, RoleNames.Trainer, cancellationToken);
        await EnsureGymCatalogAsync(
            sarajevoGym,
            treadmill,
            weights,
            functional,
            personal,
            cancellationToken);
        await EnsureGymCatalogAsync(
            mostarGym,
            treadmill,
            weights,
            functional,
            personal,
            cancellationToken);
        await EnsureTrainerAsync(
            users["trainer"].Id,
            sarajevoTenant.Id,
            functional.Id,
            "Certificirani trener funkcionalnog treninga.",
            cancellationToken);
        await EnsureTrainerAsync(
            users["trainer2"].Id,
            mostarTenant.Id,
            personal.Id,
            "Personalni trener usmjeren na siguran napredak.",
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<GymLinkIdentityUser> EnsureAccountAsync(
        SeedAccount account,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(account.Username);
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
        else if (!await userManager.CheckPasswordAsync(user, password))
        {
            if (await userManager.HasPasswordAsync(user))
            {
                Ensure(await userManager.RemovePasswordAsync(user));
            }

            Ensure(await userManager.AddPasswordAsync(user, password));
        }

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(
            x => x.Id == user.Id,
            cancellationToken);
        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile(user.Id, account.DisplayName));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            profile.DisplayName = account.DisplayName;
            profile.IsActive = true;
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || roles[0] != account.Role)
        {
            if (roles.Count > 0)
            {
                Ensure(await userManager.RemoveFromRolesAsync(user, roles));
            }

            Ensure(await userManager.AddToRoleAsync(user, account.Role));
        }

        return user;
    }

    private async Task<City> EnsureCityAsync(
        Guid countryId,
        string name,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Cities.SingleOrDefaultAsync(
            x => x.CountryId == countryId && x.Name == name,
            cancellationToken);
        if (entity is not null)
        {
            return entity;
        }

        entity = new City { CountryId = countryId, Name = name };
        dbContext.Cities.Add(entity);
        return entity;
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
            return entity;
        }

        entity = new Equipment { Name = name };
        dbContext.Equipment.Add(entity);
        return entity;
    }

    private async Task<TrainingType> EnsureTrainingTypeAsync(
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TrainingTypes.SingleOrDefaultAsync(
            x => x.Name == name,
            cancellationToken);
        if (entity is not null)
        {
            return entity;
        }

        entity = new TrainingType { Name = name, Description = description };
        dbContext.TrainingTypes.Add(entity);
        return entity;
    }

    private async Task<Tenant> EnsureTenantAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
            x => x.Name == name,
            cancellationToken);
        if (tenant is not null)
        {
            tenant.Status = TenantStatus.Active;
            return tenant;
        }

        tenant = new Tenant(Guid.NewGuid(), name) { Status = TenantStatus.Active };
        dbContext.Tenants.Add(tenant);
        return tenant;
    }

    private async Task<Gym> EnsureGymAsync(
        Guid tenantId,
        string name,
        string description,
        string address,
        Guid cityId,
        decimal latitude,
        decimal longitude,
        string phone,
        string imageUrl,
        CancellationToken cancellationToken)
    {
        var gym = await dbContext.Gyms.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (gym is null)
        {
            gym = new Gym { TenantId = tenantId };
            dbContext.Gyms.Add(gym);
        }

        gym.Name = name;
        gym.Description = description;
        gym.Address = address;
        gym.CityId = cityId;
        gym.Latitude = latitude;
        gym.Longitude = longitude;
        gym.PhoneNumber = phone;
        gym.IsPubliclyVisible = true;

        if (!await dbContext.GymImages.IgnoreQueryFilters()
                .AnyAsync(x => x.GymId == gym.Id, cancellationToken))
        {
            dbContext.GymImages.Add(new GymImage
            {
                TenantId = tenantId,
                GymId = gym.Id,
                StorageKey = $"seed/{gym.Id:N}/primary",
                PublicUrl = imageUrl,
                AltText = name,
                SortOrder = 0,
                IsPrimary = true,
            });
        }

        return gym;
    }

    private async Task EnsureAssignmentAsync(
        Guid userId,
        Guid tenantId,
        string role,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.TenantId == tenantId && x.Role == role,
                cancellationToken);
        if (assignment is null)
        {
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                Role = role,
                Status = AssignmentStatus.Active,
                StartsAtUtc = DateTime.UnixEpoch,
                Reason = "Development seed",
            });
        }
        else
        {
            assignment.Status = AssignmentStatus.Active;
            assignment.EndsAtUtc = null;
        }
    }

    private async Task EnsureGymCatalogAsync(
        Gym gym,
        Equipment treadmill,
        Equipment weights,
        TrainingType functional,
        TrainingType personal,
        CancellationToken cancellationToken)
    {
        foreach (var equipment in new[] { treadmill, weights })
        {
            if (!await dbContext.GymEquipment.IgnoreQueryFilters().AnyAsync(
                    x => x.GymId == gym.Id && x.EquipmentId == equipment.Id,
                    cancellationToken))
            {
                dbContext.GymEquipment.Add(new GymEquipment
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    EquipmentId = equipment.Id,
                });
            }
        }

        foreach (var type in new[] { functional, personal })
        {
            if (!await dbContext.GymTrainingTypes.IgnoreQueryFilters().AnyAsync(
                    x => x.GymId == gym.Id && x.TrainingTypeId == type.Id,
                    cancellationToken))
            {
                dbContext.GymTrainingTypes.Add(new GymTrainingType
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    TrainingTypeId = type.Id,
                });
            }
        }

        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            if (!await dbContext.GymWorkingHours.IgnoreQueryFilters().AnyAsync(
                    x => x.GymId == gym.Id && x.DayOfWeek == day,
                    cancellationToken))
            {
                dbContext.GymWorkingHours.Add(new GymWorkingHours
                {
                    TenantId = gym.TenantId,
                    GymId = gym.Id,
                    DayOfWeek = day,
                    OpensAt = day == DayOfWeek.Sunday ? null : new TimeOnly(6, 0),
                    ClosesAt = day == DayOfWeek.Sunday ? null : new TimeOnly(22, 0),
                    IsClosed = day == DayOfWeek.Sunday,
                });
            }
        }

        if (!await dbContext.MembershipPlans.IgnoreQueryFilters()
                .AnyAsync(x => x.GymId == gym.Id && x.Name == "Mjesečna članarina", cancellationToken))
        {
            dbContext.MembershipPlans.Add(new MembershipPlan
            {
                TenantId = gym.TenantId,
                GymId = gym.Id,
                Name = "Mjesečna članarina",
                DurationDays = 30,
                Price = 50m,
                Currency = "BAM",
            });
        }
    }

    private async Task EnsureTrainerAsync(
        Guid userId,
        Guid tenantId,
        Guid trainingTypeId,
        string biography,
        CancellationToken cancellationToken)
    {
        var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.TenantId == tenantId,
                cancellationToken);
        if (trainer is null)
        {
            trainer = new TrainerProfile
            {
                TenantId = tenantId,
                UserId = userId,
                Biography = biography,
                Credentials = "GymLink development certificate",
            };
            dbContext.TrainerProfiles.Add(trainer);
        }

        if (!await dbContext.TrainerTrainingTypes.IgnoreQueryFilters().AnyAsync(
                x => x.TrainerProfileId == trainer.Id && x.TrainingTypeId == trainingTypeId,
                cancellationToken))
        {
            dbContext.TrainerTrainingTypes.Add(new TrainerTrainingType
            {
                TenantId = tenantId,
                TrainerProfileId = trainer.Id,
                TrainingTypeId = trainingTypeId,
            });
        }

        if (!await dbContext.TrainerServiceOfferings.IgnoreQueryFilters().AnyAsync(
                x => x.TrainerProfileId == trainer.Id && x.Name == "Individualni trening",
                cancellationToken))
        {
            dbContext.TrainerServiceOfferings.Add(new TrainerServiceOffering(
                tenantId,
                trainer.Id,
                trainingTypeId,
                "Individualni trening",
                60,
                30m,
                "BAM"));
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Development seeding failed: {string.Join(" ", result.Errors.Select(x => x.Description))}");
        }
    }

    private sealed record SeedAccount(
        string Username,
        string DisplayName,
        string Role);
}
