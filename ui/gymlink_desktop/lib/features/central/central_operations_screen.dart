import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

const _requestStatuses = ['Na čekanju', 'Odobren', 'Odbijen', 'Otkazan'];
const _membershipStatuses = [
  'Čeka plaćanje',
  'Aktivno',
  'Isteklo',
  'Otkazano',
  'Suspendovano',
];
const _reservationStatuses = [
  'Na čekanju',
  'Potvrđena',
  'Završena',
  'Otkazana',
];
const _paymentStatuses = ['Kreirano', 'U obradi', 'Uspješno', 'Neuspješno'];
const _tenantStatuses = [
  'Čeka aktivaciju',
  'Aktivna',
  'Neaktivna',
  'Suspendovana',
];

class CentralOperationsScreen extends StatefulWidget {
  const CentralOperationsScreen({super.key});

  @override
  State<CentralOperationsScreen> createState() =>
      _CentralOperationsScreenState();
}

class _CentralOperationsScreenState extends State<CentralOperationsScreen> {
  Map<String, dynamic>? _gym;

  Future<void> _selectGym() async {
    final selected = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => const _GymPickerDialog(),
    );
    if (selected != null && mounted) setState(() => _gym = selected);
  }

  @override
  Widget build(BuildContext context) {
    final gym = _gym;
    return DefaultTabController(
      length: 2,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Row(
                children: [
                  Expanded(
                    child: gym == null
                        ? const Text(
                            'Odaberite teretanu za pregled operativnih zapisa.',
                          )
                        : Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                gym['name'].toString(),
                                style: Theme.of(context).textTheme.titleMedium
                                    ?.copyWith(fontWeight: FontWeight.w800),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                '${gym['address']}, ${gym['cityName']}',
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                              Text(
                                enumLabel(gym['status'], _tenantStatuses),
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                            ],
                          ),
                  ),
                  const SizedBox(width: 16),
                  FilledButton.tonalIcon(
                    key: const Key('central-select-gym'),
                    onPressed: _selectGym,
                    icon: const Icon(Icons.apartment_outlined),
                    label: Text(
                      gym == null ? 'Odaberi teretanu' : 'Promijeni teretanu',
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),
          const TabBar(
            tabs: [
              Tab(text: 'Članarine', icon: Icon(Icons.card_membership)),
              Tab(text: 'Rezervacije', icon: Icon(Icons.event_available)),
            ],
          ),
          const SizedBox(height: 12),
          Expanded(
            child: gym == null
                ? const EmptyState('Prvo odaberite teretanu.')
                : TabBarView(
                    key: ValueKey(gym['id']),
                    children: [
                      _CentralMembershipsTab(gymId: gym['id'].toString()),
                      _CentralReservationsTab(gymId: gym['id'].toString()),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

class _GymPickerDialog extends StatefulWidget {
  const _GymPickerDialog();

  @override
  State<_GymPickerDialog> createState() => _GymPickerDialogState();
}

class _GymPickerDialogState extends State<_GymPickerDialog> {
  static const _pageSize = 20;
  final _search = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  int _page = 1;
  int _totalCount = 0;
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
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final page = await context.read<ApiClient>().page(
        '/api/admin/gyms',
        query: {
          'query': _search.text.trim(),
          'page': _page,
          'pageSize': _pageSize,
        },
      );
      if (!mounted) return;
      setState(() {
        _items = page.items;
        _totalCount = page.totalCount;
      });
    } catch (error) {
      if (mounted) setState(() => _error = error);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => GymLinkDialog(
    title: const Text('Odaberite teretanu'),
    content: SizedBox(
      width: 680,
      height: 500,
      child: Column(
        children: [
          TextField(
            key: const Key('central-gym-search'),
            controller: _search,
            onSubmitted: (_) {
              _page = 1;
              _load();
            },
            decoration: InputDecoration(
              labelText: 'Naziv, adresa ili grad',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: IconButton(
                tooltip: 'Pretraži',
                onPressed: () {
                  _page = 1;
                  _load();
                },
                icon: const Icon(Icons.arrow_forward),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Expanded(
            child: AsyncPanel(
              loading: _loading,
              error: _error,
              onRetry: _load,
              child: _items.isEmpty
                  ? const EmptyState('Nema teretana za unesenu pretragu.')
                  : ListView.separated(
                      itemCount: _items.length,
                      separatorBuilder: (_, _) => const Divider(height: 1),
                      itemBuilder: (_, index) {
                        final item = _items[index];
                        return ListTile(
                          key: Key('central-gym-${item['id']}'),
                          title: Text(item['name'].toString()),
                          subtitle: Text(
                            '${item['address']}, ${item['cityName']}',
                          ),
                          trailing: StatusPill(
                            enumLabel(item['status'], _tenantStatuses),
                          ),
                          onTap: () => Navigator.pop(context, item),
                        );
                      },
                    ),
            ),
          ),
          _Pager(
            page: _page,
            pageSize: _pageSize,
            totalCount: _totalCount,
            onPage: (page) {
              setState(() => _page = page);
              _load();
            },
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
    ],
  );
}

class _CentralMembershipsTab extends StatefulWidget {
  const _CentralMembershipsTab({required this.gymId});
  final String gymId;

  @override
  State<_CentralMembershipsTab> createState() => _CentralMembershipsTabState();
}

class _CentralMembershipsTabState extends State<_CentralMembershipsTab> {
  static const _pageSize = 20;
  final _member = TextEditingController();
  List<Map<String, dynamic>> _items = const [];
  int? _requestStatus;
  int? _paymentCategory;
  int? _membershipStatus;
  int _page = 1;
  int _totalCount = 0;
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
    _member.dispose();
    super.dispose();
  }

  Future<void> _load({bool preserveData = false}) async {
    setState(() {
      if (preserveData && _items.isNotEmpty) {
        _refreshing = true;
      } else {
        _loading = true;
      }
      _error = null;
    });
    try {
      final result = await context.read<ApiClient>().page(
        '/api/admin/gyms/${widget.gymId}/membership-requests',
        query: {
          'member': _member.text.trim(),
          'status': _requestStatus,
          'paymentCategory': _paymentCategory,
          'membershipStatus': _membershipStatus,
          'page': _page,
          'pageSize': _pageSize,
        },
      );
      if (!mounted) return;
      setState(() {
        _items = result.items;
        _totalCount = result.totalCount;
      });
    } catch (error) {
      if (!mounted) return;
      if (preserveData && _items.isNotEmpty) {
        _show('Osvježavanje nije uspjelo. Prikazani su prethodni podaci.');
      } else {
        setState(() => _error = error);
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

  Future<void> _confirmCash(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Potvrda uplate uživo',
      message:
          'Potvrdite da je članarina korisnika ${item['memberDisplayName']} naplaćena uživo.',
      action: 'Potvrdi uplatu',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/admin/gyms/${widget.gymId}/membership-requests/${item['id']}/confirm-cash',
        body: {'concurrencyToken': item['concurrencyToken']},
      );
      await _load(preserveData: true);
      _show('Uplata uživo je potvrđena i članstvo je aktivirano.');
    } on ApiProblem catch (error) {
      _show(error.message);
      if (error.status == 409) await _load(preserveData: true);
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Wrap(
        spacing: 12,
        runSpacing: 12,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          SizedBox(
            width: 250,
            child: TextField(
              key: const Key('central-membership-member-filter'),
              controller: _member,
              onSubmitted: (_) {
                _page = 1;
                _load();
              },
              decoration: const InputDecoration(
                labelText: 'Član',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          _FilterDropdown(
            key: const Key('central-membership-request-status-filter'),
            label: 'Status zahtjeva',
            allLabel: 'Svi zahtjevi',
            value: _requestStatus,
            values: _requestStatuses,
            onChanged: (value) {
              _requestStatus = value;
              _page = 1;
              _load();
            },
          ),
          _FilterDropdown(
            key: const Key('central-membership-payment-filter'),
            label: 'Način plaćanja',
            allLabel: 'Sve metode',
            value: _paymentCategory,
            values: const ['Stripe', 'Plati uživo'],
            onChanged: (value) {
              _paymentCategory = value;
              _page = 1;
              _load();
            },
          ),
          _FilterDropdown(
            key: const Key('central-membership-status-filter'),
            label: 'Status članstva',
            allLabel: 'Sva članstva',
            value: _membershipStatus,
            values: _membershipStatuses,
            onChanged: (value) {
              _membershipStatus = value;
              _page = 1;
              _load();
            },
          ),
          IconButton.filledTonal(
            key: const Key('central-memberships-refresh'),
            tooltip: 'Osvježi',
            onPressed: _refreshing ? null : () => _load(preserveData: true),
            icon: _refreshing
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.refresh),
          ),
        ],
      ),
      const SizedBox(height: 12),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema članarina za izabrane filtere.')
              : Card(
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        columns: const [
                          DataColumn(label: Text('Član')),
                          DataColumn(label: Text('Plan i iznos')),
                          DataColumn(label: Text('Način plaćanja')),
                          DataColumn(label: Text('Status plaćanja')),
                          DataColumn(label: Text('Zahtjev')),
                          DataColumn(label: Text('Članstvo')),
                          DataColumn(label: Text('Datum')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final membership = item['membership'] is Map
                              ? Map<String, dynamic>.from(
                                  item['membership'] as Map,
                                )
                              : null;
                          final actions =
                              (item['allowedActions'] as List? ?? [])
                                  .map((value) => value.toString())
                                  .toSet();
                          return DataRow(
                            cells: [
                              DataCell(
                                _TwoLines(
                                  item['memberDisplayName'],
                                  item['memberEmail'],
                                ),
                              ),
                              DataCell(
                                _TwoLines(
                                  item['planName'],
                                  '${item['price']} ${item['currency']}',
                                ),
                              ),
                              DataCell(Text(_membershipPaymentMethod(item))),
                              DataCell(Text(_membershipPaymentStatus(item))),
                              DataCell(
                                StatusPill(
                                  enumLabel(item['status'], _requestStatuses),
                                ),
                              ),
                              DataCell(
                                Text(
                                  membership == null
                                      ? '—'
                                      : enumLabel(
                                          membership['status'],
                                          _membershipStatuses,
                                        ),
                                ),
                              ),
                              DataCell(Text(_date(item['requestedAtUtc']))),
                              DataCell(
                                actions.contains('confirmCashPayment')
                                    ? IconButton(
                                        key: Key(
                                          'central-confirm-cash-${item['id']}',
                                        ),
                                        tooltip: 'Potvrdi uplatu uživo',
                                        onPressed: () => _confirmCash(item),
                                        icon: const Icon(
                                          Icons.payments_outlined,
                                          color: Colors.green,
                                        ),
                                      )
                                    : const SizedBox.shrink(),
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
      _Pager(
        page: _page,
        pageSize: _pageSize,
        totalCount: _totalCount,
        onPage: (page) {
          setState(() => _page = page);
          _load();
        },
      ),
    ],
  );
}

class _CentralReservationsTab extends StatefulWidget {
  const _CentralReservationsTab({required this.gymId});
  final String gymId;

  @override
  State<_CentralReservationsTab> createState() =>
      _CentralReservationsTabState();
}

class _CentralReservationsTabState extends State<_CentralReservationsTab> {
  static const _pageSize = 20;
  List<Map<String, dynamic>> _items = const [];
  int? _status;
  int _page = 1;
  int _totalCount = 0;
  bool _loading = true;
  bool _refreshing = false;
  Object? _error;

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
      _error = null;
    });
    try {
      final result = await context.read<ApiClient>().page(
        '/api/admin/gyms/${widget.gymId}/reservations',
        query: {'status': _status, 'page': _page, 'pageSize': _pageSize},
      );
      if (!mounted) return;
      setState(() {
        _items = result.items;
        _totalCount = result.totalCount;
      });
    } catch (error) {
      if (!mounted) return;
      if (preserveData && _items.isNotEmpty) {
        _show('Osvježavanje nije uspjelo. Prikazani su prethodni podaci.');
      } else {
        setState(() => _error = error);
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

  Future<void> _complete(Map<String, dynamic> item) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Završetak rezervacije',
      message:
          'Želite li označiti potvrđenu rezervaciju korisnika ${item['memberName']} završenom?',
      action: 'Označi završenom',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/admin/gyms/${widget.gymId}/reservations/${item['id']}/complete',
        body: {'concurrencyToken': item['concurrencyToken']},
      );
      await _load(preserveData: true);
      _show('Rezervacija je označena završenom.');
    } on ApiProblem catch (error) {
      _show(error.message);
      if (error.status == 409) await _load(preserveData: true);
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Align(
        alignment: Alignment.centerLeft,
        child: Wrap(
          spacing: 12,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            _FilterDropdown(
              key: const Key('central-reservation-status-filter'),
              label: 'Status rezervacije',
              allLabel: 'Svi statusi',
              value: _status,
              values: _reservationStatuses,
              onChanged: (value) {
                _status = value;
                _page = 1;
                _load();
              },
            ),
            IconButton.filledTonal(
              key: const Key('central-reservations-refresh'),
              tooltip: 'Osvježi',
              onPressed: _refreshing ? null : () => _load(preserveData: true),
              icon: _refreshing
                  ? const SizedBox.square(
                      dimension: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.refresh),
            ),
          ],
        ),
      ),
      const SizedBox(height: 12),
      Expanded(
        child: AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const EmptyState('Nema rezervacija za izabrani status.')
              : Card(
                  child: SingleChildScrollView(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: DataTable(
                        columns: const [
                          DataColumn(label: Text('Član')),
                          DataColumn(label: Text('Trener')),
                          DataColumn(label: Text('Usluga i termin')),
                          DataColumn(label: Text('Način plaćanja')),
                          DataColumn(label: Text('Status plaćanja')),
                          DataColumn(label: Text('Rezervacija')),
                          DataColumn(label: Text('Akcije')),
                        ],
                        rows: _items.map((item) {
                          final actions =
                              (item['allowedActions'] as List? ?? [])
                                  .map((value) => value.toString())
                                  .toSet();
                          return DataRow(
                            cells: [
                              DataCell(Text(item['memberName'].toString())),
                              DataCell(Text(item['trainerName'].toString())),
                              DataCell(
                                _TwoLines(
                                  item['offeringName'],
                                  _dateTime(item['startsAtUtc']),
                                ),
                              ),
                              DataCell(Text(_reservationPaymentMethod(item))),
                              DataCell(Text(_reservationPaymentStatus(item))),
                              DataCell(
                                StatusPill(
                                  enumLabel(
                                    item['status'],
                                    _reservationStatuses,
                                  ),
                                ),
                              ),
                              DataCell(
                                actions.contains('complete')
                                    ? IconButton(
                                        key: Key(
                                          'central-complete-reservation-${item['id']}',
                                        ),
                                        tooltip: 'Označi završenom',
                                        onPressed: () => _complete(item),
                                        icon: const Icon(
                                          Icons.task_alt,
                                          color: Colors.green,
                                        ),
                                      )
                                    : const SizedBox.shrink(),
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
      _Pager(
        page: _page,
        pageSize: _pageSize,
        totalCount: _totalCount,
        onPage: (page) {
          setState(() => _page = page);
          _load();
        },
      ),
    ],
  );
}

class _FilterDropdown extends StatelessWidget {
  const _FilterDropdown({
    required this.label,
    required this.allLabel,
    required this.value,
    required this.values,
    required this.onChanged,
    super.key,
  });

  final String label;
  final String allLabel;
  final int? value;
  final List<String> values;
  final ValueChanged<int?> onChanged;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 190,
    child: DropdownButtonFormField<int?>(
      initialValue: value,
      isExpanded: true,
      decoration: InputDecoration(labelText: label),
      items: [
        DropdownMenuItem(value: null, child: Text(allLabel)),
        ...List.generate(
          values.length,
          (index) => DropdownMenuItem(
            value: index,
            child: Text(values[index], overflow: TextOverflow.ellipsis),
          ),
        ),
      ],
      onChanged: onChanged,
    ),
  );
}

class _Pager extends StatelessWidget {
  const _Pager({
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.onPage,
  });

  final int page;
  final int pageSize;
  final int totalCount;
  final ValueChanged<int> onPage;

  @override
  Widget build(BuildContext context) {
    if (totalCount <= pageSize) return const SizedBox.shrink();
    final totalPages = (totalCount / pageSize).ceil();
    return Row(
      mainAxisAlignment: MainAxisAlignment.end,
      children: [
        Text('Stranica $page od $totalPages'),
        IconButton(
          tooltip: 'Prethodna stranica',
          onPressed: page <= 1 ? null : () => onPage(page - 1),
          icon: const Icon(Icons.chevron_left),
        ),
        IconButton(
          tooltip: 'Sljedeća stranica',
          onPressed: page >= totalPages ? null : () => onPage(page + 1),
          icon: const Icon(Icons.chevron_right),
        ),
      ],
    );
  }
}

class _TwoLines extends StatelessWidget {
  const _TwoLines(this.primary, this.secondary);
  final Object? primary;
  final Object? secondary;

  @override
  Widget build(BuildContext context) => Column(
    mainAxisAlignment: MainAxisAlignment.center,
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(primary?.toString() ?? '—'),
      Text(
        secondary?.toString() ?? '—',
        style: Theme.of(context).textTheme.bodySmall,
      ),
    ],
  );
}

String _membershipPaymentMethod(Map<String, dynamic> item) =>
    _isPayInPerson(item['paymentMethod']) ? 'Plati uživo' : 'Stripe';

String _membershipPaymentStatus(Map<String, dynamic> item) {
  if (_isPayInPerson(item['paymentMethod'])) {
    final status = _enumIndex(item['status'], const [
      'Pending',
      'Approved',
      'Rejected',
      'Cancelled',
    ]);
    return status == 0
        ? 'Čeka potvrdu uplate'
        : status == 1
        ? 'Uplata potvrđena'
        : 'Uplata nije potvrđena';
  }
  final membership = item['membership'];
  final paymentStatus = membership is Map ? membership['paymentStatus'] : null;
  return paymentStatus == null
      ? 'Nije pokrenuto'
      : enumLabel(paymentStatus, _paymentStatuses);
}

String _reservationPaymentMethod(Map<String, dynamic> item) =>
    _isReservationPayInPerson(item['paymentMethod']) ? 'Plati uživo' : 'Stripe';

String _reservationPaymentStatus(Map<String, dynamic> item) {
  if (_isReservationPayInPerson(item['paymentMethod'])) {
    return 'Plaćanje uživo — bez online zapisa';
  }
  return item['paymentStatus'] == null
      ? 'Nije pokrenuto'
      : enumLabel(item['paymentStatus'], _paymentStatuses);
}

bool _isPayInPerson(Object? value) => value is num
    ? value.toInt() == 2
    : value?.toString().replaceAll('_', '').toLowerCase() == 'payinperson';

bool _isReservationPayInPerson(Object? value) => value is num
    ? value.toInt() == 1
    : value?.toString().replaceAll('_', '').toLowerCase() == 'payinperson';

int? _enumIndex(Object? value, List<String> names) {
  if (value is num) return value.toInt();
  final normalized = value?.toString().toLowerCase();
  final index = names.indexWhere((name) => name.toLowerCase() == normalized);
  return index < 0 ? null : index;
}

String _date(Object? value) {
  final parsed = DateTime.tryParse(value?.toString() ?? '');
  return parsed == null
      ? '—'
      : DateFormat('dd.MM.yyyy').format(parsed.toLocal());
}

String _dateTime(Object? value) {
  final parsed = DateTime.tryParse(value?.toString() ?? '');
  return parsed == null
      ? '—'
      : DateFormat('dd.MM.yyyy HH:mm').format(parsed.toLocal());
}
