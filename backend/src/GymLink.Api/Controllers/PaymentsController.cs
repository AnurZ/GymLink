using System.Net;
using System.Text;
using System.Text.Json;
using GymLink.Application.Authorization;
using GymLink.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymLink.Api.Controllers;

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentService service) : ControllerBase
{
    [HttpPost("memberships/checkout")]
    public async Task<IActionResult> CreateMembershipPlanCheckout(
        CreateMembershipPlanCheckoutRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateMembershipPlanCheckoutAsync(
            request.MembershipPlanId,
            cancellationToken));

    [HttpPost("memberships/{membershipId:guid}/checkout")]
    public async Task<IActionResult> CreateMembershipCheckout(
        Guid membershipId,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateMembershipCheckoutAsync(membershipId, cancellationToken));

    [HttpPost("reservations/{reservationId:guid}/checkout")]
    public async Task<IActionResult> CreateReservationCheckout(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateReservationCheckoutAsync(reservationId, cancellationToken));

    [HttpGet("{paymentId:guid}")]
    public async Task<IActionResult> Get(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetMineAsync(paymentId, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyNames.MemberSelf)]
[Route("api/payments/manual")]
public sealed class ManualPaymentsController(IPaymentService service) : ControllerBase
{
    [HttpPost("memberships/pay")]
    public async Task<IActionResult> PayMembershipPlan(
        CreateManualMembershipPlanPaymentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteManualMembershipPlanPaymentAsync(
            request.MembershipPlanId,
            cancellationToken));

    [HttpPost("memberships/{membershipId:guid}/pay")]
    public async Task<IActionResult> PayMembership(
        Guid membershipId,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteManualMembershipPaymentAsync(
            membershipId,
            cancellationToken));

    [HttpPost("reservations/{reservationId:guid}/pay")]
    public async Task<IActionResult> PayReservation(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteManualReservationPaymentAsync(
            reservationId,
            cancellationToken));
}

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/stripe")]
public sealed class StripeWebhookController(IPaymentService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();
        await service.HandleWebhookAsync(payload, signature, cancellationToken);
        return Ok();
    }
}

[ApiController]
[AllowAnonymous]
[Route("payments/stripe")]
public sealed class StripeReturnController(IPaymentService service) : ControllerBase
{
    [HttpGet("success")]
    public async Task<ContentResult> Success(
        [FromQuery(Name = "session_id")] string? sessionId,
        CancellationToken cancellationToken)
    {
        var paymentId = await service.ReconcileReturnAsync(sessionId, cancellationToken);
        return DeepLinkPage("success", sessionId, paymentId);
    }

    [HttpGet("cancel")]
    public ContentResult Cancel() => DeepLinkPage("cancel", null, null);

    private static ContentResult DeepLinkPage(
        string outcome,
        string? sessionId,
        Guid? paymentId)
    {
        var target = $"gymlink://payment/result?outcome={Uri.EscapeDataString(outcome)}";
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            target += $"&session_id={Uri.EscapeDataString(sessionId)}";
        }
        if (paymentId.HasValue)
        {
            target += $"&payment_id={paymentId.Value}";
        }

        var encodedTarget = WebUtility.HtmlEncode(target);
        var javascriptTarget = JsonSerializer.Serialize(target);
        var html = $$"""
            <!doctype html>
            <html lang="bs">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta http-equiv="refresh" content="0;url={{encodedTarget}}">
              <title>GymLink plaćanje</title>
              <style>
                body { font-family: system-ui, sans-serif; text-align: center; padding: 3rem 1rem; }
                a { display: inline-block; padding: .8rem 1.2rem; border-radius: .6rem;
                    background: #6750a4; color: white; text-decoration: none; font-weight: 600; }
              </style>
              <script>window.location.replace({{javascriptTarget}});</script>
            </head>
            <body>
              <p>Povratak u GymLink aplikaciju…</p>
              <p><a href="{{encodedTarget}}">Vrati se u GymLink</a></p>
            </body>
            </html>
            """;
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
    }
}
