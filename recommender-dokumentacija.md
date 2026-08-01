# GymLink explainable hybrid recommender

## Purpose and implementation

GymLink uses the deterministic algorithm `gymlink-hybrid-v1` to recommend
active, publicly visible Gyms and active Trainers belonging to those Gyms. The
implementation is in `backend/src/GymLink.Application/Recommendations/` and
stores generated results in `Recommendation`. It borrows only the batch
generation and persisted-result idea from FIT-RS2; it does not use ML.NET,
matrix factorization, opaque models, or hardcoded recommendation scores.

Every candidate receives a score in `[0, 1]` from three explainable components:

```text
final = (0.50 × preference + 0.30 × personalActivity + 0.20 × popularity)
        / sum(weights of available components)
```

The preference component is omitted when the Member has no profiles. The
personal-activity component is omitted when the Member has no target-bearing
activity. Popularity is always available, so a new Member receives a useful
popularity-based cold start. Missing components are removed from both numerator
and denominator; they are not treated as zero evidence.

## Preference score — 50%

A Member may save zero to three ordered `(City, TrainingType)` profiles. Both
references must be active. The server assigns rank weights `1.0`, `0.7`, and
`0.4`; clients cannot submit weights.

For candidate `c` and profile `p`:

```text
profileMatch(p,c) = 0.40 × cityMatch + 0.60 × trainingTypeMatch
preference(c) = Σ(rankWeight × profileMatch) / Σ(rankWeight)
```

Each match is `1` when equal/present and `0` otherwise. A Gym's training types
come from its catalog links. A Trainer's training types come from the Trainer
profile links.

## Personal activity score — 30%

Only events with a target Gym or Trainer contribute. Future-dated events are
ignored. Evidence decays continuously with a 60-day half-life:

```text
decayedWeight = baseWeight × 0.5^(ageInDays / 60)
```

| Event | Base weight |
|---|---:|
| Gym or Trainer view | 1 |
| Membership request | 3 |
| Membership activation | 5 |
| Reservation creation | 4 |
| Reservation completion | 6 |
| Review creation | 6 |

Trainer-targeted evidence also contributes half of its decayed weight to the
Trainer's owning Gym. Gym and Trainer raw totals are independently normalized
by the largest candidate total of that type. Search/filter signals are retained
as telemetry but have no target and therefore do not affect this component.

## Popularity score — 20%

```text
popularity = 0.50 × ratingQuality
           + 0.30 × reservationVolume
           + 0.20 × allUserActivity
```

Rating quality uses a Bayesian prior of five ratings at the weighted global
candidate average `G`, then scales five stars to `[0,1]`:

```text
ratingQuality = ((average × count + G × 5) / (count + 5)) / 5
```

Confirmed and completed reservation volume uses the preceding 180 days and is
normalized separately for Gyms and Trainers:

```text
reservationVolume = ln(1 + candidateCount) / ln(1 + maximumCandidateCount)
```

All-user target activity uses the same event weights and 60-day decay as
personal activity, including the half-weight Trainer-to-Gym contribution. It is
log-normalized by the maximum Gym or Trainer activity total. Future events do
not participate.

## Ordering, persistence, and refresh

Generation deterministically orders each type by descending score, ascending
name, then ascending target ID, and persists the best 20 Gyms and 20 Trainers.
The database enforces one row per `(UserId, TargetType, TargetId)`, a valid score
range, and a required target tenant. Replacement runs in a serializable
transaction, so readers never observe a partial generation and concurrent
generation cannot create duplicate targets.

The returned feed aims for half Gyms and half Trainers, then fills unused slots
from the available type. Selected rows are finally ordered by score, type, and
ID. The requested limit is `1–20`, defaulting to 10.

`GET /api/me/recommendations` regenerates when results are absent, at least 24
hours old, use a different algorithm version, or predate the Member's latest
preference/activity signal. `POST /api/me/recommendations/refresh` always
regenerates. Read-side signals are source-less and deduplicated for 15 minutes;
workflow signals use the affected entity ID as `SourceId` and a filtered unique
index for idempotency.

## Explanations

Every persisted result has one concise Bosnian reason chosen from the strongest
weighted contribution. Personal activity yields a prior-activity/reservation
reason. Preference yields training-type first, then preferred-location. When
popularity is strongest, sufficiently strong Bayesian quality yields a rating
reason; otherwise the reason states that the target is popular on GymLink. The
mobile parser rejects rows whose reason is empty.

## Worked example

Assume a Member has one primary Sarajevo/Strength profile. A Sarajevo Gym that
offers Strength has preference `1.00`. Suppose its normalized personal activity
is `0.50`, Bayesian/log popularity components are rating `0.80`, reservations
`0.60`, and global activity `0.40`:

```text
popularity = 0.50×0.80 + 0.30×0.60 + 0.20×0.40 = 0.66
final = 0.50×1.00 + 0.30×0.50 + 0.20×0.66 = 0.782
```

Preference contributes `0.50`, more than activity `0.15` or popularity `0.132`,
so the explanation is the preferred-training-type reason. If the same Member
had no preferences, the available weights would be activity `0.30` and
popularity `0.20`, producing `(0.15 + 0.132) / 0.50 = 0.564`.

## Signal capture and safety

Authenticated Member Gym/Trainer views and search/filter reads record telemetry
on a best-effort basis, so a telemetry failure never breaks public discovery.
Membership request/activation, reservation creation/completion, and review
signals are committed atomically with their workflows. Identity is always
derived from `ICurrentUser`; request bodies cannot select another Member.

Recommendations may cross tenants only through a safe public projection.
Generation and reads re-check active tenant, public Gym, active Trainer, and
active user state, omit private fields, and discard stale targets.

## Deterministic development data

Development seeding first converges the approved source fixtures—12
memberships, 48 reservations, 36 reviews, 8 preferences, and 184 activities—then
runs the real algorithm for the four seeded Members. Six eligible Gyms plus 12
eligible Trainers produce exactly 72 persisted rows. Repeated startup replaces
the same targets and reproduces scores/reasons without synthetic Stripe data or
hardcoded output rows.

