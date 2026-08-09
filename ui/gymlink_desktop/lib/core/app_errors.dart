import 'package:flutter/material.dart';

final class AppErrorReporter {
  AppErrorReporter._();

  static final ValueNotifier<String?> message = ValueNotifier(null);

  static void reportUnexpected([String? safeMessage]) {
    message.value =
        safeMessage ?? 'Došlo je do neočekivane greške. Pokušajte ponovo.';
  }

  static void clear() => message.value = null;
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
    ],
  );
}
