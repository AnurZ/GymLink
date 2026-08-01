using GymLink.Application.Common;

namespace GymLink.Application.Images;

internal static class ImageUploadValidator
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static string Validate(
        byte[] content,
        string declaredContentType,
        string fileName,
        long maximumFileSizeBytes,
        string errorCode)
    {
        if (content.Length == 0)
        {
            throw InvalidImage(errorCode, "Please select an image to upload.");
        }

        if (content.LongLength > maximumFileSizeBytes)
        {
            throw InvalidImage(errorCode, "The image must be 5 MiB or smaller.");
        }

        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw InvalidImage(errorCode, "The image filename is invalid.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var expectedContentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => throw InvalidImage(errorCode, "Only JPG, PNG, or WebP images are allowed."),
        };
        if (!string.Equals(
                declaredContentType,
                expectedContentType,
                StringComparison.OrdinalIgnoreCase) ||
            !SignatureMatches(content, expectedContentType))
        {
            throw InvalidImage(
                errorCode,
                "The image extension, content type, and file signature must match.");
        }

        return expectedContentType;
    }

    private static bool SignatureMatches(byte[] content, string contentType) =>
        contentType switch
        {
            "image/jpeg" => content.Length >= 3 &&
                content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
            "image/png" => content.Length >= 8 &&
                content.AsSpan(0, 8).SequenceEqual(PngSignature),
            "image/webp" => content.Length >= 12 &&
                content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };

    private static ApplicationRuleException InvalidImage(string errorCode, string message) =>
        new(errorCode, message);
}
