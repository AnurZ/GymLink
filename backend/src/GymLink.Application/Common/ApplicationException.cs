namespace GymLink.Application.Common;

public sealed class ApplicationRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
