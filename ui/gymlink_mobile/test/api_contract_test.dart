import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:http/http.dart' as http;

void main() {
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
  });

  test('missing endpoint explains that the API must be restarted', () {
    final problem = ApiProblem.fromResponse(http.Response('', 404));

    expect(problem.code, 'endpoint_not_found');
    expect(problem.message, contains('Ponovo pokrenite'));
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
}
