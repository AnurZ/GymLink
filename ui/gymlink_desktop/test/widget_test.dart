import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:gymlink_desktop/core/theme.dart';
import 'package:gymlink_desktop/features/auth/login_screen.dart';
import 'package:gymlink_desktop/features/desktop_frame.dart';
import 'package:gymlink_desktop/features/auth/password_reset_screens.dart';

void main() {
  testWidgets('desktop shell exposes role-specific navigation', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: const DesktopFrame(
          heading: 'Test',
          roleLabel: 'Centralni administrator',
          destinations: [
            DesktopDestination(
              'Pregled',
              Icons.home_outlined,
              Center(child: Text('Sadržaj')),
            ),
            DesktopDestination(
              'Korisnici',
              Icons.people_outline,
              Center(child: Text('Korisnici sadržaj')),
            ),
          ],
        ),
      ),
    );

    expect(find.text('GymLink Admin'), findsOneWidget);
    expect(find.text('Centralni administrator'), findsOneWidget);
    expect(find.text('Sadržaj'), findsOneWidget);
    await tester.tap(find.text('Korisnici'));
    await tester.pump();
    expect(find.text('Korisnici sadržaj'), findsOneWidget);
  });
  testWidgets('password reset validates empty input without leaving the form', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: const ResetPasswordScreen(initialEmail: ''),
      ),
    );

    await tester.tap(find.text('Promijeni lozinku'));
    await tester.pump();

    expect(find.text('Unesite email.'), findsOneWidget);
    expect(find.text('Kod mora sadržavati šest cifara.'), findsOneWidget);
    expect(find.byType(ResetPasswordScreen), findsOneWidget);
  });

  testWidgets('desktop login exposes and opens password recovery', (
    tester,
  ) async {
    final router = GoRouter(
      initialLocation: '/login',
      routes: [
        GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
        GoRoute(
          path: '/forgot-password',
          builder: (_, _) => const ForgotPasswordScreen(),
        ),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      MaterialApp.router(theme: buildGymLinkTheme(), routerConfig: router),
    );

    expect(find.text('Zaboravili ste lozinku?'), findsOneWidget);
    await tester.tap(find.text('Zaboravili ste lozinku?'));
    await tester.pumpAndSettle();

    expect(find.byType(ForgotPasswordScreen), findsOneWidget);
    expect(find.text('Pošalji kod'), findsOneWidget);
  });
}
