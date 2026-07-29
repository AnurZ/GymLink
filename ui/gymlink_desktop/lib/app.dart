import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/auth.dart';
import 'core/app_errors.dart';
import 'core/theme.dart';
import 'features/auth/login_screen.dart';
import 'features/auth/password_reset_screens.dart';
import 'features/central/central_shell.dart';
import 'features/gym_admin/gym_admin_shell.dart';
import 'features/notifications/notification_screen.dart';

class GymLinkDesktopApp extends StatefulWidget {
  const GymLinkDesktopApp({super.key});

  @override
  State<GymLinkDesktopApp> createState() => _GymLinkDesktopAppState();
}

class _GymLinkDesktopAppState extends State<GymLinkDesktopApp> {
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    final auth = context.read<AuthController>();
    _router = GoRouter(
      refreshListenable: auth,
      redirect: (_, state) {
        if (auth.initializing) {
          return state.matchedLocation == '/loading' ? null : '/loading';
        }
        if (!auth.isAuthenticated) {
          final recovery =
              state.matchedLocation == '/login' ||
              state.matchedLocation == '/forgot-password' ||
              state.matchedLocation == '/reset-password';
          return recovery ? null : '/login';
        }
        if (state.matchedLocation == '/login' ||
            state.matchedLocation == '/loading') {
          return '/';
        }
        return null;
      },
      routes: [
        GoRoute(
          path: '/loading',
          builder: (_, _) =>
              const Scaffold(body: Center(child: CircularProgressIndicator())),
        ),
        GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
        GoRoute(
          path: '/forgot-password',
          builder: (_, _) => const ForgotPasswordScreen(),
        ),
        GoRoute(
          path: '/reset-password',
          builder: (_, state) => ResetPasswordScreen(
            initialEmail: state.uri.queryParameters['email'] ?? '',
          ),
        ),
        GoRoute(
          path: '/',
          builder: (context, _) =>
              switch (context.watch<AuthController>().session?.role) {
                'GymAdmin' => const GymAdminShell(),
                'CentralAdmin' => const CentralAdminShell(),
                _ => const _UnsupportedRole(),
              },
        ),
        GoRoute(
          path: '/notifications',
          builder: (_, _) => const NotificationScreen(),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
    debugShowCheckedModeBanner: false,
    title: 'GymLink Admin',
    theme: buildGymLinkTheme(),
    routerConfig: _router,
    builder: (context, child) => AppErrorBanner(child: child!),
  );
}

class _UnsupportedRole extends StatelessWidget {
  const _UnsupportedRole();
  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.phone_android, size: 56),
          const SizedBox(height: 16),
          Text(
            'Ovaj račun koristi GymLink mobilnu aplikaciju.',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: context.read<AuthController>().logout,
            child: const Text('Odjavi se'),
          ),
        ],
      ),
    ),
  );
}
