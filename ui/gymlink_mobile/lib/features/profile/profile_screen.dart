import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../../shared/cached_network_image_view.dart';
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
  bool _uploadingImage = false;
  Map<String, dynamic>? _profile;
  Uint8List? _imagePreview;
  String? _imageError;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final profile = await context.read<AuthController>().loadProfile();
      _profile = profile;
      _name.text = profile['displayName']?.toString() ?? '';
      _email.text = profile['email']?.toString() ?? '';
      _phone.text = profile['phoneNumber']?.toString() ?? '';
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pickAndUploadImage() async {
    final api = context.read<ApiClient>();
    final image = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (image == null || !mounted) return;
    final bytes = await image.readAsBytes();
    if (bytes.length > 5 * 1024 * 1024) {
      setState(() => _imageError = 'Slika mora biti manja ili jednaka 5 MiB.');
      return;
    }
    final extension = image.name.split('.').last.toLowerCase();
    final contentType = switch (extension) {
      'jpg' || 'jpeg' => 'image/jpeg',
      'png' => 'image/png',
      'webp' => 'image/webp',
      _ => null,
    };
    if (contentType == null) {
      setState(() => _imageError = 'Dozvoljene su JPG, PNG i WebP slike.');
      return;
    }
    final token = _trainerImage?['concurrencyToken']?.toString();
    if (token == null || token.isEmpty) {
      setState(() => _imageError = 'Osvježite profil prije izmjene slike.');
      return;
    }
    setState(() {
      _uploadingImage = true;
      _imagePreview = bytes;
      _imageError = null;
    });
    try {
      final result = await api.postMultipart(
        '/api/profile/trainer-image',
        bytes: bytes,
        fileName: image.name,
        contentType: contentType,
        fields: {'concurrencyToken': token},
      );
      if (!mounted) return;
      setState(() {
        _profile = {
          ...?_profile,
          'trainerImage': Map<String, dynamic>.from(result! as Map),
        };
        _imagePreview = null;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Profilna slika je sačuvana.')),
      );
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _imageError = error.message);
    } finally {
      if (mounted) setState(() => _uploadingImage = false);
    }
  }

  Future<void> _removeImage() async {
    final api = context.read<ApiClient>();
    final image = _trainerImage;
    if (image?['imageUrl'] == null ||
        !await confirmAction(
          context,
          title: 'Ukloni profilnu sliku',
          message: 'Prikazat će se inicijali dok ne dodate novu sliku.',
        )) {
      return;
    }
    setState(() {
      _uploadingImage = true;
      _imageError = null;
    });
    try {
      final result = await api.delete(
        '/api/profile/trainer-image',
        body: {'concurrencyToken': image!['concurrencyToken']},
      );
      if (mounted) {
        setState(() {
          _profile = {
            ...?_profile,
            'trainerImage': Map<String, dynamic>.from(result! as Map),
          };
          _imagePreview = null;
        });
      }
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _imageError = error.message);
    } finally {
      if (mounted) setState(() => _uploadingImage = false);
    }
  }

  Map<String, dynamic>? get _trainerImage {
    final value = _profile?['trainerImage'];
    return value is Map ? Map<String, dynamic>.from(value) : null;
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      await context.read<AuthController>().updateProfile(
        displayName: _name.text,
        email: _email.text,
        phoneNumber: _phone.text,
      );
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Profil je sačuvan.')));
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _phone.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<AuthController>().session;
    final api = context.read<ApiClient>();
    final imageUrl = api.mediaUrl(_trainerImage?['imageUrl']);
    final isTrainer = session?.role == 'Trainer';
    return AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: ListView(
        padding: const EdgeInsets.all(18),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Row(
                children: [
                  _ProfileAvatar(
                    name: session?.displayName ?? '',
                    imageUrl: imageUrl,
                    preview: _imagePreview,
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          session?.displayName ?? '',
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        Text(session?.role ?? ''),
                        if (session?.tenant?['name'] != null)
                          Text(session!.tenant!['name'].toString()),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          if (isTrainer) ...[
            Card(
              child: Padding(
                padding: const EdgeInsets.all(18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Profilna slika trenera',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 6),
                    const Text('JPG, PNG ili WebP · najviše 5 MiB'),
                    if (_imageError != null) ...[
                      const SizedBox(height: 8),
                      Text(
                        _imageError!,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ],
                    const SizedBox(height: 12),
                    Wrap(
                      spacing: 10,
                      runSpacing: 8,
                      children: [
                        FilledButton.icon(
                          key: const Key('trainer-image-upload'),
                          onPressed: _uploadingImage
                              ? null
                              : _pickAndUploadImage,
                          icon: const Icon(Icons.add_a_photo_outlined),
                          label: Text(
                            _uploadingImage ? 'Slanje...' : 'Odaberi sliku',
                          ),
                        ),
                        if (imageUrl != null)
                          OutlinedButton.icon(
                            key: const Key('trainer-image-remove'),
                            onPressed: _uploadingImage ? null : _removeImage,
                            icon: const Icon(Icons.delete_outline),
                            label: const Text('Ukloni'),
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
          ],
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Form(
                key: _formKey,
                child: Column(
                  children: [
                    TextFormField(
                      controller: _name,
                      decoration: const InputDecoration(
                        labelText: 'Ime i prezime',
                      ),
                      validator: (value) =>
                          value == null || value.trim().length < 2
                          ? 'Unesite ime.'
                          : null,
                    ),
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _email,
                      keyboardType: TextInputType.emailAddress,
                      decoration: const InputDecoration(labelText: 'Email'),
                      validator: (value) =>
                          value == null || !value.contains('@')
                          ? 'Unesite ispravan email.'
                          : null,
                    ),
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _phone,
                      keyboardType: TextInputType.phone,
                      decoration: const InputDecoration(labelText: 'Telefon'),
                    ),
                    const SizedBox(height: 18),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton(
                        onPressed: _saving ? null : _save,
                        child: Text(
                          _saving ? 'Spremanje...' : 'Sačuvaj promjene',
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: context.read<AuthController>().logout,
            icon: const Icon(Icons.logout),
            label: const Text('Odjavi se'),
          ),
        ],
      ),
    );
  }
}

class _ProfileAvatar extends StatelessWidget {
  const _ProfileAvatar({required this.name, this.imageUrl, this.preview});

  final String name;
  final String? imageUrl;
  final Uint8List? preview;

  @override
  Widget build(BuildContext context) {
    final fallback = Center(
      child: Text(
        _initials(name),
        style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18),
      ),
    );
    final image = preview != null
        ? Image.memory(preview!, fit: BoxFit.cover)
        : CachedNetworkImageView(
            imageUrl: imageUrl,
            fallback: fallback,
            decodeWidth: 116,
            decodeHeight: 116,
          );
    return ClipOval(child: SizedBox.square(dimension: 58, child: image));
  }

  static String _initials(String value) {
    final parts = value.trim().split(RegExp(r'\s+')).where((x) => x.isNotEmpty);
    return parts.take(2).map((x) => x[0].toUpperCase()).join();
  }
}
