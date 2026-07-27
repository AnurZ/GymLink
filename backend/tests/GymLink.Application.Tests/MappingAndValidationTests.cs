using System.ComponentModel.DataAnnotations;
using AutoMapper;
using GymLink.Application;
using GymLink.Application.Catalog;
using GymLink.Application.ReferenceData;
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

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}
