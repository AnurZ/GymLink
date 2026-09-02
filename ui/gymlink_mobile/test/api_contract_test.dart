import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  test('field errors are case-insensitive and support explicit aliases', () {
    final problem = ApiProblem(
      status: 400,
      code: 'validation_failed',
      message: 'Validation failed',
      fieldErrors: const {
        'membershipplan.name': ['Naziv plana nije ispravan.'],
      },
    );

    expect(
      problem.fieldError('Name', aliases: const ['MembershipPlan.Name']),
      'Naziv plana nije ispravan.',
    );
  });

  test('ProblemDetails preserves safe field and business messages', () {
    final problem = ApiProblem.fromResponse(
      http.Response.bytes(
        utf8.encode(
          '{"title":"validation_failed","detail":"Provjerite unos.",'
          '"errors":{"Email":["Email nije ispravan."]}}',
        ),
        400,
        headers: {'content-type': 'application/problem+json'},
      ),
    );

    expect(problem.status, 400);
    expect(problem.code, 'validation_failed');
    expect(problem.message, 'Provjerite unos.');
    expect(problem.fieldErrors['Email'], ['Email nije ispravan.']);
    expect(problem.firstFieldError, 'Email nije ispravan.');
  });

  test('missing endpoint explains that the API must be restarted', () {
    final problem = ApiProblem.fromResponse(http.Response('', 404));

    expect(problem.code, 'endpoint_not_found');
    expect(problem.message, contains('Ponovo pokrenite'));
  });

  test('invalid credentials are localized safely', () {
    final problem = ApiProblem.fromResponse(
      http.Response(
        '{"title":"invalid_credentials","detail":"Internal English text"}',
        401,
      ),
    );

    expect(problem.message, 'Pogrešno korisničko ime/email ili lozinka.');
  });

  test('transport failures become user-facing API problems', () async {
    final api = ApiClient(
      _Tokens(),
      baseUrlOverride: 'https://example.test',
      httpClient: MockClient((_) => throw http.ClientException('offline')),
    );

    await expectLater(
      api.get('/api/test', authenticated: false),
      throwsA(
        isA<ApiProblem>().having(
          (problem) => problem.code,
          'code',
          'network_error',
        ),
      ),
    );
  });

  test('paged responses keep bounded query metadata', () {
    final page = PagedData.fromJson({
      'items': [
        {'id': 'one'},
      ],
      'page': 2,
      'pageSize': 20,
      'totalCount': 45,
    });

    expect(page.items.single['id'], 'one');
    expect(page.hasMore, isTrue);
  });

  test('array responses map catalog items without paged-envelope casts', () {
    final items = mapListFromJson([
      {'id': 'plan-one', 'name': 'Mjesečna'},
      {'id': 'plan-two', 'name': 'Godišnja'},
    ]);

    expect(items.map((item) => item['id']), ['plan-one', 'plan-two']);
  });

  test('array response parser rejects an unexpected object envelope', () {
    expect(
      () => mapListFromJson({'items': <Object?>[]}),
      throwsA(
        isA<ApiProblem>().having(
          (problem) => problem.code,
          'code',
          'invalid_response',
        ),
      ),
    );
  });

  test('business catalog reads send the bearer token', () async {
    final captured = <http.Request>[];
    final api = ApiClient(
      _AuthenticatedTokens(),
      baseUrlOverride: 'https://example.test',
      httpClient: MockClient((request) async {
        captured.add(request);
        return http.Response('{}', 200);
      }),
    );
    const paths = [
      '/api/gyms',
      '/api/gyms/gym-1',
      '/api/gyms/gym-1/trainers',
      '/api/gyms/gym-1/membership-plans',
      '/api/trainers/trainer-1/offerings',
      '/api/trainers/trainer-1/availability',
      '/api/trainers/trainer-1/availability-calendar',
      '/api/trainers/trainer-1/reviews',
      '/api/gyms/gym-1/reviews',
      '/api/reference-data/lookups',
    ];

    for (final path in paths) {
      await api.get(path);
    }

    expect(captured, hasLength(paths.length));
    expect(
      captured.map((request) => request.headers['authorization']).toSet(),
      {'Bearer token'},
    );
  });

  test('auth calls remain anonymous even when a token exists', () async {
    final captured = <http.Request>[];
    final api = ApiClient(
      _AuthenticatedTokens(),
      baseUrlOverride: 'https://example.test',
      httpClient: MockClient((request) async {
        captured.add(request);
        return http.Response('{}', 200);
      }),
    );

    for (final path in const [
      '/api/auth/login',
      '/api/auth/register',
      '/api/auth/forgot-password',
      '/api/auth/reset-password',
    ]) {
      await api.post(path, authenticated: false);
    }

    expect(
      captured.every(
        (request) => !request.headers.containsKey('authorization'),
      ),
      isTrue,
    );
  });

  test(
    'authenticated 401 refreshes once and retries with the new token',
    () async {
      final tokens = _RefreshingTokens();
      final captured = <http.Request>[];
      final api = ApiClient(
        tokens,
        baseUrlOverride: 'https://example.test',
        httpClient: MockClient((request) async {
          captured.add(request);
          return captured.length == 1
              ? http.Response('{"title":"authentication_required"}', 401)
              : http.Response('{}', 200);
        }),
      );

      await api.get('/api/gyms');

      expect(tokens.refreshCount, 1);
      expect(tokens.invalidateCount, 0);
      expect(captured.map((request) => request.headers['authorization']), [
        'Bearer old-token',
        'Bearer new-token',
      ]);
    },
  );

  test('trainer image URLs resolve against the configured API origin', () {
    final api = ApiClient(
      _Tokens(),
      baseUrlOverride: 'http://10.0.2.2:62287',
      httpClient: MockClient((_) async => http.Response('{}', 200)),
    );

    expect(
      api.mediaUrl('/uploads/trainer-images/image.jpg'),
      'http://10.0.2.2:62287/uploads/trainer-images/image.jpg',
    );
    expect(api.mediaUrl(null), isNull);
  });

  test(
    'trainer image upload sends multipart file and concurrency token',
    () async {
      late http.Request captured;
      final api = ApiClient(
        _Tokens(),
        baseUrlOverride: 'https://example.test',
        httpClient: MockClient((request) async {
          captured = request;
          return http.Response(
            '{"trainerProfileId":"trainer-1","imageUrl":null,'
            '"contentType":null,"fileSizeBytes":null,"concurrencyToken":"next"}',
            200,
          );
        }),
      );

      await api.postMultipart(
        '/api/profile/trainer-image',
        bytes: const [0xFF, 0xD8, 0xFF],
        fileName: 'trainer.jpg',
        contentType: 'image/jpeg',
        fields: const {'concurrencyToken': 'current'},
      );

      expect(captured.method, 'POST');
      expect(captured.headers['content-type'], contains('multipart/form-data'));
      expect(latin1.decode(captured.bodyBytes), contains('current'));
      expect(latin1.decode(captured.bodyBytes), contains('trainer.jpg'));
    },
  );
}

final class _Tokens implements AuthTokenSource {
  @override
  String? get accessToken => null;

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}

final class _AuthenticatedTokens implements AuthTokenSource {
  @override
  String? get accessToken => 'token';

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}

final class _RefreshingTokens implements AuthTokenSource {
  String _token = 'old-token';
  int refreshCount = 0;
  int invalidateCount = 0;

  @override
  String? get accessToken => _token;

  @override
  Future<void> invalidate() async {
    invalidateCount++;
  }

  @override
  Future<bool> refresh() async {
    refreshCount++;
    _token = 'new-token';
    return true;
  }
}
