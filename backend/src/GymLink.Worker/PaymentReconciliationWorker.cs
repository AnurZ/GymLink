using GymLink.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GymLink.Worker;

internal sealed class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(820, "PaymentReconciliationFailure"),
            "Payment reconciliation scan failed; it will be retried.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentReconciliationService>();
                await service.ReconcileDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
