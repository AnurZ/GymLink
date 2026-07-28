import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/theme.dart';
import 'package:gymlink_mobile/features/member/reservation_screen.dart';
import 'package:gymlink_mobile/features/trainer/trainer_screens.dart';
import 'package:gymlink_mobile/shared/widgets.dart';

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
}
