import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';

String? _fieldError(Map<String, List<String>> errors, String field) {
  for (final entry in errors.entries) {
    if (entry.key.toLowerCase() == field.toLowerCase()) {
      return entry.value.join('\n');
    }
  }
  return null;
}

class ForgotPasswordScreen extends StatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  State<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends State<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  bool _busy = false;
  String? _error;
  Map<String, List<String>> _fieldErrors = const {};

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
      _fieldErrors = const {};
    });
    try {
      await context.read<ApiClient>().post(
        '/api/auth/forgot-password',
        authenticated: false,
        body: {'email': _email.text.trim()},
      );
      if (mounted) {
        context.go(
          '/reset-password?email=${Uri.encodeQueryComponent(_email.text.trim())}',
        );
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _fieldErrors = error.fieldErrors;
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => _RecoveryFrame(
    title: 'Promjena lozinke',
    subtitle: 'Poslat ćemo šestocifreni kod ako račun postoji.',
    child: Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _email,
            autofocus: true,
            decoration: InputDecoration(
              labelText: 'Email',
              errorText: _fieldError(_fieldErrors, 'Email'),
            ),
            validator: (value) =>
                value == null || !value.contains('@') ? 'Unesite email.' : null,
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(_error!, style: const TextStyle(color: GymLinkColors.danger)),
          ],
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Slanje...' : 'Pošalji kod'),
          ),
          TextButton(
            onPressed: () => context.go('/login'),
            child: const Text('Nazad na prijavu'),
          ),
        ],
      ),
    ),
  );
}

class ResetPasswordScreen extends StatefulWidget {
  const ResetPasswordScreen({required this.initialEmail, super.key});
  final String initialEmail;

  @override
  State<ResetPasswordScreen> createState() => _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends State<ResetPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _email;
  final _code = TextEditingController();
  final _password = TextEditingController();
  final _confirmation = TextEditingController();
  bool _busy = false;
  String? _error;
  Map<String, List<String>> _fieldErrors = const {};

  @override
  void initState() {
    super.initState();
    _email = TextEditingController(text: widget.initialEmail);
  }

  @override
  void dispose() {
    _email.dispose();
    _code.dispose();
    _password.dispose();
    _confirmation.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
      _fieldErrors = const {};
    });
    try {
      await context.read<ApiClient>().post(
        '/api/auth/reset-password',
        authenticated: false,
        body: {
          'email': _email.text.trim(),
          'code': _code.text.trim(),
          'newPassword': _password.text,
        },
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Lozinka je uspješno promijenjena.')),
        );
        context.go('/login');
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _error = error.message;
          _fieldErrors = error.fieldErrors;
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => _RecoveryFrame(
    title: 'Unesite kod',
    subtitle: 'Kod vrijedi 15 minuta i može se iskoristiti jednom.',
    child: Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _email,
            decoration: InputDecoration(
              labelText: 'Email',
              errorText: _fieldError(_fieldErrors, 'Email'),
            ),
            validator: (value) =>
                value == null || !value.contains('@') ? 'Unesite email.' : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _code,
            maxLength: 6,
            decoration: InputDecoration(
              labelText: 'Šestocifreni kod',
              errorText: _fieldError(_fieldErrors, 'Code'),
            ),
            validator: (value) => !RegExp(r'^\d{6}$').hasMatch(value ?? '')
                ? 'Kod mora sadržavati šest cifara.'
                : null,
          ),
          TextFormField(
            controller: _password,
            obscureText: true,
            decoration: InputDecoration(
              labelText: 'Nova lozinka',
              errorText: _fieldError(_fieldErrors, 'NewPassword'),
            ),
            validator: (value) => value == null || value.length < 8
                ? 'Lozinka mora imati najmanje 8 znakova.'
                : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _confirmation,
            obscureText: true,
            decoration: const InputDecoration(labelText: 'Potvrdite lozinku'),
            validator: (value) =>
                value != _password.text ? 'Lozinke se ne podudaraju.' : null,
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(_error!, style: const TextStyle(color: GymLinkColors.danger)),
          ],
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Spremanje...' : 'Promijeni lozinku'),
          ),
          TextButton(
            onPressed: _busy ? null : () => context.go('/login'),
            child: const Text('Nazad na prijavu'),
          ),
        ],
      ),
    ),
  );
}

class _RecoveryFrame extends StatelessWidget {
  const _RecoveryFrame({
    required this.title,
    required this.subtitle,
    required this.child,
  });
  final String title;
  final String subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(40),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 460),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(28),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.fitness_center,
                    color: GymLinkColors.blue,
                    size: 44,
                  ),
                  const SizedBox(height: 18),
                  Text(
                    title,
                    style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(subtitle),
                  const SizedBox(height: 24),
                  child,
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
