import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/features/recommendations/recommendation_controller.dart';
import 'package:gymlink_mobile/features/recommendations/recommendation_repository.dart';
import 'package:gymlink_mobile/features/recommendations/recommendation_screen.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('renders a mixed Figure 9 feed on a narrow screen', (
    tester,
  ) async {
    await tester.binding.setSurfaceSize(const Size(320, 700));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final api = _api((request) async {
      expect(request.method, 'GET');
      expect(request.url.path, '/api/me/recommendations');
      expect(request.url.queryParameters['limit'], '6');
      return _jsonResponse(_feedJson());
    });

    await tester.pumpWidget(_harness(api));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('recommendation-feed')), findsOneWidget);
    expect(find.byKey(const Key('recommendation-0-gym-1')), findsOneWidget);
    expect(find.byKey(const Key('recommendation-1-trainer-1')), findsOneWidget);
    expect(find.text('Pogledaj detalje'), findsNWidgets(2));
    expect(
      find.text('Odgovara vašoj omiljenoj vrsti treninga'),
      findsOneWidget,
    );

    await tester.scrollUntilVisible(
      find.byKey(const Key('recommendation-activity-summary')),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('Na osnovu vaših aktivnosti'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('preference editor preserves ranked choices after save failure', (
    tester,
  ) async {
    final api = _api((request) async {
      if (request.url.path == '/api/me/recommendations') {
        return _jsonResponse(_feedJson());
      }
      if (request.url.path == '/api/me/preferences' &&
          request.method == 'GET') {
        return _jsonResponse([
          {
            'rank': 1,
            'cityId': 'city-1',
            'city': 'Sarajevo',
            'trainingTypeId': 'type-1',
            'trainingType': 'Snaga',
            'weight': 1.0,
          },
        ]);
      }
      if (request.url.path == '/api/reference-data/lookups') {
        return _jsonResponse({
          'cities': [
            {'id': 'city-1', 'name': 'Sarajevo'},
            {'id': 'city-2', 'name': 'Mostar'},
          ],
          'trainingTypes': [
            {'id': 'type-1', 'name': 'Snaga'},
            {'id': 'type-2', 'name': 'Yoga'},
          ],
        });
      }
      if (request.url.path == '/api/me/preferences' &&
          request.method == 'PUT') {
        return _jsonResponse({
          'title': 'validation_failed',
          'detail': 'Preference nisu sačuvane.',
        }, status: 400);
      }
      fail('Unexpected request: ${request.method} ${request.url}');
    });

    await tester.pumpWidget(_harness(api));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('edit-recommendation-preferences')));
    await tester.pumpAndSettle();

    expect(find.text('Glavna'), findsOneWidget);
    expect(find.text('Sarajevo'), findsOneWidget);
    expect(find.text('Snaga'), findsOneWidget);
    await tester.tap(find.byKey(const Key('save-ranked-preferences')));
    await tester.pumpAndSettle();

    expect(find.text('Preference nisu sačuvane.'), findsOneWidget);
    expect(find.text('Sarajevo'), findsOneWidget);
    expect(find.text('Snaga'), findsOneWidget);
  });

  test('rejects recommendation results without an explanation', () {
    final json = _feedJson();
    (json['items']! as List<Object?>).add({
      'targetType': 0,
      'targetId': 'gym-unexplained',
      'gymId': 'gym-unexplained',
      'name': 'Bez razloga',
      'subtitle': 'Sarajevo',
      'imageUrl': null,
      'ratingAverage': 5,
      'ratingCount': 1,
      'score': .9,
      'reason': '',
    });

    expect(RecommendationFeed.fromJson(json).items, hasLength(2));
  });

  test('load and refresh both request six recommendations', () async {
    final requests = <http.Request>[];
    final repository = RecommendationRepository(
      _api((request) async {
        requests.add(request);
        return _jsonResponse(_feedJson());
      }),
    );

    await repository.getFeed();
    await repository.getFeed(force: true);

    expect(requests, hasLength(2));
    expect(requests[0].url.queryParameters['limit'], '6');
    expect(requests[1].url.queryParameters['limit'], '6');
  });
}

Widget _harness(ApiClient api) {
  final repository = RecommendationRepository(api);
  return MultiProvider(
    providers: [
      Provider<ApiClient>.value(value: api),
      ChangeNotifierProvider(
        create: (_) => RecommendationController(repository),
      ),
    ],
    child: const MaterialApp(home: RecommendationScreen()),
  );
}

ApiClient _api(Future<http.Response> Function(http.Request) handler) =>
    ApiClient(
      _Tokens(),
      baseUrlOverride: 'https://example.test',
      httpClient: MockClient(handler),
    );

http.Response _jsonResponse(Object body, {int status = 200}) => http.Response(
  jsonEncode(body),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);

Map<String, Object?> _feedJson() => {
  'algorithmVersion': 'gymlink-hybrid-v1',
  'generatedAtUtc': '2026-08-01T10:00:00Z',
  'items': <Object?>[
    {
      'targetType': 0,
      'targetId': 'gym-1',
      'gymId': 'gym-1',
      'name': 'Gym Sarajevo',
      'subtitle': 'Sarajevo',
      'imageUrl': null,
      'ratingAverage': 4.8,
      'ratingCount': 20,
      'score': .91,
      'reason': 'Odgovara vašoj omiljenoj vrsti treninga',
    },
    {
      'targetType': 1,
      'targetId': 'trainer-1',
      'gymId': 'gym-1',
      'name': 'Amina Trener',
      'subtitle': 'Gym Sarajevo',
      'imageUrl': null,
      'ratingAverage': 4.7,
      'ratingCount': 12,
      'score': .88,
      'reason': 'Popularno među članovima',
    },
  ],
  'activitySummary': {
    'mostFrequentTrainingType': 'Snaga',
    'averageReservationsPerWeek': 2.5,
    'preferredCity': 'Sarajevo',
  },
};

final class _Tokens implements AuthTokenSource {
  @override
  String? get accessToken => 'test-token';

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}
