import 'dart:convert';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../shared/widgets.dart';

const _requestStatuses = ['Pending', 'Approved', 'Rejected', 'Cancelled'];
const _membershipStatuses = [
  'PendingPayment',
  'Active',
  'Expired',
  'Cancelled',
  'Suspended',
];
const _paymentStatuses = [
  'Created',
  'Processing',
  'Succeeded',
  'Failed',
  'PartiallyRefunded',
  'Refunded',
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
  int? _paymentCategory;
  int? _membershipStatus;
  int _page = 1;
  int _totalCount = 0;
  static const _pageSize = 20;

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
      final result = await context.read<ApiClient>().page(
        '/api/tenant/membership-requests',
        query: {
          'member': _search.text.trim(),
          'status': _status,
          'paymentCategory': _paymentCategory,
          'membershipStatus': _membershipStatus,
          'page': _page,
          'pageSize': _pageSize,
        },
      );
      _items = result.items;
      _totalCount = result.totalCount;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _decide(Map<String, dynamic> item, bool approve) async {
    final api = context.read<ApiClient>();
    if (!approve) {
      final saved = await submitReasonedAction(
        context,
        title: 'Razlog odbijanja',
        onSubmit: (reason) => api.post(
          '/api/tenant/membership-requests/${item['id']}/reject',
          body: {
            'concurrencyToken': item['concurrencyToken'],
            'reason': reason,
          },
        ),
      );
      if (saved) await _load();
      return;
    } else if (!await confirmAction(
      context,
      title: 'Odobri članstvo',
      message: 'Potvrdite da je članarina naplaćena uživo.',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/tenant/membership-requests/${item['id']}/${approve ? 'approve' : 'reject'}',
        body: {'concurrencyToken': item['concurrencyToken']},
      );
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              approve
                  ? 'Članstvo je uspješno aktivirano.'
                  : 'Zahtjev je uspješno odbijen.',
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
      if (error.status == 409) await _load();
    }
  }

  Future<void> _membershipAction(
    Map<String, dynamic> membership,
    String action,
  ) async {
    final api = context.read<ApiClient>();
    if (action != 'expire') {
      final saved = await submitReasonedAction(
        context,
        title: 'Razlog promjene članstva',
        onSubmit: (reason) => api.post(
          '/api/tenant/memberships/${membership['id']}/$action',
          body: {
            'concurrencyToken': membership['concurrencyToken'],
            'reason': reason,
          },
        ),
      );
      if (saved) await _load();
      return;
    }
    if (!mounted) return;
    try {
      await api.post(
        '/api/tenant/memberships/${membership['id']}/$action',
        body: {'concurrencyToken': membership['concurrencyToken']},
      );
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Status članstva je uspješno promijenjen.'),
          ),
        );
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
      if (error.status == 409) await _load();
    }
  }

  Future<void> _showDetails(Map<String, dynamic> item) => showDialog<void>(
    context: context,
    builder: (context) {
      final membership = item['membership'] is Map
          ? Map<String, dynamic>.from(item['membership'] as Map)
          : null;
      return AlertDialog(
        title: const Text('Detalji zahtjeva'),
        content: SizedBox(
          width: 520,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              _requestDetail('Korisnik', item['memberDisplayName']),
              _requestDetail('Email', item['memberEmail']),
              _requestDetail('Vrsta članarine', item['planName']),
              _requestDetail('Iznos', '${item['price']} ${item['currency']}'),
              _requestDetail('Datum', _date(item['requestedAtUtc'])),
              _requestDetail(
                'Način plaćanja',
                _membershipPaymentLabel(item['paymentMethod']),
              ),
              _requestDetail(
                'Status',
                enumLabel(item['status'], _requestStatuses),
              ),
              if (item['decisionReason'] != null)
                _requestDetail('Razlog odluke', item['decisionReason']),
              if (membership != null) ...[
                const Divider(height: 24),
                _requestDetail(
                  'Status članstva',
                  enumLabel(membership['status'], _membershipStatuses),
                ),
                _requestDetail('Početak', _date(membership['startsAtUtc'])),
                _requestDetail('Kraj', _date(membership['endsAtUtc'])),
                _requestDetail(
                  'Status plaćanja',
                  membership['paymentStatus'] == null
                      ? 'Plaćanje uživo'
                      : enumLabel(
                          membership['paymentStatus'],
                          _paymentStatuses,
                        ),
                ),
                if (membership['statusReason'] != null)
                  _requestDetail('Razlog statusa', membership['statusReason']),
              ],
            ],
          ),
        ),
        actions: [
          FilledButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Zatvori'),
          ),
        ],
      );
    },
  );

  Widget _requestDetail(String label, Object? value) => Padding(
    padding: const EdgeInsets.only(bottom: 10),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 150,
          child: Text(label, style: const TextStyle(color: Colors.blueGrey)),
        ),
        Expanded(child: Text(value?.toString() ?? '—')),
      ],
    ),
  );

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Wrap(
        spacing: 12,
        runSpacing: 12,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          SizedBox(
            width: 300,
            child: TextField(
              controller: _search,
              onSubmitted: (_) {
                _page = 1;
                _load();
              },
              decoration: const InputDecoration(
                hintText: 'Pretraži člana...',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          SizedBox(
            width: 210,
            child: DropdownButtonFormField<int?>(
              key: const Key('membership-request-status-filter'),
              initialValue: _status,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Status zahtjeva'),
              items: [
                const DropdownMenuItem(
                  value: null,
                  child: Text('Svi zahtjevi'),
                ),
                ...List.generate(
                  _requestStatuses.length,
                  (index) => DropdownMenuItem(
                    value: index,
                    child: Text(_requestStatuses[index]),
                  ),
                ),
              ],
              onChanged: (value) {
                _status = value;
                _page = 1;
                _load();
              },
            ),
          ),
          SizedBox(
            width: 210,
            child: DropdownButtonFormField<int?>(
              key: const Key('membership-payment-method-filter'),
              initialValue: _paymentCategory,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Način plaćanja'),
              items: const [
                DropdownMenuItem(value: null, child: Text('Sve metode')),
                DropdownMenuItem(value: 0, child: Text('Stripe')),
                DropdownMenuItem(value: 1, child: Text('Plati uživo')),
              ],
              onChanged: (value) {
                _paymentCategory = value;
                _page = 1;
                _load();
              },
            ),
          ),
          SizedBox(
            width: 210,
            child: DropdownButtonFormField<int?>(
              key: const Key('linked-membership-status-filter'),
              initialValue: _membershipStatus,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Status članstva'),
              items: [
                const DropdownMenuItem(
                  value: null,
                  child: Text('Sva članstva'),
                ),
                ...List.generate(
                  _membershipStatuses.length,
                  (index) => DropdownMenuItem(
                    value: index,
                    child: Text(_membershipStatuses[index]),
                  ),
                ),
              ],
              onChanged: (value) {
                _membershipStatus = value;
                _page = 1;
                _load();
              },
            ),
          ),
          IconButton.filledTonal(
            tooltip: 'Osvježi',
            onPressed: () {
              _page = 1;
              _load();
            },
            icon: const Icon(Icons.refresh),
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
              ? const EmptyState('Nema zahtjeva za izabrane filtere.')
              : Card(
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        horizontalMargin: 12,
                        columnSpacing: 18,
                        dataRowMinHeight: 52,
                        dataRowMaxHeight: 60,
                        columns: const [
                          DataColumn(label: Text('Član')),
                          DataColumn(label: Text('Članarina')),
                          DataColumn(label: Text('Plaćanje')),
                          DataColumn(label: Text('Zahtjev')),
                          DataColumn(label: Text('Status članstva')),
                          DataColumn(label: Text('Period')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final actions =
                              (item['allowedActions'] as List? ?? const [])
                                  .map((value) => value.toString())
                                  .toSet();
                          final membership = item['membership'] is Map
                              ? Map<String, dynamic>.from(
                                  item['membership'] as Map,
                                )
                              : null;
                          final membershipActions =
                              (membership?['allowedActions'] as List? ??
                                      const [])
                                  .map((value) => value.toString())
                                  .where((action) => action != 'pay')
                                  .toSet();
                          return DataRow(
                            cells: [
                              DataCell(
                                Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      item['memberDisplayName'].toString(),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                    Text(
                                      item['memberEmail']?.toString() ?? '—',
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: Theme.of(
                                        context,
                                      ).textTheme.bodySmall,
                                    ),
                                  ],
                                ),
                              ),
                              DataCell(
                                Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(item['planName'].toString()),
                                    Text(
                                      '${item['price']} ${item['currency']}',
                                      style: Theme.of(
                                        context,
                                      ).textTheme.bodySmall,
                                    ),
                                  ],
                                ),
                              ),
                              DataCell(
                                Text(
                                  _membershipPaymentLabel(
                                    item['paymentMethod'],
                                  ),
                                ),
                              ),
                              DataCell(
                                StatusPill(
                                  enumLabel(item['status'], _requestStatuses),
                                ),
                              ),
                              DataCell(
                                membership == null
                                    ? Text(
                                        _missingMembershipLabel(item['status']),
                                      )
                                    : StatusPill(
                                        enumLabel(
                                          membership['status'],
                                          _membershipStatuses,
                                        ),
                                      ),
                              ),
                              DataCell(
                                Text(
                                  membership == null
                                      ? '—'
                                      : '${_date(membership['startsAtUtc'])} – ${_date(membership['endsAtUtc'])}',
                                  maxLines: 1,
                                ),
                              ),
                              DataCell(
                                SizedBox(
                                  width: 144,
                                  child: Row(
                                    mainAxisAlignment: MainAxisAlignment.end,
                                    mainAxisSize: MainAxisSize.max,
                                    children: [
                                      if (actions.contains('approve'))
                                        IconButton(
                                          tooltip: 'Aktiviraj nakon naplate',
                                          onPressed: () => _decide(item, true),
                                          icon: const Icon(
                                            Icons.check_circle_outline,
                                            color: Colors.green,
                                          ),
                                        ),
                                      if (actions.contains('reject'))
                                        IconButton(
                                          tooltip: 'Odbij',
                                          onPressed: () => _decide(item, false),
                                          icon: const Icon(
                                            Icons.cancel_outlined,
                                            color: Colors.red,
                                          ),
                                        ),
                                      if (membership != null &&
                                          membershipActions.isNotEmpty)
                                        PopupMenuButton<String>(
                                          tooltip: 'Akcije članstva',
                                          onSelected: (action) =>
                                              _membershipAction(
                                                membership,
                                                action,
                                              ),
                                          itemBuilder: (_) => membershipActions
                                              .map(
                                                (action) => PopupMenuItem(
                                                  value: action,
                                                  child: Text(
                                                    _membershipActionLabel(
                                                      action,
                                                    ),
                                                  ),
                                                ),
                                              )
                                              .toList(),
                                        ),
                                      IconButton(
                                        tooltip: 'Detalji',
                                        onPressed: () => _showDetails(item),
                                        icon: const Icon(
                                          Icons.visibility_outlined,
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

String _membershipPaymentLabel(Object? value) {
  if (value is num) {
    return value.toInt() == 2 ? 'Plati uživo' : 'Stripe';
  }
  final normalized = value?.toString().replaceAll('_', '').toLowerCase();
  if (normalized == 'payinperson') return 'Plati uživo';
  if (normalized?.contains('stripe') == true) return 'Stripe';
  return 'API nije ažuriran';
}

String _missingMembershipLabel(Object? requestStatus) {
  final normalized = requestStatus?.toString().toLowerCase();
  if (requestStatus == 0 || normalized == 'pending') return 'Nije aktivirano';
  if (requestStatus == 2 ||
      requestStatus == 3 ||
      normalized == 'rejected' ||
      normalized == 'cancelled') {
    return 'Nema članstva';
  }
  return 'Podaci nisu dostupni — ponovo pokrenite API';
}

String _membershipActionLabel(String action) => switch (action) {
  'cancel' => 'Otkaži',
  'suspend' => 'Suspenduj',
  'reactivate' => 'Ponovo aktiviraj',
  'expire' => 'Označi isteklim',
  _ => action,
};

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
    if (!mounted) return;
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
    final api = context.read<ApiClient>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _TrainerPromotionDialog(
        onSubmit: (body) => api.post('/api/tenant/trainers', body: body),
      ),
    );
    if (saved != true || !mounted) return;
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
      final saved = await showDialog<bool>(
        context: context,
        builder: (_) => _OfferingDialog(
          trainers: _trainers
              .where((item) => item['isActive'] == true)
              .toList(),
          types: (lookups['trainingTypes'] as List? ?? const [])
              .whereType<Map>()
              .map((item) => Map<String, dynamic>.from(item))
              .toList(),
          onSubmit: (body) =>
              api.post('/api/tenant/trainer-offerings', body: body),
        ),
      );
      if (saved != true) return;
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
  bool _refreshing = false;
  Object? _error;
  int? _status;

  @override
  void initState() {
    super.initState();
    _load();
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
      final items = (await context.read<ApiClient>().page(
        '/api/tenant/reservations',
        query: {'status': _status},
      )).items;
      if (mounted) {
        setState(() {
          _items = items;
          _error = null;
        });
      }
    } catch (error) {
      if (!mounted) return;
      if (preserveData && _items.isNotEmpty) {
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

  Future<void> _command(Map<String, dynamic> item, String action) async {
    final api = context.read<ApiClient>();
    if (action == 'cancel') {
      final saved = await submitReasonedAction(
        context,
        title: 'Razlog otkazivanja',
        onSubmit: (reason) => api.post(
          '/api/tenant/reservations/${item['id']}/cancel',
          body: {
            'concurrencyToken': item['concurrencyToken'],
            'reason': reason,
          },
        ),
      );
      if (saved) await _load();
      return;
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
        body: {'concurrencyToken': item['concurrencyToken']},
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
        child: Row(
          children: [
            SizedBox(
              width: 300,
              child: DropdownButtonFormField<int?>(
                key: const Key('reservation-status-filter'),
                initialValue: _status,
                decoration: const InputDecoration(labelText: 'Status'),
                items: [
                  const DropdownMenuItem(
                    value: null,
                    child: Text('Svi statusi'),
                  ),
                  ..._visibleReservationStatuses.map(
                    (status) => DropdownMenuItem(
                      value: status,
                      child: Text(_reservationStatuses[status]),
                    ),
                  ),
                ],
                onChanged: _refreshing
                    ? null
                    : (value) {
                        _status = value;
                        _load();
                      },
              ),
            ),
            const SizedBox(width: 12),
            FilledButton.tonalIcon(
              key: const Key('refresh-reservations'),
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
          ],
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

class _GalleryDraftItem {
  _GalleryDraftItem({
    required this.server,
    this.bytes,
    this.fileName,
    this.contentType,
  });

  final Map<String, dynamic>? server;
  Uint8List? bytes;
  String? fileName;
  String? contentType;

  String? get id => server?['id']?.toString();
  String? get concurrencyToken => server?['concurrencyToken']?.toString();
  String? get imageUrl => server?['imageUrl']?.toString();
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
  bool _galleryBusy = false;
  bool _galleryDirty = false;
  List<_GalleryDraftItem> _galleryDraft = [];
  Object? _error;

  List<Map<String, dynamic>> get _galleryImages {
    final gallery = _gym?['imageGallery'];
    if (gallery is! Map) return const [];
    return (gallery['images'] as List? ?? const [])
        .map((value) => Map<String, dynamic>.from(value as Map))
        .toList();
  }

  int get _maximumGalleryImages =>
      ((_gym?['imageGallery'] as Map?)?['maximumImages'] as num?)?.toInt() ?? 5;

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
      _resetGalleryDraft();
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
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _PlanDialog(
        onSubmit: (body) =>
            api.post('/api/tenant/membership-plans', body: body),
      ),
    );
    if (saved == true) await _load();
  }

  Future<void> _editGym() async {
    final api = context.read<ApiClient>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _GymEditorDialog(
        gym: _gym!,
        lookups: _lookups!,
        onSubmit: (body) => api.put('/api/tenant/gym', body: body),
      ),
    );
    if (saved == true) await _load();
  }

  void _resetGalleryDraft() {
    _galleryDraft = _galleryImages
        .map((image) => _GalleryDraftItem(server: image))
        .toList();
    _galleryDirty = false;
  }

  Future<void> _pickGymImage([_GalleryDraftItem? existing]) async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: const ['jpg', 'jpeg', 'png', 'webp'],
      withData: true,
    );
    final file = result?.files.singleOrNull;
    if (file == null || !mounted) return;
    final bytes = file.bytes;
    if (bytes == null || bytes.isEmpty) {
      _showGalleryMessage('Odabranu sliku nije moguće pročitati.');
      return;
    }
    if (bytes.length > 5 * 1024 * 1024) {
      _showGalleryMessage('Slika mora biti manja ili jednaka 5 MiB.');
      return;
    }
    final contentType = switch (file.extension?.toLowerCase()) {
      'jpg' || 'jpeg' => 'image/jpeg',
      'png' => 'image/png',
      'webp' => 'image/webp',
      _ => null,
    };
    if (contentType == null) {
      _showGalleryMessage('Dozvoljene su JPG, PNG i WebP slike.');
      return;
    }
    if (existing == null && _galleryDraft.length >= _maximumGalleryImages) {
      _showGalleryMessage('Galerija može sadržavati najviše 5 slika.');
      return;
    }

    if (existing?.server != null &&
        (existing!.concurrencyToken == null ||
            existing.concurrencyToken!.isEmpty)) {
      _showGalleryMessage('Osvježite galeriju prije zamjene slike.');
      return;
    }
    setState(() {
      if (existing == null) {
        _galleryDraft.add(
          _GalleryDraftItem(
            server: null,
            bytes: bytes,
            fileName: file.name,
            contentType: contentType,
          ),
        );
      } else {
        existing.bytes = bytes;
        existing.fileName = file.name;
        existing.contentType = contentType;
      }
      _galleryDirty = true;
    });
  }

  Future<void> _removeGymImage(_GalleryDraftItem image) async {
    final index = _galleryDraft.indexOf(image);
    if (!await confirmAction(
      context,
      title: 'Ukloni sliku',
      message: index == 0 && _galleryDraft.length > 1
          ? 'Sljedeća slika postat će naslovna slika teretane.'
          : 'Slika će biti uklonjena iz lokalnog nacrta galerije.',
    )) {
      return;
    }
    if (!mounted) return;
    setState(() {
      _galleryDraft.remove(image);
      _galleryDirty = true;
    });
  }

  void _moveGymImage(int from, int to) {
    if (from == to || to < 0 || to >= _galleryDraft.length) return;
    setState(() {
      final moved = _galleryDraft.removeAt(from);
      _galleryDraft.insert(to, moved);
      _galleryDirty = true;
    });
  }

  Future<void> _saveGallery() async {
    if (!_galleryDirty || _galleryBusy) return;
    final files = <MultipartUploadPart>[];
    final items = <Map<String, Object?>>[];
    for (final item in _galleryDraft) {
      int? uploadIndex;
      if (item.bytes != null) {
        uploadIndex = files.length;
        files.add(
          MultipartUploadPart(
            fieldName: 'files',
            bytes: item.bytes!,
            fileName: item.fileName!,
            contentType: item.contentType!,
          ),
        );
      }
      items.add({
        'imageId': item.id,
        'concurrencyToken': item.concurrencyToken,
        'uploadIndex': uploadIndex,
      });
    }
    final retainedIds = _galleryDraft
        .map((item) => item.id)
        .whereType<String>()
        .toSet();
    final removedImages = _galleryImages
        .where((image) => !retainedIds.contains(image['id']?.toString()))
        .map(
          (image) => {
            'imageId': image['id'],
            'concurrencyToken': image['concurrencyToken'],
          },
        )
        .toList();

    setState(() => _galleryBusy = true);
    try {
      final response = await context.read<ApiClient>().putMultipart(
        '/api/tenant/gym/images',
        fields: {
          'manifest': jsonEncode({
            'items': items,
            'removedImages': removedImages,
          }),
        },
        files: files,
      );
      _applyGallery(response);
      _showGalleryMessage('Galerija je sačuvana.');
    } on ApiProblem catch (error) {
      _showGalleryMessage(error.message);
      if (error.status == 409 && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: const Text(
              'Galerija je promijenjena na serveru. Lokalni nacrt je zadržan.',
            ),
            action: SnackBarAction(label: 'Osvježi', onPressed: _load),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _galleryBusy = false);
    }
  }

  Future<void> _discardGalleryChanges() async {
    if (!_galleryDirty || _galleryBusy) return;
    if (!await confirmAction(
      context,
      title: 'Odbaci promjene',
      message: 'Sve nesačuvane promjene galerije bit će izgubljene.',
    )) {
      return;
    }
    if (mounted) setState(_resetGalleryDraft);
  }

  void _applyGallery(Object? response) {
    if (!mounted || response is! Map || _gym == null) return;
    setState(() {
      _gym!['imageGallery'] = Map<String, dynamic>.from(response);
      _gym!['imageUrls'] = _galleryImages
          .map((image) => image['imageUrl'])
          .whereType<String>()
          .toList();
      _resetGalleryDraft();
    });
  }

  void _showGalleryMessage(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
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
                  const SizedBox(height: 18),
                  _GymGalleryManager(
                    images: _galleryDraft,
                    maximumImages: _maximumGalleryImages,
                    busy: _galleryBusy,
                    dirty: _galleryDirty,
                    onAdd: () => _pickGymImage(),
                    onReplace: _pickGymImage,
                    onRemove: _removeGymImage,
                    onMove: _moveGymImage,
                    onSave: _saveGallery,
                    onDiscard: _discardGalleryChanges,
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

class _GymGalleryManager extends StatelessWidget {
  const _GymGalleryManager({
    required this.images,
    required this.maximumImages,
    required this.busy,
    required this.dirty,
    required this.onAdd,
    required this.onReplace,
    required this.onRemove,
    required this.onMove,
    required this.onSave,
    required this.onDiscard,
  });

  final List<_GalleryDraftItem> images;
  final int maximumImages;
  final bool busy;
  final bool dirty;
  final VoidCallback onAdd;
  final ValueChanged<_GalleryDraftItem> onReplace;
  final ValueChanged<_GalleryDraftItem> onRemove;
  final void Function(int from, int to) onMove;
  final VoidCallback onSave;
  final VoidCallback onDiscard;

  @override
  Widget build(BuildContext context) {
    final api = context.read<ApiClient>();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            const Expanded(
              child: Text(
                'Galerija teretane',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w800),
              ),
            ),
            Text('${images.length}/$maximumImages'),
            const SizedBox(width: 8),
            FilledButton.tonalIcon(
              key: const Key('gym-gallery-add'),
              onPressed: busy || images.length >= maximumImages ? null : onAdd,
              icon: const Icon(Icons.add_photo_alternate_outlined),
              label: const Text('Dodaj sliku'),
            ),
          ],
        ),
        const SizedBox(height: 10),
        if (images.isEmpty)
          Container(
            height: 120,
            width: double.infinity,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: const Color(0xFFF1F4FA),
              borderRadius: BorderRadius.circular(14),
            ),
            child: const Text('Dodajte do pet slika teretane.'),
          )
        else
          SizedBox(
            height: 160,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              itemCount: images.length,
              separatorBuilder: (_, _) => const SizedBox(width: 10),
              itemBuilder: (context, index) {
                final image = images[index];
                return _GalleryImageTile(
                  imageUrl: api.mediaUrl(image.imageUrl),
                  preview: image.bytes,
                  label: index == 0 ? 'Naslovna' : 'Slika ${index + 1}',
                  actions: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      IconButton(
                        tooltip: 'Pomjeri lijevo',
                        onPressed: busy || index == 0
                            ? null
                            : () => onMove(index, index - 1),
                        icon: const Icon(Icons.chevron_left, size: 20),
                      ),
                      IconButton(
                        tooltip: 'Pomjeri desno',
                        onPressed: busy || index == images.length - 1
                            ? null
                            : () => onMove(index, index + 1),
                        icon: const Icon(Icons.chevron_right, size: 20),
                      ),
                      PopupMenuButton<String>(
                        tooltip: 'Opcije slike',
                        enabled: !busy,
                        onSelected: (action) {
                          if (action == 'replace') onReplace(image);
                          if (action == 'remove') onRemove(image);
                          if (action == 'primary') onMove(index, 0);
                        },
                        itemBuilder: (_) => [
                          if (index > 0)
                            const PopupMenuItem(
                              value: 'primary',
                              child: Text('Postavi kao naslovnu'),
                            ),
                          const PopupMenuItem(
                            value: 'replace',
                            child: Text('Zamijeni sliku'),
                          ),
                          const PopupMenuItem(
                            value: 'remove',
                            child: Text('Ukloni sliku'),
                          ),
                        ],
                      ),
                    ],
                  ),
                );
              },
            ),
          ),
        const SizedBox(height: 6),
        const Text(
          'Prva slika je naslovna. Koristite strelice ili opciju za promjenu redoslijeda.',
          style: TextStyle(fontSize: 12, color: Colors.grey),
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            if (dirty)
              const Expanded(
                child: Text(
                  'Nesačuvane promjene',
                  style: TextStyle(
                    color: Color(0xFFB45309),
                    fontWeight: FontWeight.w700,
                  ),
                ),
              )
            else
              const Spacer(),
            OutlinedButton(
              key: const Key('gym-gallery-discard'),
              onPressed: dirty && !busy ? onDiscard : null,
              child: const Text('Odbaci promjene'),
            ),
            const SizedBox(width: 8),
            FilledButton.icon(
              key: const Key('gym-gallery-save'),
              onPressed: dirty && !busy ? onSave : null,
              icon: busy
                  ? const SizedBox.square(
                      key: Key('gym-gallery-save-progress'),
                      dimension: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Colors.white,
                      ),
                    )
                  : const Icon(Icons.save_outlined),
              label: Text(busy ? 'Čuvanje...' : 'Sačuvaj galeriju'),
            ),
          ],
        ),
      ],
    );
  }
}

class _GalleryImageTile extends StatelessWidget {
  const _GalleryImageTile({
    required this.label,
    this.imageUrl,
    this.preview,
    this.actions,
  });

  final String label;
  final String? imageUrl;
  final Uint8List? preview;
  final Widget? actions;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 174,
    child: ClipRRect(
      borderRadius: BorderRadius.circular(14),
      child: ColoredBox(
        color: const Color(0xFFE8EDF7),
        child: Column(
          children: [
            Expanded(
              child: SizedBox(
                width: double.infinity,
                child: preview != null
                    ? Image.memory(preview!, fit: BoxFit.cover)
                    : imageUrl == null
                    ? const Icon(
                        Icons.fitness_center,
                        color: GymLinkColors.blue,
                      )
                    : Image.network(
                        imageUrl!,
                        fit: BoxFit.cover,
                        errorBuilder: (_, _, _) => const Icon(
                          Icons.broken_image_outlined,
                          color: Colors.grey,
                        ),
                      ),
              ),
            ),
            SizedBox(
              height: 42,
              child: Row(
                children: [
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.only(left: 8),
                      child: Text(
                        label,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                    ),
                  ),
                  ?actions,
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

class _TrainerPromotionDialog extends StatefulWidget {
  const _TrainerPromotionDialog({required this.onSubmit});

  final Future<void> Function(Map<String, Object?> body) onSubmit;

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
  bool _saving = false;
  ApiProblem? _serverProblem;
  String? _formError;
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

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate() || _candidate == null) return;
    setState(() => _saving = true);
    try {
      await widget.onSubmit({
        'userId': _candidate!['userId'],
        'biography': _biography.text.trim(),
        'credentials': _credentials.text.trim().isEmpty
            ? null
            : _credentials.text.trim(),
        'trainingTypeIds': _trainingTypeIds.toList(),
        'reason': _reason.text.trim(),
      });
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

  void _clearError(String field, {Iterable<String> aliases = const []}) {
    final problem = _serverProblem;
    if (problem?.fieldError(field, aliases: aliases) == null) return;
    final names = {field, ...aliases}.map((value) => value.toLowerCase());
    final errors = Map<String, List<String>>.from(problem!.fieldErrors)
      ..removeWhere((key, _) => names.contains(key.toLowerCase()));
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
                  validator: (value) => value == null
                      ? 'Odaberite aktivnog člana.'
                      : _serverProblem?.fieldError('UserId'),
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
                  onChanged: (_) => _clearError('Biography'),
                  validator: (value) {
                    final text = value?.trim() ?? '';
                    if (text.isEmpty) return 'Biografija je obavezna.';
                    if (text.length > 4000) return 'Najviše 4000 znakova.';
                    return _serverProblem?.fieldError('Biography');
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _credentials,
                  decoration: const InputDecoration(
                    labelText: 'Kvalifikacije i iskustvo',
                  ),
                  onChanged: (_) => _clearError('Credentials'),
                  validator: (value) {
                    if ((value?.trim().length ?? 0) > 2000) {
                      return 'Najviše 2000 znakova.';
                    }
                    return _serverProblem?.fieldError('Credentials');
                  },
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
                  onChanged: (_) => _clearError('Reason'),
                  validator: (value) {
                    final length = value?.trim().length ?? 0;
                    if (length < 2) return 'Unesite razlog promocije.';
                    if (length > 1000) return 'Najviše 1000 znakova.';
                    return _serverProblem?.fieldError('Reason');
                  },
                ),
                if (_formError != null) ...[
                  const SizedBox(height: 10),
                  Text(
                    _formError!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _saving ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _saving || _candidates.isEmpty ? null : _submit,
        child: Text(_saving ? 'Promovisanje...' : 'Promoviši u trenera'),
      ),
    ],
  );
}

class _OfferingDialog extends StatefulWidget {
  const _OfferingDialog({
    required this.trainers,
    required this.types,
    required this.onSubmit,
  });
  final List<Map<String, dynamic>> trainers;
  final List<Map<String, dynamic>> types;
  final Future<void> Function(Map<String, Object?> body) onSubmit;
  @override
  State<_OfferingDialog> createState() => _OfferingDialogState();
}

class _OfferingDialogState extends State<_OfferingDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _duration = TextEditingController(text: '60');
  final _price = TextEditingController(text: '25');
  Map<String, dynamic>? _trainer;
  Map<String, dynamic>? _type;
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _trainer = widget.trainers.firstOrNull;
    _type = widget.types.firstOrNull;
  }

  @override
  void dispose() {
    _name.dispose();
    _duration.dispose();
    _price.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      await widget.onSubmit({
        'trainerProfileId': _trainer!['id'],
        'trainingTypeId': _type!['id'],
        'name': _name.text.trim(),
        'durationMinutes': int.parse(_duration.text),
        'price': double.parse(_price.text.replaceFirst(',', '.')),
        'currency': 'BAM',
      });
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
    title: const Text('Nova usluga'),
    content: SizedBox(
      width: 460,
      child: Form(
        key: _formKey,
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
              validator: (value) => value == null
                  ? 'Odaberite trenera.'
                  : _serverProblem?.fieldError('TrainerProfileId'),
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
              validator: (value) => value == null
                  ? 'Odaberite tip treninga.'
                  : _serverProblem?.fieldError('TrainingTypeId'),
            ),
            const SizedBox(height: 10),
            TextFormField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Naziv'),
              onChanged: (_) => _clearError('Name'),
              validator: (value) {
                final text = value?.trim() ?? '';
                if (text.isEmpty) return 'Naziv je obavezan.';
                if (text.length > 200) return 'Najviše 200 znakova.';
                return _serverProblem?.fieldError('Name');
              },
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    controller: _duration,
                    decoration: const InputDecoration(
                      labelText: 'Trajanje (min)',
                    ),
                    onChanged: (_) => _clearError('DurationMinutes'),
                    validator: (value) {
                      final number = int.tryParse(value?.trim() ?? '');
                      if (number == null || number < 1 || number > 1440) {
                        return 'Unesite 1–1440.';
                      }
                      return _serverProblem?.fieldError('DurationMinutes');
                    },
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: TextFormField(
                    controller: _price,
                    decoration: const InputDecoration(
                      labelText: 'Cijena (BAM)',
                    ),
                    onChanged: (_) => _clearError('Price'),
                    validator: (value) {
                      final number = double.tryParse(
                        (value ?? '').trim().replaceFirst(',', '.'),
                      );
                      if (number == null || number < 0 || number > 1000000) {
                        return 'Unesite 0–1.000.000.';
                      }
                      return _serverProblem?.fieldError('Price');
                    },
                  ),
                ),
              ],
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
        onPressed: _saving || _trainer == null || _type == null
            ? null
            : _submit,
        child: Text(_saving ? 'Čuvanje...' : 'Sačuvaj'),
      ),
    ],
  );
}

class _PlanDialog extends StatefulWidget {
  const _PlanDialog({required this.onSubmit});
  final Future<void> Function(Map<String, Object?> body) onSubmit;
  @override
  State<_PlanDialog> createState() => _PlanDialogState();
}

class _PlanDialogState extends State<_PlanDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _days = TextEditingController(text: '30');
  final _price = TextEditingController(text: '50');
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  @override
  void dispose() {
    _name.dispose();
    _days.dispose();
    _price.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      await widget.onSubmit({
        'name': _name.text.trim(),
        'durationDays': int.parse(_days.text),
        'price': double.parse(_price.text.replaceFirst(',', '.')),
        'currency': 'BAM',
      });
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
    final aliases = field == 'Name'
        ? const <String>['MembershipPlan.Name']
        : const <String>[];
    if (problem?.fieldError(field, aliases: aliases) == null) {
      return;
    }
    final errors = Map<String, List<String>>.from(problem!.fieldErrors)
      ..removeWhere(
        (key, _) =>
            key.toLowerCase() == field.toLowerCase() ||
            key.toLowerCase() == 'membershipplan.${field.toLowerCase()}',
      );
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
    title: const Text('Novi plan članstva'),
    content: SizedBox(
      width: 420,
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextFormField(
              controller: _name,
              decoration: const InputDecoration(labelText: 'Naziv'),
              onChanged: (_) => _clearError('Name'),
              validator: (value) {
                final text = value?.trim() ?? '';
                if (text.isEmpty) return 'Naziv je obavezan.';
                if (text.length > 160) return 'Najviše 160 znakova.';
                return _serverProblem?.fieldError(
                  'Name',
                  aliases: const ['MembershipPlan.Name'],
                );
              },
            ),
            const SizedBox(height: 10),
            TextFormField(
              controller: _days,
              decoration: const InputDecoration(labelText: 'Trajanje (dana)'),
              onChanged: (_) => _clearError('DurationDays'),
              validator: (value) {
                final number = int.tryParse(value?.trim() ?? '');
                if (number == null || number < 1 || number > 3660) {
                  return 'Unesite 1–3660 dana.';
                }
                return _serverProblem?.fieldError('DurationDays');
              },
            ),
            const SizedBox(height: 10),
            TextFormField(
              controller: _price,
              decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
              onChanged: (_) => _clearError('Price'),
              validator: (value) {
                final number = double.tryParse(
                  (value ?? '').trim().replaceFirst(',', '.'),
                );
                if (number == null || number < 0 || number > 1000000) {
                  return 'Unesite 0–1.000.000.';
                }
                return _serverProblem?.fieldError('Price');
              },
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

class _GymEditorDialog extends StatefulWidget {
  const _GymEditorDialog({
    required this.gym,
    required this.lookups,
    required this.onSubmit,
  });
  final Map<String, dynamic> gym;
  final Map<String, dynamic> lookups;
  final Future<void> Function(Map<String, Object?> body) onSubmit;

  @override
  State<_GymEditorDialog> createState() => _GymEditorDialogState();
}

class _GymEditorDialogState extends State<_GymEditorDialog> {
  final _formKey = GlobalKey<FormState>();
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
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  List<Map<String, dynamic>> _items(String key) =>
      (widget.lookups[key] as List? ?? const [])
          .whereType<Map>()
          .map((item) => Map<String, dynamic>.from(item))
          .where((item) => item['isActive'] == true)
          .toList();

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _address.dispose();
    _phone.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      await widget.onSubmit({
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
      });
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
    title: const Text('Uredi profil teretane'),
    content: SizedBox(
      width: 720,
      child: Form(
        key: _formKey,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextFormField(
                controller: _name,
                decoration: const InputDecoration(labelText: 'Naziv'),
                onChanged: (_) => _clearError('Name'),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.isEmpty) return 'Naziv je obavezan.';
                  if (text.length > 200) return 'Najviše 200 znakova.';
                  return _serverProblem?.fieldError('Name');
                },
              ),
              const SizedBox(height: 10),
              TextFormField(
                controller: _description,
                maxLines: 4,
                decoration: const InputDecoration(labelText: 'Opis'),
                onChanged: (_) => _clearError('Description'),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.isEmpty) return 'Opis je obavezan.';
                  if (text.length > 4000) return 'Najviše 4000 znakova.';
                  return _serverProblem?.fieldError('Description');
                },
              ),
              const SizedBox(height: 10),
              TextFormField(
                controller: _address,
                decoration: const InputDecoration(labelText: 'Adresa'),
                onChanged: (_) => _clearError('Address'),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.isEmpty) return 'Adresa je obavezna.';
                  if (text.length > 300) return 'Najviše 300 znakova.';
                  return _serverProblem?.fieldError('Address');
                },
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
                validator: (value) => value == null
                    ? 'Odaberite grad.'
                    : _serverProblem?.fieldError('CityId'),
              ),
              const SizedBox(height: 10),
              TextFormField(
                controller: _phone,
                decoration: const InputDecoration(labelText: 'Telefon'),
                onChanged: (_) => _clearError('PhoneNumber'),
                validator: (value) {
                  final text = value?.trim() ?? '';
                  if (text.length > 32) return 'Najviše 32 znaka.';
                  if (text.isNotEmpty &&
                      !RegExp(r'^\+?[0-9 ()-]+$').hasMatch(text)) {
                    return 'Unesite ispravan broj telefona.';
                  }
                  return _serverProblem?.fieldError('PhoneNumber');
                },
              ),
              const SizedBox(height: 18),
              const Text(
                'Oprema',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
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
              if (_formError != null) ...[
                const SizedBox(height: 10),
                Text(
                  _formError!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ],
            ],
          ),
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

String _date(Object? value) => DateFormat(
  'dd.MM.yyyy.',
).format(DateTime.parse(value.toString()).toLocal());
String _dateTime(Object? value) => DateFormat(
  'dd.MM.yyyy. HH:mm',
).format(DateTime.parse(value.toString()).toLocal());
