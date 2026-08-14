import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../../shared/widgets.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});
  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  bool _loading = true;
  bool _saving = false;
  Map<String, List<String>> _fieldErrors = const {};
  String? _formError;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final profile = await context.read<AuthController>().loadProfile();
      _name.text = profile['displayName']?.toString() ?? '';
      _email.text = profile['email']?.toString() ?? '';
      _phone.text = profile['phoneNumber']?.toString() ?? '';
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _save() async {
    setState(() {
      _fieldErrors = const {};
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      await context.read<AuthController>().updateProfile({
        'displayName': _name.text.trim(),
        'email': _email.text.trim(),
        'phoneNumber': _phone.text.trim().isEmpty ? null : _phone.text.trim(),
      });
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Profil je sačuvan.')));
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _fieldErrors = error.fieldErrors;
          _formError = error.fieldErrors.isEmpty ? error.message : null;
        });
        _formKey.currentState!.validate();
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  String? _serverError(String field) => ApiProblem(
    status: 400,
    code: 'validation_failed',
    message: '',
    fieldErrors: _fieldErrors,
  ).fieldError(field);

  void _clearFieldError(String field) {
    if (_serverError(field) == null) return;
    setState(() {
      _fieldErrors = Map<String, List<String>>.from(_fieldErrors)
        ..removeWhere((key, _) => key.toLowerCase() == field.toLowerCase());
      _formError = null;
    });
  }

  @override
  Widget build(BuildContext context) => AsyncPanel(
    loading: _loading,
    error: _error,
    onRetry: _load,
    child: Align(
      alignment: Alignment.topLeft,
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 720),
        child: Card(
          child: Padding(
            padding: const EdgeInsets.all(28),
            child: Form(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextFormField(
                    controller: _name,
                    decoration: const InputDecoration(
                      labelText: 'Ime i prezime',
                    ),
                    onChanged: (_) => _clearFieldError('DisplayName'),
                    validator: (value) {
                      final length = value?.trim().length ?? 0;
                      if (length < 2) return 'Unesite najmanje 2 znaka.';
                      if (length > 160) return 'Najviše 160 znakova.';
                      return _serverError('DisplayName');
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _email,
                    decoration: const InputDecoration(labelText: 'Email'),
                    onChanged: (_) => _clearFieldError('Email'),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (!RegExp(
                        r'^[^@\s]+@[^@\s]+\.[^@\s]+$',
                      ).hasMatch(text)) {
                        return 'Unesite ispravan email.';
                      }
                      if (text.length > 320) return 'Najviše 320 znakova.';
                      return _serverError('Email');
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _phone,
                    decoration: const InputDecoration(labelText: 'Telefon'),
                    onChanged: (_) => _clearFieldError('PhoneNumber'),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (text.length > 32) return 'Najviše 32 znaka.';
                      if (text.isNotEmpty &&
                          !RegExp(r'^\+?[0-9 ()-]+$').hasMatch(text)) {
                        return 'Unesite ispravan broj telefona.';
                      }
                      return _serverError('PhoneNumber');
                    },
                  ),
                  if (_formError != null) ...[
                    const SizedBox(height: 12),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text(
                        _formError!,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ),
                  ],
                  const SizedBox(height: 18),
                  Row(
                    children: [
                      FilledButton(
                        onPressed: _saving ? null : _save,
                        child: const Text('Sačuvaj'),
                      ),
                      const SizedBox(width: 12),
                      OutlinedButton.icon(
                        onPressed: context.read<AuthController>().logout,
                        icon: const Icon(Icons.logout),
                        label: const Text('Odjavi se'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
