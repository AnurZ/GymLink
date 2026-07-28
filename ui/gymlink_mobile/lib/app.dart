import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/auth.dart';
import 'core/theme.dart';
import 'features/auth/auth_screens.dart';
import 'features/member/member_shell.dart';
import 'features/trainer/trainer_shell.dart';

class GymLinkMobileApp extends StatefulWidget {
  const GymLinkMobileApp({super.key});

  @override
  State<GymLinkMobileApp> createState() => _GymLinkMobileAppState();
}

class _GymLinkMobileAppState extends State<GymLinkMobileApp> {
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    final auth = context.read<AuthController>();
    _router = GoRouter(
      initialLocation: '/',
      refreshListenable: auth,
      redirect: (context, state) {
        if (auth.initializing) {
          return state.matchedLocation == '/loading' ? null : '/loading';
        }
        final signingIn =
            state.matchedLocation == '/login' ||
            state.matchedLocation == '/register';
        if (!auth.isAuthenticated) return signingIn ? null : '/login';
        if (signingIn || state.matchedLocation == '/loading') return '/';
        return null;
      },
      routes: [
        GoRoute(path: '/loading', builder: (_, _) => const _LoadingScreen()),
        GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
        GoRoute(
          path: '/register',
          builder: (_, _) => const RegistrationScreen(),
        ),
        GoRoute(
          path: '/',
          builder: (context, _) {
            final role = context.watch<AuthController>().session?.role;
            return switch (role) {
              'Member' => const MemberShell(),
              'Trainer' => const TrainerShell(),
              _ => const UnsupportedRoleScreen(),
            };
          },
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
    debugShowCheckedModeBanner: false,
    title: 'GymLink',
    theme: buildGymLinkTheme(),
    routerConfig: _router,
  );
}

class _LoadingScreen extends StatelessWidget {
  const _LoadingScreen();

  @override
  Widget build(BuildContext context) =>
      const Scaffold(body: Center(child: CircularProgressIndicator()));
}

class UnsupportedRoleScreen extends StatelessWidget {
  const UnsupportedRoleScreen({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.desktop_windows_outlined, size: 56),
            const SizedBox(height: 16),
            Text(
              'Ovaj račun koristi GymLink desktop aplikaciju.',
              textAlign: TextAlign.center,
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
    ),
  );
}
