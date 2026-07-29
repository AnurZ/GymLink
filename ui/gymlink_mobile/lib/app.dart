import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/auth.dart';
import 'core/app_errors.dart';
import 'core/theme.dart';
import 'core/payments.dart';
import 'features/auth/auth_screens.dart';
import 'features/member/member_shell.dart';
import 'features/notifications/notification_screen.dart';
import 'features/payments/payment_result_screen.dart';
import 'features/trainer/trainer_shell.dart';

class GymLinkMobileApp extends StatefulWidget {
  const GymLinkMobileApp({super.key});

  @override
  State<GymLinkMobileApp> createState() => _GymLinkMobileAppState();
}

class _GymLinkMobileAppState extends State<GymLinkMobileApp> {
  late final GoRouter _router;
  StreamSubscription<Uri>? _paymentLinkSubscription;

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
            state.matchedLocation == '/register' ||
            state.matchedLocation == '/forgot-password' ||
            state.matchedLocation == '/reset-password';
        final paymentResult = state.matchedLocation == '/payment/result';
        if (!auth.isAuthenticated) {
          if (signingIn) return null;
          final returnTo = paymentResult ? state.uri.toString() : null;
          return Uri(
            path: '/login',
            queryParameters: returnTo == null ? null : {'returnTo': returnTo},
          ).toString();
        }
        if (signingIn || state.matchedLocation == '/loading') {
          final returnTo = state.uri.queryParameters['returnTo'];
          return returnTo != null && returnTo.startsWith('/payment/result')
              ? returnTo
              : '/';
        }
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
          builder: (context, _) {
            final role = context.watch<AuthController>().session?.role;
            return switch (role) {
              'Member' => const MemberShell(),
              'Trainer' => const TrainerShell(),
              _ => const UnsupportedRoleScreen(),
            };
          },
        ),
        GoRoute(
          path: '/notifications',
          builder: (_, _) => const NotificationScreen(),
        ),
        GoRoute(
          path: '/payment/result',
          builder: (_, state) => PaymentResultScreen(
            outcome: state.uri.queryParameters['outcome'],
            paymentId: state.uri.queryParameters['payment_id'],
          ),
        ),
      ],
    );
    final paymentLinks = context.read<PaymentDeepLinks>();
    _paymentLinkSubscription = paymentLinks.links.listen(_openPaymentLink);
    final initialLink = paymentLinks.initialLink;
    if (initialLink != null) {
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => _openPaymentLink(initialLink),
      );
    }
  }

  void _openPaymentLink(Uri uri) {
    if (!PaymentDeepLinks.isPaymentResult(uri)) return;
    _router.go(
      Uri(
        path: '/payment/result',
        queryParameters: uri.queryParameters,
      ).toString(),
    );
  }

  @override
  void dispose() {
    _paymentLinkSubscription?.cancel();
    _router.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp.router(
    debugShowCheckedModeBanner: false,
    title: 'GymLink',
    theme: buildGymLinkTheme(),
    routerConfig: _router,
    builder: (context, child) => AppErrorBanner(child: child!),
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
