import 'dart:async';
import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/core/auth.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test(
    'registration accepts returned session without a login request',
    () async {
      var loginRequests = 0;
      final api = ApiClient(
        _EmptyTokens(),
        baseUrlOverride: 'https://api.test',
        httpClient: MockClient((request) async {
          if (request.url.path == '/api/auth/login') loginRequests++;
          return http.Response(
            jsonEncode(_session('Member')),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );
      final auth = AuthController()..attachApi(api);
      addTearDown(api.close);
      addTearDown(auth.dispose);

      await auth.register(
        username: 'member',
        email: 'member@example.test',
        displayName: 'Test Member',
        password: 'Test123!',
      );

      expect(auth.session?.role, 'Member');
      expect(auth.accessToken, 'access-token');
      expect(loginRequests, 0);
    },
  );

  test(
    'logout exposes neutral transition before removing the session',
    () async {
      final logoutResponse = Completer<http.Response>();
      final api = ApiClient(
        _EmptyTokens(),
        baseUrlOverride: 'https://api.test',
        httpClient: MockClient((request) async {
          if (request.url.path == '/api/auth/login') {
            return http.Response(
              jsonEncode(_session('GymAdmin')),
              200,
              headers: {'content-type': 'application/json'},
            );
          }
          if (request.url.path == '/api/auth/logout') {
            return logoutResponse.future;
          }
          return http.Response('', 404);
        }),
      );
      final auth = AuthController()..attachApi(api);
      addTearDown(api.close);
      addTearDown(auth.dispose);
      await auth.login('admin', 'Test123!');

      final pendingLogout = auth.logout();
      expect(auth.signingOut, isTrue);
      expect(auth.session?.role, 'GymAdmin');

      logoutResponse.complete(http.Response('', 204));
      await pendingLogout;
      expect(auth.signingOut, isFalse);
      expect(auth.session, isNull);
    },
  );
}

Map<String, Object?> _session(String role) => {
  'accessToken': 'access-token',
  'refreshToken': 'refresh-token',
  'user': {'id': 'user-1', 'displayName': 'Test User', 'role': role},
};

class _EmptyTokens implements AuthTokenSource {
  @override
  String? get accessToken => null;

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}
