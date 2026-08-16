## Docker Build

To build GymLink using Docker, copy `.env.example` to `.env`, enter the required local configuration values, and run `docker compose up --build -d` from the repository root. Swagger is available at `http://localhost:62287/swagger` and RabbitMQ Management at `http://localhost:15672`. Internet access is required for Gmail SMTP, maps, address search, external gym images, and Stripe test payments.

## Password-reset email

The normal Compose stack sends password-reset codes through authenticated Gmail SMTP. Enable Google 2-Step Verification for the sender account, create a dedicated 16-character [Google App Password](https://support.google.com/mail/answer/185833?hl=en-EN), and set `Smtp__Host=smtp.gmail.com`, `Smtp__Port=465`, `Smtp__UseSsl=true`, `Smtp__Username`, `Smtp__Password`, and `Smtp__SenderEmail` in the private `.env`. Use the full Gmail address for both username and sender email. Never commit or share the App Password.

The release audit also uses Gmail SMTP. Docker verification therefore requires a real recipient through `-AuditEmail`; the isolated audit account and database are removed with the audit stack. The automated assertion proves that Gmail accepted the message and the Worker recorded completed inbox processing, while final mailbox placement remains external to GymLink.

## Address search

CentralAdmin address search uses Nominatim with `Geocoding__UserAgent=GymLink/1.0`. `Geocoding__ContactEmail` is optional; when supplied it must be a real contact address. Placeholder values such as `replace-with-your-email@example.com` are rejected during startup. If the provider is unavailable, gym creation remains available through the searchable active BiH city list, manual address field, and exact map-point selection.

## GymLink

GymLink is a fitness platform that allows users to discover gyms and trainers, purchase gym memberships, book personal training appointments, review completed services, and receive explained recommendations. Members and Trainers use the Android application, while Gym Administrators and Central Administrators use the Windows desktop application. Members can chat with Trainers and receive notifications, Gym Administrators manage their gym, members, trainers, schedules, reservations, gallery, statistics, and PDF reports, while Central Administrators manage gyms, activation, reference data, role assignments, and system statistics.

## Builds

The Android application build for Members and Trainers is located at `artifacts/release-candidate/gymlink-android-arm64.apk`. The Windows application build for Gym Administrators and Central Administrators is located at `artifacts/release-candidate/gymlink-windows-x64.zip`. File checksums are stored in `artifacts/release-candidate/SHA256SUMS.txt`.

## Demo Login Credentials for admins

| Role / Gym | Email | Password |
|---|---|---|
| Central Admin | `centraladmin@gymlink.local` | `Test123!` |
| Gym Admin (Arena Sport Centar) | `admin.arena@gymlink.local` | `Test123!` |
| Gym Admin (Perfect Fit) | `admin.perfectfit@gymlink.local` | `Test123!` |
| Gym Admin (Sportska Akademija Respect) | `admin.respect@gymlink.local` | `Test123!` |
| Gym Admin (Oxide Gym) | `admin.oxide@gymlink.local` | `Test123!` |
| Gym Admin (Fit Factory) | `admin.fitfactory@gymlink.local` | `Test123!` |
| Gym Admin (Fitness Club Iskra) | `admin.iskra@gymlink.local` | `Test123!` |

## Demo Member Accounts

| Role | Email | Password |
|---|---|---|
| Member | `mobile1@gymlink.local` | `Test123!` |
| Member | `mobile2@gymlink.local` | `Test123!` |
| Member | `mobile3@gymlink.local` | `Test123!` |
| Member | `mobile4@gymlink.local` | `Test123!` |

## Demo Trainer Accounts

| Role / Gym | Email | Password |
|---|---|---|
| Trainer - Marko Dogan (Arena Sport Centar) | `arenatrainer1@gymlink.local` | `Test123!` |
| Trainer - Ana Marić (Arena Sport Centar) | `arenatrainer2@gymlink.local` | `Test123!` |
| Trainer - Ivan Kraljević (Perfect Fit) | `perfectfittrainer1@gymlink.local` | `Test123!` |
| Trainer - Petra Bošnjak (Perfect Fit) | `perfectfittrainer2@gymlink.local` | `Test123!` |
| Trainer - Emir Hadžić (Sportska Akademija Respect) | `respecttrainer1@gymlink.local` | `Test123!` |
| Trainer - Lejla Bećirović (Sportska Akademija Respect) | `respecttrainer2@gymlink.local` | `Test123!` |
| Trainer - Amar Kovačević (Oxide Gym) | `oxidetrainer1@gymlink.local` | `Test123!` |
| Trainer - Selma Delić (Oxide Gym) | `oxidetrainer2@gymlink.local` | `Test123!` |
| Trainer - Adnan Mujić (Fit Factory) | `fitfactorytrainer1@gymlink.local` | `Test123!` |
| Trainer - Emina Alagić (Fit Factory) | `fitfactorytrainer2@gymlink.local` | `Test123!` |
| Trainer - Haris Mehić (Fitness Club Iskra) | `iskratrainer1@gymlink.local` | `Test123!` |
| Trainer - Ivana Vuković (Fitness Club Iskra) | `iskratrainer2@gymlink.local` | `Test123!` |

## Notes

Docker Compose uses real Stripe sandbox Checkout by default. Stripe payments use the `sk_test_...` credentials from the private `.env`, open Stripe Checkout in the browser, and appear in the Stripe test dashboard. The standard test card is `4242 4242 4242 4242` with any future expiry date and CVC. For live webhook delivery, run `stripe listen --forward-to http://localhost:62287/api/webhooks/stripe` and place its current `whsec_...` value in `.env`; the success return also verifies the Checkout session directly with Stripe.

## App Behavior

The Windows application is available only to Central Administrators and Gym Administrators. The Android application is available to Members and Trainers. A Member can purchase a gym membership or book a Trainer appointment using Stripe or pay in person, and open a persistent chat after the reservation is confirmed. Trainers manage their availability and appointments, while Gym Administrators manage only their assigned gym. Payment and workflow status shown by the clients is always refreshed from the API.

## RabbitMQ

RabbitMQ carries notification and password-reset messages from the API transactional outbox to the separate GymLink Worker. The Worker processes persistent notifications and sends password-reset email through the configured Gmail SMTP account, with retry, idempotent processing, manual acknowledgements, and dead-letter queues for invalid messages.
