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
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(settings.RootPath)
                ? settings.RootPath
                : Path.Combine(environment.ContentRootPath, settings.RootPath));
        Directory.CreateDirectory(rootPath);
        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(rootPath),
            RequestPath = settings.RequestPath,
            ServeUnknownFileTypes = false,
        });
    }
}
