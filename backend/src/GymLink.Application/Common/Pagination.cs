namespace GymLink.Application.Common;

public record PagedRequest
{
    public const int MaximumPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public void Validate()
    {
        if (Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Page), "Page must be at least one.");
        }

        if (PageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PageSize),
                $"Page size must be between 1 and {MaximumPageSize}.");
        }
    }
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    public long TotalPages => TotalCount == 0
        ? 0
        : (long)Math.Ceiling(TotalCount / (double)PageSize);
}
