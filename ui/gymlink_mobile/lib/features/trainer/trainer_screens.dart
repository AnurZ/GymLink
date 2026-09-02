import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../shared/widgets.dart';
import '../chat/chat_screens.dart';
import '../reservations/reservation_refresh_controller.dart';

const _reservationStatuses = ['Pending', 'Confirmed', 'Completed', 'Cancelled'];
const _visibleTrainerReservationStatuses = [1, 2, 3];
const _emptyGuid = '00000000-0000-0000-0000-000000000000';

bool _canOpenAppointmentChat(Object? status) {
  final label = enumLabel(status, _reservationStatuses);
  return label == 'Confirmed' || label == 'Completed';
}

class TrainerAppointmentsScreen extends StatefulWidget {
  const TrainerAppointmentsScreen({this.controller, super.key});

  final ReservationRefreshController? controller;

  @override
  State<TrainerAppointmentsScreen> createState() =>
      _TrainerAppointmentsScreenState();
}

class _TrainerAppointmentsScreenState extends State<TrainerAppointmentsScreen> {
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
  void didUpdateWidget(covariant TrainerAppointmentsScreen oldWidget) {
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
        '/api/me/trainer-reservations',
        query: {
          'status': _status,
          'fromUtc': DateTime.now()
              .toUtc()
              .subtract(const Duration(days: 30))
              .toIso8601String(),
        },
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
          key: const Key('trainer-appointment-status-filter'),
          initialValue: _status,
          decoration: const InputDecoration(labelText: 'Status'),
          items: [
            const DropdownMenuItem(value: null, child: Text('Svi statusi')),
            ..._visibleTrainerReservationStatuses.map(
              (status) => DropdownMenuItem(
                value: status,
                child: Text(enumLabel(status, _reservationStatuses)),
              ),
            ),
          ],
          onChanged: (value) {
            _status = value;
            _load();
          },
        ),
        const SizedBox(height: 14),
        const _AppointmentSortHint(),
        const SizedBox(height: 10),
        AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? SizedBox(
                  height: 420,
                  child: EmptyState(
                    title: 'Nema termina',
                    message: _status == null
                        ? 'Trenutno nemate zakazanih termina.'
                        : 'Nema termina za izabrani status.',
                    icon: Icons.event_available,
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
                                child: Icon(Icons.person),
                              ),
                              title: Text(item['memberName'].toString()),
                              subtitle: Text(
                                '${DateFormat('dd.MM.yyyy. HH:mm').format(DateTime.parse(item['startsAtUtc'].toString()).toLocal())}\n${item['offeringName']}',
                              ),
                              isThreeLine: true,
                              trailing: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  StatusPill(
                                    enumLabel(
                                      item['status'],
                                      _reservationStatuses,
                                    ),
                                  ),
                                  if (_canOpenAppointmentChat(item['status']))
                                    IconButton(
                                      key: Key(
                                        'trainer-appointment-chat-${item['id']}',
                                      ),
                                      tooltip: 'Otvori razgovor',
                                      onPressed: () => openChatForReservation(
                                        context,
                                        item['id'].toString(),
                                      ),
                                      icon: const Icon(Icons.forum_outlined),
                                    ),
                                ],
                              ),
                              onTap: () async {
                                await Navigator.push(
                                  context,
                                  MaterialPageRoute<void>(
                                    builder: (_) =>
                                        TrainerAppointmentDetails(item: item),
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

class _AppointmentSortHint extends StatelessWidget {
  const _AppointmentSortHint();

  @override
  Widget build(BuildContext context) => Row(
    key: const Key('trainer-appointments-sort-hint'),
    children: [
      Icon(Icons.sort, size: 18, color: Theme.of(context).colorScheme.outline),
      const SizedBox(width: 8),
      Expanded(
        child: Text(
          'Sortirano po datumu: najnoviji termini prvo.',
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ),
    ],
  );
}

class TrainerAppointmentDetails extends StatefulWidget {
  const TrainerAppointmentDetails({required this.item, super.key});
  final Map<String, dynamic> item;

  @override
  State<TrainerAppointmentDetails> createState() =>
      _TrainerAppointmentDetailsState();
}

class _TrainerAppointmentDetailsState extends State<TrainerAppointmentDetails> {
  late Map<String, dynamic> _item = widget.item;
  bool _busy = false;

  Future<void> _command(String action, {String? reason}) async {
    final api = context.read<ApiClient>();
    final confirmation = switch (action) {
      'complete' => const (
        title: 'Završetak treninga',
        message: 'Želite li označiti trening završenim?',
        action: 'Označi završenim',
      ),
      'confirm' => const (
        title: 'Potvrda termina',
        message: 'Želite li potvrditi ovaj termin?',
        action: 'Potvrdi termin',
      ),
      _ => const (
        title: 'Potvrda akcije',
        message: 'Želite li nastaviti?',
        action: 'Potvrdi',
      ),
    };
    if (!await confirmAction(
      context,
      title: confirmation.title,
      message: confirmation.message,
      action: confirmation.action,
    )) {
      return;
    }
    setState(() => _busy = true);
    try {
      final json = await api.post(
        '/api/tenant/reservations/${_item['id']}/$action',
        body: {
          'concurrencyToken': _item['concurrencyToken'],
          'reason': ?reason,
        },
      );
      _item = Map<String, dynamic>.from(json! as Map);
      if (mounted) setState(() {});
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.firstFieldError ?? error.message)),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _cancel() async {
    final api = context.read<ApiClient>();
    await showDialog<bool>(
      context: context,
      builder: (_) => TrainerCancellationReasonDialog(
        onSubmit: (reason) async {
          final json = await api.post(
            '/api/tenant/reservations/${_item['id']}/cancel',
            body: {
              'concurrencyToken': _item['concurrencyToken'],
              'reason': reason,
            },
          );
          _item = Map<String, dynamic>.from(json! as Map);
          if (mounted) setState(() {});
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final status = (_item['status'] as num?)?.toInt();
    return PageFrame(
      title: 'Detalji termina',
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _item['memberName'].toString(),
                    style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  Text('${_item['gymName']} · ${_item['offeringName']}'),
                  const SizedBox(height: 12),
                  Text(
                    DateFormat('dd.MM.yyyy. HH:mm').format(
                      DateTime.parse(_item['startsAtUtc'].toString()).toLocal(),
                    ),
                  ),
                  Text('${_item['durationMinutes']} minuta'),
                  Text(
                    (_item['paymentMethod'] as num?)?.toInt() == 1
                        ? 'Plaćanje: uživo'
                        : 'Plaćanje: Stripe',
                  ),
                  const SizedBox(height: 12),
                  StatusPill(enumLabel(_item['status'], _reservationStatuses)),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          if ((_item['allowedActions'] as List? ?? const []).contains(
            'confirm',
          ))
            FilledButton(
              onPressed: _busy ? null : () => _command('confirm'),
              child: const Text('Potvrdi termin'),
            ),
          if ((_item['allowedActions'] as List? ?? const []).contains(
            'complete',
          ))
            FilledButton(
              onPressed: _busy ? null : () => _command('complete'),
              child: const Text('Označi završenim'),
            ),
          if ((_item['allowedActions'] as List? ?? const []).contains('cancel'))
            OutlinedButton(
              onPressed: _busy ? null : _cancel,
              child: const Text('Otkaži uz razlog'),
            ),
          if (status == 1 || status == 2)
            OutlinedButton.icon(
              key: const Key('trainer-open-chat'),
              onPressed: () =>
                  openChatForReservation(context, _item['id'].toString()),
              icon: const Icon(Icons.forum_outlined),
              label: const Text('Otvori razgovor'),
            ),
        ],
      ),
    );
  }
}

class TrainerCancellationReasonDialog extends StatefulWidget {
  const TrainerCancellationReasonDialog({required this.onSubmit, super.key});

  final Future<void> Function(String reason) onSubmit;

  @override
  State<TrainerCancellationReasonDialog> createState() =>
      _TrainerCancellationReasonDialogState();
}

class _TrainerCancellationReasonDialogState
    extends State<TrainerCancellationReasonDialog> {
  final _formKey = GlobalKey<FormState>();
  final _controller = TextEditingController();
  ApiProblem? _serverProblem;
  String? _formError;
  bool _busy = false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Razlog otkazivanja'),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextFormField(
            controller: _controller,
            autofocus: true,
            maxLength: 200,
            maxLines: 3,
            decoration: const InputDecoration(labelText: 'Razlog'),
            onChanged: (_) {
              if (_serverProblem?.fieldError('Reason') != null) {
                setState(() {
                  _serverProblem = null;
                  _formError = null;
                });
              }
            },
            validator: (value) {
              final length = value?.trim().length ?? 0;
              if (length < 2) return 'Unesite razlog otkazivanja.';
              if (length > 200) return 'Najviše 200 znakova.';
              return _serverProblem?.fieldError('Reason');
            },
            onFieldSubmitted: (_) => _submit(),
          ),
          if (_formError != null)
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                _formError!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _busy ? null : _submit,
        child: Text(_busy ? 'Otkazivanje...' : 'Otkaži'),
      ),
    ],
  );

  Future<void> _submit() async {
    setState(() {
      _serverProblem = null;
      _formError = null;
    });
    if (!_formKey.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      await widget.onSubmit(_controller.text.trim());
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
      if (mounted) setState(() => _busy = false);
    }
  }
}

class TrainerAvailabilityScreen extends StatefulWidget {
  const TrainerAvailabilityScreen({super.key});

  @override
  State<TrainerAvailabilityScreen> createState() =>
      _TrainerAvailabilityScreenState();
}

class _TrainerAvailabilityScreenState extends State<TrainerAvailabilityScreen> {
  static const _days = [
    'Ponedjeljak',
    'Utorak',
    'Srijeda',
    'Četvrtak',
    'Petak',
    'Subota',
    'Nedjelja',
  ];
  final Set<(int, int)> _selected = {};
  final Set<(int, int)> _savedSelection = {};
  String? _concurrencyToken;
  bool _loading = true;
  bool _saving = false;
  Object? _error;

  bool get _hasUnsavedChanges =>
      _selected.length != _savedSelection.length ||
      !_selected.containsAll(_savedSelection);

  int get _activeDays => _selected.map((item) => item.$1).toSet().length;

  String get _activeDaysLabel => switch (_activeDays) {
    1 => '1 aktivan dan',
    2 || 3 || 4 => '$_activeDays aktivna dana',
    _ => '$_activeDays aktivnih dana',
  };

  String get _selectedShiftsLabel => switch (_selected.length) {
    1 => '1 odabrana smjena',
    2 || 3 || 4 => '${_selected.length} odabrane smjene',
    _ => '${_selected.length} odabranih smjena',
  };

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool preserveEdits = false}) async {
    final edits = Set<(int, int)>.of(_selected);
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final schedule = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/tenant/trainer-availability/schedule',
              query: {'trainerProfileId': _emptyGuid},
            ))!
            as Map,
      );
      _concurrencyToken = schedule['concurrencyToken']?.toString();
      final serverSelection = _selectionFrom(schedule);
      _savedSelection
        ..clear()
        ..addAll(serverSelection);
      _selected
        ..clear()
        ..addAll(preserveEdits ? edits : serverSelection);
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _save() async {
    if (!_hasUnsavedChanges || _saving) return;
    final submitted = Set<(int, int)>.of(_selected);
    final shifts = submitted.toList()
      ..sort((left, right) {
        final dayComparison = left.$1.compareTo(right.$1);
        return dayComparison != 0 ? dayComparison : left.$2.compareTo(right.$2);
      });
    setState(() => _saving = true);
    try {
      final schedule = Map<String, dynamic>.from(
        (await context.read<ApiClient>().put(
              '/api/tenant/trainer-availability/schedule',
              body: {
                'trainerProfileId': _emptyGuid,
                'shifts': shifts
                    .map((item) => {'dayOfWeek': item.$1, 'period': item.$2})
                    .toList(),
                'concurrencyToken': _concurrencyToken,
              },
            ))!
            as Map,
      );
      _concurrencyToken = schedule['concurrencyToken']?.toString();
      final savedSelection = schedule['shifts'] is List
          ? _selectionFrom(schedule)
          : submitted;
      _savedSelection
        ..clear()
        ..addAll(savedSelection);
      _selected
        ..clear()
        ..addAll(savedSelection);
      if (mounted) {
        setState(() {});
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Sedmični raspored je sačuvan.')),
        );
      }
    } on ApiProblem catch (error) {
      if (error.status == 409) {
        await _load(preserveEdits: true);
      }
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              error.status == 409
                  ? '${error.message} Vaše izmjene su zadržane; pregledajte ih i pokušajte ponovo.'
                  : error.message,
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _refresh() async {
    if (_hasUnsavedChanges &&
        !await confirmAction(
          context,
          title: 'Odbaciti izmjene?',
          message: 'Nesačuvane izmjene rasporeda će biti izgubljene.',
          action: 'Odbaci',
        )) {
      return;
    }
    await _load();
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Expanded(
        child: RefreshIndicator(
          onRefresh: _refresh,
          child: ListView(
            key: const Key('trainer-availability-scroll'),
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
            children: [
              AsyncPanel(
                loading: _loading,
                error: _error,
                onRetry: _load,
                child: Column(
                  children: [
                    _summaryCard(context),
                    const SizedBox(height: 12),
                    _weekEditor(context),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
      _saveBar(context),
    ],
  );

  Widget _summaryCard(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(
              color: GymLinkColors.blue.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(13),
            ),
            child: const Icon(
              Icons.calendar_month_outlined,
              color: GymLinkColors.blue,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Sedmični raspored',
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  _selected.isEmpty
                      ? 'Nema odabranih smjena'
                      : '$_activeDaysLabel · $_selectedShiftsLabel',
                  key: const Key('availability-summary-count'),
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 5),
                Text(
                  'Europe/Sarajevo · termini 8 sedmica unaprijed',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    ),
  );

  Widget _weekEditor(BuildContext context) => Card(
    child: Column(
      children: [
        for (var index = 0; index < _days.length; index++) ...[
          _dayRow(context, index),
          if (index != _days.length - 1) const Divider(height: 1),
        ],
      ],
    ),
  );

  Widget _dayRow(BuildContext context, int index) {
    final day = (index + 1) % 7;
    return Padding(
      key: Key('availability-day-$day'),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 13),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '${_days[index]} - ${DateFormat('dd.MM.').format(_nextOccurrence(day))}',
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 9),
          Row(
            children: [
              Expanded(
                child: _shiftToggle(
                  context,
                  day: day,
                  period: 0,
                  label: 'Jutarnja',
                  time: '08:00–15:00',
                  icon: Icons.light_mode_outlined,
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: _shiftToggle(
                  context,
                  day: day,
                  period: 1,
                  label: 'Večernja',
                  time: '15:00–22:00',
                  icon: Icons.dark_mode_outlined,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _shiftToggle(
    BuildContext context, {
    required int day,
    required int period,
    required String label,
    required String time,
    required IconData icon,
  }) {
    final selected = _selected.contains((day, period));
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      button: true,
      selected: selected,
      label: '$label smjena $time',
      child: InkWell(
        key: Key('availability-shift-$day-$period'),
        onTap: _saving ? null : () => _toggle(day, period, !selected),
        borderRadius: BorderRadius.circular(12),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 9),
          decoration: BoxDecoration(
            color: selected
                ? colors.primaryContainer
                : colors.surfaceContainerHighest.withValues(alpha: 0.55),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: selected ? colors.primary : Colors.transparent,
            ),
          ),
          child: Row(
            children: [
              Icon(
                selected ? Icons.check_circle : icon,
                size: 18,
                color: selected ? colors.primary : colors.onSurfaceVariant,
              ),
              const SizedBox(width: 7),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                    Text(
                      time,
                      maxLines: 1,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _saveBar(BuildContext context) => Material(
    color: Colors.white,
    elevation: 8,
    child: SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 16, 12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(
                  _hasUnsavedChanges
                      ? Icons.edit_calendar_outlined
                      : Icons.check_circle_outline,
                  size: 18,
                  color: _hasUnsavedChanges
                      ? Theme.of(context).colorScheme.primary
                      : GymLinkColors.success,
                ),
                const SizedBox(width: 7),
                Expanded(
                  child: Text(
                    _hasUnsavedChanges
                        ? 'Nesačuvane izmjene'
                        : 'Raspored je sačuvan',
                    key: const Key('availability-save-state'),
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                if (_hasUnsavedChanges)
                  TextButton(
                    key: const Key('availability-reset'),
                    onPressed: _saving ? null : _reset,
                    child: const Text('Poništi'),
                  ),
              ],
            ),
            const SizedBox(height: 6),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                key: const Key('availability-save'),
                onPressed:
                    _loading || _error != null || !_hasUnsavedChanges || _saving
                    ? null
                    : _save,
                icon: _saving
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.save_outlined),
                label: Text(_saving ? 'Čuvanje…' : 'Sačuvaj raspored'),
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Set<(int, int)> _selectionFrom(Map<String, dynamic> schedule) =>
      (schedule['shifts'] as List? ?? const [])
          .whereType<Map>()
          .map(
            (item) => (
              (item['dayOfWeek'] as num).toInt(),
              (item['period'] as num).toInt(),
            ),
          )
          .toSet();

  DateTime _nextOccurrence(int day) {
    final today = DateUtils.dateOnly(DateTime.now());
    final weekday = day == 0 ? DateTime.sunday : day;
    final daysUntil = (weekday - today.weekday + 7) % 7;
    return today.add(Duration(days: daysUntil));
  }

  void _reset() {
    setState(() {
      _selected
        ..clear()
        ..addAll(_savedSelection);
    });
  }

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

class TrainerOfferingsScreen extends StatefulWidget {
  const TrainerOfferingsScreen({super.key});

  @override
  State<TrainerOfferingsScreen> createState() => _TrainerOfferingsScreenState();
}

class _TrainerOfferingsScreenState extends State<TrainerOfferingsScreen> {
  List<Map<String, dynamic>> _items = const [];
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
      _items = (await context.read<ApiClient>().page(
        '/api/tenant/trainer-offerings',
      )).items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _create() async {
    final api = context.read<ApiClient>();
    try {
      final lookups = Map<String, dynamic>.from(
        (await api.get('/api/reference-data/lookups'))! as Map,
      );
      if (!mounted) return;
      final created = await showDialog<bool>(
        context: context,
        builder: (_) => _OfferingDialog(
          trainingTypes: (lookups['trainingTypes'] as List? ?? const [])
              .whereType<Map>()
              .map((item) => Map<String, dynamic>.from(item))
              .toList(),
        ),
      );
      if (created == true) await _load();
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error.firstFieldError ?? error.message)),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        FilledButton.icon(
          onPressed: _create,
          icon: const Icon(Icons.add),
          label: const Text('Dodaj uslugu'),
        ),
        const SizedBox(height: 14),
        AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _items.isEmpty
              ? const SizedBox(
                  height: 400,
                  child: EmptyState(
                    title: 'Nema usluga',
                    message:
                        'Dodajte uslugu prije nego članovi rezervišu termin.',
                    icon: Icons.sell_outlined,
                  ),
                )
              : Column(
                  children: _items
                      .map(
                        (item) => Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Card(
                            child: ListTile(
                              title: Text(item['name'].toString()),
                              subtitle: Text(
                                '${item['trainingType']} · ${item['durationMinutes']} min',
                              ),
                              trailing: Text(
                                '${item['price']} ${item['currency']}',
                              ),
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

class _OfferingDialog extends StatefulWidget {
  const _OfferingDialog({required this.trainingTypes});
  final List<Map<String, dynamic>> trainingTypes;

  @override
  State<_OfferingDialog> createState() => _OfferingDialogState();
}

class _OfferingDialogState extends State<_OfferingDialog> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _duration = TextEditingController(text: '60');
  final _price = TextEditingController(text: '25');
  Map<String, dynamic>? _type;
  Map<String, List<String>> _serverErrors = const {};
  String? _serverMessage;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _type = widget.trainingTypes.firstOrNull;
  }

  @override
  void dispose() {
    _name.dispose();
    _duration.dispose();
    _price.dispose();
    super.dispose();
  }

  String? _serverError(String field) {
    for (final entry in _serverErrors.entries) {
      if (entry.key.toLowerCase() == field.toLowerCase()) {
        return entry.value.firstOrNull;
      }
    }
    return null;
  }

  Future<void> _submit() async {
    setState(() {
      _serverErrors = const {};
      _serverMessage = null;
    });
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await context.read<ApiClient>().post(
        '/api/tenant/trainer-offerings',
        body: {
          'trainerProfileId': _emptyGuid,
          'trainingTypeId': _type!['id'],
          'name': _name.text.trim(),
          'durationMinutes': int.parse(_duration.text.trim()),
          'price': double.parse(_price.text.trim().replaceFirst(',', '.')),
          'currency': 'BAM',
        },
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiProblem catch (error) {
      if (!mounted) return;
      if (error.fieldErrors.isNotEmpty) {
        setState(() {
          _serverErrors = error.fieldErrors;
          _serverMessage = error.firstFieldError;
        });
        _formKey.currentState?.validate();
      } else {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Uslugu trenutno nije moguće sačuvati.'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Nova usluga'),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextFormField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Naziv'),
            validator: (value) {
              final server = _serverError('Name');
              if (server != null) return server;
              final normalized = value?.trim() ?? '';
              if (normalized.isEmpty) return 'Unesite naziv usluge.';
              if (normalized.length > 200) {
                return 'Naziv može imati najviše 200 znakova.';
              }
              return null;
            },
          ),
          const SizedBox(height: 10),
          DropdownButtonFormField<Map<String, dynamic>>(
            initialValue: _type,
            decoration: const InputDecoration(labelText: 'Tip treninga'),
            items: widget.trainingTypes
                .map(
                  (item) => DropdownMenuItem(
                    value: item,
                    child: Text(item['name'].toString()),
                  ),
                )
                .toList(),
            onChanged: (value) => _type = value,
            validator: (value) => value == null
                ? 'Izaberite tip treninga.'
                : _serverError('TrainingTypeId'),
          ),
          const SizedBox(height: 10),
          TextFormField(
            controller: _duration,
            keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Trajanje (min)'),
            validator: (value) {
              final server = _serverError('DurationMinutes');
              if (server != null) return server;
              final duration = int.tryParse(value?.trim() ?? '');
              if (duration == null || duration < 1 || duration > 1440) {
                return 'Unesite cijeli broj od 1 do 1440.';
              }
              return null;
            },
          ),
          const SizedBox(height: 10),
          TextFormField(
            controller: _price,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
            validator: (value) {
              final server = _serverError('Price');
              if (server != null) return server;
              final price = double.tryParse(
                (value ?? '').trim().replaceFirst(',', '.'),
              );
              if (price == null || price < 0 || price > 1000000) {
                return 'Unesite cijenu od 0 do 1.000.000.';
              }
              return null;
            },
          ),
          if (_serverMessage != null) ...[
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                _serverMessage!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          ],
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: _busy ? null : () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _busy ? null : _submit,
        child: Text(_busy ? 'Čuvanje...' : 'Sačuvaj'),
      ),
    ],
  );
}

class TrainerReviewsScreen extends StatefulWidget {
  const TrainerReviewsScreen({super.key});

  @override
  State<TrainerReviewsScreen> createState() => _TrainerReviewsScreenState();
}

class _TrainerReviewsScreenState extends State<TrainerReviewsScreen> {
  List<Map<String, dynamic>> _items = const [];
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
      final profile = Map<String, dynamic>.from(
        (await api.get('/api/profile'))! as Map,
      );
      final trainerId = profile['trainerProfileId']?.toString();
      if (trainerId == null || trainerId.isEmpty) {
        throw StateError('Aktivan profil trenera nije pronađen.');
      }
      _items = (await api.page('/api/trainers/$trainerId/reviews')).items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: _items.isEmpty
          ? ListView(
              children: const [
                SizedBox(
                  height: 500,
                  child: EmptyState(
                    title: 'Još nema recenzija',
                    message:
                        'Recenzije se mogu ostaviti nakon završenog termina.',
                    icon: Icons.star_border,
                  ),
                ),
              ],
            )
          : ListView(
              padding: const EdgeInsets.all(16),
              children: _items
                  .map(
                    (item) => Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: Card(
                        child: ListTile(
                          leading: CircleAvatar(
                            child: Text('★ ${item['rating']}'),
                          ),
                          title: Text(
                            item['comment']?.toString() ?? 'Bez komentara',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                          subtitle: Text(
                            DateFormat('dd.MM.yyyy.').format(
                              DateTime.parse(
                                item['createdAtUtc'].toString(),
                              ).toLocal(),
                            ),
                          ),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () => Navigator.push<void>(
                            context,
                            MaterialPageRoute(
                              builder: (_) =>
                                  TrainerReviewDetailsScreen(review: item),
                            ),
                          ),
                        ),
                      ),
                    ),
                  )
                  .toList(),
            ),
    ),
  );
}

class TrainerReviewDetailsScreen extends StatelessWidget {
  const TrainerReviewDetailsScreen({required this.review, super.key});

  final Map<String, dynamic> review;

  @override
  Widget build(BuildContext context) {
    final rating = (review['rating'] as num?)?.toInt() ?? 0;
    final createdAt = DateTime.tryParse(
      review['createdAtUtc']?.toString() ?? '',
    );
    final comment = review['comment']?.toString().trim();
    return Scaffold(
      appBar: AppBar(title: const Text('Detalji recenzije')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.star, color: Colors.amber, size: 30),
                      const SizedBox(width: 8),
                      Text(
                        '$rating od 5',
                        style: Theme.of(context).textTheme.headlineSmall
                            ?.copyWith(fontWeight: FontWeight.w800),
                      ),
                    ],
                  ),
                  const SizedBox(height: 18),
                  Text(
                    comment == null || comment.isEmpty
                        ? 'Bez komentara'
                        : comment,
                    style: Theme.of(context).textTheme.bodyLarge,
                  ),
                  const SizedBox(height: 18),
                  Text(
                    createdAt == null
                        ? 'Datum nije dostupan'
                        : DateFormat(
                            'dd.MM.yyyy. HH:mm',
                          ).format(createdAt.toLocal()),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),
          const Card(
            child: ListTile(
              leading: Icon(Icons.privacy_tip_outlined),
              title: Text('Anonimna recenzija'),
              subtitle: Text('Identitet korisnika se ne prikazuje treneru.'),
            ),
          ),
        ],
      ),
    );
  }
}
