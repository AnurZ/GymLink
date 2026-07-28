import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../../core/theme.dart';

String? _fieldError(Map<String, List<String>> errors, String field) {
  for (final entry in errors.entries) {
    if (entry.key.toLowerCase() == field.toLowerCase()) {
      return entry.value.join('\n');
    }
  }
  return null;
}

class _AuthLayout extends StatelessWidget {
  const _AuthLayout({
    required this.title,
    required this.subtitle,
    required this.child,
  });
  final String title;
  final String subtitle;
  final Widget child;

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 440),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Icon(
                  Icons.fitness_center,
                  color: GymLinkColors.blue,
                  size: 48,
                ),
                const SizedBox(height: 14),
                Text(
                  'GymLink',
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
                ),
                const SizedBox(height: 28),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(22),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text(
                          title,
                          style: Theme.of(context).textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        const SizedBox(height: 6),
                        Text(subtitle),
                        const SizedBox(height: 22),
                        child,
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

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

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
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
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => _AuthLayout(
    title: 'Dobro došli',
    subtitle: 'Prijavite se da nastavite.',
    child: Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _identifier,
            keyboardType: TextInputType.emailAddress,
            autofillHints: const [AutofillHints.username, AutofillHints.email],
            decoration: const InputDecoration(
              labelText: 'Email ili korisničko ime',
              prefixIcon: Icon(Icons.person_outline),
            ),
            validator: (value) => value == null || value.trim().isEmpty
                ? 'Unesite email ili korisničko ime.'
                : null,
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _password,
            obscureText: _obscure,
            autofillHints: const [AutofillHints.password],
            decoration: InputDecoration(
              labelText: 'Lozinka',
              prefixIcon: const Icon(Icons.lock_outline),
              suffixIcon: IconButton(
                tooltip: _obscure ? 'Prikaži lozinku' : 'Sakrij lozinku',
                onPressed: () => setState(() => _obscure = !_obscure),
                icon: Icon(
                  _obscure
                      ? Icons.visibility_outlined
                      : Icons.visibility_off_outlined,
                ),
              ),
            ),
            validator: (value) =>
                value == null || value.isEmpty ? 'Unesite lozinku.' : null,
            onFieldSubmitted: (_) => _busy ? null : _submit(),
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(_error!, style: const TextStyle(color: GymLinkColors.danger)),
          ],
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: _busy
                ? const SizedBox.square(
                    dimension: 22,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Prijavi se'),
          ),
          const SizedBox(height: 10),
          TextButton(
            onPressed: _busy ? null : () => context.go('/forgot-password'),
            child: const Text('Zaboravili ste lozinku?'),
          ),
          TextButton(
            onPressed: _busy ? null : () => context.go('/register'),
            child: const Text('Nemate račun? Registrujte se'),
          ),
        ],
      ),
    ),
  );
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
  Widget build(BuildContext context) => _AuthLayout(
    title: 'Promjena lozinke',
    subtitle: 'Poslat ćemo šestocifreni kod ako račun postoji.',
    child: Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _email,
            keyboardType: TextInputType.emailAddress,
            decoration: InputDecoration(
              labelText: 'Email',
              prefixIcon: const Icon(Icons.mail_outline),
              errorText: _fieldError(_fieldErrors, 'Email'),
            ),
            validator: (value) =>
                value == null || !value.contains('@') ? 'Unesite email.' : null,
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(_error!, style: const TextStyle(color: GymLinkColors.danger)),
          ],
          const SizedBox(height: 18),
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
  Widget build(BuildContext context) => _AuthLayout(
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
            keyboardType: TextInputType.number,
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
          const SizedBox(height: 18),
          FilledButton(
            onPressed: _busy ? null : _submit,
            child: Text(_busy ? 'Spremanje...' : 'Promijeni lozinku'),
          ),
        ],
      ),
    ),
  );
}

class RegistrationScreen extends StatefulWidget {
  const RegistrationScreen({super.key});

  @override
  State<RegistrationScreen> createState() => _RegistrationScreenState();
}

class _RegistrationScreenState extends State<RegistrationScreen> {
  final _formKey = GlobalKey<FormState>();
  final _username = TextEditingController();
  final _email = TextEditingController();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _password = TextEditingController();
  final _confirmation = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final controller in [
      _username,
      _email,
      _name,
      _phone,
      _password,
      _confirmation,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<AuthController>().register(
        username: _username.text,
        email: _email.text,
        displayName: _name.text,
        phoneNumber: _phone.text,
        password: _password.text,
      );
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => _AuthLayout(
    title: 'Kreirajte račun',
    subtitle: 'Registracija automatski kreira članski račun.',
    child: Form(
      key: _formKey,
      child: Column(
        children: [
          _field(_name, 'Ime i prezime', Icons.badge_outlined),
          _field(_username, 'Korisničko ime', Icons.alternate_email),
          _field(_email, 'Email', Icons.mail_outline, email: true),
          _field(
            _phone,
            'Telefon (opcionalno)',
            Icons.phone_outlined,
            required: false,
          ),
          TextFormField(
            controller: _password,
            obscureText: true,
            decoration: const InputDecoration(
              labelText: 'Lozinka',
              prefixIcon: Icon(Icons.lock_outline),
              helperText: 'Najmanje 8 znakova.',
            ),
            validator: (value) => value == null || value.length < 8
                ? 'Lozinka mora imati najmanje 8 znakova.'
                : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _confirmation,
            obscureText: true,
            decoration: const InputDecoration(
              labelText: 'Potvrdite lozinku',
              prefixIcon: Icon(Icons.lock_reset),
            ),
            validator: (value) =>
                value != _password.text ? 'Lozinke se ne podudaraju.' : null,
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                _error!,
                style: const TextStyle(color: GymLinkColors.danger),
              ),
            ),
          ],
          const SizedBox(height: 20),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              onPressed: _busy ? null : _submit,
              child: Text(_busy ? 'Registracija...' : 'Registrujte se'),
            ),
          ),
          TextButton(
            onPressed: () => context.go('/login'),
            child: const Text('Nazad na prijavu'),
          ),
        ],
      ),
    ),
  );

  Widget _field(
    TextEditingController controller,
    String label,
    IconData icon, {
    bool email = false,
    bool required = true,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: TextFormField(
      controller: controller,
      keyboardType: email ? TextInputType.emailAddress : TextInputType.text,
      decoration: InputDecoration(labelText: label, prefixIcon: Icon(icon)),
      validator: required
          ? (value) => value == null || value.trim().isEmpty
                ? 'Polje je obavezno.'
                : null
          : null,
    ),
  );
}
