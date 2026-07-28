import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_desktop/core/api.dart';
import 'package:http/http.dart' as http;

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
}
