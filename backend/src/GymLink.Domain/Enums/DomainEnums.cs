namespace GymLink.Domain.Enums;

public enum GymRegistrationStatus { Draft, Submitted, Approved, Rejected }
public enum TenantStatus { PendingActivation, Active, Inactive, Suspended }
public enum AssignmentStatus { Invited, Active, Suspended, Ended }
public enum MembershipRequestStatus { Pending, Approved, Rejected, Cancelled }
public enum MembershipPaymentMethod { Stripe, StripeFallback, PayInPerson }
public enum MembershipStatus { PendingPayment, Active, Expired, Cancelled, Suspended }
public enum AvailabilitySlotStatus { Available, Unavailable, Reserved, Cancelled }
public enum TrainerShiftPeriod { Morning, Evening }
public enum ReservationStatus { Pending, Confirmed, Completed, Cancelled }
public enum ReservationPaymentMethod { Stripe, PayInPerson }
public enum PaymentStatus { Created, Processing, Succeeded, Failed, PartiallyRefunded, Refunded }
public enum RefundStatus { Created, Processing, Succeeded, Failed }
public enum PaymentPurpose { Membership, TrainerReservation }
public enum RecommendationTargetType { Gym, Trainer }
public enum ActivityEventType
{
    GymView,
    TrainerView,
    Search,
    Filter,
    MembershipRequest,
    MembershipActivation,
    ReservationCreation,
    ReservationCompletion,
    ReviewCreation,
    PreferredTrainingTypeChange,
}
