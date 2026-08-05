using GymLink.Application.Payments;

namespace GymLink.Infrastructure.Payments;

internal sealed class FakePaymentAvailability(bool enabled) : IFakePaymentAvailability
{
    public bool Enabled { get; } = enabled;
}
