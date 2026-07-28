import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

import 'api.dart';

final class UserSession {
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

final class AuthController extends ChangeNotifier implements AuthTokenSource {
  static const _refreshKey = 'gymlink.refresh_token';
  static const _storage = FlutterSecureStorage();
  ApiClient? _api;
  UserSession? _session;
  bool _initializing = true;
  ApiProblem? _error;

  UserSession? get session => _session;
  bool get initializing => _initializing;
  ApiProblem? get error => _error;
  bool get isAuthenticated => _session != null;

  @override
  String? get accessToken => _session?.accessToken;

  void attachApi(ApiClient api) => _api = api;

  Future<void> initialize() async {
    final token = await _storage.read(key: _refreshKey);
    if (token != null) await _refreshWith(token);
    _initializing = false;
    notifyListeners();
  }

  Future<void> login(String identifier, String password) async {
    _error = null;
    notifyListeners();
    try {
      final json = await _api!.post(
        '/api/auth/login',
        authenticated: false,
        body: {'identifier': identifier.trim(), 'password': password},
      );
      await _accept(
        UserSession.fromJson(Map<String, dynamic>.from(json! as Map)),
      );
    } on ApiProblem catch (error) {
      _error = error;
      notifyListeners();
      rethrow;
    }
  }

  Future<void> register({
    required String username,
    required String email,
    required String displayName,
    required String password,
    String? phoneNumber,
  }) async {
    _error = null;
    notifyListeners();
    try {
      final json = await _api!.post(
        '/api/auth/register',
        authenticated: false,
        body: {
          'username': username.trim(),
          'email': email.trim(),
          'displayName': displayName.trim(),
          'phoneNumber': phoneNumber?.trim().isEmpty == true
              ? null
              : phoneNumber?.trim(),
          'password': password,
        },
      );
      await _accept(
        UserSession.fromJson(Map<String, dynamic>.from(json! as Map)),
      );
    } on ApiProblem catch (error) {
      _error = error;
      notifyListeners();
      rethrow;
    }
  }

  Future<Map<String, dynamic>> loadProfile() async =>
      Map<String, dynamic>.from((await _api!.get('/api/profile'))! as Map);

  Future<void> updateProfile({
    required String displayName,
    required String email,
    String? phoneNumber,
  }) async {
    final json = await _api!.put(
      '/api/profile',
      body: {
        'displayName': displayName.trim(),
        'email': email.trim(),
        'phoneNumber': phoneNumber?.trim().isEmpty == true
            ? null
            : phoneNumber?.trim(),
      },
    );
    _session = UserSession(
      accessToken: _session!.accessToken,
      refreshToken: _session!.refreshToken,
      user: Map<String, dynamic>.from(json! as Map),
    );
    notifyListeners();
  }

  Future<void> logout() async {
    final refreshToken = _session?.refreshToken;
    try {
      if (refreshToken != null) {
        await _api?.post(
          '/api/auth/logout',
          body: {'refreshToken': refreshToken},
        );
      }
    } finally {
      await invalidate();
    }
  }

  @override
  Future<bool> refresh() async {
    final token =
        _session?.refreshToken ?? await _storage.read(key: _refreshKey);
    return token != null && await _refreshWith(token);
  }

  Future<bool> _refreshWith(String token) async {
    try {
      final response = await http.post(
        Uri.parse(
          '${ApiClient.baseUrl.replaceFirst(RegExp(r'/$'), '')}/api/auth/refresh',
        ),
        headers: const {
          'Accept': 'application/json',
          'Content-Type': 'application/json',
        },
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

  Future<void> _accept(UserSession session) async {
    _session = session;
    await _storage.write(key: _refreshKey, value: session.refreshToken);
    _error = null;
    notifyListeners();
  }

  @override
  Future<void> invalidate() async {
    _session = null;
    await _storage.delete(key: _refreshKey);
    notifyListeners();
  }
}
