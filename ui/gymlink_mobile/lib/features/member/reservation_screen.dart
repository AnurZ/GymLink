import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';
import '../chat/chat_screens.dart';
import '../reservations/reservation_refresh_controller.dart';

const _reservationStatuses = ['Pending', 'Confirmed', 'Completed', 'Cancelled'];
const _visibleReservationStatuses = [1, 2, 3];

class MemberReservationsScreen extends StatefulWidget {
  const MemberReservationsScreen({this.controller, super.key});

  final ReservationRefreshController? controller;

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
    widget.controller?.addListener(_refreshRequested);
    _load();
  }

  @override
  void didUpdateWidget(covariant MemberReservationsScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller == widget.controller) return;
    oldWidget.controller?.removeListener(_refreshRequested);
    widget.controller?.addListener(_refreshRequested);
  }

  void _refreshRequested() => _load();

  @override
  void dispose() {
    widget.controller?.removeListener(_refreshRequested);
    super.dispose();
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
                              leading: TrainerImageAvatar(
                                name: item['trainerName'].toString(),
                                imageUrl: context.read<ApiClient>().mediaUrl(
                                  item['trainerImageUrl'],
                                ),
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
      message:
          'Rezervacija će biti otkazana. Ovu radnju nije moguće poništiti.',
      action: 'Otkaži',
    )) {
      return;
    }
    await _command('/api/me/reservations/${widget.reservationId}/cancel', {
      'concurrencyToken': _item!['concurrencyToken'],
    });
  }

  Future<void> _review() async {
    final api = context.read<ApiClient>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => TrainerReviewDialog(
        onSubmit: (body) => api.post(
          '/api/reservations/${widget.reservationId}/review',
          body: body,
        ),
      ),
    );
    if (saved == true) await _load();
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
                        Text(
                          (_item!['paymentMethod'] as num?)?.toInt() == 1
                              ? 'Način plaćanja: uživo'
                              : 'Način plaćanja: Stripe',
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
  const TrainerReviewDialog({required this.onSubmit, super.key});

  final Future<void> Function(Map<String, Object?> body) onSubmit;

  @override
  State<TrainerReviewDialog> createState() => _TrainerReviewDialogState();
}

class _TrainerReviewDialogState extends State<TrainerReviewDialog> {
  final _formKey = GlobalKey<FormState>();
  final _comment = TextEditingController();
  int _rating = 5;
  ApiProblem? _serverProblem;
  String? _formError;
  bool _saving = false;

  @override
  void dispose() {
    _comment.dispose();
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
      final comment = _comment.text.trim();
      await widget.onSubmit({
        'rating': _rating,
        'comment': comment.isEmpty ? null : comment,
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

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Ocijenite trenera'),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          SegmentedButton<int>(
            segments: List.generate(
              5,
              (index) =>
                  ButtonSegment(value: index + 1, label: Text('${index + 1}')),
            ),
            selected: {_rating},
            onSelectionChanged: _saving
                ? null
                : (values) {
                    setState(() => _rating = values.first);
                  },
          ),
          if (_serverProblem?.fieldError('Rating') case final error?)
            Text(
              error,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _comment,
            maxLines: 4,
            maxLength: 2000,
            decoration: const InputDecoration(labelText: 'Komentar'),
            onChanged: (_) => setState(() => _serverProblem = null),
            validator: (value) {
              if ((value?.trim().length ?? 0) > 2000) {
                return 'Najviše 2000 znakova.';
              }
              return _serverProblem?.fieldError('Comment');
            },
          ),
          if (_formError != null)
            Text(
              _formError!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: _saving ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _saving ? null : _submit,
        child: Text(_saving ? 'Objavljivanje...' : 'Objavi'),
      ),
    ],
  );
}
