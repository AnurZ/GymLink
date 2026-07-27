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
