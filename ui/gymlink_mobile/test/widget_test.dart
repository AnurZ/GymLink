import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/core/theme.dart';
import 'package:gymlink_mobile/features/auth/auth_screens.dart';
import 'package:gymlink_mobile/features/member/gym_screens.dart';
import 'package:gymlink_mobile/features/member/reservation_screen.dart';
import 'package:gymlink_mobile/features/trainer/trainer_screens.dart';
import 'package:gymlink_mobile/shared/widgets.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets(
    'GymLink status and empty states render approved visual language',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(
            body: Column(
              children: [
                StatusPill('Active'),
                Expanded(
                  child: EmptyState(
                    title: 'Nema rezultata',
                    message: 'Promijenite filter i pokušajte ponovo.',
                  ),
                ),
              ],
            ),
          ),
        ),
      );

      expect(find.text('Active'), findsOneWidget);
      expect(find.text('Nema rezultata'), findsOneWidget);
      expect(find.byIcon(Icons.inbox_outlined), findsOneWidget);
    },
  );

  testWidgets('trainer cancellation requires a reason without closing', (
    tester,
  ) async {
    String? submittedReason;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                submittedReason = await showDialog<String>(
                  context: context,
                  builder: (_) => const TrainerCancellationReasonDialog(),
                );
              },
              child: const Text('Otkaži'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Otkaži'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Nastavi'));
    await tester.pump();

    expect(find.text('Unesite razlog otkazivanja.'), findsOneWidget);
    expect(find.byType(TrainerCancellationReasonDialog), findsOneWidget);
    expect(submittedReason, isNull);

    await tester.enterText(find.byType(TextFormField), 'Bolest trenera');
    await tester.tap(find.text('Nastavi'));
    await tester.pumpAndSettle();

    expect(find.byType(TrainerCancellationReasonDialog), findsNothing);
    expect(submittedReason, 'Bolest trenera');
    expect(tester.takeException(), isNull);
  });

  testWidgets('trainer review accepts an empty optional comment', (
    tester,
  ) async {
    Map<String, Object?>? submittedReview;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                submittedReview = await showDialog<Map<String, Object?>>(
                  context: context,
                  builder: (_) => const TrainerReviewDialog(),
                );
              },
              child: const Text('Ocijeni'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Ocijeni'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Objavi'));
    await tester.pumpAndSettle();

    expect(find.byType(TrainerReviewDialog), findsNothing);
    expect(submittedReview, {'rating': 5, 'comment': null});
    expect(tester.takeException(), isNull);
  });
  testWidgets('password reset keeps the form open for empty values', (
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

  testWidgets('login exposes and opens password recovery', (tester) async {
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

  testWidgets('active gym membership is explained and purchase is disabled', (
    tester,
  ) async {
    final client = MockClient((request) async {
      final body = switch (request.url.path) {
        '/api/gyms/gym-1' => {
          'id': 'gym-1',
          'name': 'Test Gym',
          'description': 'Test description',
          'address': 'Testna 1',
          'city': 'Sarajevo',
          'averageRating': 4.5,
          'reviewCount': 2,
          'imageUrls': <String>[],
        },
        '/api/gyms/gym-1/membership-plans' => [
          {
            'id': 'plan-1',
            'name': 'Mjesečna',
            'durationDays': 30,
            'price': 50,
            'currency': 'BAM',
          },
        ],
        '/api/gyms/gym-1/trainers' => <Object>[],
        '/api/gyms/gym-1/reviews' => _page(<Object>[]),
        '/api/me/memberships' => _page([
          {
            'id': 'membership-1',
            'gymId': 'gym-1',
            'status': 1,
            'endsAtUtc': '2030-01-31T00:00:00Z',
          },
        ]),
        '/api/me/membership-requests' => _page(<Object>[]),
        _ => throw StateError('Unexpected request: ${request.url}'),
      };
      return http.Response(
        jsonEncode(body),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const GymDetailsScreen(gymId: 'gym-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final renderedText = tester
        .widgetList<Text>(find.byType(Text))
        .map((widget) => widget.data)
        .whereType<String>()
        .join(' | ');
    expect(
      find.byType(ListView),
      findsOneWidget,
      reason: 'Rendered text: $renderedText',
    );
    await tester.drag(find.byType(ListView), const Offset(0, -420));
    await tester.pumpAndSettle();

    expect(find.text('Status članstva'), findsOneWidget);
    expect(find.textContaining('Već imate aktivno članstvo'), findsOneWidget);
    final purchase = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, '50 BAM'),
    );
    expect(purchase.onPressed, isNull);
    expect(tester.takeException(), isNull);
  });
}

Map<String, Object> _page(List<Object> items) => {
  'items': items,
  'page': 1,
  'pageSize': 20,
  'totalCount': items.length,
};

class _TestTokenSource implements AuthTokenSource {
  @override
  String? get accessToken => 'test-token';

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}
