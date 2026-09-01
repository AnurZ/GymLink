using DotNetEnv;
using GymLink.Application;
using GymLink.Application.Abstractions;
using GymLink.Application.Identity;
using GymLink.Infrastructure.Identity;
using GymLink.Infrastructure;
using GymLink.Infrastructure.Messaging;
using GymLink.Infrastructure.Persistence;
using GymLink.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Env.TraversePath().NoClobber().Load();
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkerTenantContext>();
builder.Services.AddSingleton<ITenantContext>(provider =>
    provider.GetRequiredService<WorkerTenantContext>());
builder.Services.AddSingleton<ICurrentUser>(provider =>
    provider.GetRequiredService<WorkerTenantContext>());
builder.Services.AddSingleton<IRequestMetadata>(provider =>
    provider.GetRequiredService<WorkerTenantContext>());
builder.Services.AddGymLinkWorkerApplication();
builder.Services.AddGymLinkWorkerInfrastructure(builder.Configuration);
builder.Services.AddGymLinkRabbitMqOptions(builder.Configuration);
builder.Services.AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName))
    .Validate(
        options => System.Text.Encoding.UTF8.GetByteCount(options.CodePepper) >= 32,
        "PasswordReset__CodePepper must contain at least 32 UTF-8 bytes.")
    .ValidateOnStart();
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IPasswordResetCodeService, PasswordResetCodeService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<RabbitMqWorkerService>();
builder.Services.AddHostedService<PaymentReconciliationWorker>();
builder.Services.AddHostedService<MembershipExpiryWorker>();

await builder.Build().RunAsync();
