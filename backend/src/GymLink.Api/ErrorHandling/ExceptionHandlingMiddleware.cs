using GymLink.Application.Common;
using GymLink.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Api.ErrorHandling;

internal sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandled =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "UnhandledApiException"),
            "Unhandled API exception with trace ID {TraceId}.");

    private static readonly Action<ILogger, string, string, Exception?> LogHandled =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, "HandledApiException"),
            "API request failed with code {Code} and trace ID {TraceId}.");

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, code, detail) = exception switch
        {
            NotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Code, notFound.Message),
            ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Code, conflict.Message),
            AuthenticationFailedException authentication => (
                StatusCodes.Status401Unauthorized,
                authentication.Code,
                authentication.Message),
            AuthorizationDeniedException authorization => (
                StatusCodes.Status403Forbidden,
                authorization.Code,
                authorization.Message),
            ExternalServiceUnavailableException unavailable => (
                StatusCodes.Status503ServiceUnavailable,
                unavailable.Code,
                unavailable.Message),
            DomainException domain when domain.Code == "invalid_state_transition" => (
                StatusCodes.Status409Conflict,
                domain.Code,
                domain.Message),
            DomainException domain => (StatusCodes.Status400BadRequest, domain.Code, domain.Message),
            ApplicationRuleException rule => (StatusCodes.Status400BadRequest, rule.Code, rule.Message),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "concurrency_conflict",
                "The record was changed by another request. Reload it and try again."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "unexpected_error",
                "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, context.TraceIdentifier, exception);
        }
        else
        {
            LogHandled(logger, code, context.TraceIdentifier, exception);
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Extensions = { ["traceId"] = context.TraceIdentifier },
        });
    }
}
