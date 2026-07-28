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

The default development connection uses Windows authentication. It does not require a SQL username or password:

```text
Server=localhost;Database=230038;Trusted_Connection=True;TrustServerCertificate=True
```

## Local setup

1. Copy `.env.example` to `.env`.
2. Keep `ConnectionStrings__GymLink` pointed at your local SQL Server, or change only that value for your installed instance.
3. Replace `Jwt__SigningKey` with a local value containing at least 32 UTF-8 bytes.
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

## Start the Flutter clients

The clients require an explicit API address at build/run time.

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

## Phase boundary

Password reset and notifications are scheduled for Phase 7. Stripe is Phase 8,
chat is Phase 9, recommendations are Phase 10, and statistics/PDF reports are
Phase 11; their client navigation is intentionally absent until the matching
backend contracts exist.
