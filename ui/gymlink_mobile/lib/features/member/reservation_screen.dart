import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/payments.dart';
import '../../shared/widgets.dart';
import '../chat/chat_screens.dart';

const _reservationStatuses = ['Pending', 'Confirmed', 'Completed', 'Cancelled'];

class MemberReservationsScreen extends StatefulWidget {
  const MemberReservationsScreen({super.key});

  @override
  State<MemberReservationsScreen> createState() =>
      _MemberReservationsScreenState();
}

class _MemberReservationsScreenState extends State<MemberReservationsScreen> {
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
        '/api/me/reservations',
        query: {'status': _status},
      )).items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        DropdownButtonFormField<int?>(
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
        const SizedBox(height: 14),
        AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const SizedBox(
                  height: 420,
                  child: EmptyState(
                    title: 'Nema rezervacija',
                    message: 'Nema rezervacija za izabrani filter.',
                    icon: Icons.event_busy,
                  ),
                )
              : Column(
                  children: _items
                      .map(
                        (item) => Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Card(
                            child: ListTile(
                              leading: const CircleAvatar(
                                child: Icon(Icons.fitness_center),
                              ),
                              title: Text(
                                '${item['trainerName']} · ${item['gymName']}',
                              ),
                              subtitle: Text(
                                '${DateFormat('dd.MM.yyyy. HH:mm').format(DateTime.parse(item['startsAtUtc'].toString()).toLocal())}\n${item['offeringName']} · ${item['price']} ${item['currency']}',
                              ),
                              isThreeLine: true,
                              trailing: StatusPill(
                                enumLabel(item['status'], _reservationStatuses),
                              ),
                              onTap: () async {
                                await Navigator.push(
                                  context,
                                  MaterialPageRoute<void>(
                                    builder: (_) => ReservationDetailsScreen(
                                      reservationId: item['id'].toString(),
                                    ),
                                  ),
                                );
                                await _load();
                              },
                            ),
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

class ReservationDetailsScreen extends StatefulWidget {
  const ReservationDetailsScreen({required this.reservationId, super.key});
  final String reservationId;

  @override
  State<ReservationDetailsScreen> createState() =>
      _ReservationDetailsScreenState();
}

class _ReservationDetailsScreenState extends State<ReservationDetailsScreen> {
  Map<String, dynamic>? _item;
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
      _item = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/me/reservations/${widget.reservationId}',
            ))!
            as Map,
      );
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _cancel() async {
    if (!await confirmAction(
      context,
      title: 'Otkaži rezervaciju',
      message: 'Termin će ponovo postati dostupan.',
      action: 'Otkaži',
    )) {
      return;
    }
    await _command('/api/me/reservations/${widget.reservationId}/cancel', {
      'concurrencyToken': _item!['concurrencyToken'],
    });
  }

  Future<void> _review() async {
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => const TrainerReviewDialog(),
    );
    if (result == null) return;
    await _command('/api/reservations/${widget.reservationId}/review', result);
  }

  Future<void> _pay() async {
    setState(() => _paying = true);
    try {
      await openHostedCheckout(
        context.read<ApiClient>(),
        '/api/payments/reservations/${widget.reservationId}/checkout',
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

  Future<void> _command(String path, Map<String, Object?> body) async {
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
  Widget build(BuildContext context) => PageFrame(
    title: 'Detalji rezervacije',
    child: AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: _item == null
          ? const SizedBox.shrink()
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _item!['trainerName'].toString(),
                          style: Theme.of(context).textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        Text(
                          '${_item!['gymName']} · ${_item!['offeringName']}',
                        ),
                        const SizedBox(height: 14),
                        Text(
                          DateFormat('dd.MM.yyyy. HH:mm').format(
                            DateTime.parse(
                              _item!['startsAtUtc'].toString(),
                            ).toLocal(),
                          ),
                        ),
                        Text('${_item!['durationMinutes']} min'),
                        Text('${_item!['price']} ${_item!['currency']}'),
                        if (_item!['paymentDueAtUtc'] != null &&
                            _item!['isPaid'] != true)
                          Text(
                            'Platiti do ${DateFormat('HH:mm').format(DateTime.parse(_item!['paymentDueAtUtc'].toString()).toLocal())}',
                          ),
                        if (_item!['isPaid'] == true)
                          const Text(
                            'Plaćeno',
                            style: TextStyle(
                              color: Colors.green,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        const SizedBox(height: 14),
                        StatusPill(
                          enumLabel(_item!['status'], _reservationStatuses),
                        ),
                        if (_item!['cancellationReason'] != null) ...[
                          const SizedBox(height: 10),
                          Text('Razlog: ${_item!['cancellationReason']}'),
                        ],
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                if ((_item!['allowedActions'] as List? ?? const []).contains(
                  'pay',
                ))
                  FilledButton.icon(
                    onPressed: _paying ? null : _pay,
                    icon: _paying
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.payment),
                    label: const Text('Plati rezervaciju'),
                  ),
                if ((_item!['allowedActions'] as List? ?? const []).contains(
                  'cancel',
                ))
                  OutlinedButton.icon(
                    onPressed: _cancel,
                    icon: const Icon(Icons.cancel_outlined),
                    label: const Text('Otkaži rezervaciju'),
                  ),
                if (_item!['canReview'] == true)
                  FilledButton.icon(
                    onPressed: _review,
                    icon: const Icon(Icons.star_outline),
                    label: const Text('Ocijeni trenera'),
                  ),
                if (((_item!['status'] as num?)?.toInt() == 1) ||
                    ((_item!['status'] as num?)?.toInt() == 2))
                  OutlinedButton.icon(
                    key: const Key('member-open-chat'),
                    onPressed: () =>
                        openChatForReservation(context, widget.reservationId),
                    icon: const Icon(Icons.forum_outlined),
                    label: const Text('Otvori razgovor'),
                  ),
              ],
            ),
    ),
  );
}

class TrainerReviewDialog extends StatefulWidget {
  const TrainerReviewDialog({super.key});

  @override
  State<TrainerReviewDialog> createState() => _TrainerReviewDialogState();
}

class _TrainerReviewDialogState extends State<TrainerReviewDialog> {
  final _comment = TextEditingController();
  int _rating = 5;

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Ocijenite trenera'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SegmentedButton<int>(
          segments: List.generate(
            5,
            (index) =>
                ButtonSegment(value: index + 1, label: Text('${index + 1}')),
          ),
          selected: {_rating},
          onSelectionChanged: (values) {
            setState(() => _rating = values.first);
          },
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _comment,
          maxLines: 4,
          maxLength: 2000,
          decoration: const InputDecoration(labelText: 'Komentar'),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: () {
          final comment = _comment.text.trim();
          Navigator.pop(context, {
            'rating': _rating,
            'comment': comment.isEmpty ? null : comment,
          });
        },
        child: const Text('Objavi'),
      ),
    ],
  );
}
