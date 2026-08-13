import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/theme.dart';
import 'cached_network_image_view.dart';

class TrainerImageAvatar extends StatelessWidget {
  const TrainerImageAvatar({
    super.key,
    required this.name,
    this.imageUrl,
    this.radius = 20,
  });

  final String name;
  final String? imageUrl;
  final double radius;

  @override
  Widget build(BuildContext context) {
    final initials = name
        .trim()
        .split(RegExp(r'\s+'))
        .where((part) => part.isNotEmpty)
        .take(2)
        .map((part) => part[0].toUpperCase())
        .join();
    final fallback = ColoredBox(
      color: Theme.of(context).colorScheme.primaryContainer,
      child: Center(
        child: Text(
          initials,
          style: TextStyle(
            color: Theme.of(context).colorScheme.onPrimaryContainer,
            fontWeight: FontWeight.w800,
          ),
        ),
      ),
    );
    return ClipOval(
      child: SizedBox.square(
        dimension: radius * 2,
        child: CachedNetworkImageView(
          imageUrl: imageUrl,
          fallback: fallback,
          decodeWidth: (radius * 2).round(),
          decodeHeight: (radius * 2).round(),
        ),
      ),
    );
  }
}

class PageFrame extends StatelessWidget {
  const PageFrame({
    required this.title,
    required this.child,
    this.actions,
    super.key,
  });
  final String title;
  final Widget child;
  final List<Widget>? actions;

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(title), actions: actions),
    body: SafeArea(child: child),
  );
}

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
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.cloud_off_outlined, size: 42),
              const SizedBox(height: 12),
              Text(
                error is ApiProblem
                    ? (error! as ApiProblem).message
                    : 'Došlo je do neočekivane greške.',
                textAlign: TextAlign.center,
              ),
              if (onRetry != null) ...[
                const SizedBox(height: 16),
                OutlinedButton(
                  onPressed: onRetry,
                  child: const Text('Pokušaj ponovo'),
                ),
              ],
            ],
          ),
        ),
      );
    }
    return child;
  }
}

class EmptyState extends StatelessWidget {
  const EmptyState({
    required this.title,
    required this.message,
    this.icon = Icons.inbox_outlined,
    super.key,
  });
  final String title;
  final String message;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 48, color: Colors.blueGrey),
          const SizedBox(height: 12),
          Text(title, style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 6),
          Text(message, textAlign: TextAlign.center),
        ],
      ),
    ),
  );
}

class StatusPill extends StatelessWidget {
  const StatusPill(this.label, {super.key});
  final String label;

  @override
  Widget build(BuildContext context) {
    final normalized = label.toLowerCase();
    final color =
        normalized.contains('active') ||
            normalized.contains('approved') ||
            normalized.contains('confirm') ||
            normalized.contains('complete')
        ? GymLinkColors.success
        : normalized.contains('pending')
        ? const Color(0xFFD68A00)
        : normalized.contains('cancel') ||
              normalized.contains('reject') ||
              normalized.contains('inactive')
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

Future<bool> confirmAction(
  BuildContext context, {
  required String title,
  required String message,
  String action = 'Potvrdi',
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
            child: Text(action),
          ),
        ],
      ),
    ) ??
    false;

String enumLabel(Object? value, List<String> values) {
  if (value is String) return value;
  if (value is num && value.toInt() >= 0 && value.toInt() < values.length) {
    return values[value.toInt()];
  }
  return value?.toString() ?? '';
}
