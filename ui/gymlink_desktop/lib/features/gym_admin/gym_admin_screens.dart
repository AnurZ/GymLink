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
const _availabilityStatuses = [
  'Available',
  'Unavailable',
  'Reserved',
  'Cancelled',
];

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
                const ListTile(
                  title: Text(
                    'Lista trenera',
                    style: TextStyle(fontWeight: FontWeight.w800),
                  ),
                  subtitle: Text(
                    'Novi trener se prvo dodjeljuje računu kroz CentralAdmin.',
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
                            return ListTile(
                              leading: const CircleAvatar(
                                child: Icon(Icons.person),
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
                                  if (item['isActive'] == true)
                                    IconButton(
                                      tooltip: 'Deaktiviraj',
                                      onPressed: () => _deactivate(item),
                                      icon: const Icon(
                                        Icons.person_off_outlined,
                                        color: Colors.red,
                                      ),
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

class TenantAvailabilityScreen extends StatefulWidget {
  const TenantAvailabilityScreen({super.key});
  @override
  State<TenantAvailabilityScreen> createState() =>
      _TenantAvailabilityScreenState();
}

class _TenantAvailabilityScreenState extends State<TenantAvailabilityScreen> {
  List<Map<String, dynamic>> _trainers = const [];
  List<Map<String, dynamic>> _slots = const [];
  String? _trainerId;
  bool _loading = true;
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
      await _loadSlots();
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadSlots() async {
    if (_trainerId == null) {
      _slots = const [];
      return;
    }
    _slots = (await context.read<ApiClient>().page(
      '/api/tenant/trainer-availability',
      query: {
        'trainerProfileId': _trainerId,
        'fromUtc': DateTime.now().toUtc().toIso8601String(),
      },
    )).items;
    if (mounted) setState(() {});
  }

  Future<void> _create() async {
    if (_trainerId == null) return;
    final api = context.read<ApiClient>();
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => const _AvailabilityDialog(),
    );
    if (result == null) return;
    try {
      await api.post(
        '/api/tenant/trainer-availability',
        body: {'trainerProfileId': _trainerId, ...result},
      );
      await _loadSlots();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _cancel(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Otkaži termin',
      message: 'Rezervisani termin nije moguće promijeniti.',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/tenant/trainer-availability/${item['id']}/cancel',
        body: {'concurrencyToken': item['concurrencyToken']},
      );
      await _loadSlots();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
      if (error.status == 409) await _loadSlots();
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
                  _loadSlots();
                },
              ),
            ),
            const Spacer(),
            FilledButton.icon(
              onPressed: _trainerId == null ? null : _create,
              icon: const Icon(Icons.add),
              label: const Text('Dodaj termin'),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Expanded(
          child: _slots.isEmpty
              ? const EmptyState('Nema budućih termina za ovog trenera.')
              : Card(
                  child: ListView.separated(
                    itemCount: _slots.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, index) {
                      final item = _slots[index];
                      final status = (item['status'] as num?)?.toInt() ?? -1;
                      return ListTile(
                        title: Text(_dateTime(item['startsAtUtc'])),
                        subtitle: Text('do ${_time(item['endsAtUtc'])}'),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _availabilityStatuses),
                            ),
                            if (status < 2)
                              IconButton(
                                tooltip: 'Otkaži',
                                onPressed: () => _cancel(item),
                                icon: const Icon(Icons.cancel_outlined),
                              ),
                          ],
                        ),
                      );
                    },
                  ),
                ),
        ),
      ],
    ),
  );
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
            initialValue: _status,
            decoration: const InputDecoration(labelText: 'Status'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ...List.generate(
                _reservationStatuses.length,
                (index) => DropdownMenuItem(
                  value: index,
                  child: Text(_reservationStatuses[index]),
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

class _AvailabilityDialog extends StatefulWidget {
  const _AvailabilityDialog();
  @override
  State<_AvailabilityDialog> createState() => _AvailabilityDialogState();
}

class _AvailabilityDialogState extends State<_AvailabilityDialog> {
  DateTime _date = DateTime.now().add(const Duration(days: 1));
  TimeOfDay _start = const TimeOfDay(hour: 9, minute: 0);
  TimeOfDay _end = const TimeOfDay(hour: 10, minute: 0);
  bool _unavailable = false;

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Novi termin'),
    content: SizedBox(
      width: 420,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            title: const Text('Datum'),
            trailing: Text(DateFormat('dd.MM.yyyy.').format(_date)),
            onTap: () async {
              final value = await showDatePicker(
                context: context,
                initialDate: _date,
                firstDate: DateTime.now(),
                lastDate: DateTime.now().add(const Duration(days: 365)),
              );
              if (value != null) setState(() => _date = value);
            },
          ),
          ListTile(
            title: const Text('Početak'),
            trailing: Text(_start.format(context)),
            onTap: () async {
              final value = await showTimePicker(
                context: context,
                initialTime: _start,
              );
              if (value != null) setState(() => _start = value);
            },
          ),
          ListTile(
            title: const Text('Kraj'),
            trailing: Text(_end.format(context)),
            onTap: () async {
              final value = await showTimePicker(
                context: context,
                initialTime: _end,
              );
              if (value != null) setState(() => _end = value);
            },
          ),
          SwitchListTile(
            value: _unavailable,
            onChanged: (value) => setState(() => _unavailable = value),
            title: const Text('Nedostupno'),
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
          final start = DateTime(
            _date.year,
            _date.month,
            _date.day,
            _start.hour,
            _start.minute,
          );
          final end = DateTime(
            _date.year,
            _date.month,
            _date.day,
            _end.hour,
            _end.minute,
          );
          if (!end.isAfter(start)) return;
          Navigator.pop(context, {
            'startsAtUtc': start.toUtc().toIso8601String(),
            'endsAtUtc': end.toUtc().toIso8601String(),
            'status': _unavailable ? 1 : 0,
          });
        },
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
String _time(Object? value) =>
    DateFormat('HH:mm').format(DateTime.parse(value.toString()).toLocal());
