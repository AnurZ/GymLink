import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_desktop/core/api.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  test('ProblemDetails keeps backend validation messages', () {
    final problem = ApiProblem.fromResponse(
      http.Response.bytes(
        utf8.encode(
          '{"title":"concurrency_conflict",'
          '"detail":"The record changed.",'
          '"errors":{"ConcurrencyToken":["Reload the record."]}}',
        ),
        409,
      ),
    );

    expect(problem.status, 409);
    expect(problem.code, 'concurrency_conflict');
    expect(problem.fieldErrors['ConcurrencyToken'], ['Reload the record.']);
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

  test('location and GymAdmin business failures are localized safely', () {
    final outside = ApiProblem.fromResponse(
      http.Response(
        '{"title":"location_outside_bih",'
        '"detail":"The selected location must be in Bosnia and Herzegovina."}',
        400,
      ),
    );
    final assigned = ApiProblem.fromResponse(
      http.Response(
        '{"title":"gym_admin_already_assigned",'
        '"detail":"The selected account already has an active gym assignment."}',
        409,
      ),
    );

    expect(
      outside.message,
      'Odabrana lokacija mora biti u Bosni i Hercegovini.',
    );
    expect(
      assigned.message,
      'Odabrani korisnik je već dodijeljen drugoj teretani. '
      'Izaberite drugog korisnika.',
    );
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

  test('paged response parses list and totals', () {
    final page = PagedData.fromJson({
      'items': [
        {'name': 'GymLink'},
      ],
      'page': 1,
      'pageSize': 50,
      'totalCount': 1,
    });

    expect(page.items.single['name'], 'GymLink');
    expect(page.totalCount, 1);
  });

  test('trainer image URLs and multipart upload use the API origin', () async {
    late http.Request captured;
    final api = ApiClient(
      _Tokens(),
      baseUrlOverride: 'http://localhost:62287',
      httpClient: MockClient((request) async {
        captured = request;
        return http.Response(
          '{"trainerProfileId":"trainer-1","imageUrl":null,'
          '"contentType":null,"fileSizeBytes":null,"concurrencyToken":"next"}',
          200,
        );
      }),
    );

    expect(
      api.mediaUrl('/uploads/trainer-images/image.webp'),
      'http://localhost:62287/uploads/trainer-images/image.webp',
    );
    await api.postMultipart(
      '/api/tenant/trainers/trainer-1/image',
      bytes: const [0x52, 0x49, 0x46, 0x46],
      fileName: 'trainer.webp',
      contentType: 'image/webp',
      fields: const {'concurrencyToken': 'current'},
    );

    expect(captured.headers['content-type'], contains('multipart/form-data'));
    expect(utf8.decode(captured.bodyBytes), contains('current'));
    expect(utf8.decode(captured.bodyBytes), contains('trainer.webp'));
  });
}

final class _Tokens implements AuthTokenSource {
  @override
  String? get accessToken => null;

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}
