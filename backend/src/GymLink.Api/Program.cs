using DotNetEnv;
using GymLink.Application;
using GymLink.Infrastructure;
using GymLink.Api;
using GymLink.Infrastructure.Seeding;

Env.TraversePath().NoClobber().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGymLinkApplication();
builder.Services.AddGymLinkInfrastructure(builder.Configuration);
builder.Services.AddGymLinkApi(builder.Configuration);

var app = builder.Build();
await app.SeedDevelopmentDataAsync();
app.UseGymLinkApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.Run();

public partial class Program;
