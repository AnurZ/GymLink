# GymLink Admin

Flutter Windows client for the `GymAdmin` and `CentralAdmin` roles.

```powershell
flutter pub get
flutter run -d windows `
  --dart-define=API_BASE_URL=http://localhost:62287
```

The client uses role-specific wide navigation, secure refresh-session storage,
central HTTP 401 handling, bounded administrative lists, concurrency tokens,
and backend-authoritative workflow actions.

Verification:

```powershell
flutter analyze
flutter test
flutter build windows --release `
  --dart-define=API_BASE_URL=http://localhost:62287
```
