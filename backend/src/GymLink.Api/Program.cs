using DotNetEnv;
using GymLink.Application;
using GymLink.Infrastructure;
using GymLink.Api;
using GymLink.Api.Hubs;
using GymLink.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using GymLink.Infrastructure.Seeding;

Env.TraversePath().NoClobber().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGymLinkApplication();
builder.Services.AddGymLinkInfrastructure(builder.Configuration);
builder.Services.AddGymLinkApi(builder.Configuration);

var app = builder.Build();
await app.SeedDevelopmentDataAsync();
app.UseGymLinkApi();
app.UseGymLinkFileStorage(
    app.Environment,
    app.Services.GetRequiredService<IOptions<FileStorageOptions>>());
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.Run();

public partial class Program;
