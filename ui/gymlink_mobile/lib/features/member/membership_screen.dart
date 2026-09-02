import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/payments.dart';
import '../../shared/widgets.dart';
import 'gym_screens.dart';

const _requestStatuses = ['Pending', 'Approved', 'Rejected', 'Cancelled'];
const _visibleRequestStatuses = [0, 1, 2, 3];
const _membershipStatuses = [
  'PendingPayment',
  'Active',
  'Expired',
  'Cancelled',
  'Suspended',
];
const _visibleMembershipStatuses = [0, 1, 2, 3, 4];

class MembershipScreen extends StatefulWidget {
  const MembershipScreen({super.key});

  @override
  State<MembershipScreen> createState() => _MembershipScreenState();
}

class _MembershipScreenState extends State<MembershipScreen> {
  List<Map<String, dynamic>> _requests = const [];
  List<Map<String, dynamic>> _memberships = const [];
  bool _loading = true;
  Object? _error;
  int? _requestStatus;
  int? _membershipStatus;

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
        api.page(
          '/api/me/membership-requests',
          query: {'status': _requestStatus},
        ),
        api.page('/api/me/memberships', query: {'status': _membershipStatus}),
      ]);
      _requests = results[0].items;
      _memberships = results[1].items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _cancelRequest(Map<String, dynamic> item) async {
    if (!await confirmAction(
      context,
      title: 'Otkaži zahtjev',
      message: 'Zahtjev će biti otkazan.',
      action: 'Otkaži',
    )) {
      return;
    }
    await _mutate('/api/membership-requests/${item['id']}/cancel', {
      'concurrencyToken': item['concurrencyToken'],
    });
  }

  Future<void> _openMembership(Map<String, dynamic> item) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) =>
            MembershipDetailsScreen(membershipId: item['id'].toString()),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _mutate(String path, Map<String, Object?> body) async {
    try {
      await context.read<ApiClient>().post(path, body: body);
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
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            'Aktivna i prethodna članstva',
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<int?>(
            key: const Key('membership-status-filter'),
            initialValue: _membershipStatus,
            decoration: const InputDecoration(labelText: 'Status članstva'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ..._visibleMembershipStatuses.map(
                (status) => DropdownMenuItem(
                  value: status,
                  child: Text(enumLabel(status, _membershipStatuses)),
                ),
              ),
            ],
            onChanged: (value) {
              _membershipStatus = value;
              _load();
            },
          ),
          const SizedBox(height: 10),
          if (_memberships.isEmpty)
            SizedBox(
              height: 220,
              child: EmptyState(
                title: _membershipStatus == null
                    ? 'Još nemate članstvo'
                    : 'Nema članstava',
                message: _membershipStatus == null
                    ? 'Otvorite teretanu i izaberite odgovarajući plan.'
                    : 'Nema članstava za izabrani status.',
                icon: Icons.card_membership,
              ),
            )
          else
            ..._memberships.map(
              (item) => Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Card(
                  child: ListTile(
                    title: Text(item['gymName'].toString()),
                    subtitle: Text(
                      '${item['planName']} · ${_date(item['startsAtUtc'])} – ${_date(item['endsAtUtc'])}',
                    ),
                    trailing: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        StatusPill(
                          enumLabel(item['status'], _membershipStatuses),
                        ),
                        const SizedBox(width: 6),
                        const Icon(Icons.chevron_right),
                      ],
                    ),
                    onTap: () => _openMembership(item),
                  ),
                ),
              ),
            ),
          const SizedBox(height: 18),
          Text(
            'Zahtjevi',
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<int?>(
            key: const Key('membership-request-status-filter'),
            initialValue: _requestStatus,
            decoration: const InputDecoration(labelText: 'Status zahtjeva'),
            items: [
              const DropdownMenuItem(value: null, child: Text('Svi statusi')),
              ..._visibleRequestStatuses.map(
                (status) => DropdownMenuItem(
                  value: status,
                  child: Text(enumLabel(status, _requestStatuses)),
                ),
              ),
            ],
            onChanged: (value) {
              _requestStatus = value;
              _load();
            },
          ),
          const SizedBox(height: 10),
          if (_requests.isEmpty)
            SizedBox(
              height: 220,
              child: EmptyState(
                title: _requestStatus == null
                    ? 'Još nemate zahtjeva'
                    : 'Nema zahtjeva',
                message: _requestStatus == null
                    ? 'Zahtjevi će se prikazati nakon izbora plana članstva.'
                    : 'Nema zahtjeva za izabrani status.',
                icon: Icons.assignment_outlined,
              ),
            )
          else
            ..._requests.map(
              (item) => Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Card(
                  child: ListTile(
                    title: Text('${item['gymName']} · ${item['planName']}'),
                    subtitle: Text('${item['price']} ${item['currency']}'),
                    trailing: StatusPill(
                      enumLabel(item['status'], _requestStatuses),
                    ),
                    onTap:
                        (item['allowedActions'] as List? ?? const []).contains(
                          'cancel',
                        )
                        ? () => _cancelRequest(item)
                        : null,
                  ),
                ),
              ),
            ),
        ],
      ),
    ),
  );

  String _date(Object? value) => value == null
      ? 'Nakon plaćanja'
      : DateFormat(
          'dd.MM.yyyy.',
        ).format(DateTime.parse(value.toString()).toLocal());
}

class MembershipDetailsScreen extends StatefulWidget {
  const MembershipDetailsScreen({required this.membershipId, super.key});

  final String membershipId;

  @override
  State<MembershipDetailsScreen> createState() =>
      _MembershipDetailsScreenState();
}

class _MembershipDetailsScreenState extends State<MembershipDetailsScreen> {
  Map<String, dynamic>? _membership;
  bool _loading = true;
  bool _paying = false;
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
      _membership = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/me/memberships/${widget.membershipId}',
            ))!
            as Map,
      );
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pay() async {
    final api = context.read<ApiClient>();
    setState(() => _paying = true);
    try {
      await openHostedCheckout(
        api,
        '/api/payments/memberships/${widget.membershipId}/checkout',
      );
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } on StateError catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _paying = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final membership = _membership;
    return Scaffold(
      appBar: AppBar(title: const Text('Detalji članstva')),
      body: AsyncPanel(
        loading: _loading,
        error: _error,
        onRetry: _load,
        child: membership == null
            ? const SizedBox.shrink()
            : ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(18),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Expanded(
                                child: Text(
                                  membership['gymName'].toString(),
                                  style: Theme.of(context).textTheme.titleLarge
                                      ?.copyWith(fontWeight: FontWeight.w800),
                                ),
                              ),
                              StatusPill(
                                enumLabel(
                                  membership['status'],
                                  _membershipStatuses,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 18),
                          _DetailRow(
                            label: 'Plan',
                            value: membership['planName'].toString(),
                          ),
                          _DetailRow(
                            label: 'Cijena',
                            value:
                                '${membership['price']} ${membership['currency']}',
                          ),
                          _DetailRow(
                            label: 'Početak',
                            value: _date(membership['startsAtUtc']),
                          ),
                          _DetailRow(
                            label: 'Važi do',
                            value: _date(membership['endsAtUtc']),
                          ),
                          if (membership['statusReason'] != null)
                            _DetailRow(
                              label: 'Razlog promjene',
                              value: membership['statusReason'].toString(),
                            ),
                        ],
                      ),
                    ),
                  ),
                  if ((membership['gymId']?.toString().trim() ?? '')
                      .isNotEmpty) ...[
                    const SizedBox(height: 16),
                    OutlinedButton.icon(
                      key: const Key('membership-open-gym'),
                      onPressed: () => Navigator.push<void>(
                        context,
                        MaterialPageRoute(
                          builder: (_) => GymDetailsScreen(
                            gymId: membership['gymId'].toString(),
                          ),
                        ),
                      ),
                      icon: const Icon(Icons.fitness_center),
                      label: const Text('Otvori teretanu'),
                    ),
                  ],
                  if ((membership['allowedActions'] as List? ?? const [])
                      .contains('pay')) ...[
                    const SizedBox(height: 16),
                    FilledButton.icon(
                      onPressed: _paying ? null : _pay,
                      icon: _paying
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.payment),
                      label: const Text('Plati članarinu'),
                    ),
                  ],
                ],
              ),
      ),
    );
  }

  String _date(Object? value) => value == null
      ? 'Nakon plaćanja'
      : DateFormat(
          'dd.MM.yyyy.',
        ).format(DateTime.parse(value.toString()).toLocal());
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 120,
          child: Text(label, style: const TextStyle(color: Colors.blueGrey)),
        ),
        Expanded(
          child: Text(
            value,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}
