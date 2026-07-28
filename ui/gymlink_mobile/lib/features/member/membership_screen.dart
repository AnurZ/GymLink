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
        api.page('/api/me/membership-requests'),
        api.page('/api/me/memberships'),
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

  Future<void> _cancelMembership(Map<String, dynamic> item) async {
    if (!await confirmAction(
      context,
      title: 'Otkaži članstvo',
      message: 'Aktivno članstvo će biti otkazano.',
      action: 'Otkaži',
    )) {
      return;
    }
    await _mutate('/api/me/memberships/${item['id']}/cancel', {
      'concurrencyToken': item['concurrencyToken'],
    });
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
      child: _requests.isEmpty && _memberships.isEmpty
          ? ListView(
              children: const [
                SizedBox(
                  height: 500,
                  child: EmptyState(
                    title: 'Još nemate članstvo',
                    message: 'Otvorite teretanu i izaberite odgovarajući plan.',
                    icon: Icons.card_membership,
                  ),
                ),
              ],
            )
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text(
                  'Aktivna i prethodna članstva',
                  style: Theme.of(
                    context,
                  ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 10),
                ..._memberships.map(
                  (item) => Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Card(
                      child: ListTile(
                        title: Text(item['gymName'].toString()),
                        subtitle: Text(
                          '${item['planName']} · ${_date(item['startsAtUtc'])} – ${_date(item['endsAtUtc'])}',
                        ),
                        trailing: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            StatusPill(
                              enumLabel(item['status'], _membershipStatuses),
                            ),
                          ],
                        ),
                        onTap:
                            (item['allowedActions'] as List? ?? const [])
                                .contains('cancel')
                            ? () => _cancelMembership(item)
                            : null,
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
                            (item['allowedActions'] as List? ?? const [])
                                .contains('cancel')
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

  String _date(Object? value) => DateFormat(
    'dd.MM.yyyy.',
  ).format(DateTime.parse(value.toString()).toLocal());
}
