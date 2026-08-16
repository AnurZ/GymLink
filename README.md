## Docker Build

To build GymLink using Docker, unzip and copy .env to root folder and run `docker compose up --build -d` from the repository root. Swagger is available at `http://localhost:62287/swagger` and RabbitMQ Management at `http://localhost:15672`. Internet access is required for Gmail SMTP, maps, address search, external gym images, and Stripe test payments.

## GymLink

GymLink is a fitness platform that allows users to discover gyms and trainers, purchase gym memberships, book personal training appointments, review completed services, and receive explained recommendations. Members and Trainers use the Android application, while Gym Administrators and Central Administrators use the Windows desktop application. Members can chat with Trainers and receive notifications, Gym Administrators manage their gym, members, trainers, schedules, reservations, gallery, statistics, and PDF reports, while Central Administrators manage gyms, activation, reference data, role assignments, and system statistics.

## Demo Login Credentials for admins

Both username and email are accepted at login. The shorter usernames are listed below.

| Role / Gym | Username | Password |
|---|---|---|
| Central Admin | `centraladmin` | `Test123!` |
| Gym Admin (Arena Sport Centar) | `admin.arena` | `Test123!` |
| Gym Admin (Perfect Fit) | `admin.perfectfit` | `Test123!` |
| Gym Admin (Sportska Akademija Respect) | `admin.respect` | `Test123!` |
| Gym Admin (Oxide Gym) | `admin.oxide` | `Test123!` |
| Gym Admin (Fit Factory) | `admin.fitfactory` | `Test123!` |
| Gym Admin (Fitness Club Iskra) | `admin.iskra` | `Test123!` |

## Demo Member Accounts

| Role | Username | Password |
|---|---|---|
| Member | `mobile1` | `Test123!` |
| Member | `mobile2` | `Test123!` |
| Member | `mobile3` | `Test123!` |
| Member | `mobile4` | `Test123!` |

## Demo Trainer Accounts

| Role / Gym | Username | Password |
|---|---|---|
| Trainer - Marko Dogan (Arena Sport Centar) | `arenatrainer1` | `Test123!` |
| Trainer - Ana Marić (Arena Sport Centar) | `arenatrainer2` | `Test123!` |
| Trainer - Ivan Kraljević (Perfect Fit) | `perfectfittrainer1` | `Test123!` |
| Trainer - Petra Bošnjak (Perfect Fit) | `perfectfittrainer2` | `Test123!` |
| Trainer - Emir Hadžić (Sportska Akademija Respect) | `respecttrainer1` | `Test123!` |
| Trainer - Lejla Bećirović (Sportska Akademija Respect) | `respecttrainer2` | `Test123!` |
| Trainer - Amar Kovačević (Oxide Gym) | `oxidetrainer1` | `Test123!` |
| Trainer - Selma Delić (Oxide Gym) | `oxidetrainer2` | `Test123!` |
| Trainer - Adnan Mujić (Fit Factory) | `fitfactorytrainer1` | `Test123!` |
| Trainer - Emina Alagić (Fit Factory) | `fitfactorytrainer2` | `Test123!` |
| Trainer - Haris Mehić (Fitness Club Iskra) | `iskratrainer1` | `Test123!` |
| Trainer - Ivana Vuković (Fitness Club Iskra) | `iskratrainer2` | `Test123!` |

## Notes

Docker Compose uses real Stripe sandbox Checkout by default. Stripe payments use the `sk_test_...` credentials from the private `.env`, open Stripe Checkout in the browser, and appear in the Stripe test dashboard. The standard test card is `4242 4242 4242 4242` with any future expiry date and CVC. For live webhook delivery, run `stripe listen --forward-to http://localhost:62287/api/webhooks/stripe` and place its current `whsec_...` value in `.env`; the success return also verifies the Checkout session directly with Stripe.

## App Behavior

The Windows application is available only to Central Administrators and Gym Administrators. The Android application is available to Members and Trainers. A Member can purchase a gym membership or book a Trainer appointment using Stripe or pay in person, and open a persistent chat after the reservation is confirmed. Trainers manage their availability and appointments, while Gym Administrators manage only their assigned gym. Payment and workflow status shown by the clients is always refreshed from the API.

## RabbitMQ

RabbitMQ carries notification and password-reset messages from the API transactional outbox to the separate GymLink Worker. The Worker processes persistent notifications and sends password-reset email through the configured Gmail SMTP account, with retry, idempotent processing, manual acknowledgements, and dead-letter queues for invalid messages.
