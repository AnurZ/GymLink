namespace GymLink.Domain.Enums;

public enum GymRegistrationStatus { Submitted = 1, Approved = 2, Rejected = 3 }
public enum TenantStatus { PendingActivation, Active, Inactive, Suspended }
public enum AssignmentStatus { Active = 1, Ended = 3 }
public enum MembershipRequestStatus { Pending, Approved, Rejected, Cancelled }
public enum MembershipPaymentMethod { Stripe = 0, PayInPerson = 2 }
public enum MembershipStatus { PendingPayment, Active, Expired, Cancelled, Suspended }
public enum AvailabilitySlotStatus { Available, Unavailable, Reserved, Cancelled }
public enum TrainerShiftPeriod { Morning, Evening }
public enum ReservationStatus { Pending, Confirmed, Completed, Cancelled }
public enum ReservationPaymentMethod { Stripe, PayInPerson }
public enum PaymentStatus { Created, Processing, Succeeded, Failed }
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
