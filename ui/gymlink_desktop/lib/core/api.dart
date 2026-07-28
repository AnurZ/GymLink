import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

abstract interface class AuthTokenSource {
  String? get accessToken;
  Future<bool> refresh();
  Future<void> invalidate();
}

class ApiProblem implements Exception {
  const ApiProblem({
    required this.status,
    required this.code,
    required this.message,
    this.fieldErrors = const {},
  });

  final int status;
  final String code;
  final String message;
  final Map<String, List<String>> fieldErrors;

  factory ApiProblem.fromResponse(http.Response response) {
    Map<String, dynamic> data = const {};
    try {
      final decoded = jsonDecode(response.body);
      if (decoded is Map<String, dynamic>) data = decoded;
    } on FormatException {
      // A proxy/non-JSON response uses the safe fallback below.
    }
    final errors = <String, List<String>>{};
    if (data['errors'] case final Map rawErrors) {
      for (final entry in rawErrors.entries) {
        errors[entry.key.toString()] = entry.value is List
            ? (entry.value as List).map((value) => value.toString()).toList()
            : [entry.value.toString()];
      }
    }
    return ApiProblem(
      status: response.statusCode,
      code:
          data['title']?.toString() ??
          (response.statusCode == 404
              ? 'endpoint_not_found'
              : 'request_failed'),
      message:
          data['detail']?.toString() ??
          switch (response.statusCode) {
            404 =>
              'API endpoint nije pronađen. Ponovo pokrenite najnoviju verziju API-ja.',
            >= 500 => 'Server trenutno nije dostupan.',
            _ => 'Zahtjev nije moguće izvršiti.',
          },
      fieldErrors: errors,
    );
  }

  @override
  String toString() => message;
}

class PagedData {
  const PagedData({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
  });
  final List<Map<String, dynamic>> items;
  final int page;
  final int pageSize;
  final int totalCount;

  factory PagedData.fromJson(Map<String, dynamic> json) => PagedData(
    items: (json['items'] as List? ?? const [])
        .whereType<Map>()
        .map((item) => Map<String, dynamic>.from(item))
        .toList(growable: false),
    page: (json['page'] as num?)?.toInt() ?? 1,
    pageSize: (json['pageSize'] as num?)?.toInt() ?? 20,
    totalCount: (json['totalCount'] as num?)?.toInt() ?? 0,
  );
}

class ApiClient {
  ApiClient(this._tokens, {http.Client? httpClient})
    : _http = httpClient ?? http.Client();

  static const baseUrl = String.fromEnvironment('API_BASE_URL');
  final AuthTokenSource _tokens;
  final http.Client _http;
  Future<bool>? _refreshing;

  Uri _uri(String path, Map<String, Object?> query) {
    if (baseUrl.isEmpty) {
      throw StateError(
        'API_BASE_URL nije postavljen. Pokrenite aplikaciju uz --dart-define.',
      );
    }
    final values = <String, String>{};
    for (final entry in query.entries) {
      if (entry.value != null && entry.value.toString().isNotEmpty) {
        values[entry.key] = entry.value.toString();
      }
    }
    return Uri.parse(
      '${baseUrl.replaceFirst(RegExp(r'/$'), '')}${path.startsWith('/') ? path : '/$path'}',
    ).replace(queryParameters: values.isEmpty ? null : values);
  }

  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) => _send('GET', path, query: query, authenticated: authenticated);

  Future<Object?> post(
    String path, {
    Object? body,
    bool authenticated = true,
  }) => _send('POST', path, body: body, authenticated: authenticated);

  Future<Object?> put(String path, {Object? body}) =>
      _send('PUT', path, body: body);

  Future<void> delete(String path) async => _send('DELETE', path);

  Future<Object?> _send(
    String method,
    String path, {
    Map<String, Object?> query = const {},
    Object? body,
    bool authenticated = true,
    bool retry = true,
  }) async {
    final request = http.Request(method, _uri(path, query));
    request.headers['Accept'] = 'application/json';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    if (authenticated && _tokens.accessToken != null) {
      request.headers['Authorization'] = 'Bearer ${_tokens.accessToken}';
    }
    final response = await http.Response.fromStream(
      await _http.send(request).timeout(const Duration(seconds: 20)),
    );
    if (response.statusCode == 401 && authenticated && retry) {
      final future = _refreshing ??= _tokens.refresh();
      final refreshed = await future.whenComplete(() => _refreshing = null);
      if (refreshed) {
        return _send(
          method,
          path,
          query: query,
          body: body,
          authenticated: authenticated,
          retry: false,
        );
      }
      await _tokens.invalidate();
    }
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiProblem.fromResponse(response);
    }
    return response.body.trim().isEmpty ? null : jsonDecode(response.body);
  }

  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async => PagedData.fromJson(
    Map<String, dynamic>.from(
      (await get(path, query: {'page': 1, 'pageSize': 50, ...query}))! as Map,
    ),
  );
}
