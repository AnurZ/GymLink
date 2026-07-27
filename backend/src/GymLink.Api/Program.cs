using GymLink.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGymLinkInfrastructure(builder.Configuration);

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();

public partial class Program;
