import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

const _registrationStatuses = ['Draft', 'Submitted', 'Approved', 'Rejected'];

class CentralDashboardScreen extends StatefulWidget {
  const CentralDashboardScreen({super.key});
  @override
  State<CentralDashboardScreen> createState() => _CentralDashboardScreenState();
}

class _CentralDashboardScreenState extends State<CentralDashboardScreen> {
  bool _loading = true;
  Object? _error;
  Map<String, int> _counts = const {};
  List<Map<String, dynamic>> _recent = const [];

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page('/api/admin/gyms'),
        api.page('/api/admin/gyms', query: {'status': 0}),
        api.page('/api/admin/users'),
      ]);
      _counts = {
        'Sve teretane': results[0].totalCount,
        'Čeka aktivaciju': results[1].totalCount,
        'Korisnički računi': results[2].totalCount,
      };
      _recent = results[0].items.take(8).toList();
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => AsyncPanel(
    loading: _loading,
    error: _error,
    onRetry: _load,
    child: ListView(
      children: [
        Wrap(
          spacing: 18,
          runSpacing: 18,
          children: _counts.entries
              .map(
                (entry) => SizedBox(
                  width: 290,
                  child: Card(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(entry.key),
                          const SizedBox(height: 18),
                          Text(
                            '${entry.value}',
                            style: Theme.of(context).textTheme.headlineLarge
                                ?.copyWith(fontWeight: FontWeight.w800),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              )
              .toList(),
        ),
        const SizedBox(height: 26),
        Text(
          'Nedavno dodane teretane',
          style: Theme.of(
            context,
          ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
        ),
        const SizedBox(height: 12),
        Card(
          child: _recent.isEmpty
              ? const SizedBox(
                  height: 150,
                  child: EmptyState('Nema dodanih teretana.'),
                )
              : Column(
                  children: _recent
                      .map(
                        (item) => ListTile(
                          title: Text(item['name'].toString()),
                          subtitle: Text(
                            '${item['cityName']} · ${_date(item['createdAtUtc'])}',
                          ),
                          trailing: StatusPill(
                            enumLabel(item['status'], const [
                              'PendingActivation',
                              'Active',
                              'Inactive',
                              'Suspended',
                            ]),
                          ),
                        ),
                      )
                      .toList(),
                ),
        ),
      ],
    ),
  );
}

class RegistrationManagementScreen extends StatefulWidget {
  const RegistrationManagementScreen({super.key});
  @override
  State<RegistrationManagementScreen> createState() =>
      _RegistrationManagementScreenState();
}

class _RegistrationManagementScreenState
    extends State<RegistrationManagementScreen> {
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  Object? _error;
  int? _status;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      _items = (await context.read<ApiClient>().page(
        '/api/admin/gym-registration-requests',
        query: {'status': _status},
      )).items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _decide(Map<String, dynamic> item, bool approve) async {
    final api = context.read<ApiClient>();
    final reason = await _reasonDialog(
      context,
      approve ? 'Bilješka odobrenja' : 'Razlog odbijanja',
    );
    if (reason == null) return;
    try {
      await api.post(
        '/api/admin/gym-registration-requests/${item['id']}/${approve ? 'approve' : 'reject'}',
        body: {'reason': reason},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _tenantAction(Map<String, dynamic> item, String action) async {
    final tenantId = item['createdTenantId'];
    if (tenantId == null) return;
    final api = context.read<ApiClient>();
    final reason = action == 'activate'
        ? null
        : await _reasonDialog(context, 'Razlog promjene statusa');
    if (action != 'activate' && reason == null) return;
    try {
      await api.post(
        '/api/admin/tenants/$tenantId/$action',
        body: reason == null ? null : {'reason': reason},
      );
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Status je ažuriran.')));
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Align(
        alignment: Alignment.centerLeft,
        child: SizedBox(
          width: 300,
          child: DropdownButtonFormField<int?>(
            initialValue: _status,
            decoration: const InputDecoration(labelText: 'Status pregleda'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ...List.generate(
                _registrationStatuses.length,
                (index) => DropdownMenuItem(
                  value: index,
                  child: Text(_registrationStatuses[index]),
                ),
              ),
            ],
            onChanged: (value) {
              _status = value;
              _load();
            },
          ),
        ),
      ),
      const SizedBox(height: 16),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema registracija za izabrani status.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _items[index];
                      final status = (item['status'] as num?)?.toInt() ?? -1;
                      return ListTile(
                        title: Text(item['gymName'].toString()),
                        subtitle: Text(
                          '${item['address']}, ${item['cityName']}\n${item['description']}',
                        ),
                        isThreeLine: true,
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _registrationStatuses),
                            ),
                            if (status == 1) ...[
                              IconButton(
                                tooltip: 'Odobri',
                                onPressed: () => _decide(item, true),
                                icon: const Icon(
                                  Icons.check_circle_outline,
                                  color: Colors.green,
                                ),
                              ),
                              IconButton(
                                tooltip: 'Odbij',
                                onPressed: () => _decide(item, false),
                                icon: const Icon(
                                  Icons.cancel_outlined,
                                  color: Colors.red,
                                ),
                              ),
                            ],
                            if (item['createdTenantId'] != null)
                              PopupMenuButton<String>(
                                tooltip: 'Status teretane',
                                onSelected: (action) =>
                                    _tenantAction(item, action),
                                itemBuilder: (_) => const [
                                  PopupMenuItem(
                                    value: 'activate',
                                    child: Text('Aktiviraj'),
                                  ),
                                  PopupMenuItem(
                                    value: 'suspend',
                                    child: Text('Suspenduj'),
                                  ),
                                  PopupMenuItem(
                                    value: 'deactivate',
                                    child: Text('Deaktiviraj'),
                                  ),
                                  PopupMenuItem(
                                    value: 'reactivate',
                                    child: Text('Ponovo aktiviraj'),
                                  ),
                                ],
                              ),
                          ],
                        ),
                      );
                    },
                  ),
                ),
        ),
      ),
    ],
  );
}

class GymManagementScreen extends StatefulWidget {
  const GymManagementScreen({super.key});

  @override
  State<GymManagementScreen> createState() => _GymManagementScreenState();
}

class _GymManagementScreenState extends State<GymManagementScreen> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  List<Map<String, dynamic>> _cities = const [];
  bool _loading = true;
  Object? _error;
  int? _status;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page(
          '/api/admin/gyms',
          query: {'query': _search.text.trim(), 'status': _status},
        ),
        api.get('/api/reference-data/lookups', authenticated: false),
      ]);
      _items = (results[0] as PagedData).items;
      final lookups = Map<String, dynamic>.from(results[1]! as Map);
      _cities = (lookups['cities'] as List? ?? const [])
          .whereType<Map>()
          .map((item) => Map<String, dynamic>.from(item))
          .where((item) => item['isActive'] == true)
          .toList();
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _create() async {
    final created = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _GymCreateDialog(cities: _cities),
    );
    if (created == true) {
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Teretana je kreirana i čeka dodjelu administratora.',
            ),
          ),
        );
      }
    }
  }

  Future<void> _tenantAction(Map<String, dynamic> item, String action) async {
    final api = context.read<ApiClient>();
    final reason = action == 'activate'
        ? null
        : await _reasonDialog(context, 'Razlog promjene statusa');
    if (action != 'activate' && reason == null) return;
    try {
      await api.post(
        '/api/admin/tenants/${item['tenantId']}/$action',
        body: reason == null ? null : {'reason': reason},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Row(
        children: [
          SizedBox(
            width: 350,
            child: TextField(
              controller: _search,
              onSubmitted: (_) => _load(),
              decoration: const InputDecoration(
                hintText: 'Naziv, adresa ili grad...',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          const SizedBox(width: 14),
          SizedBox(
            width: 230,
            child: DropdownButtonFormField<int?>(
              initialValue: _status,
              decoration: const InputDecoration(labelText: 'Status'),
              items: const [
                DropdownMenuItem(value: null, child: Text('Svi statusi')),
                DropdownMenuItem(value: 0, child: Text('Čeka aktivaciju')),
                DropdownMenuItem(value: 1, child: Text('Aktivna')),
                DropdownMenuItem(value: 2, child: Text('Neaktivna')),
                DropdownMenuItem(value: 3, child: Text('Suspendovana')),
              ],
              onChanged: (value) {
                _status = value;
                _load();
              },
            ),
          ),
          const Spacer(),
          FilledButton.icon(
            onPressed: _cities.isEmpty ? null : _create,
            icon: const Icon(Icons.add),
            label: const Text('Dodaj teretanu'),
          ),
        ],
      ),
      const SizedBox(height: 16),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema teretana za zadanu pretragu.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _items[index];
                      final status = (item['status'] as num?)?.toInt() ?? 0;
                      const labels = [
                        'PendingActivation',
                        'Active',
                        'Inactive',
                        'Suspended',
                      ];
                      return ListTile(
                        leading: const CircleAvatar(
                          child: Icon(Icons.fitness_center),
                        ),
                        title: Text(item['name'].toString()),
                        subtitle: Text(
                          '${item['address']}, ${item['cityName']}\n'
                          'Aktivni administratori: ${item['activeGymAdminCount']}',
                        ),
                        isThreeLine: true,
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(labels[status.clamp(0, 3)]),
                            PopupMenuButton<String>(
                              tooltip: 'Promijeni status',
                              onSelected: (action) =>
                                  _tenantAction(item, action),
                              itemBuilder: (_) => [
                                if (status == 0)
                                  const PopupMenuItem(
                                    value: 'activate',
                                    child: Text('Aktiviraj'),
                                  ),
                                if (status == 1) ...[
                                  const PopupMenuItem(
                                    value: 'suspend',
                                    child: Text('Suspenduj'),
                                  ),
                                  const PopupMenuItem(
                                    value: 'deactivate',
                                    child: Text('Deaktiviraj'),
                                  ),
                                ],
                                if (status == 2 || status == 3)
                                  const PopupMenuItem(
                                    value: 'reactivate',
                                    child: Text('Ponovo aktiviraj'),
                                  ),
                              ],
                            ),
                          ],
                        ),
                      );
                    },
                  ),
                ),
        ),
      ),
    ],
  );
}

class _GymCreateDialog extends StatefulWidget {
  const _GymCreateDialog({required this.cities});

  final List<Map<String, dynamic>> cities;

  @override
  State<_GymCreateDialog> createState() => _GymCreateDialogState();
}

class _GymCreateDialogState extends State<_GymCreateDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final _address = TextEditingController();
  final _phone = TextEditingController();
  Map<String, dynamic>? _city;
  LatLng? _location;
  bool _busy = false;
  String? _error;
  Map<String, List<String>> _fieldErrors = const {};

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _address.dispose();
    _phone.dispose();
    super.dispose();
  }

  String? _serverError(String field) {
    for (final entry in _fieldErrors.entries) {
      if (entry.key.toLowerCase() == field.toLowerCase()) {
        return entry.value.join('\n');
      }
    }
    return null;
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    if (_location == null) {
      setState(() => _error = 'Označite lokaciju teretane na mapi.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
      _fieldErrors = const {};
    });
    try {
      await context.read<ApiClient>().post(
        '/api/admin/gyms',
        body: {
          'name': _name.text.trim(),
          'description': _description.text.trim(),
          'address': _address.text.trim(),
          'cityId': _city!['id'],
          'latitude': _location!.latitude,
          'longitude': _location!.longitude,
          'phoneNumber': _phone.text.trim().isEmpty ? null : _phone.text.trim(),
        },
      );
      if (mounted) Navigator.pop(context, true);
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
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Dodaj teretanu'),
    content: SizedBox(
      width: 720,
      height: 680,
      child: Form(
        key: _formKey,
        child: ListView(
          children: [
            TextFormField(
              controller: _name,
              decoration: InputDecoration(
                labelText: 'Naziv',
                errorText: _serverError('Name'),
              ),
              validator: (value) => (value?.trim().length ?? 0) < 2
                  ? 'Unesite naziv teretane.'
                  : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _description,
              minLines: 3,
              maxLines: 5,
              decoration: InputDecoration(
                labelText: 'Opis',
                errorText: _serverError('Description'),
              ),
              validator: (value) => (value?.trim().length ?? 0) < 10
                  ? 'Opis mora imati najmanje 10 znakova.'
                  : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _address,
              decoration: InputDecoration(
                labelText: 'Adresa',
                prefixIcon: const Icon(Icons.location_on_outlined),
                errorText: _serverError('Address'),
              ),
              validator: (value) =>
                  (value?.trim().length ?? 0) < 3 ? 'Unesite adresu.' : null,
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<Map<String, dynamic>>(
              initialValue: _city,
              decoration: InputDecoration(
                labelText: 'Grad',
                errorText: _serverError('CityId'),
              ),
              items: widget.cities
                  .map(
                    (city) => DropdownMenuItem(
                      value: city,
                      child: Text('${city['name']} · ${city['countryName']}'),
                    ),
                  )
                  .toList(),
              onChanged: (value) => setState(() => _city = value),
              validator: (value) => value == null ? 'Izaberite grad.' : null,
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _phone,
              decoration: InputDecoration(
                labelText: 'Telefon (opcionalno)',
                prefixIcon: const Icon(Icons.phone_outlined),
                errorText: _serverError('PhoneNumber'),
              ),
            ),
            const SizedBox(height: 16),
            const Text('Kliknite na mapu da označite tačnu lokaciju.'),
            const SizedBox(height: 8),
            SizedBox(
              height: 270,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(14),
                child: FlutterMap(
                  options: MapOptions(
                    initialCenter: const LatLng(43.8563, 18.4131),
                    initialZoom: 12,
                    onTap: (_, point) => setState(() => _location = point),
                  ),
                  children: [
                    TileLayer(
                      urlTemplate:
                          'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'ba.gymlink.gymlink_desktop',
                    ),
                    if (_location != null)
                      MarkerLayer(
                        markers: [
                          Marker(
                            point: _location!,
                            width: 52,
                            height: 52,
                            child: const Icon(
                              Icons.location_pin,
                              color: Colors.red,
                              size: 48,
                            ),
                          ),
                        ],
                      ),
                    const RichAttributionWidget(
                      attributions: [
                        TextSourceAttribution('OpenStreetMap contributors'),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Text(_error!, style: const TextStyle(color: Colors.red)),
            ],
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context, false),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _busy ? null : _submit,
        child: Text(_busy ? 'Kreiranje...' : 'Kreiraj'),
      ),
    ],
  );
}

class UserManagementScreen extends StatefulWidget {
  const UserManagementScreen({super.key});
  @override
  State<UserManagementScreen> createState() => _UserManagementScreenState();
}

class _UserManagementScreenState extends State<UserManagementScreen> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      _items = (await context.read<ApiClient>().page(
        '/api/admin/users',
        query: {'query': _search.text.trim()},
      )).items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _assign() async {
    try {
      final api = context.read<ApiClient>();
      final gyms = await api.page('/api/admin/gyms', query: {'pageSize': 100});
      if (!mounted) return;
      final result = await showDialog<Map<String, Object?>>(
        context: context,
        builder: (_) => _RoleDialog(tenants: gyms.items),
      );
      if (result == null) return;
      await api.post('/api/admin/users/roles/assign', body: result);
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _userAction(Map<String, dynamic> item, String endpoint) async {
    final api = context.read<ApiClient>();
    final reason = await _reasonDialog(context, 'Razlog akcije');
    if (reason == null) return;
    try {
      await api.post(
        '/api/admin/users/$endpoint',
        body: {'identifier': item['email'], 'reason': reason},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Row(
        children: [
          SizedBox(
            width: 390,
            child: TextField(
              controller: _search,
              onSubmitted: (_) => _load(),
              decoration: const InputDecoration(
                hintText: 'Email, korisničko ime ili ime...',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          const Spacer(),
          FilledButton.icon(
            onPressed: _assign,
            icon: const Icon(Icons.admin_panel_settings_outlined),
            label: const Text('Dodijeli ulogu'),
          ),
        ],
      ),
      const SizedBox(height: 16),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema korisnika za zadanu pretragu.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _items[index];
                      return ListTile(
                        title: Text(item['displayName'].toString()),
                        subtitle: Text(
                          '${item['email']} · ${item['assignment']?['name'] ?? 'Bez dodijeljene teretane'}',
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(item['role'].toString()),
                            const SizedBox(width: 8),
                            StatusPill(
                              item['isActive'] == true ? 'Active' : 'Inactive',
                            ),
                            PopupMenuButton<String>(
                              onSelected: (action) => _userAction(item, action),
                              itemBuilder: (_) => [
                                if (item['role'] != 'Member')
                                  const PopupMenuItem(
                                    value: 'roles/revoke',
                                    child: Text('Opozovi ulogu'),
                                  ),
                                PopupMenuItem(
                                  value: item['isActive'] == true
                                      ? 'deactivate'
                                      : 'reactivate',
                                  child: Text(
                                    item['isActive'] == true
                                        ? 'Deaktiviraj račun'
                                        : 'Reaktiviraj račun',
                                  ),
                                ),
                              ],
                            ),
                          ],
                        ),
                      );
                    },
                  ),
                ),
        ),
      ),
    ],
  );
}

class ReferenceDataScreen extends StatefulWidget {
  const ReferenceDataScreen({super.key});
  @override
  State<ReferenceDataScreen> createState() => _ReferenceDataScreenState();
}

class _ReferenceDataScreenState extends State<ReferenceDataScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(length: 4, vsync: this);

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      TabBar(
        controller: _tabs,
        tabs: const [
          Tab(text: 'Države'),
          Tab(text: 'Gradovi'),
          Tab(text: 'Oprema'),
          Tab(text: 'Tipovi treninga'),
        ],
      ),
      const SizedBox(height: 14),
      Expanded(
        child: TabBarView(
          controller: _tabs,
          children: const [
            _ReferenceSection(kind: _ReferenceKind.country),
            _ReferenceSection(kind: _ReferenceKind.city),
            _ReferenceSection(kind: _ReferenceKind.equipment),
            _ReferenceSection(kind: _ReferenceKind.trainingType),
          ],
        ),
      ),
    ],
  );
}

enum _ReferenceKind {
  country('countries', 'Država'),
  city('cities', 'Grad'),
  equipment('equipment', 'Oprema'),
  trainingType('training-types', 'Tip treninga');

  const _ReferenceKind(this.path, this.label);
  final String path;
  final String label;
}

class _ReferenceSection extends StatefulWidget {
  const _ReferenceSection({required this.kind});
  final _ReferenceKind kind;
  @override
  State<_ReferenceSection> createState() => _ReferenceSectionState();
}

class _ReferenceSectionState extends State<_ReferenceSection> {
  final _search = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  List<Map<String, dynamic>> _countries = const [];
  bool _loading = true;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page(
          '/api/admin/reference-data/${widget.kind.path}',
          query: {'query': _search.text.trim()},
        ),
        if (widget.kind == _ReferenceKind.city)
          api.page(
            '/api/admin/reference-data/countries',
            query: {'isActive': true},
          ),
      ]);
      _items = results[0].items;
      if (results.length > 1) _countries = results[1].items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _edit([Map<String, dynamic>? item]) async {
    final api = context.read<ApiClient>();
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => _ReferenceDialog(
        kind: widget.kind,
        value: item,
        countries: _countries,
      ),
    );
    if (result == null) return;
    try {
      if (item == null) {
        await api.post(
          '/api/admin/reference-data/${widget.kind.path}',
          body: result,
        );
      } else {
        await api.put(
          '/api/admin/reference-data/${widget.kind.path}/${item['id']}',
          body: result,
        );
      }
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _deactivate(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Deaktiviraj zapis',
      message: 'Historijski podaci će zadržati ovu oznaku.',
    )) {
      return;
    }
    try {
      await api.delete(
        '/api/admin/reference-data/${widget.kind.path}/${item['id']}',
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Row(
        children: [
          SizedBox(
            width: 360,
            child: TextField(
              controller: _search,
              onSubmitted: (_) => _load(),
              decoration: const InputDecoration(
                hintText: 'Pretraži...',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          const Spacer(),
          FilledButton.icon(
            onPressed: widget.kind == _ReferenceKind.city && _countries.isEmpty
                ? null
                : () => _edit(),
            icon: const Icon(Icons.add),
            label: Text('Dodaj: ${widget.kind.label}'),
          ),
        ],
      ),
      const SizedBox(height: 14),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema referentnih podataka.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _items[index];
                      return ListTile(
                        title: Text(item['name'].toString()),
                        subtitle: Text(
                          item['code']?.toString() ??
                              item['countryName']?.toString() ??
                              item['description']?.toString() ??
                              '',
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              item['isActive'] == true ? 'Active' : 'Inactive',
                            ),
                            IconButton(
                              tooltip: 'Uredi',
                              onPressed: () => _edit(item),
                              icon: const Icon(Icons.edit_outlined),
                            ),
                            if (item['isActive'] == true)
                              IconButton(
                                tooltip: 'Deaktiviraj',
                                onPressed: () => _deactivate(item),
                                icon: const Icon(Icons.block),
                              ),
                          ],
                        ),
                      );
                    },
                  ),
                ),
        ),
      ),
    ],
  );
}

class _RoleDialog extends StatefulWidget {
  const _RoleDialog({required this.tenants});
  final List<Map<String, dynamic>> tenants;
  @override
  State<_RoleDialog> createState() => _RoleDialogState();
}

class _RoleDialogState extends State<_RoleDialog> {
  final _email = TextEditingController();
  final _reason = TextEditingController();
  String _role = 'Member';
  Map<String, dynamic>? _tenant;

  @override
  Widget build(BuildContext context) {
    final needsTenant = _role == 'GymAdmin' || _role == 'Trainer';
    return AlertDialog(
      title: const Text('Dodijeli predefinisanu ulogu'),
      content: SizedBox(
        width: 480,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _email,
              decoration: const InputDecoration(labelText: 'Email korisnika'),
            ),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: _role,
              decoration: const InputDecoration(labelText: 'Uloga'),
              items: const [
                DropdownMenuItem(value: 'Member', child: Text('Član')),
                DropdownMenuItem(value: 'Trainer', child: Text('Trener')),
                DropdownMenuItem(
                  value: 'GymAdmin',
                  child: Text('Administrator teretane'),
                ),
                DropdownMenuItem(
                  value: 'CentralAdmin',
                  child: Text('Centralni administrator'),
                ),
              ],
              onChanged: (value) => setState(() {
                _role = value!;
                if (_role == 'Member' || _role == 'CentralAdmin') {
                  _tenant = null;
                }
              }),
            ),
            if (needsTenant) ...[
              const SizedBox(height: 10),
              DropdownButtonFormField<Map<String, dynamic>>(
                initialValue: _tenant,
                decoration: const InputDecoration(labelText: 'Teretana'),
                items: widget.tenants
                    .map(
                      (item) => DropdownMenuItem(
                        value: item,
                        child: Text(item['name'].toString()),
                      ),
                    )
                    .toList(),
                onChanged: (value) => _tenant = value,
              ),
            ],
            const SizedBox(height: 10),
            TextField(
              controller: _reason,
              decoration: const InputDecoration(labelText: 'Razlog'),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: needsTenant && _tenant == null
              ? null
              : () => Navigator.pop(context, {
                  'identifier': _email.text.trim(),
                  'role': _role,
                  'tenantId': needsTenant ? _tenant!['tenantId'] : null,
                  'reason': _reason.text.trim(),
                }),
          child: const Text('Dodijeli'),
        ),
      ],
    );
  }
}

class _ReferenceDialog extends StatefulWidget {
  const _ReferenceDialog({
    required this.kind,
    required this.countries,
    this.value,
  });
  final _ReferenceKind kind;
  final Map<String, dynamic>? value;
  final List<Map<String, dynamic>> countries;
  @override
  State<_ReferenceDialog> createState() => _ReferenceDialogState();
}

class _ReferenceDialogState extends State<_ReferenceDialog> {
  late final _name = TextEditingController(
    text: widget.value?['name']?.toString() ?? '',
  );
  late final _code = TextEditingController(
    text: widget.value?['code']?.toString() ?? '',
  );
  late final _description = TextEditingController(
    text: widget.value?['description']?.toString() ?? '',
  );
  Map<String, dynamic>? _country;
  late bool _active = widget.value?['isActive'] as bool? ?? true;

  @override
  void initState() {
    super.initState();
    _country = widget.countries
        .where((item) => item['id'] == widget.value?['countryId'])
        .firstOrNull;
    _country ??= widget.countries.firstOrNull;
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.value == null ? 'Novi zapis' : 'Uredi zapis'),
    content: SizedBox(
      width: 440,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (widget.kind == _ReferenceKind.country) ...[
            TextField(
              controller: _code,
              decoration: const InputDecoration(labelText: 'Kod'),
            ),
            const SizedBox(height: 10),
          ],
          if (widget.kind == _ReferenceKind.city) ...[
            DropdownButtonFormField<Map<String, dynamic>>(
              initialValue: _country,
              decoration: const InputDecoration(labelText: 'Država'),
              items: widget.countries
                  .map(
                    (item) => DropdownMenuItem(
                      value: item,
                      child: Text(item['name'].toString()),
                    ),
                  )
                  .toList(),
              onChanged: (value) => _country = value,
            ),
            const SizedBox(height: 10),
          ],
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Naziv'),
          ),
          if (widget.kind == _ReferenceKind.trainingType) ...[
            const SizedBox(height: 10),
            TextField(
              controller: _description,
              maxLines: 3,
              decoration: const InputDecoration(labelText: 'Opis'),
            ),
          ],
          if (widget.value != null)
            SwitchListTile(
              value: _active,
              onChanged: (value) => setState(() => _active = value),
              title: const Text('Aktivno'),
            ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: () {
          final body = <String, Object?>{'name': _name.text.trim()};
          switch (widget.kind) {
            case _ReferenceKind.country:
              body['code'] = _code.text.trim().toUpperCase();
            case _ReferenceKind.city:
              body['countryId'] = _country?['id'];
            case _ReferenceKind.trainingType:
              body['description'] = _description.text.trim().isEmpty
                  ? null
                  : _description.text.trim();
            case _ReferenceKind.equipment:
              break;
          }
          if (widget.value != null) body['isActive'] = _active;
          Navigator.pop(context, body);
        },
        child: const Text('Sačuvaj'),
      ),
    ],
  );
}

Future<String?> _reasonDialog(BuildContext context, String title) async {
  final controller = TextEditingController();
  final result = await showDialog<String>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: TextField(
        controller: controller,
        maxLength: 1000,
        decoration: const InputDecoration(labelText: 'Razlog'),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(context, controller.text.trim()),
          child: const Text('Potvrdi'),
        ),
      ],
    ),
  );
  controller.dispose();
  return result != null && result.length >= 2 ? result : null;
}

String _date(Object? value) => value == null
    ? 'Nije dostavljeno'
    : DateFormat(
        'dd.MM.yyyy.',
      ).format(DateTime.parse(value.toString()).toLocal());
