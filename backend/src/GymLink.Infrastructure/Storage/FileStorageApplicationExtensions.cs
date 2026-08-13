using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Storage;

public static class FileStorageApplicationExtensions
{
    public static IApplicationBuilder UseGymLinkFileStorage(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        IOptions<FileStorageOptions> options)
    {
        var settings = options.Value;
        UseArea(app, environment, settings.RootPath, settings.RequestPath);
        UseArea(app, environment, settings.GymRootPath, settings.GymRequestPath);
        return app;
    }

    private static void UseArea(
        IApplicationBuilder app,
        IHostEnvironment environment,
        string configuredRootPath,
        string requestPath)
    {
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.Combine(environment.ContentRootPath, configuredRootPath));
        Directory.CreateDirectory(rootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(rootPath),
            RequestPath = requestPath,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl =
                    "public,max-age=31536000,immutable",
        });
    }
}
