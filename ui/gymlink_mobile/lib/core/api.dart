import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

abstract interface class AuthTokenSource {
  String? get accessToken;
  Future<bool> refresh();
  Future<void> invalidate();
}

final class ApiProblem implements Exception {
  ApiProblem({
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
    Map<String, dynamic> json = const {};
    try {
      final decoded = jsonDecode(response.body);
      if (decoded is Map<String, dynamic>) json = decoded;
    } on FormatException {
      // A non-JSON server/proxy response is represented by the safe fallback.
    }
    final errors = <String, List<String>>{};
    final rawErrors = json['errors'];
    if (rawErrors is Map) {
      for (final entry in rawErrors.entries) {
        final value = entry.value;
        errors[entry.key.toString()] = value is List
            ? value.map((item) => item.toString()).toList(growable: false)
            : [value.toString()];
      }
    }
    final code =
        json['title']?.toString() ??
        (response.statusCode == 404 ? 'endpoint_not_found' : 'request_failed');
    return ApiProblem(
      status: response.statusCode,
      code: code,
      message: _localizedMessage(
        response.statusCode,
        code,
        json['detail']?.toString(),
      ),
      fieldErrors: errors,
    );
  }

  static String _localizedMessage(
    int status,
    String code,
    String? detail,
  ) => switch (code) {
    'invalid_credentials' => 'Pogrešno korisničko ime/email ili lozinka.',
    'authentication_required' ||
    'invalid_refresh_token' => 'Sesija je istekla. Prijavite se ponovo.',
    'access_denied' => 'Nemate dozvolu za ovu radnju.',
    _ => switch (status) {
      404 when detail == null || detail.isEmpty =>
        'API endpoint nije pronađen. Ponovo pokrenite najnoviju verziju API-ja.',
      429 => 'Previše zahtjeva. Sačekajte i pokušajte ponovo.',
      500 => 'Došlo je do greške na serveru. Pokušajte ponovo.',
      503 => 'Usluga trenutno nije dostupna. Pokušajte ponovo.',
      _ when detail != null && detail.isNotEmpty => detail,
      _ => 'Zahtjev nije moguće izvršiti.',
    },
  };

  @override
  String toString() => message;
}

final class PagedData {
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

  bool get hasMore => page * pageSize < totalCount;

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

List<Map<String, dynamic>> mapListFromJson(Object? json) {
  if (json is! List || json.any((item) => item is! Map)) {
    throw ApiProblem(
      status: 0,
      code: 'invalid_response',
      message: 'Server je vratio neočekivan odgovor.',
    );
  }

  return json
      .map((item) => Map<String, dynamic>.from(item as Map))
      .toList(growable: false);
}

final class ApiClient {
  ApiClient(this._tokens, {http.Client? httpClient, String? baseUrlOverride})
    : _http = httpClient ?? http.Client(),
      _baseUrl = baseUrlOverride ?? baseUrl;

  static const baseUrl = String.fromEnvironment('API_BASE_URL');
  final AuthTokenSource _tokens;
  final http.Client _http;
  final String _baseUrl;
  Future<bool>? _refreshing;

  Uri _uri(String path, [Map<String, Object?> query = const {}]) {
    if (_baseUrl.isEmpty) {
      throw StateError(
        'API_BASE_URL nije postavljen. Pokrenite aplikaciju uz --dart-define.',
      );
    }
    final normalized = path.startsWith('/') ? path : '/$path';
    final values = <String, String>{};
    for (final entry in query.entries) {
      if (entry.value != null && entry.value.toString().isNotEmpty) {
        values[entry.key] = entry.value.toString();
      }
    }
    return Uri.parse(
      '${_baseUrl.replaceFirst(RegExp(r'/$'), '')}$normalized',
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

  Future<Object?> delete(String path, {Object? body}) =>
      _send('DELETE', path, body: body);

  Future<Object?> postMultipart(
    String path, {
    required List<int> bytes,
    required String fileName,
    required String contentType,
    required Map<String, String> fields,
  }) => _sendMultipart(
    path,
    bytes: bytes,
    fileName: fileName,
    contentType: contentType,
    fields: fields,
  );

  String? mediaUrl(Object? value) {
    final raw = value?.toString().trim() ?? '';
    if (raw.isEmpty) return null;
    final parsed = Uri.tryParse(raw);
    if (parsed?.hasScheme == true) return raw;
    return _uri(raw).toString();
  }

  Future<Object?> _send(
    String method,
    String path, {
    Map<String, Object?> query = const {},
    Object? body,
    bool authenticated = true,
    bool retry = true,
  }) async {
    try {
      final headers = <String, String>{'Accept': 'application/json'};
      if (body != null) headers['Content-Type'] = 'application/json';
      if (authenticated && _tokens.accessToken != null) {
        headers['Authorization'] = 'Bearer ${_tokens.accessToken}';
      }
      final request = http.Request(method, _uri(path, query));
      request.headers.addAll(headers);
      if (body != null) request.body = jsonEncode(body);
      final streamed = await _http
          .send(request)
          .timeout(const Duration(seconds: 20));
      final response = await http.Response.fromStream(streamed);
      if (response.statusCode == 401 && authenticated && retry) {
        final refreshFuture = _refreshing ??= _tokens.refresh();
        final refreshed = await refreshFuture.whenComplete(() {
          _refreshing = null;
        });
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
        throw ApiProblem(
          status: 401,
          code: 'authentication_required',
          message: 'Sesija je istekla. Prijavite se ponovo.',
        );
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiProblem.fromResponse(response);
      }
      if (response.body.trim().isEmpty) return null;
      return jsonDecode(response.body);
    } on ApiProblem {
      rethrow;
    } on TimeoutException {
      throw ApiProblem(
        status: 0,
        code: 'request_timeout',
        message: 'Zahtjev je istekao. Provjerite vezu i pokušajte ponovo.',
      );
    } on SocketException {
      throw ApiProblem(
        status: 0,
        code: 'network_unavailable',
        message: 'Nije moguće povezati se sa serverom.',
      );
    } on http.ClientException {
      throw ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Mrežni zahtjev nije uspio. Pokušajte ponovo.',
      );
    } on FormatException {
      throw ApiProblem(
        status: 0,
        code: 'invalid_response',
        message: 'Server je vratio neočekivan odgovor.',
      );
    } catch (_) {
      throw ApiProblem(
        status: 0,
        code: 'client_error',
        message: 'Zahtjev nije moguće izvršiti.',
      );
    }
  }

  Future<Object?> _sendMultipart(
    String path, {
    required List<int> bytes,
    required String fileName,
    required String contentType,
    required Map<String, String> fields,
    bool retry = true,
  }) async {
    try {
      final request = http.MultipartRequest('POST', _uri(path));
      request.headers['Accept'] = 'application/json';
      if (_tokens.accessToken != null) {
        request.headers['Authorization'] = 'Bearer ${_tokens.accessToken}';
      }
      request.fields.addAll(fields);
      request.files.add(
        http.MultipartFile.fromBytes(
          'file',
          bytes,
          filename: fileName,
          contentType: MediaType.parse(contentType),
        ),
      );
      final streamed = await _http
          .send(request)
          .timeout(const Duration(seconds: 30));
      final response = await http.Response.fromStream(streamed);
      if (response.statusCode == 401 && retry) {
        final refreshFuture = _refreshing ??= _tokens.refresh();
        final refreshed = await refreshFuture.whenComplete(() {
          _refreshing = null;
        });
        if (refreshed) {
          return _sendMultipart(
            path,
            bytes: bytes,
            fileName: fileName,
            contentType: contentType,
            fields: fields,
            retry: false,
          );
        }
        await _tokens.invalidate();
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiProblem.fromResponse(response);
      }
      if (response.body.trim().isEmpty) return null;
      return jsonDecode(response.body);
    } on ApiProblem {
      rethrow;
    } on TimeoutException {
      throw ApiProblem(
        status: 0,
        code: 'request_timeout',
        message: 'Zahtjev je istekao. Provjerite vezu i pokušajte ponovo.',
      );
    } on SocketException {
      throw ApiProblem(
        status: 0,
        code: 'network_unavailable',
        message: 'Nije moguće povezati se sa serverom.',
      );
    } on http.ClientException {
      throw ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Mrežni zahtjev nije uspio. Pokušajte ponovo.',
      );
    } on FormatException {
      throw ApiProblem(
        status: 0,
        code: 'invalid_response',
        message: 'Server je vratio neočekivan odgovor.',
      );
    }
  }

  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    final json = await get(
      path,
      query: {'page': 1, 'pageSize': 20, ...query},
      authenticated: authenticated,
    );
    return PagedData.fromJson(Map<String, dynamic>.from(json! as Map));
  }

  Future<List<Map<String, dynamic>>> list(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    final json = await get(path, query: query, authenticated: authenticated);
    return mapListFromJson(json);
  }

  void close() => _http.close();
}
