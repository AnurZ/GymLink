import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';

const _reservationStatuses = ['Pending', 'Confirmed', 'Completed', 'Cancelled'];
const _emptyGuid = '00000000-0000-0000-0000-000000000000';

class TrainerAppointmentsScreen extends StatefulWidget {
  const TrainerAppointmentsScreen({super.key});

  @override
  State<TrainerAppointmentsScreen> createState() =>
      _TrainerAppointmentsScreenState();
}

class _TrainerAppointmentsScreenState extends State<TrainerAppointmentsScreen> {
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
        '/api/me/trainer-reservations',
        query: {
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
                    title: 'Nema termina',
                    message: 'Trenutno nemate zakazanih termina.',
                    icon: Icons.event_available,
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
                          leading: const CircleAvatar(
                            child: Icon(Icons.person),
                          ),
                          title: Text(item['memberName'].toString()),
                          subtitle: Text(
                            '${DateFormat('dd.MM.yyyy. HH:mm').format(DateTime.parse(item['startsAtUtc'].toString()).toLocal())}\n${item['offeringName']}',
                          ),
                          isThreeLine: true,
                          trailing: StatusPill(
                            enumLabel(item['status'], _reservationStatuses),
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
    if (!await confirmAction(
      context,
      title: 'Potvrda akcije',
      message: 'Želite li izvršiti akciju “$action”?',
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
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _cancel() async {
    final reason = await showDialog<String>(
      context: context,
      builder: (_) => const TrainerCancellationReasonDialog(),
    );
    if (reason != null) await _command('cancel', reason: reason);
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
                  const SizedBox(height: 12),
                  StatusPill(enumLabel(_item['status'], _reservationStatuses)),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          if (status == 0)
            FilledButton(
              onPressed: _busy ? null : () => _command('confirm'),
              child: const Text('Potvrdi termin'),
            ),
          if (status == 1)
            FilledButton(
              onPressed: _busy ? null : () => _command('complete'),
              child: const Text('Označi završenim'),
            ),
          if (status != 2 && status != 3)
            OutlinedButton(
              onPressed: _busy ? null : _cancel,
              child: const Text('Otkaži uz razlog'),
            ),
        ],
      ),
    );
  }
}

class TrainerCancellationReasonDialog extends StatefulWidget {
  const TrainerCancellationReasonDialog({super.key});

  @override
  State<TrainerCancellationReasonDialog> createState() =>
      _TrainerCancellationReasonDialogState();
}

class _TrainerCancellationReasonDialogState
    extends State<TrainerCancellationReasonDialog> {
  final _formKey = GlobalKey<FormState>();
  final _controller = TextEditingController();

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
      child: TextFormField(
        controller: _controller,
        autofocus: true,
        maxLength: 1000,
        maxLines: 3,
        decoration: const InputDecoration(labelText: 'Razlog'),
        validator: (value) => value == null || value.trim().length < 2
            ? 'Unesite razlog otkazivanja.'
            : null,
        onFieldSubmitted: (_) => _submit(),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(onPressed: _submit, child: const Text('Nastavi')),
    ],
  );

  void _submit() {
    if (!_formKey.currentState!.validate()) return;
    Navigator.pop(context, _controller.text.trim());
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
  String? _concurrencyToken;
  bool _loading = true;
  bool _saving = false;
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
      final schedule = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/tenant/trainer-availability/schedule',
              query: {'trainerProfileId': _emptyGuid},
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
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _save() async {
    setState(() => _saving = true);
    try {
      final schedule = Map<String, dynamic>.from(
        (await context.read<ApiClient>().put(
              '/api/tenant/trainer-availability/schedule',
              body: {
                'trainerProfileId': _emptyGuid,
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
      if (error.status == 409) await _load();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Sedmične smjene',
                  style: Theme.of(
                    context,
                  ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Raspored se ponavlja u vremenskoj zoni Sarajevo, a termini su dostupni osam sedmica unaprijed.',
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 14),
        AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: Column(
            children: [
              for (var index = 0; index < _days.length; index++)
                Card(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _days[index],
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                        CheckboxListTile(
                          contentPadding: EdgeInsets.zero,
                          value: _selected.contains(((index + 1) % 7, 0)),
                          onChanged: (value) =>
                              _toggle((index + 1) % 7, 0, value ?? false),
                          title: const Text('Smjena 1 · 08:00–15:00'),
                        ),
                        CheckboxListTile(
                          contentPadding: EdgeInsets.zero,
                          value: _selected.contains(((index + 1) % 7, 1)),
                          onChanged: (value) =>
                              _toggle((index + 1) % 7, 1, value ?? false),
                          title: const Text('Smjena 2 · 15:00–22:00'),
                        ),
                      ],
                    ),
                  ),
                ),
              const SizedBox(height: 10),
              FilledButton.icon(
                onPressed: _saving ? null : _save,
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
        (await api.get('/api/reference-data/lookups', authenticated: false))!
            as Map,
      );
      if (!mounted) return;
      final result = await showDialog<Map<String, Object?>>(
        context: context,
        builder: (_) => _OfferingDialog(
          trainingTypes: (lookups['trainingTypes'] as List? ?? const [])
              .whereType<Map>()
              .map((item) => Map<String, dynamic>.from(item))
              .toList(),
        ),
      );
      if (result == null) return;
      await api.post(
        '/api/tenant/trainer-offerings',
        body: {'trainerProfileId': _emptyGuid, ...result},
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
  final _name = TextEditingController();
  final _duration = TextEditingController(text: '60');
  final _price = TextEditingController(text: '25');
  Map<String, dynamic>? _type;

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

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Nova usluga'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        TextField(
          controller: _name,
          decoration: const InputDecoration(labelText: 'Naziv'),
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
        ),
        const SizedBox(height: 10),
        TextField(
          controller: _duration,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(labelText: 'Trajanje (min)'),
        ),
        const SizedBox(height: 10),
        TextField(
          controller: _price,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(labelText: 'Cijena (BAM)'),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: _type == null
            ? null
            : () => Navigator.pop(context, {
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
      _items = (await api.page(
        '/api/trainers/$trainerId/reviews',
        authenticated: false,
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
                          ),
                          subtitle: Text(
                            DateFormat('dd.MM.yyyy.').format(
                              DateTime.parse(
                                item['createdAtUtc'].toString(),
                              ).toLocal(),
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
