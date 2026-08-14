import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../../core/theme.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _identifier = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  String? _error;
  ApiProblem? _serverProblem;

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _error = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<AuthController>().login(
        _identifier.text,
        _password.text,
      );
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _serverProblem = error;
          _error = error.fieldErrors.isEmpty ? error.message : null;
        });
        _formKey.currentState!.validate();
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Row(
      children: [
        Expanded(
          child: ColoredBox(
            color: GymLinkColors.blue,
            child: Center(
              child: Padding(
                padding: const EdgeInsets.all(48),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(
                      Icons.fitness_center,
                      color: Colors.white,
                      size: 72,
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'GymLink Admin',
                      style: Theme.of(context).textTheme.displaySmall?.copyWith(
                        color: Colors.white,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 12),
                    const Text(
                      'Upravljajte teretanom, članstvima i terminima na jednom mjestu.',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: Colors.white, fontSize: 18),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        Expanded(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(48),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: Form(
                  key: _formKey,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'Prijava',
                        style: Theme.of(context).textTheme.headlineLarge
                            ?.copyWith(fontWeight: FontWeight.w800),
                      ),
                      const SizedBox(height: 8),
                      const Text('Prijavite se administrativnim računom.'),
                      const SizedBox(height: 28),
                      TextFormField(
                        controller: _identifier,
                        autofocus: true,
                        decoration: const InputDecoration(
                          labelText: 'Email ili korisničko ime',
                          prefixIcon: Icon(Icons.person_outline),
                        ),
                        onChanged: (_) => setState(() => _serverProblem = null),
                        validator: (value) {
                          final text = value?.trim() ?? '';
                          if (text.isEmpty) return 'Polje je obavezno.';
                          if (text.length > 320) return 'Najviše 320 znakova.';
                          return _serverProblem?.fieldError('Identifier');
                        },
                      ),
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _password,
                        obscureText: _obscure,
                        onFieldSubmitted: (_) => _busy ? null : _submit(),
                        decoration: InputDecoration(
                          labelText: 'Lozinka',
                          prefixIcon: const Icon(Icons.lock_outline),
                          suffixIcon: IconButton(
                            onPressed: () =>
                                setState(() => _obscure = !_obscure),
                            icon: Icon(
                              _obscure
                                  ? Icons.visibility_outlined
                                  : Icons.visibility_off_outlined,
                            ),
                          ),
                        ),
                        onChanged: (_) => setState(() => _serverProblem = null),
                        validator: (value) {
                          if (value == null || value.isEmpty) {
                            return 'Polje je obavezno.';
                          }
                          if (value.length > 100) return 'Najviše 100 znakova.';
                          return _serverProblem?.fieldError('Password');
                        },
                      ),
                      if (_error != null) ...[
                        const SizedBox(height: 12),
                        Text(
                          _error!,
                          style: const TextStyle(color: GymLinkColors.danger),
                        ),
                      ],
                      const SizedBox(height: 22),
                      FilledButton(
                        onPressed: _busy ? null : _submit,
                        child: Text(_busy ? 'Prijava...' : 'Prijavi se'),
                      ),
                      TextButton(
                        onPressed: _busy
                            ? null
                            : () => context.go('/forgot-password'),
                        child: const Text('Zaboravili ste lozinku?'),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    ),
  );
}
