# GymLink Mobile

Flutter Android client for the `Member` and `Trainer` roles.

```powershell
flutter pub get
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:62287
```

The client restores refresh sessions from encrypted platform storage, performs
one centralized refresh after HTTP 401, preserves API ProblemDetails messages,
and routes authenticated users into the correct mobile role shell.

Verification:

```powershell
flutter analyze
flutter test
flutter build apk --release --target-platform android-arm64 `
  --dart-define=API_BASE_URL=http://10.0.2.2:62287
```
