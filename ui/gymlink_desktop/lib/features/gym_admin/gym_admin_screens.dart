import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

const _requestStatuses = ['Pending', 'Approved', 'Rejected', 'Cancelled'];
const _membershipStatuses = [
  'PendingPayment',
  'Active',
  'Expired',
  'Cancelled',
  'Suspended',
];
const _reservationStatuses = ['Pending', 'Confirmed', 'Completed', 'Cancelled'];
const _visibleReservationStatuses = [1, 2, 3];

class GymDashboardScreen extends StatefulWidget {
  const GymDashboardScreen({super.key});
  @override
  State<GymDashboardScreen> createState() => _GymDashboardScreenState();
}

class _GymDashboardScreenState extends State<GymDashboardScreen> {
  bool _loading = true;
  Object? _error;
  Map<String, int> _counts = const {};
  List<Map<String, dynamic>> _upcoming = const [];

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
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page('/api/tenant/membership-requests', query: {'status': 0}),
        api.page('/api/tenant/memberships', query: {'status': 1}),
        api.page('/api/tenant/trainers', query: {'isActive': true}),
        api.page(
          '/api/tenant/reservations',
          query: {'fromUtc': DateTime.now().toUtc().toIso8601String()},
        ),
      ]);
      _counts = {
        'Zahtjevi na čekanju': results[0].totalCount,
        'Aktivni članovi': results[1].totalCount,
        'Aktivni treneri': results[2].totalCount,
        'Buduće rezervacije': results[3].totalCount,
      };
      _upcoming = results[3].items.take(5).toList();
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
          spacing: 16,
          runSpacing: 16,
          children: _counts.entries
              .map(
                (entry) => SizedBox(
                  width: 250,
                  child: Card(
                    child: Padding(
                      padding: const EdgeInsets.all(22),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(entry.key),
                          const SizedBox(height: 12),
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
          'Nadolazeći termini',
          style: Theme.of(
            context,
          ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
        ),
        const SizedBox(height: 12),
        Card(
          child: _upcoming.isEmpty
              ? const SizedBox(
                  height: 150,
                  child: EmptyState('Nema nadolazećih termina.'),
                )
              : Column(
                  children: _upcoming
                      .map(
                        (item) => ListTile(
                          title: Text(
                            '${item['memberName']} · ${item['trainerName']}',
                          ),
                          subtitle: Text(_dateTime(item['startsAtUtc'])),
                          trailing: StatusPill(
                            enumLabel(item['status'], _reservationStatuses),
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

class TenantMembershipRequestsScreen extends StatefulWidget {
  const TenantMembershipRequestsScreen({super.key});
  @override
  State<TenantMembershipRequestsScreen> createState() =>
      _TenantMembershipRequestsScreenState();
}

class _TenantMembershipRequestsScreenState
    extends State<TenantMembershipRequestsScreen> {
  final _search = TextEditingController();
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
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      _items = (await context.read<ApiClient>().page(
        '/api/tenant/membership-requests',
        query: {'member': _search.text.trim(), 'status': _status},
      )).items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _decide(Map<String, dynamic> item, bool approve) async {
    final api = context.read<ApiClient>();
    String? reason;
    if (!approve) {
      reason = await _reasonDialog(context, 'Razlog odbijanja');
      if (reason == null) return;
    } else if (!await confirmAction(
      context,
      title: 'Odobri članstvo',
      message: 'Članstvo će biti aktivirano bez online naplate.',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/tenant/membership-requests/${item['id']}/${approve ? 'approve' : 'reject'}',
        body: {'concurrencyToken': item['concurrencyToken'], 'reason': ?reason},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
      if (error.status == 409) await _load();
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      _FilterBar(
        search: _search,
        hint: 'Pretraži člana...',
        status: _status,
        statuses: _requestStatuses,
        onStatus: (value) {
          _status = value;
          _load();
        },
        onSearch: _load,
      ),
      const SizedBox(height: 16),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema zahtjeva za izabrane filtere.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final item = _items[index];
                      final pending = (item['status'] as num?)?.toInt() == 0;
                      return ListTile(
                        title: Text(item['memberDisplayName'].toString()),
                        subtitle: Text(
                          '${item['planName']} · ${item['price']} ${item['currency']} · ${_date(item['requestedAtUtc'])}',
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _requestStatuses),
                            ),
                            if (pending) ...[
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

class TenantMembershipsScreen extends StatefulWidget {
  const TenantMembershipsScreen({super.key});
  @override
  State<TenantMembershipsScreen> createState() =>
      _TenantMembershipsScreenState();
}

class _TenantMembershipsScreenState extends State<TenantMembershipsScreen> {
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
        '/api/tenant/memberships',
        query: {'member': _search.text.trim()},
      )).items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _action(Map<String, dynamic> item, String action) async {
    final api = context.read<ApiClient>();
    final reason = action == 'expire'
        ? null
        : await _reasonDialog(context, 'Razlog promjene');
    if (action != 'expire' && reason == null) return;
    try {
      await api.post(
        '/api/tenant/memberships/${item['id']}/$action',
        body: {'concurrencyToken': item['concurrencyToken'], 'reason': ?reason},
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
      _FilterBar(search: _search, hint: 'Pretraži člana...', onSearch: _load),
      const SizedBox(height: 16),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema članstava za izabrane filtere.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final item = _items[index];
                      final actions =
                          (item['allowedActions'] as List? ?? const [])
                              .map((value) => value.toString())
                              .toList();
                      return ListTile(
                        title: Text(item['memberDisplayName'].toString()),
                        subtitle: Text(
                          '${item['planName']} · ${_date(item['startsAtUtc'])} – ${_date(item['endsAtUtc'])}',
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _membershipStatuses),
                            ),
                            PopupMenuButton<String>(
                              enabled: actions.isNotEmpty,
                              onSelected: (action) => _action(item, action),
                              itemBuilder: (_) => actions
                                  .map(
                                    (action) => PopupMenuItem(
                                      value: action,
                                      child: Text(action),
                                    ),
                                  )
                                  .toList(),
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

class TrainerManagementScreen extends StatefulWidget {
  const TrainerManagementScreen({super.key});
  @override
  State<TrainerManagementScreen> createState() =>
      _TrainerManagementScreenState();
}

class _TrainerManagementScreenState extends State<TrainerManagementScreen> {
  List<Map<String, dynamic>> _trainers = const [];
  List<Map<String, dynamic>> _offerings = const [];
  bool _loading = true;
  final Set<String> _imageBusy = {};
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
        api.page('/api/tenant/trainers'),
        api.page('/api/tenant/trainer-offerings'),
      ]);
      _trainers = results[0].items;
      _offerings = results[1].items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _deactivate(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Deaktiviraj trenera',
      message:
          'Trener neće biti izbrisan. Historijski termini i recenzije ostaju sačuvani.',
    )) {
      return;
    }
    try {
      await api.delete('/api/tenant/trainers/${item['id']}');
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _addTrainer() async {
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => const _TrainerPromotionDialog(),
    );
    if (result == null || !mounted) return;
    try {
      await context.read<ApiClient>().post(
        '/api/tenant/trainers',
        body: result,
      );
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Član je promovisan u trenera. Njegove postojeće sesije su odjavljene.',
            ),
          ),
        );
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _uploadTrainerImage(Map<String, dynamic> item) async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: const ['jpg', 'jpeg', 'png', 'webp'],
      withData: true,
    );
    final file = result?.files.singleOrNull;
    if (file == null || !mounted) return;
    final bytes = file.bytes;
    if (bytes == null) {
      _showImageError('Odabranu sliku nije moguće pročitati.');
      return;
    }
    if (bytes.length > 5 * 1024 * 1024) {
      _showImageError('Slika mora biti manja ili jednaka 5 MiB.');
      return;
    }
    final extension = file.extension?.toLowerCase();
    final contentType = switch (extension) {
      'jpg' || 'jpeg' => 'image/jpeg',
      'png' => 'image/png',
      'webp' => 'image/webp',
      _ => null,
    };
    final image = item['managementImage'];
    final token = image is Map ? image['concurrencyToken']?.toString() : null;
    if (contentType == null || token == null || token.isEmpty) {
      _showImageError('Osvježite listu prije izmjene slike.');
      return;
    }
    final id = item['id'].toString();
    setState(() => _imageBusy.add(id));
    try {
      await context.read<ApiClient>().postMultipart(
        '/api/tenant/trainers/$id/image',
        bytes: bytes,
        fileName: file.name,
        contentType: contentType,
        fields: {'concurrencyToken': token},
      );
      await _load();
    } on ApiProblem catch (error) {
      _showImageError(error.message);
    } finally {
      if (mounted) setState(() => _imageBusy.remove(id));
    }
  }

  Future<void> _removeTrainerImage(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    final image = item['managementImage'];
    if (image is! Map || image['imageUrl'] == null) return;
    if (!await confirmAction(
      context,
      title: 'Ukloni sliku trenera',
      message: 'Na mjestima bez slike prikazat će se inicijali trenera.',
    )) {
      return;
    }
    final id = item['id'].toString();
    setState(() => _imageBusy.add(id));
    try {
      await api.delete(
        '/api/tenant/trainers/$id/image',
        body: {'concurrencyToken': image['concurrencyToken']},
      );
      await _load();
    } on ApiProblem catch (error) {
      _showImageError(error.message);
    } finally {
      if (mounted) setState(() => _imageBusy.remove(id));
    }
  }

  void _showImageError(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _addOffering() async {
    final api = context.read<ApiClient>();
    try {
      final lookups = Map<String, dynamic>.from(
        (await api.get('/api/reference-data/lookups', authenticated: false))!
            as Map,
      );
      if (!mounted) return;
      final result = await showDialog<Map<String, Object?>>(
        context: context,
        builder: (_) => _OfferingDialog(
          trainers: _trainers
              .where((item) => item['isActive'] == true)
              .toList(),
          types: (lookups['trainingTypes'] as List? ?? const [])
              .whereType<Map>()
              .map((item) => Map<String, dynamic>.from(item))
              .toList(),
        ),
      );
      if (result == null) return;
      await api.post('/api/tenant/trainer-offerings', body: result);
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
  Widget build(BuildContext context) => AsyncPanel(
    loading: _loading,
    error: _error,
    onRetry: _load,
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Card(
            child: Column(
              children: [
                ListTile(
                  title: const Text(
                    'Lista trenera',
                    style: TextStyle(fontWeight: FontWeight.w800),
                  ),
                  subtitle: const Text(
                    'Aktivnog člana ove teretane možete unaprijediti u trenera.',
                  ),
                  trailing: FilledButton.icon(
                    onPressed: _addTrainer,
                    icon: const Icon(Icons.person_add_alt_1),
                    label: const Text('Dodaj trenera'),
                  ),
                ),
                const Divider(height: 1),
                Expanded(
                  child: _trainers.isEmpty
                      ? const EmptyState('Nema dodijeljenih trenera.')
                      : ListView.separated(
                          itemCount: _trainers.length,
                          separatorBuilder: (_, _) => const Divider(height: 1),
                          itemBuilder: (_, index) {
                            final item = _trainers[index];
                            final id = item['id'].toString();
                            final imageUrl = context.read<ApiClient>().mediaUrl(
                              item['imageUrl'],
                            );
                            return ListTile(
                              leading: _TrainerAvatar(
                                name: item['displayName'].toString(),
                                imageUrl: imageUrl,
                              ),
                              title: Text(item['displayName'].toString()),
                              subtitle: Text(
                                '${item['credentials'] ?? 'Bez unesenih kvalifikacija'} · ★ ${item['averageRating']}',
                              ),
                              trailing: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  StatusPill(
                                    item['isActive'] == true
                                        ? 'Active'
                                        : 'Inactive',
                                  ),
                                  if (_imageBusy.contains(id))
                                    const Padding(
                                      padding: EdgeInsets.all(12),
                                      child: SizedBox.square(
                                        dimension: 18,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      ),
                                    )
                                  else
                                    PopupMenuButton<String>(
                                      tooltip: 'Radnje trenera',
                                      onSelected: (value) {
                                        if (value == 'image') {
                                          _uploadTrainerImage(item);
                                        } else if (value == 'remove-image') {
                                          _removeTrainerImage(item);
                                        } else if (value == 'deactivate') {
                                          _deactivate(item);
                                        }
                                      },
                                      itemBuilder: (_) => [
                                        const PopupMenuItem(
                                          value: 'image',
                                          child: Text(
                                            'Dodaj ili zamijeni sliku',
                                          ),
                                        ),
                                        if (imageUrl != null)
                                          const PopupMenuItem(
                                            value: 'remove-image',
                                            child: Text('Ukloni sliku'),
                                          ),
                                        if (item['isActive'] == true)
                                          const PopupMenuItem(
                                            value: 'deactivate',
                                            child: Text('Deaktiviraj trenera'),
                                          ),
                                      ],
                                    ),
                                ],
                              ),
                            );
                          },
                        ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(width: 18),
        Expanded(
          child: Card(
            child: Column(
              children: [
                ListTile(
                  title: const Text(
                    'Usluge trenera',
                    style: TextStyle(fontWeight: FontWeight.w800),
                  ),
                  trailing: FilledButton.icon(
                    onPressed: _trainers.isEmpty ? null : _addOffering,
                    icon: const Icon(Icons.add),
                    label: const Text('Dodaj'),
                  ),
                ),
                const Divider(height: 1),
                Expanded(
                  child: _offerings.isEmpty
                      ? const EmptyState('Nema definisanih usluga.')
                      : ListView.separated(
                          itemCount: _offerings.length,
                          separatorBuilder: (_, _) => const Divider(height: 1),
                          itemBuilder: (_, index) {
                            final item = _offerings[index];
                            final trainer = _trainers
                                .where(
                                  (trainer) =>
                                      trainer['id'] == item['trainerProfileId'],
                                )
                                .firstOrNull;
                            return ListTile(
                              title: Text(item['name'].toString()),
                              subtitle: Text(
                                '${trainer?['displayName'] ?? 'Trener'} · ${item['trainingType']} · ${item['durationMinutes']} min',
                              ),
                              trailing: Text(
                                '${item['price']} ${item['currency']}',
                              ),
                            );
                          },
                        ),
                ),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}

class _TrainerAvatar extends StatelessWidget {
  const _TrainerAvatar({required this.name, this.imageUrl});

  final String name;
  final String? imageUrl;

  @override
  Widget build(BuildContext context) {
    final initials = name
        .trim()
        .split(RegExp(r'\s+'))
        .where((part) => part.isNotEmpty)
        .take(2)
        .map((part) => part[0].toUpperCase())
        .join();
    final fallback = Center(
      child: Text(
        initials,
        style: const TextStyle(fontWeight: FontWeight.w700),
      ),
    );
    return ClipOval(
      child: SizedBox.square(
        dimension: 42,
        child: imageUrl == null
            ? fallback
            : Image.network(
                imageUrl!,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => fallback,
              ),
      ),
    );
  }
}

class TenantAvailabilityScreen extends StatefulWidget {
  const TenantAvailabilityScreen({super.key});
  @override
  State<TenantAvailabilityScreen> createState() =>
      _TenantAvailabilityScreenState();
}

class _TenantAvailabilityScreenState extends State<TenantAvailabilityScreen> {
  static const _days = [
    'Ponedjeljak',
    'Utorak',
    'Srijeda',
    'Četvrtak',
    'Petak',
    'Subota',
    'Nedjelja',
  ];
  List<Map<String, dynamic>> _trainers = const [];
  final Set<(int, int)> _selected = {};
  String? _trainerId;
  String? _concurrencyToken;
  bool _loading = true;
  bool _saving = false;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _loadInitial();
  }

  Future<void> _loadInitial() async {
    setState(() => _loading = true);
    try {
      _trainers = (await context.read<ApiClient>().page(
        '/api/tenant/trainers',
        query: {'isActive': true},
      )).items;
      _trainerId ??= _trainers.firstOrNull?['id']?.toString();
      await _loadSchedule();
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadSchedule() async {
    if (_trainerId == null) {
      _selected.clear();
      _concurrencyToken = null;
      return;
    }
    final schedule = Map<String, dynamic>.from(
      (await context.read<ApiClient>().get(
            '/api/tenant/trainer-availability/schedule',
            query: {'trainerProfileId': _trainerId},
          ))!
          as Map,
    );
    _concurrencyToken = schedule['concurrencyToken']?.toString();
    _selected
      ..clear()
      ..addAll(
        (schedule['shifts'] as List? ?? const []).whereType<Map>().map(
          (item) => (
            (item['dayOfWeek'] as num).toInt(),
            (item['period'] as num).toInt(),
          ),
        ),
      );
    if (mounted) setState(() {});
  }

  Future<void> _save() async {
    if (_trainerId == null) return;
    setState(() => _saving = true);
    try {
      final schedule = Map<String, dynamic>.from(
        (await context.read<ApiClient>().put(
              '/api/tenant/trainer-availability/schedule',
              body: {
                'trainerProfileId': _trainerId,
                'shifts': _selected
                    .map((item) => {'dayOfWeek': item.$1, 'period': item.$2})
                    .toList(),
                'concurrencyToken': _concurrencyToken,
              },
            ))!
            as Map,
      );
      _concurrencyToken = schedule['concurrencyToken']?.toString();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Sedmični raspored je sačuvan.')),
        );
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
      if (error.status == 409) await _loadSchedule();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => AsyncPanel(
    loading: _loading,
    error: _error,
    onRetry: _loadInitial,
    child: Column(
      children: [
        Row(
          children: [
            SizedBox(
              width: 380,
              child: DropdownButtonFormField<String>(
                initialValue: _trainerId,
                decoration: const InputDecoration(labelText: 'Trener'),
                items: _trainers
                    .map(
                      (item) => DropdownMenuItem(
                        value: item['id'].toString(),
                        child: Text(item['displayName'].toString()),
                      ),
                    )
                    .toList(),
                onChanged: (value) {
                  _trainerId = value;
                  setState(() => _loading = true);
                  _loadSchedule()
                      .catchError((Object error) {
                        _error = error;
                      })
                      .whenComplete(() {
                        if (mounted) setState(() => _loading = false);
                      });
                },
              ),
            ),
            const Spacer(),
            FilledButton.icon(
              onPressed: _trainerId == null || _saving ? null : _save,
              icon: _saving
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.save_outlined),
              label: const Text('Sačuvaj raspored'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Expanded(
          child: _trainerId == null
              ? const EmptyState('Nema aktivnih trenera.')
              : GridView.builder(
                  gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                    maxCrossAxisExtent: 330,
                    mainAxisExtent: 170,
                    crossAxisSpacing: 12,
                    mainAxisSpacing: 12,
                  ),
                  itemCount: _days.length,
                  itemBuilder: (context, index) {
                    final day = (index + 1) % 7;
                    return Card(
                      margin: EdgeInsets.zero,
                      child: Padding(
                        padding: const EdgeInsets.all(14),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              _days[index],
                              style: const TextStyle(
                                fontWeight: FontWeight.w800,
                              ),
                            ),
                            CheckboxListTile(
                              dense: true,
                              contentPadding: EdgeInsets.zero,
                              value: _selected.contains((day, 0)),
                              onChanged: (value) =>
                                  _toggle(day, 0, value ?? false),
                              title: const Text('Jutarnja · 08:00–15:00'),
                            ),
                            CheckboxListTile(
                              dense: true,
                              contentPadding: EdgeInsets.zero,
                              value: _selected.contains((day, 1)),
                              onChanged: (value) =>
                                  _toggle(day, 1, value ?? false),
                              title: const Text('Popodnevna · 15:00–22:00'),
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
        const SizedBox(height: 8),
        const Align(
          alignment: Alignment.centerLeft,
          child: Text(
            'Aktivne smjene se ponavljaju sedmično u vremenskoj zoni Sarajevo.',
          ),
        ),
      ],
    ),
  );

  void _toggle(int day, int period, bool selected) {
    setState(() {
      if (selected) {
        _selected.add((day, period));
      } else {
        _selected.remove((day, period));
      }
    });
  }
}

class TenantReservationsScreen extends StatefulWidget {
  const TenantReservationsScreen({super.key});
  @override
  State<TenantReservationsScreen> createState() =>
      _TenantReservationsScreenState();
}

class _TenantReservationsScreenState extends State<TenantReservationsScreen> {
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
        '/api/tenant/reservations',
        query: {'status': _status},
      )).items;
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _command(Map<String, dynamic> item, String action) async {
    final api = context.read<ApiClient>();
    String? reason;
    if (action == 'cancel') {
      reason = await _reasonDialog(context, 'Razlog otkazivanja');
      if (reason == null) return;
    } else if (!await confirmAction(
      context,
      title: 'Promjena rezervacije',
      message: 'Izvrši akciju “$action”?',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/tenant/reservations/${item['id']}/$action',
        body: {'concurrencyToken': item['concurrencyToken'], 'reason': ?reason},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
      if (error.status == 409) await _load();
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
            key: const Key('reservation-status-filter'),
            initialValue: _status,
            decoration: const InputDecoration(labelText: 'Status'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ..._visibleReservationStatuses.map(
                (status) => DropdownMenuItem(
                  value: status,
                  child: Text(_reservationStatuses[status]),
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
              ? const EmptyState('Nema rezervacija za izabrani status.')
              : Card(
                  child: ListView.separated(
                    itemCount: _items.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _items[index];
                      final status = (item['status'] as num?)?.toInt() ?? -1;
                      return ListTile(
                        title: Text(
                          '${item['memberName']} · ${item['trainerName']}',
                        ),
                        subtitle: Text(
                          '${item['offeringName']} · ${_dateTime(item['startsAtUtc'])}',
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _reservationStatuses),
                            ),
                            PopupMenuButton<String>(
                              enabled: status < 2,
                              onSelected: (action) => _command(item, action),
                              itemBuilder: (_) => [
                                if (status == 0)
                                  const PopupMenuItem(
                                    value: 'confirm',
                                    child: Text('Potvrdi'),
                                  ),
                                if (status == 1)
                                  const PopupMenuItem(
                                    value: 'complete',
                                    child: Text('Označi završenom'),
                                  ),
                                const PopupMenuItem(
                                  value: 'cancel',
                                  child: Text('Otkaži uz razlog'),
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

class GymCatalogScreen extends StatefulWidget {
  const GymCatalogScreen({super.key});
  @override
  State<GymCatalogScreen> createState() => _GymCatalogScreenState();
}

class _GymCatalogScreenState extends State<GymCatalogScreen> {
  Map<String, dynamic>? _gym;
  Map<String, dynamic>? _lookups;
  List<Map<String, dynamic>> _plans = const [];
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
        api.get('/api/tenant/gym'),
        api.page('/api/tenant/membership-plans'),
        api.get('/api/reference-data/lookups', authenticated: false),
      ]);
      _gym = Map<String, dynamic>.from(results[0]! as Map);
      _plans = (results[1] as PagedData).items;
      _lookups = Map<String, dynamic>.from(results[2]! as Map);
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _addPlan() async {
    final api = context.read<ApiClient>();
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => const _PlanDialog(),
    );
    if (result == null) return;
    try {
      await api.post('/api/tenant/membership-plans', body: result);
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _editGym() async {
    final api = context.read<ApiClient>();
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => _GymEditorDialog(gym: _gym!, lookups: _lookups!),
    );
    if (result == null) return;
    try {
      await api.put('/api/tenant/gym', body: result);
      await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _deactivatePlan(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Deaktiviraj plan',
      message: 'Plan ostaje vidljiv u historijskim članstvima.',
    )) {
      return;
    }
    try {
      await api.delete('/api/tenant/membership-plans/${item['id']}');
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
  Widget build(BuildContext context) => AsyncPanel(
    loading: _loading,
    error: _error,
    onRetry: _load,
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _gym?['name']?.toString() ?? '',
                    style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 10),
                  Text('${_gym?['address']}, ${_gym?['city']}'),
                  const SizedBox(height: 16),
                  Text(_gym?['description']?.toString() ?? ''),
                  const SizedBox(height: 20),
                  Text(
                    'Oprema: ${(_gym?['equipment'] as List? ?? const []).join(', ')}',
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Tipovi treninga: ${(_gym?['trainingTypes'] as List? ?? const []).join(', ')}',
                  ),
                  const SizedBox(height: 18),
                  FilledButton.icon(
                    onPressed: _gym == null || _lookups == null
                        ? null
                        : _editGym,
                    icon: const Icon(Icons.edit_outlined),
                    label: const Text('Uredi profil teretane'),
                  ),
                ],
              ),
            ),
          ),
        ),
        const SizedBox(width: 18),
        Expanded(
          child: Card(
            child: Column(
              children: [
                ListTile(
                  title: const Text(
                    'Planovi članstva',
                    style: TextStyle(fontWeight: FontWeight.w800),
                  ),
                  trailing: FilledButton.icon(
                    onPressed: _addPlan,
                    icon: const Icon(Icons.add),
                    label: const Text('Dodaj'),
                  ),
                ),
                const Divider(height: 1),
                Expanded(
                  child: _plans.isEmpty
                      ? const EmptyState('Nema planova članstva.')
                      : ListView.separated(
                          itemCount: _plans.length,
                          separatorBuilder: (_, _) => const Divider(height: 1),
                          itemBuilder: (_, index) {
                            final item = _plans[index];
                            return ListTile(
                              title: Text(item['name'].toString()),
                              subtitle: Text(
                                '${item['durationDays']} dana · ${item['price']} ${item['currency']}',
                              ),
                              trailing: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  StatusPill(
                                    item['isActive'] == true
                                        ? 'Active'
                                        : 'Inactive',
                                  ),
                                  if (item['isActive'] == true)
                                    IconButton(
                                      tooltip: 'Deaktiviraj',
                                      onPressed: () => _deactivatePlan(item),
                                      icon: const Icon(Icons.block),
                                    ),
                                ],
                              ),
                            );
                          },
                        ),
                ),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}

class _FilterBar extends StatelessWidget {
  const _FilterBar({
    required this.search,
    required this.hint,
    required this.onSearch,
    this.status,
    this.statuses,
    this.onStatus,
  });
  final TextEditingController search;
  final String hint;
  final VoidCallback onSearch;
  final int? status;
  final List<String>? statuses;
  final ValueChanged<int?>? onStatus;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      SizedBox(
        width: 380,
        child: TextField(
          controller: search,
          onSubmitted: (_) => onSearch(),
          decoration: InputDecoration(
            hintText: hint,
            prefixIcon: const Icon(Icons.search),
          ),
        ),
      ),
      if (statuses != null) ...[
        const SizedBox(width: 12),
        SizedBox(
          width: 250,
          child: DropdownButtonFormField<int?>(
            initialValue: status,
            decoration: const InputDecoration(labelText: 'Status'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ...List.generate(
                statuses!.length,
                (index) => DropdownMenuItem(
                  value: index,
                  child: Text(statuses![index]),
                ),
              ),
            ],
            onChanged: onStatus,
          ),
        ),
      ],
      const Spacer(),
      IconButton.filledTonal(
        tooltip: 'Osvježi',
        onPressed: onSearch,
        icon: const Icon(Icons.refresh),
      ),
    ],
  );
}

class _TrainerPromotionDialog extends StatefulWidget {
  const _TrainerPromotionDialog();

  @override
  State<_TrainerPromotionDialog> createState() =>
      _TrainerPromotionDialogState();
}

class _TrainerPromotionDialogState extends State<_TrainerPromotionDialog> {
  final _formKey = GlobalKey<FormState>();
  final _search = TextEditingController();
  final _biography = TextEditingController();
  final _credentials = TextEditingController();
  final _reason = TextEditingController();
  final Set<String> _trainingTypeIds = {};
  List<Map<String, dynamic>> _candidates = const [];
  List<Map<String, dynamic>> _trainingTypes = const [];
  Map<String, dynamic>? _candidate;
  bool _loading = true;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    _biography.dispose();
    _credentials.dispose();
    _reason.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page(
          '/api/tenant/trainer-candidates',
          query: {'query': _search.text.trim()},
        ),
        api.get('/api/reference-data/lookups', authenticated: false),
      ]);
      _candidates = (results[0] as PagedData).items;
      final lookups = Map<String, dynamic>.from(results[1]! as Map);
      _trainingTypes = (lookups['trainingTypes'] as List? ?? const [])
          .whereType<Map>()
          .map((item) => Map<String, dynamic>.from(item))
          .toList(growable: false);
      if (!_candidates.contains(_candidate)) {
        _candidate = _candidates.firstOrNull;
      }
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Dodaj trenera'),
    content: SizedBox(
      width: 620,
      child: AsyncPanel(
        loading: _loading,
        error: _error,
        onRetry: _load,
        child: Form(
          key: _formKey,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _search,
                        onSubmitted: (_) => _load(),
                        decoration: const InputDecoration(
                          labelText: 'Pretraži aktivne članove',
                          prefixIcon: Icon(Icons.search),
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    IconButton(
                      tooltip: 'Pretraži',
                      onPressed: _load,
                      icon: const Icon(Icons.search),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<Map<String, dynamic>>(
                  initialValue: _candidate,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Aktivni član'),
                  items: _candidates
                      .map(
                        (item) => DropdownMenuItem(
                          value: item,
                          child: Text(
                            '${item['displayName']} · ${item['email']} · ${item['membershipPlan']}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      )
                      .toList(),
                  onChanged: (value) => _candidate = value,
                  validator: (value) =>
                      value == null ? 'Odaberite aktivnog člana.' : null,
                ),
                if (_candidates.isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 8),
                    child: Text(
                      'Nema članova koji ispunjavaju uslove za promociju.',
                    ),
                  ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _biography,
                  maxLines: 3,
                  decoration: const InputDecoration(labelText: 'Biografija'),
                  validator: (value) => value == null || value.trim().isEmpty
                      ? 'Biografija je obavezna.'
                      : null,
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: _credentials,
                  decoration: const InputDecoration(
                    labelText: 'Kvalifikacije i iskustvo',
                  ),
                ),
                const SizedBox(height: 12),
                const Text(
                  'Specijalnosti',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 8,
                  runSpacing: 6,
                  children: _trainingTypes.map((item) {
                    final id = item['id'].toString();
                    return FilterChip(
                      label: Text(item['name'].toString()),
                      selected: _trainingTypeIds.contains(id),
                      onSelected: (selected) => setState(
                        () => selected
                            ? _trainingTypeIds.add(id)
                            : _trainingTypeIds.remove(id),
                      ),
                    );
                  }).toList(),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _reason,
                  decoration: const InputDecoration(
                    labelText: 'Razlog promocije',
                  ),
                  validator: (value) => value == null || value.trim().length < 2
                      ? 'Unesite razlog promocije.'
                      : null,
                ),
              ],
            ),
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _candidates.isEmpty
            ? null
            : () {
                if (!_formKey.currentState!.validate()) return;
                Navigator.pop(context, {
                  'userId': _candidate!['userId'],
                  'biography': _biography.text.trim(),
                  'credentials': _credentials.text.trim().isEmpty
                      ? null
                      : _credentials.text.trim(),
                  'trainingTypeIds': _trainingTypeIds.toList(),
                  'reason': _reason.text.trim(),
                });
              },
        child: const Text('Promoviši u trenera'),
      ),
    ],
  );
}

class _OfferingDialog extends StatefulWidget {
  const _OfferingDialog({required this.trainers, required this.types});
  final List<Map<String, dynamic>> trainers;
  final List<Map<String, dynamic>> types;
  @override
  State<_OfferingDialog> createState() => _OfferingDialogState();
}

class _OfferingDialogState extends State<_OfferingDialog> {
  final _name = TextEditingController();
  final _duration = TextEditingController(text: '60');
  final _price = TextEditingController(text: '25');
  Map<String, dynamic>? _trainer;
  Map<String, dynamic>? _type;

  @override
  void initState() {
    super.initState();
    _trainer = widget.trainers.firstOrNull;
    _type = widget.types.firstOrNull;
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Nova usluga'),
    content: SizedBox(
      width: 460,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          DropdownButtonFormField<Map<String, dynamic>>(
            initialValue: _trainer,
            decoration: const InputDecoration(labelText: 'Trener'),
            items: widget.trainers
                .map(
                  (item) => DropdownMenuItem(
                    value: item,
                    child: Text(item['displayName'].toString()),
                  ),
                )
                .toList(),
            onChanged: (value) => _trainer = value,
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<Map<String, dynamic>>(
            initialValue: _type,
            decoration: const InputDecoration(labelText: 'Tip treninga'),
            items: widget.types
                .map(
                  (item) => DropdownMenuItem(
                    value: item,
                    child: Text(item['name'].toString()),
                  ),
                )
                .toList(),
            onChanged: (value) => _type = value,
          ),
          const SizedBox(height: 10),
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Naziv'),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _duration,
                  decoration: const InputDecoration(
                    labelText: 'Trajanje (min)',
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: TextField(
                  controller: _price,
                  decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
                ),
              ),
            ],
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
        onPressed: _trainer == null || _type == null
            ? null
            : () => Navigator.pop(context, {
                'trainerProfileId': _trainer!['id'],
                'trainingTypeId': _type!['id'],
                'name': _name.text.trim(),
                'durationMinutes': int.tryParse(_duration.text),
                'price': double.tryParse(_price.text.replaceFirst(',', '.')),
                'currency': 'BAM',
              }),
        child: const Text('Sačuvaj'),
      ),
    ],
  );
}

class _PlanDialog extends StatefulWidget {
  const _PlanDialog();
  @override
  State<_PlanDialog> createState() => _PlanDialogState();
}

class _PlanDialogState extends State<_PlanDialog> {
  final _name = TextEditingController();
  final _days = TextEditingController(text: '30');
  final _price = TextEditingController(text: '50');

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Novi plan članstva'),
    content: SizedBox(
      width: 420,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Naziv'),
          ),
          const SizedBox(height: 10),
          TextField(
            controller: _days,
            decoration: const InputDecoration(labelText: 'Trajanje (dana)'),
          ),
          const SizedBox(height: 10),
          TextField(
            controller: _price,
            decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
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
        onPressed: () => Navigator.pop(context, {
          'name': _name.text.trim(),
          'durationDays': int.tryParse(_days.text),
          'price': double.tryParse(_price.text.replaceFirst(',', '.')),
          'currency': 'BAM',
        }),
        child: const Text('Sačuvaj'),
      ),
    ],
  );
}

class _GymEditorDialog extends StatefulWidget {
  const _GymEditorDialog({required this.gym, required this.lookups});
  final Map<String, dynamic> gym;
  final Map<String, dynamic> lookups;

  @override
  State<_GymEditorDialog> createState() => _GymEditorDialogState();
}

class _GymEditorDialogState extends State<_GymEditorDialog> {
  late final _name = TextEditingController(
    text: widget.gym['name']?.toString() ?? '',
  );
  late final _description = TextEditingController(
    text: widget.gym['description']?.toString() ?? '',
  );
  late final _address = TextEditingController(
    text: widget.gym['address']?.toString() ?? '',
  );
  late final _phone = TextEditingController(
    text: widget.gym['phoneNumber']?.toString() ?? '',
  );
  late String _cityId = widget.gym['cityId'].toString();
  late final Set<String> _equipmentIds = (widget.gym['equipmentIds'] as List)
      .map((value) => value.toString())
      .toSet();
  late final Set<String> _trainingTypeIds =
      (widget.gym['trainingTypeIds'] as List)
          .map((value) => value.toString())
          .toSet();

  List<Map<String, dynamic>> _items(String key) =>
      (widget.lookups[key] as List? ?? const [])
          .whereType<Map>()
          .map((item) => Map<String, dynamic>.from(item))
          .where((item) => item['isActive'] == true)
          .toList();

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Uredi profil teretane'),
    content: SizedBox(
      width: 720,
      child: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Naziv'),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _description,
              maxLines: 4,
              decoration: const InputDecoration(labelText: 'Opis'),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _address,
              decoration: const InputDecoration(labelText: 'Adresa'),
            ),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: _cityId,
              decoration: const InputDecoration(labelText: 'Grad'),
              items: _items('cities')
                  .map(
                    (item) => DropdownMenuItem(
                      value: item['id'].toString(),
                      child: Text('${item['name']}, ${item['countryName']}'),
                    ),
                  )
                  .toList(),
              onChanged: (value) => _cityId = value!,
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _phone,
              decoration: const InputDecoration(labelText: 'Telefon'),
            ),
            const SizedBox(height: 18),
            const Text('Oprema', style: TextStyle(fontWeight: FontWeight.w800)),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              runSpacing: 6,
              children: _items('equipment').map((item) {
                final id = item['id'].toString();
                return FilterChip(
                  label: Text(item['name'].toString()),
                  selected: _equipmentIds.contains(id),
                  onSelected: (selected) => setState(
                    () => selected
                        ? _equipmentIds.add(id)
                        : _equipmentIds.remove(id),
                  ),
                );
              }).toList(),
            ),
            const SizedBox(height: 18),
            const Text(
              'Tipovi treninga',
              style: TextStyle(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              runSpacing: 6,
              children: _items('trainingTypes').map((item) {
                final id = item['id'].toString();
                return FilterChip(
                  label: Text(item['name'].toString()),
                  selected: _trainingTypeIds.contains(id),
                  onSelected: (selected) => setState(
                    () => selected
                        ? _trainingTypeIds.add(id)
                        : _trainingTypeIds.remove(id),
                  ),
                );
              }).toList(),
            ),
            const SizedBox(height: 16),
            const Text(
              'Postojeće radno vrijeme će biti sačuvano. Koordinate mape ostaju vezane za postojeću lokaciju.',
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(context, {
          'name': _name.text.trim(),
          'description': _description.text.trim(),
          'address': _address.text.trim(),
          'cityId': _cityId,
          'latitude': widget.gym['latitude'],
          'longitude': widget.gym['longitude'],
          'phoneNumber': _phone.text.trim().isEmpty ? null : _phone.text.trim(),
          'equipmentIds': _equipmentIds.toList(),
          'trainingTypeIds': _trainingTypeIds.toList(),
          'workingHours': widget.gym['workingHours'],
        }),
        child: const Text('Sačuvaj'),
      ),
    ],
  );
}

Future<String?> _reasonDialog(BuildContext context, String title) async {
  final controller = TextEditingController();
  final value = await showDialog<String>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: TextField(
        controller: controller,
        autofocus: true,
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
  return value != null && value.length >= 2 ? value : null;
}

String _date(Object? value) => DateFormat(
  'dd.MM.yyyy.',
).format(DateTime.parse(value.toString()).toLocal());
String _dateTime(Object? value) => DateFormat(
  'dd.MM.yyyy. HH:mm',
).format(DateTime.parse(value.toString()).toLocal());
