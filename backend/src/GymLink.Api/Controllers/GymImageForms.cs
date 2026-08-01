using GymLink.Application.GymImages;
using GymLink.Domain.Catalog;

namespace GymLink.Api.Controllers;

public sealed class GymImageUploadForm
{
    public required IFormFile File { get; init; }
    public string? ConcurrencyToken { get; init; }

    public async Task<GymImageUpload> ToUploadAsync(CancellationToken cancellationToken)
    {
        if (File.Length > GymImage.MaximumFileSizeBytes)
        {
            return new GymImageUpload(
                new byte[GymImage.MaximumFileSizeBytes + 1],
                File.ContentType,
                File.FileName,
                ConcurrencyToken);
        }

        await using var content = new MemoryStream();
        await File.CopyToAsync(content, cancellationToken);
        return new GymImageUpload(
            content.ToArray(),
            File.ContentType,
            File.FileName,
            ConcurrencyToken);
    }
}
