using GymLink.Application.GymImages;
using GymLink.Application.Common;
using GymLink.Domain.Catalog;
using System.Text.Json;

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

public sealed record GymImageGallerySaveRequest(
    GymImageGallerySaveManifest Manifest,
    IReadOnlyList<GymImageUpload> Uploads);

public sealed class GymImageGallerySaveForm
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public required string Manifest { get; init; }
    public List<IFormFile> Files { get; init; } = [];

    public async Task<GymImageGallerySaveRequest> ToRequestAsync(
        CancellationToken cancellationToken)
    {
        GymImageGallerySaveManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<GymImageGallerySaveManifest>(
                Manifest,
                SerializerOptions) ?? throw InvalidManifest();
        }
        catch (JsonException)
        {
            throw new ApplicationRuleException(
                "gym_image_gallery_invalid",
                "The gallery manifest is invalid.");
        }

        var uploads = new List<GymImageUpload>(Files.Count);
        foreach (var file in Files)
        {
            if (file.Length > GymImage.MaximumFileSizeBytes)
            {
                uploads.Add(new GymImageUpload(
                    new byte[GymImage.MaximumFileSizeBytes + 1],
                    file.ContentType,
                    file.FileName));
                continue;
            }
            await using var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            uploads.Add(new GymImageUpload(
                content.ToArray(),
                file.ContentType,
                file.FileName));
        }
        return new GymImageGallerySaveRequest(manifest, uploads);
    }

    private static ApplicationRuleException InvalidManifest() =>
        new("gym_image_gallery_invalid", "The gallery manifest is invalid.");
}
