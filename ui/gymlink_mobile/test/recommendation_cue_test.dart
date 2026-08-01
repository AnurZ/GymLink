import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/features/member/member_shell.dart';

void main() {
  testWidgets('recommendation cue hides and persists after first open', (
    tester,
  ) async {
    final storage = _FakeRecommendationCueStorage();
    var opened = false;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          appBar: AppBar(
            actions: [
              RecommendationAttentionAction(
                storage: storage,
                onPressed: () => opened = true,
              ),
            ],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(_badge(tester).isLabelVisible, isTrue);
    await tester.tap(find.byKey(const Key('open-recommendations')));
    await tester.pump();

    expect(opened, isTrue);
    expect(storage.markCalls, 1);
    expect(_badge(tester).isLabelVisible, isFalse);
  });

  testWidgets('recommendation cue stays hidden when already seen', (
    tester,
  ) async {
    final storage = _FakeRecommendationCueStorage(hasSeen: true);
    await tester.pumpWidget(
      MaterialApp(
        home: RecommendationAttentionAction(storage: storage, onPressed: () {}),
      ),
    );
    await tester.pumpAndSettle();

    expect(_badge(tester).isLabelVisible, isFalse);
  });

  testWidgets('recommendation cue storage failures never block opening', (
    tester,
  ) async {
    final storage = _FakeRecommendationCueStorage(throwOnAccess: true);
    var opened = false;
    await tester.pumpWidget(
      MaterialApp(
        home: RecommendationAttentionAction(
          storage: storage,
          onPressed: () => opened = true,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(_badge(tester).isLabelVisible, isTrue);
    await tester.tap(find.byKey(const Key('open-recommendations')));
    await tester.pump();

    expect(opened, isTrue);
    expect(_badge(tester).isLabelVisible, isFalse);
    expect(tester.takeException(), isNull);
  });
}

Badge _badge(WidgetTester tester) =>
    tester.widget<Badge>(find.byKey(const Key('recommendation-attention-dot')));

final class _FakeRecommendationCueStorage implements RecommendationCueStorage {
  _FakeRecommendationCueStorage({
    this.hasSeen = false,
    this.throwOnAccess = false,
  });

  final bool hasSeen;
  final bool throwOnAccess;
  int markCalls = 0;

  @override
  Future<bool> hasSeenRecommendations() async {
    if (throwOnAccess) throw StateError('read failed');
    return hasSeen;
  }

  @override
  Future<void> markRecommendationsSeen() async {
    markCalls++;
    if (throwOnAccess) throw StateError('write failed');
  }
}
