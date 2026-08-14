import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

import 'api.dart';

class UserSession {
  const UserSession({
    required this.accessToken,
    required this.refreshToken,
    required this.user,
  });
  final String accessToken;
  final String refreshToken;
  final Map<String, dynamic> user;
  String get role => user['role']?.toString() ?? '';
  String get displayName => user['displayName']?.toString() ?? '';
  Map<String, dynamic>? get tenant => user['tenant'] is Map
      ? Map<String, dynamic>.from(user['tenant'] as Map)
      : null;

  factory UserSession.fromJson(Map<String, dynamic> json) => UserSession(
    accessToken: json['accessToken'].toString(),
    refreshToken: json['refreshToken'].toString(),
    user: Map<String, dynamic>.from(json['user'] as Map),
  );
}

class AuthController extends ChangeNotifier implements AuthTokenSource {
  static const _key = 'gymlink.desktop.refresh_token';
  static const _storage = FlutterSecureStorage();
  ApiClient? _api;
  UserSession? _session;
  bool _initializing = true;
  bool _signingOut = false;

  UserSession? get session => _session;
  bool get initializing => _initializing;
  bool get signingOut => _signingOut;
  bool get isAuthenticated => _session != null;
  @override
  String? get accessToken => _session?.accessToken;

  void attachApi(ApiClient api) => _api = api;

  Future<void> initialize() async {
    final token = await _storage.read(key: _key);
    if (token != null) await _refreshWith(token);
    _initializing = false;
    notifyListeners();
  }

  Future<void> login(String identifier, String password) async {
    final json = await _api!.post(
      '/api/auth/login',
      authenticated: false,
      body: {'identifier': identifier.trim(), 'password': password},
    );
    await _accept(
      UserSession.fromJson(Map<String, dynamic>.from(json! as Map)),
    );
  }

  Future<Map<String, dynamic>> loadProfile() async =>
      Map<String, dynamic>.from((await _api!.get('/api/profile'))! as Map);

  Future<void> updateProfile(Map<String, Object?> body) async {
    final json = await _api!.put('/api/profile', body: body);
    _session = UserSession(
      accessToken: _session!.accessToken,
      refreshToken: _session!.refreshToken,
      user: Map<String, dynamic>.from(json! as Map),
    );
    notifyListeners();
  }

  Future<void> logout() async {
    if (_signingOut) return;
    _signingOut = true;
    notifyListeners();
    final refreshToken = _session?.refreshToken;
    try {
      if (refreshToken != null) {
        await _api?.post(
          '/api/auth/logout',
          body: {'refreshToken': refreshToken},
        );
      }
    } finally {
      _session = null;
      await _storage.delete(key: _key);
      _signingOut = false;
      notifyListeners();
    }
  }

  @override
  Future<bool> refresh() async {
    final token = _session?.refreshToken ?? await _storage.read(key: _key);
    return token != null && await _refreshWith(token);
  }

  Future<bool> _refreshWith(String token) async {
    if (ApiClient.baseUrl.isEmpty) return false;
    try {
      final response = await http.post(
        Uri.parse(
          '${ApiClient.baseUrl.replaceFirst(RegExp(r'/$'), '')}/api/auth/refresh',
        ),
        headers: const {'Content-Type': 'application/json'},
        body: jsonEncode({'refreshToken': token}),
      );
      if (response.statusCode < 200 || response.statusCode >= 300) return false;
      await _accept(
        UserSession.fromJson(
          Map<String, dynamic>.from(jsonDecode(response.body) as Map),
        ),
      );
      return true;
    } catch (_) {
      return false;
    }
  }

  Future<void> _accept(UserSession value) async {
    _session = value;
    await _storage.write(key: _key, value: value.refreshToken);
    notifyListeners();
  }

  @override
  Future<void> invalidate() async {
    _session = null;
    await _storage.delete(key: _key);
    notifyListeners();
  }
}
