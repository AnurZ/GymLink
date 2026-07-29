import 'dart:async';
import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'app.dart';
import 'core/api.dart';
import 'core/app_errors.dart';
import 'core/auth.dart';

void main() {
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();
    FlutterError.onError = (details) {
      FlutterError.presentError(details);
      AppErrorReporter.reportUnexpected(
        details.exception is ApiProblem
            ? (details.exception as ApiProblem).message
            : null,
      );
    };
    PlatformDispatcher.instance.onError = (error, stack) {
      FlutterError.dumpErrorToConsole(
        FlutterErrorDetails(exception: error, stack: stack),
      );
      AppErrorReporter.reportUnexpected(
        error is ApiProblem ? error.message : null,
      );
      return true;
    };
    ErrorWidget.builder = (_) => const Directionality(
      textDirection: TextDirection.ltr,
      child: Center(
        child: Text('Prikaz nije moguće učitati. Pokušajte ponovo.'),
      ),
    );
    final auth = AuthController();
    final api = ApiClient(auth);
    auth.attachApi(api);
    await auth.initialize();
    runApp(
      MultiProvider(
        providers: [
          ChangeNotifierProvider.value(value: auth),
          Provider.value(value: api),
        ],
        child: const GymLinkMobileApp(),
      ),
    );
  }, (error, stack) {
    FlutterError.dumpErrorToConsole(
      FlutterErrorDetails(exception: error, stack: stack),
    );
    AppErrorReporter.reportUnexpected(
      error is ApiProblem ? error.message : null,
    );
  });
}
