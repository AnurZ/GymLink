using GymLink.Application.Common;
using GymLink.Application.Images;

namespace GymLink.Application.Tests;

public sealed class ImageUploadValidatorTests
{
    [Theory]
    [MemberData(nameof(DetectedImages))]
    public void Chat_validation_detects_supported_content(
        byte[] content,
        string expectedContentType)
    {
        Assert.Equal(
            expectedContentType,
            ImageUploadValidator.ValidateDetectedContent(
                content,
                "downloaded.webp",
                5 * 1024 * 1024,
                "invalid_chat_image"));
    }

    [Fact]
    public void Chat_validation_rejects_unsupported_or_oversized_content()
    {
        Assert.Throws<ApplicationRuleException>(() =>
            ImageUploadValidator.ValidateDetectedContent(
                [0x00, 0x01, 0x02],
                "downloaded.jpg",
                5 * 1024 * 1024,
                "invalid_chat_image"));
        Assert.Throws<ApplicationRuleException>(() =>
            ImageUploadValidator.ValidateDetectedContent(
                new byte[5 * 1024 * 1024 + 1],
                "downloaded.jpg",
                5 * 1024 * 1024,
                "invalid_chat_image"));
    }

    [Fact]
    public void Existing_strict_validation_still_rejects_mismatched_metadata()
    {
        Assert.Throws<ApplicationRuleException>(() =>
            ImageUploadValidator.Validate(
                [0xFF, 0xD8, 0xFF],
                "image/webp",
                "downloaded.webp",
                5 * 1024 * 1024,
                "invalid_image"));
    }

    public static TheoryData<byte[], string> DetectedImages => new()
    {
        { [0xFF, 0xD8, 0xFF], "image/jpeg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png" },
        { [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50], "image/webp" },
    };
}
