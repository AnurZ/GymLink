# GymLink UI

This directory contains the two approved Flutter clients:

```text
ui/
  gymlink_mobile/   # Android: Member and Trainer
  gymlink_desktop/  # Windows: GymAdmin and CentralAdmin
```

Both clients use Provider/ChangeNotifier feature state, centralized authenticated
HTTP transport, one refresh attempt after HTTP 401, secure refresh-token
storage, role-specific navigation, and the approved GymLink visual system.

Run them with an explicit API endpoint:

```powershell
cd ui/gymlink_mobile
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:62287

cd ../gymlink_desktop
flutter run -d windows `
  --dart-define=API_BASE_URL=http://localhost:62287
```

Never commit an environment-specific API address or secret.
