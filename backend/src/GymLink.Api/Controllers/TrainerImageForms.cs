using System.ComponentModel.DataAnnotations;
using GymLink.Application.TrainerImages;

namespace GymLink.Api.Controllers;

public sealed class TrainerImageUploadForm
{
    [Required]
    public required IFormFile File { get; init; }

    [Required]
    public required string ConcurrencyToken { get; init; }

    public async Task<TrainerImageUpload> ToUploadAsync(CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        await File.CopyToAsync(content, cancellationToken);
        return new TrainerImageUpload(
            content.ToArray(),
            File.ContentType,
            File.FileName,
            ConcurrencyToken);
    }
}
