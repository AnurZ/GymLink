import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

const _registrationStatuses = ['Draft', 'Submitted', 'Approved', 'Rejected'];
const _gymAdminEligibilityHint =
    'Registrujte novi aktivni korisnički račun kroz mobilnu aplikaciju. '
    'Račun mora imati ulogu člana, bez aktivnog članstva i bez druge aktivne '
    'dodjele teretani.';

class CentralAdminRefresh extends ChangeNotifier {
  void dataChanged() => notifyListeners();
}

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
  CentralAdminRefresh? _refresh;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final refresh = context.read<CentralAdminRefresh>();
    if (!identical(refresh, _refresh)) {
      _refresh?.removeListener(_load);
      _refresh = refresh..addListener(_load);
    }
  }

  @override
  void dispose() {
    _refresh?.removeListener(_load);
    super.dispose();
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
    final saved = await submitReasonedAction(
      context,
      title: approve ? 'Bilješka odobrenja' : 'Razlog odbijanja',
      onSubmit: (reason) => api.post(
        '/api/admin/gym-registration-requests/${item['id']}/${approve ? 'approve' : 'reject'}',
        body: {'reason': reason},
      ),
    );
    if (saved) await _load();
  }

  Future<void> _tenantAction(Map<String, dynamic> item, String action) async {
    final tenantId = item['createdTenantId'];
    if (tenantId == null) return;
    final api = context.read<ApiClient>();
    final saved = await submitReasonedAction(
      context,
      title: 'Razlog promjene statusa',
      onSubmit: (reason) => api.post(
        '/api/admin/tenants/$tenantId/$action',
        body: {'reason': reason},
      ),
    );
    if (saved && mounted) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Status je ažuriran.')));
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
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        horizontalMargin: 16,
                        columnSpacing: 28,
                        dataRowMinHeight: 62,
                        dataRowMaxHeight: 74,
                        columns: const [
                          DataColumn(label: Text('Teretana')),
                          DataColumn(label: Text('Lokacija')),
                          DataColumn(label: Text('Opis')),
                          DataColumn(label: Text('Status')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final status =
                              (item['status'] as num?)?.toInt() ?? -1;
                          final location =
                              '${item['address']}, ${item['cityName']}';
                          final description = item['description'].toString();
                          return DataRow(
                            cells: [
                              DataCell(
                                SizedBox(
                                  width: 190,
                                  child: Text(
                                    item['gymName'].toString(),
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                ),
                              ),
                              DataCell(
                                Tooltip(
                                  message: location,
                                  child: SizedBox(
                                    width: 240,
                                    child: Text(
                                      location,
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  ),
                                ),
                              ),
                              DataCell(
                                Tooltip(
                                  message: description,
                                  child: SizedBox(
                                    width: 300,
                                    child: Text(
                                      description,
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  ),
                                ),
                              ),
                              DataCell(
                                StatusPill(
                                  enumLabel(
                                    item['status'],
                                    _registrationStatuses,
                                  ),
                                ),
                              ),
                              DataCell(
                                Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
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
                                        tooltip: 'Promijeni status teretane',
                                        icon: const Icon(
                                          Icons.sync_alt_outlined,
                                        ),
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
                              ),
                            ],
                          );
                        }).toList(),
                      ),
                    ),
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
  static const _pageSize = 20;
  final _search = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  bool _refreshing = false;
  Object? _error;
  int? _status;
  int _page = 1;
  int _totalCount = 0;

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

  Future<bool> _load({
    bool preserveDataOnError = false,
    bool reportRefreshFailure = false,
  }) async {
    setState(() {
      if (preserveDataOnError && _items.isNotEmpty) {
        _refreshing = true;
      } else {
        _loading = true;
      }
      if (!preserveDataOnError) _error = null;
    });
    try {
      final api = context.read<ApiClient>();
      final result = await api.page(
        '/api/admin/gyms',
        query: {
          'query': _search.text.trim(),
          'status': _status,
          'page': _page,
          'pageSize': _pageSize,
        },
      );
      _items = result.items;
      _totalCount = result.totalCount;
      _error = null;
      return true;
    } catch (error) {
      if (!preserveDataOnError || _items.isEmpty) {
        _error = error;
      } else if (mounted && reportRefreshFailure) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Osvježavanje nije uspjelo. Prikazani su prethodni podaci.',
            ),
          ),
        );
      }
      return false;
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _refreshing = false;
        });
      }
    }
  }

  Future<void> _create() async {
    final created = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => const _GymCreateDialog(),
    );
    if (created == true) {
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Teretana je kreirana i spremna za aktivaciju.'),
          ),
        );
        context.read<CentralAdminRefresh>().dataChanged();
      }
    }
  }

  Future<void> _manageGymAdmin(Map<String, dynamic> item) async {
    final result = await showDialog<String>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _GymAdminManagementDialog(
        gym: item,
        onChanged: () async {
          await _load(preserveDataOnError: true, reportRefreshFailure: true);
          if (mounted) context.read<CentralAdminRefresh>().dataChanged();
        },
      ),
    );
    if (result == 'assigned' && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('GymAdmin je uspješno dodijeljen.')),
      );
    }
  }

  Future<void> _tenantAction(Map<String, dynamic> item, String action) async {
    final api = context.read<ApiClient>();
    if (action == 'activate') {
      final confirmed = await confirmAction(
        context,
        title: 'Aktiviraj teretanu',
        message:
            'Teretana ${item['name']} će postati javno vidljiva članovima.',
      );
      if (!confirmed || !mounted) return;
    }
    final saved = await submitReasonedAction(
      context,
      title: 'Razlog promjene statusa',
      onSubmit: (reason) async {
        try {
          await api.post(
            '/api/admin/tenants/${item['tenantId']}/$action',
            body: {'reason': reason},
          );
        } on ApiProblem catch (error) {
          if (error.status != 409 ||
              (error.code != 'tenant_admin_required' &&
                  error.code != 'tenant_catalog_incomplete')) {
            rethrow;
          }
          await _load();
          Map<String, dynamic> refreshed = item;
          for (final candidate in _items) {
            if (candidate['tenantId'] == item['tenantId']) {
              refreshed = candidate;
              break;
            }
          }
          throw ApiProblem(
            status: error.status,
            code: error.code,
            message: _readinessText(refreshed['missingActivationRequirements']),
          );
        }
      },
    );
    if (saved) {
      final refreshed = await _load(preserveDataOnError: true);
      if (mounted) context.read<CentralAdminRefresh>().dataChanged();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              refreshed
                  ? 'Status teretane je uspješno promijenjen.'
                  : 'Promjena je sačuvana, ali lista nije osvježena. Pokušajte ponovo.',
            ),
            action: refreshed
                ? null
                : SnackBarAction(label: 'Pokušaj ponovo', onPressed: _load),
          ),
        );
      }
    }
  }

  static String _readinessText(Object? raw) {
    final codes = (raw as List? ?? const []).map((value) => value.toString());
    if (codes.isEmpty) return 'Osvježite podatke i pokušajte ponovo.';
    return 'Nedostaje: ${codes.map(_activationRequirementLabel).join(', ')}.';
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
              onSubmitted: (_) {
                _page = 1;
                _load();
              },
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
              isExpanded: true,
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
                _page = 1;
                _load();
              },
            ),
          ),
          const Spacer(),
          FilledButton.tonalIcon(
            key: const Key('refresh-central-gyms'),
            onPressed: _loading || _refreshing
                ? null
                : () => _load(
                    preserveDataOnError: true,
                    reportRefreshFailure: true,
                  ),
            icon: _refreshing
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.refresh),
            label: const Text('Osvježi'),
          ),
          const SizedBox(width: 10),
          FilledButton.icon(
            onPressed: _create,
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
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        horizontalMargin: 16,
                        columnSpacing: 30,
                        dataRowMinHeight: 58,
                        dataRowMaxHeight: 68,
                        columns: const [
                          DataColumn(label: Text('Naziv teretane')),
                          DataColumn(label: Text('Lokacija')),
                          DataColumn(label: Text('Broj članova')),
                          DataColumn(label: Text('Status')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final status = (item['status'] as num?)?.toInt() ?? 0;
                          final canActivate = item['canActivate'] == true;
                          final readiness = _readinessText(
                            item['missingActivationRequirements'],
                          );
                          final location =
                              '${item['address']}, ${item['cityName']}';
                          const labels = [
                            'Čeka aktivaciju',
                            'Aktivna',
                            'Neaktivna',
                            'Suspendovana',
                          ];
                          return DataRow(
                            cells: [
                              DataCell(
                                Text(
                                  item['name'].toString(),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                              DataCell(
                                Tooltip(
                                  message: location,
                                  excludeFromSemantics: true,
                                  child: Semantics(
                                    label: location,
                                    child: SizedBox(
                                      width: 280,
                                      child: Text(
                                        location,
                                        maxLines: 1,
                                        overflow: TextOverflow.ellipsis,
                                      ),
                                    ),
                                  ),
                                ),
                              ),
                              DataCell(Text('${item['memberCount'] ?? 0}')),
                              DataCell(StatusPill(labels[status.clamp(0, 3)])),
                              DataCell(
                                SizedBox(
                                  width: 190,
                                  child: Row(
                                    mainAxisAlignment: MainAxisAlignment.end,
                                    children: [
                                      IconButton(
                                        key: Key(
                                          'manage-gym-admin-${item['id']}',
                                        ),
                                        tooltip: 'Upravljaj GymAdminom',
                                        onPressed: () => _manageGymAdmin(item),
                                        icon: const Icon(
                                          Icons.manage_accounts_outlined,
                                        ),
                                      ),
                                      if (status == 0)
                                        Tooltip(
                                          message: canActivate
                                              ? 'Aktiviraj'
                                              : readiness,
                                          child: IconButton(
                                            onPressed: canActivate
                                                ? () => _tenantAction(
                                                    item,
                                                    'activate',
                                                  )
                                                : null,
                                            icon: const Icon(
                                              Icons.play_arrow_outlined,
                                              color: Colors.green,
                                            ),
                                          ),
                                        ),
                                      if (status == 1) ...[
                                        IconButton(
                                          tooltip: 'Suspenduj',
                                          onPressed: () =>
                                              _tenantAction(item, 'suspend'),
                                          icon: const Icon(
                                            Icons.pause_circle_outline,
                                          ),
                                        ),
                                        IconButton(
                                          tooltip: 'Deaktiviraj',
                                          onPressed: () =>
                                              _tenantAction(item, 'deactivate'),
                                          icon: const Icon(
                                            Icons.block,
                                            color: Colors.red,
                                          ),
                                        ),
                                      ],
                                      if (status == 2 || status == 3)
                                        IconButton(
                                          tooltip: 'Ponovo aktiviraj',
                                          onPressed: () =>
                                              _tenantAction(item, 'reactivate'),
                                          icon: const Icon(
                                            Icons.replay_outlined,
                                          ),
                                        ),
                                    ],
                                  ),
                                ),
                              ),
                            ],
                          );
                        }).toList(),
                      ),
                    ),
                  ),
                ),
        ),
      ),
      if (_totalCount > _pageSize)
        Row(
          mainAxisAlignment: MainAxisAlignment.end,
          children: [
            Text('Stranica $_page od ${(_totalCount / _pageSize).ceil()}'),
            IconButton(
              tooltip: 'Prethodna stranica',
              onPressed: _page == 1
                  ? null
                  : () {
                      setState(() => _page--);
                      _load();
                    },
              icon: const Icon(Icons.chevron_left),
            ),
            IconButton(
              tooltip: 'Sljedeća stranica',
              onPressed: _page * _pageSize >= _totalCount
                  ? null
                  : () {
                      setState(() => _page++);
                      _load();
                    },
              icon: const Icon(Icons.chevron_right),
            ),
          ],
        ),
    ],
  );
}

class _GymCreateDialog extends StatefulWidget {
  const _GymCreateDialog();

  @override
  State<_GymCreateDialog> createState() => _GymCreateDialogState();
}

class _GymCreateDialogState extends State<_GymCreateDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _description = TextEditingController();
  final _address = TextEditingController();
  final _phone = TextEditingController();
  final _locationSearch = TextEditingController();
  final _planName = TextEditingController();
  final _planDuration = TextEditingController(text: '30');
  final _planPrice = TextEditingController(text: '50');
  final _adminSearch = TextEditingController();
  final _mapController = MapController();
  final _stepperScrollController = ScrollController();
  final _days = List.generate(
    7,
    (index) =>
        _WorkingDayState(dayOfWeek: index, isClosed: index == 0 || index == 6),
  );
  Map<String, dynamic>? _city;
  List<Map<String, dynamic>> _locationResults = const [];
  List<Map<String, dynamic>> _cities = const [];
  List<Map<String, dynamic>> _equipment = const [];
  List<Map<String, dynamic>> _trainingTypes = const [];
  final Set<String> _equipmentIds = {};
  final Set<String> _trainingTypeIds = {};
  List<Map<String, dynamic>> _adminCandidates = const [];
  Map<String, dynamic>? _gymAdmin;
  Timer? _adminDebounce;
  Timer? _locationDebounce;
  int _locationRequestVersion = 0;
  bool _loadingSetup = true;
  bool _locationLoading = false;
  bool _reverseLocationLoading = false;
  bool _manualLocationMode = false;
  bool _adminLoading = false;
  String? _locationError;
  LatLng? _location;
  int _step = 0;
  bool _busy = false;
  String? _error;
  Map<String, List<String>> _fieldErrors = const {};

  @override
  void initState() {
    super.initState();
    _loadSetup();
  }

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _address.dispose();
    _phone.dispose();
    _locationSearch.dispose();
    _planName.dispose();
    _planDuration.dispose();
    _planPrice.dispose();
    _adminSearch.dispose();
    _mapController.dispose();
    _stepperScrollController.dispose();
    _adminDebounce?.cancel();
    _locationDebounce?.cancel();
    super.dispose();
  }

  Future<void> _loadSetup() async {
    try {
      final api = context.read<ApiClient>();
      final results = await Future.wait([
        api.page(
          '/api/admin/reference-data/equipment',
          query: {'isActive': true, 'pageSize': 100},
        ),
        api.page(
          '/api/admin/reference-data/training-types',
          query: {'isActive': true, 'pageSize': 100},
        ),
        _loadActiveBihCities(api),
      ]);
      if (!mounted) return;
      setState(() {
        _equipment = (results[0] as PagedData).items;
        _trainingTypes = (results[1] as PagedData).items;
        _cities = results[2] as List<Map<String, dynamic>>;
        _loadingSetup = false;
      });
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = error is ApiProblem ? error.message : error.toString();
          _loadingSetup = false;
        });
      }
    }
  }

  Future<List<Map<String, dynamic>>> _loadActiveBihCities(ApiClient api) async {
    final countries = await api.page(
      '/api/admin/reference-data/countries',
      query: {'query': 'BIH', 'isActive': true, 'pageSize': 10},
    );
    final country = countries.items.where(
      (item) => item['code']?.toString().toUpperCase() == 'BIH',
    );
    if (country.isEmpty) return const [];

    final result = <Map<String, dynamic>>[];
    var page = 1;
    while (true) {
      final cities = await api.page(
        '/api/admin/reference-data/cities',
        query: {
          'countryId': country.first['id'],
          'isActive': true,
          'page': page,
          'pageSize': 100,
        },
      );
      result.addAll(cities.items);
      if (page * cities.pageSize >= cities.totalCount) break;
      page++;
    }
    result.sort(
      (left, right) =>
          left['name'].toString().compareTo(right['name'].toString()),
    );
    return result;
  }

  Future<void> _searchLocation() async {
    final query = _locationSearch.text.trim();
    if (query.length < 2) {
      setState(() => _locationError = 'Unesite najmanje dva znaka.');
      return;
    }
    _locationDebounce?.cancel();
    _locationRequestVersion++;
    setState(() {
      _locationLoading = true;
      _reverseLocationLoading = false;
      _locationError = null;
      _locationResults = const [];
    });
    try {
      final raw = await context.read<ApiClient>().get(
        '/api/admin/locations/search',
        query: {'query': query},
      );
      if (!mounted) return;
      final results = (raw as List? ?? const [])
          .whereType<Map>()
          .map((item) => Map<String, dynamic>.from(item))
          .toList(growable: false);
      setState(() {
        _locationResults = results;
        if (results.isEmpty) {
          _locationError = 'Nema rezultata za unesenu adresu.';
        }
      });
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _locationResults = const [];
          _locationError = error.code == 'location_search_unavailable'
              ? 'Online pretraga trenutno nije dostupna. Unesite grad i adresu ručno te označite lokaciju na mapi.'
              : error.message;
          if (error.code == 'location_search_unavailable') {
            _manualLocationMode = true;
          }
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _locationError =
              'Online pretraga trenutno nije dostupna. Unesite grad i adresu ručno te označite lokaciju na mapi.';
          _manualLocationMode = true;
        });
      }
    } finally {
      if (mounted) setState(() => _locationLoading = false);
    }
  }

  void _selectLocation(Map<String, dynamic> result) {
    _locationDebounce?.cancel();
    _locationRequestVersion++;
    final point = LatLng(
      (result['latitude'] as num).toDouble(),
      (result['longitude'] as num).toDouble(),
    );
    setState(() {
      _city = {'id': result['cityId'], 'name': result['cityName']};
      _address.text = result['address'].toString();
      _location = point;
      _locationResults = const [];
      _locationError = null;
      _reverseLocationLoading = false;
      _manualLocationMode = false;
    });
    _mapController.move(point, 15);
  }

  void _selectMapPoint(LatLng point) {
    _locationDebounce?.cancel();
    final requestVersion = ++_locationRequestVersion;
    setState(() {
      _location = point;
      if (!_manualLocationMode) {
        _city = null;
        _address.clear();
      }
      _locationResults = const [];
      _locationError = null;
      _reverseLocationLoading = true;
    });
    _locationDebounce = Timer(
      const Duration(milliseconds: 300),
      () => _reverseLocation(point, requestVersion),
    );
  }

  Future<void> _reverseLocation(LatLng point, int requestVersion) async {
    try {
      final raw = await context.read<ApiClient>().get(
        '/api/admin/locations/reverse',
        query: {'latitude': point.latitude, 'longitude': point.longitude},
      );
      if (!mounted || requestVersion != _locationRequestVersion) return;
      final result = Map<String, dynamic>.from(raw! as Map);
      setState(() {
        _city = {'id': result['cityId'], 'name': result['cityName']};
        _address.text = result['address'].toString();
        _locationError = null;
        _manualLocationMode = false;
      });
    } on ApiProblem catch (error) {
      if (!mounted || requestVersion != _locationRequestVersion) return;
      setState(() {
        _locationError = switch (error.code) {
          'location_outside_bih' =>
            'Odabrana lokacija mora biti u Bosni i Hercegovini.',
          'location_not_resolved' =>
            'Adresa nije automatski pronađena. Unesite grad i adresu ručno; označene koordinate su sačuvane.',
          'location_search_unavailable' =>
            'Automatsko pronalaženje adrese trenutno nije dostupno. Unesite grad i adresu ručno; označene koordinate su sačuvane.',
          _ => error.message,
        };
        if (error.code == 'location_not_resolved' ||
            error.code == 'location_search_unavailable') {
          _manualLocationMode = true;
        }
      });
    } catch (_) {
      if (!mounted || requestVersion != _locationRequestVersion) return;
      setState(() {
        _locationError =
            'Automatsko pronalaženje adrese trenutno nije dostupno. Unesite grad i adresu ručno; označene koordinate su sačuvane.';
        _manualLocationMode = true;
      });
    } finally {
      if (mounted && requestVersion == _locationRequestVersion) {
        setState(() => _reverseLocationLoading = false);
      }
    }
  }

  String? _serverError(String field) {
    for (final entry in _fieldErrors.entries) {
      if (entry.key.toLowerCase() == field.toLowerCase()) {
        return entry.value.join('\n');
      }
    }
    return null;
  }

  void _adminSearchChanged(String value) {
    if (_gymAdmin != null &&
        value.trim() != _gymAdmin!['displayName']?.toString()) {
      _gymAdmin = null;
    }
    _adminDebounce?.cancel();
    final query = value.trim();
    if (query.length < 2) {
      setState(() => _adminCandidates = const []);
      return;
    }
    _adminDebounce = Timer(
      const Duration(milliseconds: 300),
      () => _loadAdminCandidates(query),
    );
  }

  Future<void> _loadAdminCandidates(String query) async {
    setState(() {
      _adminLoading = true;
      _error = null;
    });
    try {
      final result = await context.read<ApiClient>().page(
        '/api/admin/users',
        query: {
          'query': query,
          'role': 'Member',
          'isActive': true,
          'pageSize': 10,
        },
      );
      if (!mounted || _adminSearch.text.trim() != query) return;
      setState(() => _adminCandidates = result.items);
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted && _adminSearch.text.trim() == query) {
        setState(() => _adminLoading = false);
      }
    }
  }

  void _selectAdmin(Map<String, dynamic> candidate) {
    setState(() {
      _gymAdmin = candidate;
      _adminSearch.text = candidate['displayName'].toString();
      _adminSearch.selection = TextSelection.collapsed(
        offset: _adminSearch.text.length,
      );
      _adminCandidates = const [];
      _error = null;
    });
  }

  bool _validateStep() {
    String? message;
    if (_step == 0) {
      if (_name.text.trim().length < 2) {
        message = 'Unesite naziv teretane.';
      } else if (_description.text.trim().length < 10) {
        message = 'Opis mora imati najmanje 10 znakova.';
      } else if (_gymAdmin == null) {
        message = 'Izaberite aktivnog Member korisnika.';
      }
    } else if (_step == 1) {
      if (_reverseLocationLoading) {
        message = 'Sačekajte da se pronađe adresa odabrane lokacije.';
      } else if (_city == null || _location == null) {
        message = 'Pretražite adresu ili izaberite lokaciju na mapi.';
      } else if (_address.text.trim().length < 3) {
        message = 'Unesite adresu.';
      }
    } else if (_step == 2) {
      if (!_days.any((day) => !day.isClosed)) {
        message = 'Najmanje jedan dan mora biti otvoren.';
      } else if (_days.any(
        (day) =>
            !day.isClosed &&
            (day.closes.hour * 60 + day.closes.minute <=
                day.opens.hour * 60 + day.opens.minute),
      )) {
        message = 'Vrijeme zatvaranja mora biti nakon vremena otvaranja.';
      }
    } else if (_step == 3) {
      if (_equipmentIds.isEmpty) {
        message = 'Izaberite najmanje jednu stavku opreme.';
      } else if (_trainingTypeIds.isEmpty) {
        message = 'Izaberite najmanje jedan tip treninga.';
      } else if (_planName.text.trim().isEmpty ||
          (int.tryParse(_planDuration.text) ?? 0) <= 0 ||
          double.tryParse(_planPrice.text.replaceFirst(',', '.')) == null) {
        message = 'Unesite ispravne podatke početnog plana članstva.';
      }
    }
    setState(() => _error = message);
    return message == null;
  }

  void _continue() {
    if (!_validateStep()) return;
    _setStep(_step + 1);
  }

  void _setStep(int value, {String? error}) {
    setState(() {
      _step = value;
      _error = error;
    });
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_stepperScrollController.hasClients) return;
      _stepperScrollController.jumpTo(0);
    });
  }

  Future<void> _submit() async {
    if (!_validateStep()) return;
    final confirmed = await confirmAction(
      context,
      title: 'Kreiraj teretanu',
      message:
          '${_gymAdmin!['displayName']} će postati GymAdmin za ${_name.text.trim()}. Korisničke sesije bit će opozvane, a teretana će biti spremna za aktivaciju.',
    );
    if (!confirmed || !mounted) return;
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
          'workingHours': _days
              .map(
                (day) => {
                  'dayOfWeek': day.dayOfWeek,
                  'opensAt': day.isClosed ? null : _timeValue(day.opens),
                  'closesAt': day.isClosed ? null : _timeValue(day.closes),
                  'isClosed': day.isClosed,
                },
              )
              .toList(),
          'equipmentIds': _equipmentIds.toList(),
          'trainingTypeIds': _trainingTypeIds.toList(),
          'membershipPlan': {
            'name': _planName.text.trim(),
            'durationDays': int.parse(_planDuration.text),
            'price': double.parse(_planPrice.text.replaceFirst(',', '.')),
            'currency': 'BAM',
          },
          'gymAdminUserId': _gymAdmin!['id'],
        },
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiProblem catch (error) {
      if (mounted) {
        final invalidAdmin =
            error.code == 'gym_admin_already_assigned' ||
            error.code == 'gym_admin_candidate_invalid';
        setState(() {
          _error = switch (error.code) {
            'gym_admin_already_assigned' =>
              'Odabrani račun ima aktivno članstvo ili drugu aktivnu dodjelu '
                  'teretani. Registrujte novi Member račun bez aktivnog članstva.',
            'gym_admin_candidate_invalid' =>
              'Odabrani korisnik više nije dostupan za GymAdmin ulogu. '
                  'Izaberite drugog korisnika.',
            'tenant_gym_admin_exists' =>
              'Ova teretana već ima aktivnog GymAdmina.',
            'location_search_unavailable' =>
              'Pretraga lokacija trenutno nije dostupna.',
            _ => error.message,
          };
          _fieldErrors = error.fieldErrors;
          if (invalidAdmin) {
            _step = 0;
            _gymAdmin = null;
            _adminCandidates = const [];
            _adminSearch.clear();
          }
        });
        if (invalidAdmin) {
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (!mounted || !_stepperScrollController.hasClients) return;
            _stepperScrollController.jumpTo(0);
          });
        }
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => _error =
              'Zahtjev trenutno nije moguće izvršiti. Pokušajte ponovo.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  static String _timeValue(TimeOfDay value) =>
      '${value.hour.toString().padLeft(2, '0')}:'
      '${value.minute.toString().padLeft(2, '0')}:00';

  void _changeMapZoom(double delta) {
    final camera = _mapController.camera;
    final zoom = (camera.zoom + delta).clamp(6.0, 19.0);
    _mapController.move(camera.center, zoom);
  }

  void _centerMap() {
    final selectedLocation = _location;
    _mapController.move(
      selectedLocation ?? const LatLng(43.8563, 18.4131),
      selectedLocation == null ? 12 : 15,
    );
  }

  Widget _mapControlButton({
    required Key key,
    required String tooltip,
    required VoidCallback onPressed,
    required IconData icon,
  }) => SizedBox.square(
    dimension: 32,
    child: IconButton(
      key: key,
      tooltip: tooltip,
      padding: EdgeInsets.zero,
      visualDensity: VisualDensity.compact,
      constraints: const BoxConstraints.tightFor(width: 32, height: 32),
      iconSize: 18,
      onPressed: onPressed,
      icon: Icon(icon),
    ),
  );

  Widget _basicInfoStep() => ListView(
    key: const Key('gym-basic-info-scroll'),
    children: [
      const Text(
        'Osnovni podaci',
        style: TextStyle(fontWeight: FontWeight.w800),
      ),
      const SizedBox(height: 10),
      Row(
        children: [
          Expanded(
            child: TextFormField(
              controller: _name,
              decoration: InputDecoration(
                labelText: 'Naziv',
                errorText: _serverError('Name'),
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: TextFormField(
              controller: _phone,
              decoration: InputDecoration(
                labelText: 'Telefon (opcionalno)',
                errorText: _serverError('PhoneNumber'),
              ),
            ),
          ),
        ],
      ),
      const SizedBox(height: 12),
      TextFormField(
        controller: _description,
        minLines: 5,
        maxLines: 8,
        decoration: InputDecoration(
          labelText: 'Opis',
          errorText: _serverError('Description'),
        ),
      ),
      const SizedBox(height: 20),
      const Divider(),
      const SizedBox(height: 12),
      const Text('GymAdmin', style: TextStyle(fontWeight: FontWeight.w800)),
      const SizedBox(height: 6),
      const Text(_gymAdminEligibilityHint),
      const SizedBox(height: 12),
      _adminSelector(),
    ],
  );

  Widget _locationStep() => LayoutBuilder(
    builder: (context, constraints) {
      final wideLayout = constraints.maxWidth >= 760;
      return ListView(
        key: const Key('gym-location-scroll'),
        children: [
          const Text('Lokacija', style: TextStyle(fontWeight: FontWeight.w800)),
          const SizedBox(height: 4),
          const Row(
            key: Key('gym-location-tip'),
            children: [
              Icon(Icons.info_outline, size: 16, color: Colors.black54),
              SizedBox(width: 6),
              Expanded(
                child: Text(
                  'Savjet: Pretražite adresu ili odaberite tačku na mapi.',
                  style: TextStyle(fontSize: 12, color: Colors.black54),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          if (wideLayout)
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(flex: 9, child: _locationFields()),
                const SizedBox(width: 16),
                Expanded(flex: 11, child: _locationMap(420)),
              ],
            )
          else ...[
            _locationFields(),
            const SizedBox(height: 12),
            _locationMap(320),
          ],
        ],
      );
    },
  );

  Widget _locationFields() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Row(
        children: [
          Expanded(
            child: TextField(
              key: const Key('gym-location-search'),
              controller: _locationSearch,
              onSubmitted: (_) => _searchLocation(),
              decoration: const InputDecoration(
                hintText: 'Pretraži adresu',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          const SizedBox(width: 10),
          OutlinedButton.icon(
            key: const Key('gym-location-search-button'),
            onPressed: _locationLoading ? null : _searchLocation,
            icon: _locationLoading
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.search),
            label: const Text('Pretraži'),
          ),
        ],
      ),
      if (_locationResults.isNotEmpty)
        Container(
          constraints: const BoxConstraints(maxHeight: 180),
          margin: const EdgeInsets.only(top: 6),
          decoration: BoxDecoration(
            color: Colors.white,
            border: Border.all(color: Colors.black12),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Material(
            color: Colors.transparent,
            child: ListView.separated(
              shrinkWrap: true,
              itemCount: _locationResults.length,
              separatorBuilder: (_, _) => const Divider(height: 1),
              itemBuilder: (_, index) {
                final result = _locationResults[index];
                return ListTile(
                  dense: true,
                  leading: const Icon(Icons.place_outlined),
                  title: Text(
                    result['displayName'].toString(),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  subtitle: Text(result['cityName'].toString()),
                  onTap: () => _selectLocation(result),
                );
              },
            ),
          ),
        ),
      if (_locationError != null) ...[
        const SizedBox(height: 6),
        Text(_locationError!, style: const TextStyle(color: Colors.red)),
      ],
      const SizedBox(height: 4),
      Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(
          key: const Key('gym-manual-location-toggle'),
          onPressed: () => setState(() {
            _manualLocationMode = !_manualLocationMode;
            _locationResults = const [];
          }),
          icon: Icon(
            _manualLocationMode
                ? Icons.public
                : Icons.edit_location_alt_outlined,
          ),
          label: Text(
            _manualLocationMode
                ? 'Koristi online pretragu'
                : 'Unesi lokaciju ručno',
          ),
        ),
      ),
      if (_manualLocationMode) ...[
        const SizedBox(height: 4),
        const Card(
          key: Key('gym-manual-city-reference-hint'),
          color: Color(0xFFF3F7FF),
          child: Padding(
            padding: EdgeInsets.all(10),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.info_outline, size: 18),
                SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Lista gradova dolazi iz referentnih podataka. Ako grad '
                    'nedostaje, prvo ga dodajte u tabu Gradovi.',
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 8),
        DropdownMenu<String>(
          key: const Key('gym-manual-city'),
          expandedInsets: EdgeInsets.zero,
          enableFilter: true,
          enableSearch: true,
          label: const Text('Grad/općina'),
          hintText: 'Pretražite i odaberite grad',
          initialSelection: _city?['id']?.toString(),
          dropdownMenuEntries: _cities
              .map(
                (city) => DropdownMenuEntry<String>(
                  value: city['id'].toString(),
                  label: city['name'].toString(),
                ),
              )
              .toList(growable: false),
          onSelected: (id) => setState(() {
            _city = id == null
                ? null
                : _cities.firstWhere((city) => city['id'].toString() == id);
          }),
        ),
      ],
      const SizedBox(height: 10),
      TextFormField(
        controller: _address,
        maxLines: 2,
        decoration: InputDecoration(
          labelText: _manualLocationMode ? 'Adresa' : 'Odabrana adresa',
          prefixIcon: const Icon(Icons.location_on_outlined),
          errorText: _serverError('Address'),
        ),
      ),
      if (_city != null) ...[
        const SizedBox(height: 6),
        Text('Grad/općina: ${_city!['name']}'),
      ],
      if (_manualLocationMode) ...[
        const SizedBox(height: 6),
        const Text(
          'Odaberite grad, unesite adresu i kliknite tačnu lokaciju na mapi.',
          style: TextStyle(fontSize: 12, color: Colors.black54),
        ),
      ],
    ],
  );

  Widget _locationMap(double height) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      SizedBox(
        key: const Key('gym-location-map'),
        height: height,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(14),
          child: FlutterMap(
            mapController: _mapController,
            options: MapOptions(
              initialCenter: const LatLng(43.8563, 18.4131),
              initialZoom: 12,
              minZoom: 6,
              maxZoom: 19,
              onTap: (_, point) => _selectMapPoint(point),
            ),
            children: [
              TileLayer(
                urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
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
              Align(
                alignment: Alignment.topRight,
                child: Padding(
                  padding: const EdgeInsets.all(8),
                  child: Material(
                    key: const Key('gym-map-controls'),
                    color: Colors.white.withValues(alpha: 0.94),
                    elevation: 2,
                    borderRadius: BorderRadius.circular(6),
                    clipBehavior: Clip.antiAlias,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        _mapControlButton(
                          key: const Key('gym-map-zoom-in'),
                          tooltip: 'Uvećaj mapu',
                          onPressed: () => _changeMapZoom(1),
                          icon: Icons.add,
                        ),
                        const SizedBox(
                          height: 20,
                          child: VerticalDivider(width: 1),
                        ),
                        _mapControlButton(
                          key: const Key('gym-map-zoom-out'),
                          tooltip: 'Umanji mapu',
                          onPressed: () => _changeMapZoom(-1),
                          icon: Icons.remove,
                        ),
                        const SizedBox(
                          height: 20,
                          child: VerticalDivider(width: 1),
                        ),
                        _mapControlButton(
                          key: const Key('gym-map-center'),
                          tooltip: 'Centriraj mapu',
                          onPressed: _centerMap,
                          icon: Icons.center_focus_strong,
                        ),
                      ],
                    ),
                  ),
                ),
              ),
              Align(
                alignment: Alignment.bottomRight,
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: Colors.white.withValues(alpha: 0.88),
                    borderRadius: const BorderRadius.only(
                      topLeft: Radius.circular(6),
                    ),
                  ),
                  child: const Padding(
                    padding: EdgeInsets.symmetric(horizontal: 6, vertical: 3),
                    child: Text(
                      '© OpenStreetMap contributors',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 11),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
      const SizedBox(height: 6),
      if (_reverseLocationLoading)
        const Wrap(
          spacing: 8,
          runSpacing: 4,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox.square(
              dimension: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
            Text('Pronalaženje najbliže adrese...'),
          ],
        )
      else
        const Text(
          'Kliknite na mapu da automatski pronađete najbližu adresu. '
          'Adresu zatim možete dopuniti.',
        ),
    ],
  );

  Widget _workingHoursStep() => ListView(
    children: [
      const Text(
        'Radno vrijeme',
        style: TextStyle(fontWeight: FontWeight.w800),
      ),
      const SizedBox(height: 8),
      GridView.builder(
        shrinkWrap: true,
        physics: const NeverScrollableScrollPhysics(),
        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
          maxCrossAxisExtent: 210,
          mainAxisExtent: 176,
          crossAxisSpacing: 10,
          mainAxisSpacing: 10,
        ),
        itemCount: _days.length,
        itemBuilder: (context, index) {
          final day = _days[index];
          return Card(
            margin: EdgeInsets.zero,
            child: Padding(
              padding: const EdgeInsets.all(10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          _weekdayLabel(day.dayOfWeek),
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                      ),
                      Switch(
                        value: !day.isClosed,
                        onChanged: (value) =>
                            setState(() => day.isClosed = !value),
                      ),
                    ],
                  ),
                  if (day.isClosed)
                    const Expanded(child: Center(child: Text('Zatvoreno')))
                  else ...[
                    OutlinedButton(
                      onPressed: () => _pickTime(day, true),
                      child: Text('Od ${day.opens.format(context)}'),
                    ),
                    const SizedBox(height: 4),
                    OutlinedButton(
                      onPressed: () => _pickTime(day, false),
                      child: Text('Do ${day.closes.format(context)}'),
                    ),
                  ],
                ],
              ),
            ),
          );
        },
      ),
    ],
  );

  Widget _catalogStep() => ListView(
    children: [
      const SizedBox(height: 14),
      const Text('Oprema', style: TextStyle(fontWeight: FontWeight.w800)),
      Wrap(
        spacing: 8,
        children: _equipment.map((item) {
          final id = item['id'].toString();
          return FilterChip(
            label: Text(item['name'].toString()),
            selected: _equipmentIds.contains(id),
            onSelected: (selected) => setState(
              () => selected ? _equipmentIds.add(id) : _equipmentIds.remove(id),
            ),
          );
        }).toList(),
      ),
      const SizedBox(height: 14),
      const Text(
        'Tipovi treninga',
        style: TextStyle(fontWeight: FontWeight.w800),
      ),
      Wrap(
        spacing: 8,
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
      const SizedBox(height: 16),
      const Text(
        'Početni plan članstva',
        style: TextStyle(fontWeight: FontWeight.w800),
      ),
      const SizedBox(height: 8),
      Row(
        children: [
          Expanded(
            child: TextField(
              controller: _planName,
              decoration: const InputDecoration(labelText: 'Naziv plana'),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: TextField(
              controller: _planDuration,
              decoration: const InputDecoration(labelText: 'Trajanje (dana)'),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: TextField(
              controller: _planPrice,
              decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
            ),
          ),
        ],
      ),
    ],
  );

  Widget _adminSelector() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      TextField(
        key: const Key('gym-admin-search'),
        controller: _adminSearch,
        onChanged: _adminSearchChanged,
        decoration: InputDecoration(
          labelText: 'GymAdmin',
          hintText: 'Ime, korisničko ime ili email...',
          prefixIcon: const Icon(Icons.person_search_outlined),
          suffixIcon: _adminLoading
              ? const Padding(
                  padding: EdgeInsets.all(14),
                  child: SizedBox.square(
                    dimension: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              : null,
        ),
      ),
      if (_adminCandidates.isNotEmpty)
        Container(
          constraints: const BoxConstraints(maxHeight: 220),
          margin: const EdgeInsets.only(top: 4),
          decoration: BoxDecoration(
            color: Colors.white,
            border: Border.all(color: Colors.black12),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Material(
            color: Colors.transparent,
            child: ListView.builder(
              shrinkWrap: true,
              itemCount: _adminCandidates.length,
              itemBuilder: (_, index) {
                final candidate = _adminCandidates[index];
                return ListTile(
                  title: Text(candidate['displayName'].toString()),
                  subtitle: Text(candidate['email'].toString()),
                  onTap: () => _selectAdmin(candidate),
                );
              },
            ),
          ),
        ),
    ],
  );

  Widget _reviewStep() => ListView(
    children: [
      Text(
        _name.text.trim(),
        style: Theme.of(
          context,
        ).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800),
      ),
      const SizedBox(height: 10),
      Text('${_address.text.trim()}, ${_city?['name'] ?? ''}'),
      Text('Otvoreni dani: ${_days.where((day) => !day.isClosed).length} od 7'),
      Text('Odabrana oprema: ${_equipmentIds.length}'),
      Text('Tipovi treninga: ${_trainingTypeIds.length}'),
      Text(
        'Plan: ${_planName.text.trim()}, ${_planDuration.text} dana, ${_planPrice.text} BAM',
      ),
      Text('GymAdmin: ${_gymAdmin?['displayName'] ?? ''}'),
      const SizedBox(height: 18),
      const Text(
        'Teretana će biti privatna i u statusu čekanja. Nakon kreiranja bit će spremna za zasebnu CentralAdmin aktivaciju.',
      ),
    ],
  );

  Future<void> _pickTime(_WorkingDayState day, bool opening) async {
    final selected = await showTimePicker(
      context: context,
      initialTime: opening ? day.opens : day.closes,
    );
    if (selected == null) return;
    setState(() {
      if (opening) {
        day.opens = selected;
      } else {
        day.closes = selected;
      }
    });
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Dodaj teretanu'),
    content: SizedBox(
      width: 900,
      height: 700,
      child: _loadingSetup
          ? const Center(child: CircularProgressIndicator())
          : Form(
              key: _formKey,
              child: Column(
                children: [
                  Expanded(
                    child: Stepper(
                      controller: _stepperScrollController,
                      type: StepperType.horizontal,
                      headerPadding: const EdgeInsets.symmetric(horizontal: 8),
                      currentStep: _step,
                      onStepTapped: (value) {
                        if (value < _step) _setStep(value);
                      },
                      controlsBuilder: (_, _) => const SizedBox.shrink(),
                      steps: [
                        Step(
                          title: const Text('Osnovno'),
                          isActive: _step >= 0,
                          state: _step > 0
                              ? StepState.complete
                              : StepState.indexed,
                          content: SizedBox(
                            height: 570,
                            child: _basicInfoStep(),
                          ),
                        ),
                        Step(
                          title: const Text('Lokacija'),
                          isActive: _step >= 1,
                          state: _step > 1
                              ? StepState.complete
                              : StepState.indexed,
                          content: SizedBox(
                            height: 570,
                            child: _locationStep(),
                          ),
                        ),
                        Step(
                          title: const Text('Radno vrijeme'),
                          isActive: _step >= 2,
                          state: _step > 2
                              ? StepState.complete
                              : StepState.indexed,
                          content: SizedBox(
                            height: 570,
                            child: _workingHoursStep(),
                          ),
                        ),
                        Step(
                          title: const Text('Katalog'),
                          isActive: _step >= 3,
                          state: _step > 3
                              ? StepState.complete
                              : StepState.indexed,
                          content: SizedBox(height: 570, child: _catalogStep()),
                        ),
                        Step(
                          title: const Text('Pregled'),
                          isActive: _step >= 4,
                          content: SizedBox(height: 570, child: _reviewStep()),
                        ),
                      ],
                    ),
                  ),
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text(
                        _error!,
                        style: const TextStyle(color: Colors.red),
                      ),
                    ),
                ],
              ),
            ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context, false),
        child: const Text('Odustani'),
      ),
      if (_step > 0)
        TextButton(
          onPressed: _busy ? null : () => _setStep(_step - 1),
          child: const Text('Nazad'),
        ),
      FilledButton(
        key: const Key('gym-create-continue'),
        onPressed: _busy ? null : (_step == 4 ? _submit : _continue),
        child: Text(
          _busy ? 'Kreiranje...' : (_step == 4 ? 'Kreiraj' : 'Dalje'),
        ),
      ),
    ],
  );
}

class _WorkingDayState {
  _WorkingDayState({required this.dayOfWeek, required this.isClosed});

  final int dayOfWeek;
  bool isClosed;
  TimeOfDay opens = const TimeOfDay(hour: 8, minute: 0);
  TimeOfDay closes = const TimeOfDay(hour: 22, minute: 0);
}

String _weekdayLabel(int dayOfWeek) => const [
  'Nedjelja',
  'Ponedjeljak',
  'Utorak',
  'Srijeda',
  'Četvrtak',
  'Petak',
  'Subota',
][dayOfWeek.clamp(0, 6)];

String _activationRequirementLabel(String code) => switch (code) {
  'gym_admin' => 'GymAdmin',
  'description' => 'opis',
  'working_hours' => 'radno vrijeme',
  'equipment' => 'oprema',
  'training_type' => 'tip treninga',
  'membership_plan' => 'aktivan plan članstva',
  _ => code,
};

class _GymAdminManagementDialog extends StatefulWidget {
  const _GymAdminManagementDialog({required this.gym, required this.onChanged});

  final Map<String, dynamic> gym;
  final Future<void> Function() onChanged;

  @override
  State<_GymAdminManagementDialog> createState() =>
      _GymAdminManagementDialogState();
}

class _GymAdminManagementDialogState extends State<_GymAdminManagementDialog> {
  final _formKey = GlobalKey<FormState>();
  final _search = TextEditingController();
  final _reason = TextEditingController();
  Timer? _debounce;
  List<Map<String, dynamic>> _candidates = const [];
  Map<String, dynamic>? _selected;
  bool _loading = false;
  bool _busy = false;
  String? _error;
  String? _notice;
  Map<String, dynamic>? _activeGymAdmin;
  bool _administratorRemoved = false;

  bool get _canAssign {
    final status = (widget.gym['status'] as num?)?.toInt();
    return status == 0 || status == 1;
  }

  bool get _hasUnresolvedAdministrator =>
      !_administratorRemoved &&
      _activeGymAdmin == null &&
      ((widget.gym['activeGymAdminCount'] as num?)?.toInt() ?? 0) > 0;

  @override
  void initState() {
    super.initState();
    final administrator = widget.gym['activeGymAdmin'];
    _activeGymAdmin = administrator is Map
        ? Map<String, dynamic>.from(administrator)
        : null;
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    _reason.dispose();
    super.dispose();
  }

  void _searchChanged(String value) {
    if (_selected != null &&
        value.trim() != _selected!['displayName']?.toString()) {
      _selected = null;
    }
    _debounce?.cancel();
    final query = value.trim();
    if (query.length < 2) {
      setState(() => _candidates = const []);
      return;
    }
    _debounce = Timer(
      const Duration(milliseconds: 300),
      () => _loadCandidates(query),
    );
  }

  Future<void> _loadCandidates(String query) async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await context.read<ApiClient>().page(
        '/api/admin/users',
        query: {
          'query': query,
          'role': 'Member',
          'isActive': true,
          'pageSize': 10,
        },
      );
      if (!mounted || _search.text.trim() != query) return;
      setState(() => _candidates = result.items);
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _candidates = const [];
          _error = error.message;
        });
      }
    } finally {
      if (mounted && _search.text.trim() == query) {
        setState(() => _loading = false);
      }
    }
  }

  void _select(Map<String, dynamic> candidate) {
    setState(() {
      _selected = candidate;
      _search.text = candidate['displayName'].toString();
      _search.selection = TextSelection.collapsed(offset: _search.text.length);
      _candidates = const [];
      _error = null;
    });
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    final confirmed = await confirmAction(
      context,
      title: 'Dodijeli GymAdmina',
      message:
          '${_selected!['displayName']} će dobiti administratorski pristup teretani ${widget.gym['name']}. Sve aktivne sesije tog korisnika bit će opozvane.',
    );
    if (!confirmed || !mounted) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<ApiClient>().post(
        '/api/admin/users/roles/assign',
        body: {
          'identifier': _selected!['id'],
          'role': 'GymAdmin',
          'tenantId': widget.gym['tenantId'],
          'reason': _reason.text.trim(),
        },
      );
      await widget.onChanged();
      if (mounted) Navigator.pop(context, 'assigned');
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _error = switch (error.code) {
            'tenant_gym_admin_exists' =>
              'Ova teretana već ima aktivnog GymAdmina.',
            'gym_admin_already_assigned' =>
              'Odabrani račun ima aktivno članstvo ili drugu aktivnu dodjelu '
                  'teretani. Registrujte novi Member račun bez aktivnog članstva.',
            _ => error.message,
          };
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _remove() async {
    final administrator = _activeGymAdmin;
    if (administrator == null) return;
    final confirmed = await confirmAction(
      context,
      title: 'Ukloni GymAdmina',
      message:
          '${administrator['displayName']} će izgubiti administratorski '
          'pristup teretani ${widget.gym['name']}, postati Member i biti '
          'odjavljen iz aktivnih sesija.',
      action: 'Nastavi',
    );
    if (!confirmed || !mounted) return;
    final removed = await submitReasonedAction(
      context,
      title: 'Razlog uklanjanja GymAdmina',
      onSubmit: (reason) async {
        await context.read<ApiClient>().post(
          '/api/admin/users/roles/revoke',
          body: {'identifier': administrator['id'], 'reason': reason},
        );
      },
    );
    if (!removed || !mounted) return;
    setState(() {
      _activeGymAdmin = null;
      _administratorRemoved = true;
      _selected = null;
      _search.clear();
      _reason.clear();
      _candidates = const [];
      _error = null;
      _notice = 'GymAdmin je uklonjen. Sada možete dodijeliti novi račun.';
    });
    await widget.onChanged();
  }

  Widget _assignmentForm() => Form(
    key: _formKey,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (_notice != null) ...[
          Text(_notice!, style: const TextStyle(color: Colors.green)),
          const SizedBox(height: 10),
        ],
        const Card(
          color: Color(0xFFF3F7FF),
          child: Padding(
            padding: EdgeInsets.all(12),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.info_outline, size: 20),
                SizedBox(width: 8),
                Expanded(child: Text(_gymAdminEligibilityHint)),
              ],
            ),
          ),
        ),
        const SizedBox(height: 14),
        TextFormField(
          key: const Key('gym-admin-search'),
          controller: _search,
          onChanged: _searchChanged,
          decoration: InputDecoration(
            labelText: 'Registrovani korisnik',
            hintText: 'Ime, korisničko ime ili email...',
            prefixIcon: const Icon(Icons.person_search_outlined),
            suffixIcon: _loading
                ? const Padding(
                    padding: EdgeInsets.all(14),
                    child: SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  )
                : null,
          ),
          validator: (_) => _selected == null
              ? 'Izaberite aktivnog korisnika iz liste.'
              : null,
        ),
        if (_candidates.isNotEmpty)
          Container(
            constraints: const BoxConstraints(maxHeight: 220),
            margin: const EdgeInsets.only(top: 4),
            decoration: BoxDecoration(
              color: Colors.white,
              border: Border.all(color: Colors.black12),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Material(
              color: Colors.transparent,
              child: ListView.builder(
                shrinkWrap: true,
                itemCount: _candidates.length,
                itemBuilder: (_, index) {
                  final candidate = _candidates[index];
                  return ListTile(
                    dense: true,
                    title: Text(candidate['displayName'].toString()),
                    subtitle: Text(candidate['email'].toString()),
                    onTap: () => _select(candidate),
                  );
                },
              ),
            ),
          ),
        const SizedBox(height: 12),
        TextFormField(
          controller: _reason,
          minLines: 2,
          maxLines: 3,
          maxLength: 200,
          decoration: const InputDecoration(labelText: 'Razlog dodjele'),
          validator: (value) {
            final length = value?.trim().length ?? 0;
            if (length < 2) return 'Unesite razlog dodjele.';
            if (length > 200) return 'Najviše 200 znakova.';
            return null;
          },
        ),
        if (_error != null) ...[
          const SizedBox(height: 12),
          Text(_error!, style: const TextStyle(color: Colors.red)),
        ],
      ],
    ),
  );

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Upravljaj GymAdminom'),
    content: SizedBox(
      width: 560,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.gym['name'].toString(),
              style: Theme.of(
                context,
              ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 10),
            if (_activeGymAdmin case final administrator?) ...[
              DropdownButtonFormField<String>(
                key: const Key('current-gym-admin-dropdown'),
                initialValue: administrator['id'].toString(),
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'Trenutni GymAdmin',
                ),
                items: [
                  DropdownMenuItem(
                    value: administrator['id'].toString(),
                    child: Text(administrator['displayName'].toString()),
                  ),
                ],
                onChanged: null,
              ),
              const SizedBox(height: 8),
              Text(administrator['email'].toString()),
              const SizedBox(height: 10),
              Align(
                alignment: Alignment.centerLeft,
                child: StatusPill(
                  administrator['isActive'] == true ? 'Aktivan' : 'Neaktivan',
                ),
              ),
              const SizedBox(height: 12),
              OutlinedButton.icon(
                key: const Key('remove-gym-admin'),
                onPressed: _busy ? null : _remove,
                icon: const Icon(Icons.remove_moderator_outlined),
                label: const Text('Ukloni GymAdmina'),
              ),
            ] else if (_hasUnresolvedAdministrator)
              const Card(
                color: Color(0xFFFFF5E5),
                child: Padding(
                  padding: EdgeInsets.all(12),
                  child: Text(
                    'Teretana ima dodijeljenog GymAdmina, ali pokrenuti API '
                    'ne vraća njegove podatke. Ponovo pokrenite API pa '
                    'osvježite listu; dodjela novog admina je blokirana da se '
                    'ne bi napravio konflikt.',
                  ),
                ),
              )
            else if (_canAssign)
              _assignmentForm()
            else
              const Card(
                color: Color(0xFFFFF5E5),
                child: Padding(
                  padding: EdgeInsets.all(12),
                  child: Text(
                    'Ova teretana nema aktivnog GymAdmina. Dodjela je moguća '
                    'samo dok teretana čeka aktivaciju ili je aktivna.',
                  ),
                ),
              ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context),
        child: const Text('Zatvori'),
      ),
      if (_activeGymAdmin == null && !_hasUnresolvedAdministrator && _canAssign)
        FilledButton(
          onPressed: _busy ? null : _submit,
          child: Text(_busy ? 'Dodjela...' : 'Dodijeli'),
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
  bool _refreshing = false;
  Object? _error;

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

  Future<void> _load({bool preserveData = false}) async {
    setState(() {
      if (preserveData && _items.isNotEmpty) {
        _refreshing = true;
      } else {
        _loading = true;
      }
    });
    try {
      _items = (await context.read<ApiClient>().page(
        '/api/admin/users',
        query: {'query': _search.text.trim()},
      )).items;
      _error = null;
    } catch (error) {
      if (preserveData && _items.isNotEmpty && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Osvježavanje nije uspjelo. Prikazani su prethodni podaci.',
            ),
          ),
        );
      } else {
        _error = error;
      }
    } finally {
      if (mounted) {
        setState(() {
          _loading = false;
          _refreshing = false;
        });
      }
    }
  }

  Future<void> _assign() async {
    try {
      final api = context.read<ApiClient>();
      final gyms = await api.page('/api/admin/gyms', query: {'pageSize': 100});
      if (!mounted) return;
      final saved = await showDialog<bool>(
        context: context,
        builder: (_) => _RoleDialog(
          tenants: gyms.items,
          onSubmit: (body) =>
              api.post('/api/admin/users/roles/assign', body: body),
        ),
      );
      if (saved == true) await _load();
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
    final saved = await submitReasonedAction(
      context,
      title: 'Razlog akcije',
      onSubmit: (reason) => api.post(
        '/api/admin/users/$endpoint',
        body: {'identifier': item['email'], 'reason': reason},
      ),
    );
    if (saved) await _load();
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
          FilledButton.tonalIcon(
            key: const Key('refresh-central-users'),
            onPressed: _loading || _refreshing
                ? null
                : () => _load(preserveData: true),
            icon: _refreshing
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.refresh),
            label: const Text('Osvježi'),
          ),
          const SizedBox(width: 10),
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
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        horizontalMargin: 16,
                        columnSpacing: 30,
                        dataRowMinHeight: 58,
                        dataRowMaxHeight: 68,
                        columns: const [
                          DataColumn(label: Text('Korisnik')),
                          DataColumn(label: Text('Email')),
                          DataColumn(label: Text('Teretana')),
                          DataColumn(label: Text('Uloga')),
                          DataColumn(label: Text('Status')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final active = item['isActive'] == true;
                          return DataRow(
                            cells: [
                              DataCell(
                                Text(
                                  item['displayName'].toString(),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                              DataCell(Text(item['email'].toString())),
                              DataCell(
                                Text(
                                  item['assignment']?['name']?.toString() ??
                                      'Bez dodijeljene teretane',
                                ),
                              ),
                              DataCell(StatusPill(item['role'].toString())),
                              DataCell(
                                StatusPill(active ? 'Active' : 'Inactive'),
                              ),
                              DataCell(
                                Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    SizedBox(
                                      width: 48,
                                      child: item['role'] != 'Member'
                                          ? IconButton(
                                              key: Key(
                                                'revoke-role-${item['id']}',
                                              ),
                                              tooltip: 'Opozovi ulogu',
                                              onPressed: () => _userAction(
                                                item,
                                                'roles/revoke',
                                              ),
                                              icon: const Icon(
                                                Icons.remove_moderator_outlined,
                                              ),
                                            )
                                          : null,
                                    ),
                                    IconButton(
                                      key: Key(
                                        'toggle-user-active-${item['id']}',
                                      ),
                                      tooltip: active
                                          ? 'Deaktiviraj račun'
                                          : 'Reaktiviraj račun',
                                      onPressed: () => _userAction(
                                        item,
                                        active ? 'deactivate' : 'reactivate',
                                      ),
                                      icon: Icon(
                                        active
                                            ? Icons.block_outlined
                                            : Icons.check_circle_outline,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          );
                        }).toList(),
                      ),
                    ),
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
  late final TabController _tabs = TabController(length: 3, vsync: this);

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
          Tab(text: 'Oprema'),
          Tab(text: 'Tipovi treninga'),
          Tab(text: 'Gradovi'),
        ],
      ),
      const SizedBox(height: 14),
      Expanded(
        child: TabBarView(
          controller: _tabs,
          children: const [
            _ReferenceSection(kind: _ReferenceKind.equipment),
            _ReferenceSection(kind: _ReferenceKind.trainingType),
            _ReferenceSection(kind: _ReferenceKind.city),
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
      if (results.length > 1) {
        _countries = results[1].items
            .where((country) => country['code'] == 'BIH')
            .toList(growable: false);
        if (_countries.isEmpty) {
          throw const ApiProblem(
            status: 0,
            code: 'bih_country_missing',
            message:
                'Bosna i Hercegovina nije dostupna u referentnim podacima.',
          );
        }
      }
      _error = null;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _edit([Map<String, dynamic>? item]) async {
    final api = context.read<ApiClient>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _ReferenceDialog(
        kind: widget.kind,
        value: item,
        countries: _countries,
        onSubmit: (body) => item == null
            ? api.post(
                '/api/admin/reference-data/${widget.kind.path}',
                body: body,
              )
            : api.put(
                '/api/admin/reference-data/${widget.kind.path}/${item['id']}',
                body: body,
              ),
      ),
    );
    if (saved == true) await _load();
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
      if (widget.kind == _ReferenceKind.city) ...[
        const Card(
          key: Key('city-reference-manual-address-hint'),
          color: Color(0xFFF3F7FF),
          child: Padding(
            padding: EdgeInsets.all(12),
            child: Row(
              children: [
                Icon(Icons.info_outline, size: 20),
                SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Gradovi se koriste pri ručnom unosu adrese teretane kada '
                    'lokaciju nije moguće automatski pronaći.',
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 10),
      ],
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
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        horizontalMargin: 16,
                        columnSpacing: 34,
                        dataRowMinHeight: 56,
                        dataRowMaxHeight: 66,
                        columns: [
                          DataColumn(label: Text(widget.kind.label)),
                          DataColumn(
                            label: Text(
                              widget.kind == _ReferenceKind.city
                                  ? 'Država'
                                  : 'Opis',
                            ),
                          ),
                          const DataColumn(label: Text('Status')),
                          const DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final active = item['isActive'] == true;
                          final detail =
                              item['code']?.toString() ??
                              item['countryName']?.toString() ??
                              item['description']?.toString() ??
                              '';
                          return DataRow(
                            cells: [
                              DataCell(
                                Text(
                                  item['name'].toString(),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                              DataCell(
                                SizedBox(
                                  width: 320,
                                  child: Text(
                                    detail,
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ),
                              DataCell(
                                StatusPill(active ? 'Active' : 'Inactive'),
                              ),
                              DataCell(
                                Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    IconButton(
                                      tooltip: 'Uredi',
                                      onPressed: () => _edit(item),
                                      icon: const Icon(Icons.edit_outlined),
                                    ),
                                    if (active)
                                      IconButton(
                                        tooltip: 'Deaktiviraj',
                                        onPressed: () => _deactivate(item),
                                        icon: const Icon(Icons.block_outlined),
                                      ),
                                  ],
                                ),
                              ),
                            ],
                          );
                        }).toList(),
                      ),
                    ),
                  ),
                ),
        ),
      ),
    ],
  );
}

class _RoleDialog extends StatefulWidget {
  const _RoleDialog({required this.tenants, required this.onSubmit});
  final List<Map<String, dynamic>> tenants;
  final Future<void> Function(Map<String, Object?> body) onSubmit;
  @override
  State<_RoleDialog> createState() => _RoleDialogState();
}

class _RoleDialogState extends State<_RoleDialog> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _reason = TextEditingController();
  String _role = 'Member';
  Map<String, dynamic>? _tenant;
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  @override
  void dispose() {
    _email.dispose();
    _reason.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    final needsTenant = _role == 'GymAdmin';
    setState(() => _saving = true);
    try {
      await widget.onSubmit({
        'identifier': _email.text.trim(),
        'role': _role,
        'tenantId': needsTenant ? _tenant!['tenantId'] : null,
        'reason': _reason.text.trim(),
      });
      if (mounted) Navigator.pop(context, true);
    } on ApiProblem catch (error) {
      if (mounted) {
        setState(() {
          _serverProblem = error;
          _formError = error.fieldErrors.isEmpty
              ? switch (error.code) {
                  'gym_admin_already_assigned' =>
                    'Odabrani račun ima aktivno članstvo ili drugu aktivnu '
                        'dodjelu teretani. Registrujte novi Member račun bez '
                        'aktivnog članstva.',
                  _ => error.message,
                }
              : null;
        });
        _formKey.currentState!.validate();
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  void _clearError(String field) {
    final problem = _serverProblem;
    if (problem?.fieldError(field) == null) return;
    final errors = Map<String, List<String>>.from(problem!.fieldErrors)
      ..removeWhere((key, _) => key.toLowerCase() == field.toLowerCase());
    setState(() {
      _serverProblem = ApiProblem(
        status: problem.status,
        code: problem.code,
        message: problem.message,
        fieldErrors: errors,
      );
      _formError = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    final needsTenant = _role == 'GymAdmin';
    return AlertDialog(
      title: const Text('Dodijeli predefinisanu ulogu'),
      content: SizedBox(
        width: 480,
        child: Form(
          key: _formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: _email,
                decoration: const InputDecoration(labelText: 'Email korisnika'),
                onChanged: (_) => _clearError('Identifier'),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.isEmpty) return 'Unesite email ili korisničko ime.';
                  if (text.length > 320) return 'Najviše 320 znakova.';
                  return _serverProblem?.fieldError('Identifier');
                },
              ),
              const SizedBox(height: 10),
              DropdownButtonFormField<String>(
                initialValue: _role,
                decoration: const InputDecoration(labelText: 'Uloga'),
                items: const [
                  DropdownMenuItem(value: 'Member', child: Text('Član')),
                  DropdownMenuItem(
                    value: 'GymAdmin',
                    child: Text('Administrator teretane'),
                  ),
                ],
                onChanged: (value) {
                  setState(() {
                    _role = value!;
                    if (_role == 'Member') _tenant = null;
                  });
                  _clearError('Role');
                },
                validator: (_) => _serverProblem?.fieldError('Role'),
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
                  validator: (value) => value == null
                      ? 'Odaberite teretanu.'
                      : _serverProblem?.fieldError('TenantId'),
                ),
              ],
              if (_role == 'GymAdmin') ...[
                const SizedBox(height: 10),
                const Card(
                  color: Color(0xFFF3F7FF),
                  child: Padding(
                    padding: EdgeInsets.all(12),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(Icons.info_outline, size: 20),
                        SizedBox(width: 8),
                        Expanded(child: Text(_gymAdminEligibilityHint)),
                      ],
                    ),
                  ),
                ),
              ],
              const SizedBox(height: 10),
              TextFormField(
                controller: _reason,
                maxLength: 200,
                decoration: const InputDecoration(labelText: 'Razlog'),
                onChanged: (_) => _clearError('Reason'),
                validator: (value) {
                  final length = value?.trim().length ?? 0;
                  if (length < 2) return 'Unesite razlog.';
                  if (length > 200) return 'Najviše 200 znakova.';
                  return _serverProblem?.fieldError('Reason');
                },
              ),
              if (_formError != null) ...[
                const SizedBox(height: 10),
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
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _saving ? null : () => Navigator.pop(context),
          child: const Text('Odustani'),
        ),
        FilledButton(
          onPressed: _saving || (needsTenant && _tenant == null)
              ? null
              : _submit,
          child: Text(_saving ? 'Dodjela...' : 'Dodijeli'),
        ),
      ],
    );
  }
}

class _ReferenceDialog extends StatefulWidget {
  const _ReferenceDialog({
    required this.kind,
    required this.countries,
    required this.onSubmit,
    this.value,
  });
  final _ReferenceKind kind;
  final Map<String, dynamic>? value;
  final List<Map<String, dynamic>> countries;
  final Future<void> Function(Map<String, Object?> body) onSubmit;
  @override
  State<_ReferenceDialog> createState() => _ReferenceDialogState();
}

class _ReferenceDialogState extends State<_ReferenceDialog> {
  final _formKey = GlobalKey<FormState>();
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
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _country = widget.countries
        .where((item) => item['id'] == widget.value?['countryId'])
        .firstOrNull;
    _country ??= widget.countries.firstOrNull;
  }

  @override
  void dispose() {
    _name.dispose();
    _code.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
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
    setState(() => _saving = true);
    try {
      await widget.onSubmit(body);
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
      if (mounted) setState(() => _saving = false);
    }
  }

  void _clearError(String field) {
    final problem = _serverProblem;
    if (problem?.fieldError(field) == null) return;
    final errors = Map<String, List<String>>.from(problem!.fieldErrors)
      ..removeWhere((key, _) => key.toLowerCase() == field.toLowerCase());
    setState(() {
      _serverProblem = ApiProblem(
        status: problem.status,
        code: problem.code,
        message: problem.message,
        fieldErrors: errors,
      );
      _formError = null;
    });
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.value == null ? 'Novi zapis' : 'Uredi zapis'),
    content: SizedBox(
      width: 440,
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.kind == _ReferenceKind.country) ...[
              TextFormField(
                controller: _code,
                decoration: const InputDecoration(labelText: 'Kod'),
                onChanged: (_) => _clearError('Code'),
                validator: (value) {
                  final length = value?.trim().length ?? 0;
                  if (length < 2 || length > 3) return 'Unesite 2–3 znaka.';
                  return _serverProblem?.fieldError('Code');
                },
              ),
              const SizedBox(height: 10),
            ],
            TextFormField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Naziv'),
              onChanged: (_) => _clearError('Name'),
              validator: (value) {
                final text = value?.trim() ?? '';
                if (text.isEmpty) return 'Naziv je obavezan.';
                final maxLength = widget.kind == _ReferenceKind.country
                    ? 120
                    : 160;
                if (text.length > maxLength) {
                  return 'Najviše $maxLength znakova.';
                }
                return _serverProblem?.fieldError('Name');
              },
            ),
            if (widget.kind == _ReferenceKind.trainingType) ...[
              const SizedBox(height: 10),
              TextFormField(
                controller: _description,
                maxLines: 3,
                decoration: const InputDecoration(labelText: 'Opis'),
                onChanged: (_) => _clearError('Description'),
                validator: (value) {
                  if ((value?.trim().length ?? 0) > 1000) {
                    return 'Najviše 1000 znakova.';
                  }
                  return _serverProblem?.fieldError('Description');
                },
              ),
            ],
            if (widget.kind == _ReferenceKind.city)
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
                onChanged: (value) => setState(() => _country = value),
                validator: (value) => value == null
                    ? 'Odaberite državu.'
                    : _serverProblem?.fieldError('CountryId'),
              ),
            if (widget.value != null)
              SwitchListTile(
                value: _active,
                onChanged: (value) => setState(() => _active = value),
                title: const Text('Aktivno'),
              ),
            if (_formError != null) ...[
              const SizedBox(height: 10),
              Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  _formError!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ),
            ],
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _saving ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _saving ? null : _submit,
        child: Text(_saving ? 'Čuvanje...' : 'Sačuvaj'),
      ),
    ],
  );
}

String _date(Object? value) => value == null
    ? 'Nije dostavljeno'
    : DateFormat(
        'dd.MM.yyyy.',
      ).format(DateTime.parse(value.toString()).toLocal());
