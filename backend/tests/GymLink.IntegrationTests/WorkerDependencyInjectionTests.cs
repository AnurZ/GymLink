using GymLink.Application;
using GymLink.Application.Abstractions;
using GymLink.Application.Payments;
using GymLink.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.IntegrationTests;

public sealed class WorkerDependencyInjectionTests
{
    [Fact]
    public void Payment_worker_resolves_the_reconciliation_service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GymLink"] =
                    "Server=localhost;Database=GymLinkWorkerDiTest;Trusted_Connection=True;" +
                    "TrustServerCertificate=True",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WorkerTestContext>();
        services.AddSingleton<ITenantContext>(provider =>
            provider.GetRequiredService<WorkerTestContext>());
        services.AddSingleton<ICurrentUser>(provider =>
            provider.GetRequiredService<WorkerTestContext>());
        services.AddSingleton<IRequestMetadata>(provider =>
            provider.GetRequiredService<WorkerTestContext>());
        services.AddGymLinkPaymentWorkerApplication();
        services.AddGymLinkPaymentWorkerInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IPaymentReconciliationService>());
    }

    private sealed class WorkerTestContext :
        ITenantContext,
        ICurrentUser,
        IRequestMetadata
    {
        public Guid? TenantId => null;
        public string? TenantRole => null;
        public bool HasTenant => false;
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
        public string CorrelationId => "worker-di-test";
        public string? RemoteIpAddress => null;
    }
}
