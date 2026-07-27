using GymLink.Domain.Catalog;
using GymLink.Domain.Memberships;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Identity;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<Country> Countries { get; }
    DbSet<City> Cities { get; }
    DbSet<Equipment> Equipment { get; }
    DbSet<TrainingType> TrainingTypes { get; }
    DbSet<Gym> Gyms { get; }
    DbSet<GymImage> GymImages { get; }
    DbSet<GymWorkingHours> GymWorkingHours { get; }
    DbSet<GymEquipment> GymEquipment { get; }
    DbSet<GymTrainingType> GymTrainingTypes { get; }
    DbSet<TrainerProfile> TrainerProfiles { get; }
    DbSet<TrainerTrainingType> TrainerTrainingTypes { get; }
    DbSet<TrainerServiceOffering> TrainerServiceOfferings { get; }
    DbSet<MembershipPlan> MembershipPlans { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
