using DotNetEnv;
using GymLink.Application;
using GymLink.Infrastructure;
using GymLink.Api;

Env.TraversePath().NoClobber().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGymLinkApplication();
builder.Services.AddGymLinkInfrastructure(builder.Configuration);
builder.Services.AddGymLinkApi();

var app = builder.Build();
app.UseGymLinkApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.Run();

public partial class Program;
