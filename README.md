# GymLink

GymLink combines a .NET 10 API with two Flutter clients: an Android app for
Members and Trainers, and a Windows app for GymAdmins and CentralAdmins.

## Prerequisites

- .NET SDK 10.0.103 or a compatible .NET 10 patch
- SQL Server available as `localhost`
- Visual Studio 2026/2022 with the ASP.NET and web development workload, or the .NET CLI
- A trusted local ASP.NET Core HTTPS development certificate
- Flutter 3.44.8 or a compatible stable release
- Android SDK/emulator for the mobile client
- Visual Studio with Desktop development with C++ for the Windows client
- Docker Desktop for the local RabbitMQ and Mailpit services

The default development connection uses Windows authentication. It does not require a SQL username or password:

```text
Server=localhost;Database=230038;Trusted_Connection=True;TrustServerCertificate=True
```

## Local setup

1. Copy `.env.example` to `.env`.
2. Keep `ConnectionStrings__GymLink` pointed at your local SQL Server, or change only that value for your installed instance.
3. Replace `Jwt__SigningKey` and `PasswordReset__CodePepper` with separate
   local values containing at least 32 UTF-8 bytes each.
4. Restore tools and packages:

```powershell
dotnet tool restore
dotnet restore backend/GymLink.sln
```

5. Apply the committed EF Core migrations. The API never applies migrations automatically:

```powershell
dotnet ef database update `
  --project backend/src/GymLink.Infrastructure `
  --startup-project backend/src/GymLink.Api
```

6. To create the linked evaluation dataset, set this value in `.env`:

```text
Seed__Enabled=true
```

The seed is idempotent and runs only in `Development`. Startup rejects development seeding in every other environment.

## Start the API

From the command line:

```powershell
dotnet run --project backend/src/GymLink.Api --launch-profile https
```

Swagger opens at [https://localhost:62286/swagger](https://localhost:62286/swagger). The HTTP profile is also available at [http://localhost:62287/swagger](http://localhost:62287/swagger).

In Visual Studio:

1. Open `backend/GymLink.sln`.
2. Set `GymLink.Api` as the startup project.
3. Select the `https` or `http` launch profile, not IIS Express.
4. Start the project. The selected profile opens `/swagger`.

Use Swagger's **Authorize** button with `Bearer <access-token>` after calling `POST /api/auth/login`.

## Start RabbitMQ, Mailpit, and the Worker

The API safely retains committed workflow events in its outbox while RabbitMQ
is unavailable. Enable publishing only when the local broker is running:

```powershell
docker run -d --name gymlink-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management
docker run -d --name gymlink-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
```

Set `RabbitMq__Enabled=true` in `.env`, restart the API, and run the separate
consumer:

```powershell
dotnet run --project backend/src/GymLink.Worker
```

RabbitMQ management is available at [http://localhost:15672](http://localhost:15672)
and Mailpit's captured reset emails at [http://localhost:8025](http://localhost:8025).
Only password-reset codes generate email; workflow updates remain in-app
notifications.

If another local project already owns ports `5672`/`15672`, map GymLink to
unused host ports, for example `-p 5673:5672 -p 15673:15672`, and set
`RabbitMq__Port=5673` for both the API and Worker.

## Start the Flutter clients

The clients require an explicit API address at build/run time.

Use Flutter hot reload for ordinary widget/code edits. Changes to route tables,
providers, native plugins, or startup configuration require a hot restart. An
already installed APK does not update itself; rebuild/reinstall it or use
`flutter run` during development.

Android emulator:

```powershell
cd ui/gymlink_mobile
flutter pub get
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:62287
```

Windows:

```powershell
cd ui/gymlink_desktop
flutter pub get
flutter run -d windows --dart-define=API_BASE_URL=http://localhost:62287
```

Use the HTTPS profile only after configuring the emulator or Windows host to
trust the ASP.NET development certificate.

## Evaluation credentials

These credentials are intentionally public development/evaluation fixtures. They must never be enabled or reused in a production deployment.

| Context | Username | Password |
|---|---|---|
| Desktop version | `desktop` | `Test123!` |
| Mobile version | `mobile` | `Test123!` |
| Multiple user roles | Use the role account below | `Test123!` |

### Role accounts

| Username | Email | Role | Gym assignment | Password |
|---|---|---|---|---|
| `centraladmin` | `centraladmin@gymlink.local` | CentralAdmin | None | `Test123!` |
| `desktop` | `desktop@gymlink.local` | GymAdmin | GymLink Sarajevo | `Test123!` |
| `gymadmin` | `gymadmin@gymlink.local` | GymAdmin | GymLink Mostar | `Test123!` |
| `trainer` | `trainer@gymlink.local` | Trainer | GymLink Sarajevo | `Test123!` |
| `trainer2` | `trainer2@gymlink.local` | Trainer | GymLink Mostar | `Test123!` |
| `mobile` | `mobile@gymlink.local` | Member | None | `Test123!` |
| `member` | `member@gymlink.local` | Member | None | `Test123!` |

Login accepts either the username or email. Staff access tokens automatically contain the account's single active gym assignment; Member and CentralAdmin tokens do not contain tenant claims.

### Add a new gym and owner

1. Sign in to the Windows app as `centraladmin`.
2. Open **Teretane** and select **Dodaj teretanu**.
3. Enter the basic profile, select an active city, and place the location pin.
4. Ask the owner to register a normal account.
5. In **Korisnici i uloge**, assign that account the `GymAdmin` role for the
   new gym.
6. The GymAdmin signs in to the Windows app and completes working hours,
   equipment, training types, and at least one active membership plan.
7. CentralAdmin returns to **Teretane** and activates the gym.

New gyms remain private and `PendingActivation` until these steps are complete.
Owner-submitted registration requests are retained only as a legacy API and
have no client entry.

## Verification

```powershell
dotnet build backend/GymLink.sln --no-restore
dotnet test backend/GymLink.sln --no-build --no-restore
dotnet format backend/GymLink.sln --verify-no-changes --no-restore
flutter analyze ui/gymlink_mobile
flutter test ui/gymlink_mobile
flutter analyze ui/gymlink_desktop
flutter test ui/gymlink_desktop
git diff --check
```

The public catalog endpoint is `GET /api/gyms`. With the development seed enabled it returns the Sarajevo and Mostar gyms.

## Troubleshooting

### SQL login or database-open failure

Confirm the SQL Server service is running and that the server name in `.env` matches your installation. `Trusted_Connection=True` uses the Windows account running the API. A message such as `Cannot open database "230038"` normally means the database has not been created for that account; run the `dotnet ef database update` command above and verify that the account can connect to `localhost`.

If your installation uses a named instance, update only the server portion, for example `Server=localhost\SQLEXPRESS`. Keep secrets out of tracked files.

### Required environment variable

The API loads the root `.env` by walking up from its working directory. Make sure `.env` exists, contains `ConnectionStrings__GymLink`, and has a JWT signing key of at least 32 bytes. Restart Visual Studio after changing environment values.

### Port already in use or stale Visual Studio process

Stop the previous debugging session. If Visual Studio left an API process running, close it in Task Manager or run:

```powershell
Get-Process GymLink.Api -ErrorAction SilentlyContinue | Stop-Process
```

Then start the selected launch profile again.

### Visual Studio stops on an expected business conflict

Membership and reservation rules use handled application exceptions that the
API middleware converts to stable `409 ProblemDetails` responses. If Visual
Studio stops on the `throw` line but the Flutter app continues after pressing
Continue, this is a first-chance debugger pause rather than an unhandled crash.

Open **Debug → Windows → Exception Settings** and disable **Break on thrown**
for the relevant handled application exception type. Keep breaking on
user-unhandled exceptions enabled. The Flutter clients display these conflicts
inline and the backend remains the authoritative validator.

## Phase boundary

Durable notifications and password reset are implemented in Phase 7. Stripe is
Phase 8, chat is Phase 9, recommendations are Phase 10, and statistics/PDF
reports are Phase 11.
