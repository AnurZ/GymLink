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

Future<String?> promptForReason(
  BuildContext context, {
  required String title,
}) => showDialog<String>(
  context: context,
  barrierDismissible: false,
  builder: (_) => _ReasonDialog(title: title),
);

Future<bool> submitReasonedAction(
  BuildContext context, {
  required String title,
  required Future<void> Function(String reason) onSubmit,
}) async =>
    await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _ReasonDialog(title: title, onSubmit: onSubmit),
    ) ??
    false;

class _ReasonDialog extends StatefulWidget {
  const _ReasonDialog({required this.title, this.onSubmit});

  final String title;
  final Future<void> Function(String reason)? onSubmit;

  @override
  State<_ReasonDialog> createState() => _ReasonDialogState();
}

class _ReasonDialogState extends State<_ReasonDialog> {
  final _controller = TextEditingController();
  final _formKey = GlobalKey<FormState>();
  bool _busy = false;
  ApiProblem? _serverProblem;
  String? _formError;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (_formKey.currentState?.validate() != true) return;
    final reason = _controller.text.trim();
    if (widget.onSubmit == null) {
      Navigator.pop(context, reason);
      return;
    }
    setState(() => _busy = true);
    try {
      await widget.onSubmit!(reason);
      if (mounted) Navigator.pop(context, true);
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _serverProblem = error;
          _formError = error.fieldErrors.isEmpty ? error.message : null;
        });
        _formKey.currentState!.validate();
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextFormField(
            controller: _controller,
            autofocus: true,
            minLines: 2,
            maxLines: 4,
            maxLength: 200,
            decoration: const InputDecoration(
              labelText: 'Razlog',
              helperText: 'Najmanje 2 znaka',
            ),
            onChanged: (_) {
              if (_serverProblem?.fieldError('Reason') == null) return;
              setState(() {
                _serverProblem = null;
                _formError = null;
              });
            },
            validator: (value) {
              final length = value?.trim().length ?? 0;
              if (length < 2) return 'Unesite razlog (najmanje 2 znaka).';
              if (length > 200) return 'Najviše 200 znakova.';
              return _serverProblem?.fieldError('Reason');
            },
            onFieldSubmitted: (_) => _submit(),
          ),
          if (_formError != null)
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                _formError!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _busy ? null : _submit,
        child: Text(_busy ? 'Čuvanje...' : 'Potvrdi'),
      ),
    ],
  );
}
