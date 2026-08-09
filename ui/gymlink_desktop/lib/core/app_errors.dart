import 'dart:async';

import 'package:flutter/material.dart';

final class AppErrorReporter {
  AppErrorReporter._();

  static final ValueNotifier<String?> message = ValueNotifier(null);
  static final ValueNotifier<String?> successMessage = ValueNotifier(null);
  static Timer? _successTimer;

  static void reportUnexpected([String? safeMessage]) {
    successMessage.value = null;
    message.value =
        safeMessage ?? 'Došlo je do neočekivane greške. Pokušajte ponovo.';
  }

  static void reportSuccess([String? value]) {
    message.value = null;
    successMessage.value = value ?? 'Promjena je uspješno sačuvana.';
    _successTimer?.cancel();
    _successTimer = Timer(
      const Duration(seconds: 3),
      () => successMessage.value = null,
    );
  }

  static void clear() {
    message.value = null;
    successMessage.value = null;
  }
}

class AppErrorBanner extends StatelessWidget {
  const AppErrorBanner({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) => Stack(
    children: [
      child,
      ValueListenableBuilder<String?>(
        valueListenable: AppErrorReporter.message,
        builder: (context, message, _) {
          if (message == null) return const SizedBox.shrink();
          return Positioned(
            right: 24,
            top: 24,
            width: 440,
            child: Material(
              color: Colors.red.shade700,
              borderRadius: BorderRadius.circular(12),
              elevation: 8,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
                child: Row(
                  children: [
                    const Icon(Icons.error_outline, color: Colors.white),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        message,
                        style: const TextStyle(color: Colors.white),
                      ),
                    ),
                    IconButton(
                      tooltip: 'Zatvori',
                      onPressed: AppErrorReporter.clear,
                      icon: const Icon(Icons.close, color: Colors.white),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
      ValueListenableBuilder<String?>(
        valueListenable: AppErrorReporter.successMessage,
        builder: (context, message, _) {
          if (message == null) return const SizedBox.shrink();
          return Positioned(
            right: 24,
            top: 24,
            width: 440,
            child: Material(
              color: Colors.green.shade700,
              borderRadius: BorderRadius.circular(12),
              elevation: 8,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
                child: Row(
                  children: [
                    const Icon(Icons.check_circle_outline, color: Colors.white),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        message,
                        style: const TextStyle(color: Colors.white),
                      ),
                    ),
                    IconButton(
                      tooltip: 'Zatvori',
                      onPressed: AppErrorReporter.clear,
                      icon: const Icon(Icons.close, color: Colors.white),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    ],
  );
}
