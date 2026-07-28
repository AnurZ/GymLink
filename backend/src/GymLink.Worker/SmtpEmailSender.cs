using System.ComponentModel.DataAnnotations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GymLink.Worker;

internal sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 1025;

    public bool UseSsl { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    [Required, EmailAddress]
    public string SenderEmail { get; init; } = string.Empty;

    [Required]
    public string SenderName { get; init; } = "GymLink";
}

internal interface IEmailSender
{
    Task SendResetCodeAsync(
        string recipient,
        string code,
        Guid messageId,
        CancellationToken cancellationToken);
}

internal sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendResetCodeAsync(
        string recipient,
        string code,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var message = new MimeMessage
        {
            MessageId = $"<{messageId:N}@gymlink.local>",
            Subject = "GymLink kod za promjenu lozinke",
            Body = new TextPart("plain")
            {
                Text = $"Vaš GymLink kod za promjenu lozinke je: {code}\n\nKod vrijedi 15 minuta.",
            },
        };
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(recipient));

        using var client = new SmtpClient();
        var socketOptions = settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(
            settings.Host,
            settings.Port,
            socketOptions,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            await client.AuthenticateAsync(
                settings.Username,
                settings.Password ?? string.Empty,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
