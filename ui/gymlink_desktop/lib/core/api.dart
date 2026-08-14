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

  String? fieldError(String field, {Iterable<String> aliases = const []}) {
    final names = {field, ...aliases}.map((value) => value.toLowerCase());
    for (final entry in fieldErrors.entries) {
      if (!names.contains(entry.key.toLowerCase())) continue;
      for (final value in entry.value) {
        if (value.trim().isNotEmpty) return value;
      }
    }
    return null;
  }

  String? get firstFieldError {
    for (final values in fieldErrors.values) {
      for (final value in values) {
        if (value.trim().isNotEmpty) return value;
      }
    }
    return null;
  }

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
    final code =
        data['title']?.toString() ??
        (response.statusCode == 404 ? 'endpoint_not_found' : 'request_failed');
    return ApiProblem(
      status: response.statusCode,
      code: code,
      message: _localizedMessage(
        response.statusCode,
        code,
        data['detail']?.toString(),
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
    'location_outside_bih' =>
      'Odabrana lokacija mora biti u Bosni i Hercegovini.',
    'location_not_resolved' =>
      'Za ovu tačku nije pronađena upotrebljiva adresa. Izaberite drugu lokaciju.',
    'gym_admin_already_assigned' =>
      'Odabrani korisnik je već dodijeljen drugoj teretani. Izaberite drugog korisnika.',
    'tenant_gym_admin_exists' => 'Ova teretana već ima aktivnog GymAdmina.',
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

class DownloadedFile {
  const DownloadedFile({
    required this.bytes,
    required this.fileName,
    required this.contentType,
    required this.recordCount,
  });

  final List<int> bytes;
  final String fileName;
  final String contentType;
  final int recordCount;
}

class MultipartUploadPart {
  const MultipartUploadPart({
    required this.fieldName,
    required this.bytes,
    required this.fileName,
    required this.contentType,
  });

  final String fieldName;
  final List<int> bytes;
  final String fileName;
  final String contentType;
}

class ApiClient {
  ApiClient(this._tokens, {http.Client? httpClient, String? baseUrlOverride})
    : _http = httpClient ?? http.Client(),
      _baseUrl = baseUrlOverride ?? baseUrl;

  static const baseUrl = String.fromEnvironment('API_BASE_URL');
  final AuthTokenSource _tokens;
  final http.Client _http;
  final String _baseUrl;
  Future<bool>? _refreshing;

  Uri _uri(String path, Map<String, Object?> query) {
    if (_baseUrl.isEmpty) {
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
      '${_baseUrl.replaceFirst(RegExp(r'/$'), '')}${path.startsWith('/') ? path : '/$path'}',
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

  Future<DownloadedFile> download(String path) => _download(path);

  Future<Object?> postMultipart(
    String path, {
    required List<int> bytes,
    required String fileName,
    required String contentType,
    required Map<String, String> fields,
  }) => _sendMultipartRequest(
    'POST',
    path,
    fields: fields,
    files: [
      MultipartUploadPart(
        fieldName: 'file',
        bytes: bytes,
        fileName: fileName,
        contentType: contentType,
      ),
    ],
  );

  Future<Object?> putMultipart(
    String path, {
    required Map<String, String> fields,
    required List<MultipartUploadPart> files,
  }) => _sendMultipartRequest('PUT', path, fields: fields, files: files);

  String? mediaUrl(Object? value) {
    final raw = value?.toString().trim() ?? '';
    if (raw.isEmpty) return null;
    final parsed = Uri.tryParse(raw);
    if (parsed?.hasScheme == true) return raw;
    return _uri(raw, const {}).toString();
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
        throw const ApiProblem(
          status: 401,
          code: 'authentication_required',
          message: 'Sesija je istekla. Prijavite se ponovo.',
        );
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiProblem.fromResponse(response);
      }
      return response.body.trim().isEmpty ? null : jsonDecode(response.body);
    } on ApiProblem {
      rethrow;
    } on TimeoutException {
      throw const ApiProblem(
        status: 0,
        code: 'request_timeout',
        message: 'Zahtjev je istekao. Provjerite vezu i pokušajte ponovo.',
      );
    } on SocketException {
      throw const ApiProblem(
        status: 0,
        code: 'network_unavailable',
        message: 'Nije moguće povezati se sa serverom.',
      );
    } on http.ClientException {
      throw const ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Mrežni zahtjev nije uspio. Pokušajte ponovo.',
      );
    } on FormatException {
      throw const ApiProblem(
        status: 0,
        code: 'invalid_response',
        message: 'Server je vratio neočekivan odgovor.',
      );
    } catch (_) {
      throw const ApiProblem(
        status: 0,
        code: 'client_error',
        message: 'Zahtjev nije moguće izvršiti.',
      );
    }
  }

  Future<Object?> _sendMultipartRequest(
    String method,
    String path, {
    required Map<String, String> fields,
    required List<MultipartUploadPart> files,
    bool retry = true,
  }) async {
    try {
      final request = http.MultipartRequest(method, _uri(path, const {}));
      request.headers['Accept'] = 'application/json';
      if (_tokens.accessToken != null) {
        request.headers['Authorization'] = 'Bearer ${_tokens.accessToken}';
      }
      request.fields.addAll(fields);
      request.files.addAll(
        files.map(
          (file) => http.MultipartFile.fromBytes(
            file.fieldName,
            file.bytes,
            filename: file.fileName,
            contentType: MediaType.parse(file.contentType),
          ),
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
          return _sendMultipartRequest(
            method,
            path,
            fields: fields,
            files: files,
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
      throw const ApiProblem(
        status: 0,
        code: 'request_timeout',
        message: 'Zahtjev je istekao. Provjerite vezu i pokušajte ponovo.',
      );
    } on SocketException {
      throw const ApiProblem(
        status: 0,
        code: 'network_unavailable',
        message: 'Nije moguće povezati se sa serverom.',
      );
    } on http.ClientException {
      throw const ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Mrežni zahtjev nije uspio. Pokušajte ponovo.',
      );
    } on FormatException {
      throw const ApiProblem(
        status: 0,
        code: 'invalid_response',
        message: 'Server je vratio neočekivan odgovor.',
      );
    }
  }

  Future<DownloadedFile> _download(String path, {bool retry = true}) async {
    try {
      final request = http.Request('GET', _uri(path, const {}));
      request.headers['Accept'] = 'application/pdf';
      if (_tokens.accessToken != null) {
        request.headers['Authorization'] = 'Bearer ${_tokens.accessToken}';
      }
      final response = await http.Response.fromStream(
        await _http.send(request).timeout(const Duration(seconds: 30)),
      );
      if (response.statusCode == 401 && retry) {
        final future = _refreshing ??= _tokens.refresh();
        final refreshed = await future.whenComplete(() => _refreshing = null);
        if (refreshed) return _download(path, retry: false);
        await _tokens.invalidate();
      }
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiProblem.fromResponse(response);
      }
      final contentType = response.headers['content-type']?.split(';').first;
      if (contentType != 'application/pdf') {
        throw const ApiProblem(
          status: 0,
          code: 'invalid_report_response',
          message: 'Server nije vratio ispravan PDF izvještaj.',
        );
      }
      return DownloadedFile(
        bytes: response.bodyBytes,
        fileName: _downloadFileName(response.headers['content-disposition']),
        contentType: contentType!,
        recordCount:
            int.tryParse(response.headers['x-report-record-count'] ?? '') ?? 0,
      );
    } on ApiProblem {
      rethrow;
    } on TimeoutException {
      throw const ApiProblem(
        status: 0,
        code: 'request_timeout',
        message: 'Generisanje izvještaja je isteklo. Pokušajte ponovo.',
      );
    } on SocketException {
      throw const ApiProblem(
        status: 0,
        code: 'network_unavailable',
        message: 'Nije moguće povezati se sa serverom.',
      );
    } on http.ClientException {
      throw const ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Preuzimanje izvještaja nije uspjelo.',
      );
    }
  }

  static String _downloadFileName(String? disposition) {
    final encoded = RegExp(
      r"filename\*=UTF-8''([^;]+)",
      caseSensitive: false,
    ).firstMatch(disposition ?? '')?.group(1);
    final plain = RegExp(
      r'filename="?([^";]+)',
      caseSensitive: false,
    ).firstMatch(disposition ?? '')?.group(1);
    final decoded = encoded == null ? plain : Uri.decodeComponent(encoded);
    final safe = (decoded ?? 'gymlink-izvjestaj.pdf').replaceAll(
      RegExp(r'[^A-Za-z0-9._-]'),
      '-',
    );
    return safe.toLowerCase().endsWith('.pdf') ? safe : '$safe.pdf';
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
