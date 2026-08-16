using System.ComponentModel.DataAnnotations;
using AutoMapper;
using GymLink.Application;
using GymLink.Application.Administration;
using GymLink.Application.Catalog;
using GymLink.Application.Memberships;
using GymLink.Application.ReferenceData;
using GymLink.Application.Registration;
using GymLink.Application.Reservations;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.Application.Tests;

public sealed class MappingAndValidationTests
{
    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddGymLinkApplication()
            .BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        mapper.ConfigurationProvider.AssertConfigurationIsValid();
    }

    [Fact]
    public void Country_contract_rejects_invalid_code()
    {
        var request = new CreateCountryRequest { Code = "B", Name = "Bosnia and Herzegovina" };

        var errors = Validate(request);

        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(request.Code)));
    }

    [Fact]
    public void Membership_plan_contract_rejects_invalid_duration_and_currency()
    {
        var request = new CreateMembershipPlanRequest
        {
            Name = "Monthly",
            DurationDays = 0,
            Price = 25,
            Currency = "BA",
        };

        var errors = Validate(request);

        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(request.DurationDays)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(request.Currency)));
    }

    [Fact]
    public void Review_contract_rejects_out_of_range_rating_and_oversized_comment()
    {
        var request = new CreateReviewRequest
        {
            Rating = 6,
            Comment = new string('x', 301),
        };

        var errors = Validate(request);

        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(request.Rating)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(request.Comment)));
    }

    [Fact]
    public void Review_contract_accepts_300_character_comment()
    {
        var request = new CreateReviewRequest
        {
            Rating = 5,
            Comment = new string('x', 300),
        };

        Assert.DoesNotContain(
            Validate(request),
            x => x.MemberNames.Contains(nameof(request.Comment)));
    }

    [Fact]
    public void Workflow_reason_contracts_enforce_200_character_limit()
    {
        foreach (var pair in ReasonRequests())
        {
            var accepted = pair(new string('x', 200));
            var rejected = pair(new string('x', 201));

            Assert.DoesNotContain(
                Validate(accepted),
                x => x.MemberNames.Contains("Reason"));
            Assert.Contains(
                Validate(rejected),
                x => x.MemberNames.Contains("Reason"));
        }
    }

    private static IReadOnlyList<Func<string, object>> ReasonRequests() =>
    [
        reason => new RoleAssignmentRequest
        {
            Identifier = "member@example.com",
            Role = "Trainer",
            Reason = reason,
        },
        reason => new UserActionRequest
        {
            Identifier = "member@example.com",
            Reason = reason,
        },
        reason => new TenantStatusReasonRequest { Reason = reason },
        reason => new CreateTrainerRequest
        {
            Biography = "Biography",
            Reason = reason,
        },
        reason => new ReasonedConcurrencyRequest
        {
            ConcurrencyToken = "token",
            Reason = reason,
        },
        reason => new StaffCancellationRequest
        {
            ConcurrencyToken = "token",
            Reason = reason,
        },
        reason => new RegistrationDecisionRequest { Reason = reason },
    ];

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}
