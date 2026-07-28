namespace GymLink.Application.Common;

public sealed class NotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ConflictException : Exception
{
    public ConflictException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ConflictException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class AuthenticationFailedException(
    string code = "invalid_credentials",
    string message = "The supplied credentials are invalid.") : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class AuthorizationDeniedException(
    string code = "access_denied",
    string message = "You are not authorized to perform this action.") : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ExternalServiceUnavailableException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
