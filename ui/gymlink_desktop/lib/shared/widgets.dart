import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/theme.dart';

class AsyncPanel extends StatelessWidget {
  const AsyncPanel({
    required this.loading,
    required this.child,
    this.error,
    this.onRetry,
    super.key,
  });
  final bool loading;
  final Object? error;
  final VoidCallback? onRetry;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    if (loading) return const Center(child: CircularProgressIndicator());
    if (error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.cloud_off_outlined, size: 46),
            const SizedBox(height: 12),
            Text(
              error is ApiProblem
                  ? (error! as ApiProblem).message
                  : 'Došlo je do neočekivane greške.',
            ),
            const SizedBox(height: 12),
            if (onRetry != null)
              OutlinedButton(
                onPressed: onRetry,
                child: const Text('Pokušaj ponovo'),
              ),
          ],
        ),
      );
    }
    return child;
  }
}

class EmptyState extends StatelessWidget {
  const EmptyState(this.message, {super.key});
  final String message;
  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Text(message, style: Theme.of(context).textTheme.titleMedium),
    ),
  );
}

class StatusPill extends StatelessWidget {
  const StatusPill(this.label, {super.key});
  final String label;
  @override
  Widget build(BuildContext context) {
    final positive =
        label.contains('Active') ||
        label.contains('Approved') ||
        label.contains('Completed');
    final negative =
        label.contains('Rejected') ||
        label.contains('Cancelled') ||
        label.contains('Inactive');
    final color = positive
        ? GymLinkColors.success
        : negative
        ? GymLinkColors.danger
        : GymLinkColors.blue;
    return DecoratedBox(
      decoration: BoxDecoration(
        color: color.withValues(alpha: .12),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
        child: Text(
          label,
          style: TextStyle(color: color, fontWeight: FontWeight.w700),
        ),
      ),
    );
  }
}

String enumLabel(Object? value, List<String> values) {
  if (value is String) return value;
  if (value is num && value >= 0 && value < values.length) {
    return values[value.toInt()];
  }
  return value?.toString() ?? '';
}

Future<bool> confirmAction(
  BuildContext context, {
  required String title,
  required String message,
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Odustani'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Potvrdi'),
          ),
        ],
      ),
    ) ??
    false;
