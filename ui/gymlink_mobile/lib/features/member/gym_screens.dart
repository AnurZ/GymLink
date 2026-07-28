import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../shared/widgets.dart';

class GymDiscoveryScreen extends StatefulWidget {
  const GymDiscoveryScreen({super.key});

  @override
  State<GymDiscoveryScreen> createState() => _GymDiscoveryScreenState();
}

class _GymDiscoveryScreenState extends State<GymDiscoveryScreen> {
  final _search = TextEditingController();
  PagedData? _data;
  Object? _error;
  bool _loading = true;
  bool _mapMode = true;

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
      _data = await context.read<ApiClient>().page(
        '/api/gyms',
        authenticated: false,
        query: {'query': _search.text.trim()},
      );
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => RefreshIndicator(
    onRefresh: _load,
    child: ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _search,
                textInputAction: TextInputAction.search,
                onSubmitted: (_) => _load(),
                decoration: const InputDecoration(
                  hintText: 'Pretraži teretane...',
                  prefixIcon: Icon(Icons.search),
                ),
              ),
            ),
            const SizedBox(width: 10),
            IconButton.filledTonal(
              tooltip: 'Primijeni pretragu',
              onPressed: _load,
              icon: const Icon(Icons.tune),
            ),
          ],
        ),
        const SizedBox(height: 16),
        SegmentedButton<bool>(
          segments: const [
            ButtonSegment(
              value: true,
              icon: Icon(Icons.map_outlined),
              label: Text('Mapa'),
            ),
            ButtonSegment(
              value: false,
              icon: Icon(Icons.list),
              label: Text('Lista'),
            ),
          ],
          selected: {_mapMode},
          onSelectionChanged: (value) => setState(() => _mapMode = value.first),
        ),
        const SizedBox(height: 16),
        AsyncPanel(
          loading: _loading,
          error: _error,
          onRetry: _load,
          child: _data == null || _data!.items.isEmpty
              ? const SizedBox(
                  height: 400,
                  child: EmptyState(
                    title: 'Nema teretana',
                    message: 'Nema aktivnih teretana koje odgovaraju pretrazi.',
                    icon: Icons.location_off_outlined,
                  ),
                )
              : _mapMode
              ? _GymMap(
                  gyms: _data!.items,
                  onOpen: (gym) => Navigator.push(
                    context,
                    MaterialPageRoute<void>(
                      builder: (_) =>
                          GymDetailsScreen(gymId: gym['id'].toString()),
                    ),
                  ),
                )
              : Column(
                  children: _data!.items
                      .map(
                        (gym) => Padding(
                          padding: const EdgeInsets.only(bottom: 12),
                          child: _GymCard(
                            gym: gym,
                            onOpen: () => Navigator.push(
                              context,
                              MaterialPageRoute<void>(
                                builder: (_) => GymDetailsScreen(
                                  gymId: gym['id'].toString(),
                                ),
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

class _GymMap extends StatelessWidget {
  const _GymMap({required this.gyms, required this.onOpen});
  final List<Map<String, dynamic>> gyms;
  final ValueChanged<Map<String, dynamic>> onOpen;

  @override
  Widget build(BuildContext context) {
    final valid = gyms
        .where((gym) => gym['latitude'] is num && gym['longitude'] is num)
        .toList();
    final center = valid.isEmpty
        ? const LatLng(43.8563, 18.4131)
        : LatLng(
            valid
                    .map((gym) => (gym['latitude'] as num).toDouble())
                    .reduce((a, b) => a + b) /
                valid.length,
            valid
                    .map((gym) => (gym['longitude'] as num).toDouble())
                    .reduce((a, b) => a + b) /
                valid.length,
          );
    return SizedBox(
      height: 560,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(18),
        child: Stack(
          children: [
            FlutterMap(
              options: MapOptions(initialCenter: center, initialZoom: 13),
              children: [
                TileLayer(
                  urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                  userAgentPackageName: 'ba.gymlink.gymlink_mobile',
                ),
                MarkerLayer(
                  markers: valid
                      .map(
                        (gym) => Marker(
                          point: LatLng(
                            (gym['latitude'] as num).toDouble(),
                            (gym['longitude'] as num).toDouble(),
                          ),
                          width: 62,
                          height: 62,
                          child: Semantics(
                            button: true,
                            label: gym['name'].toString(),
                            child: GestureDetector(
                              onTap: () => onOpen(gym),
                              child: Container(
                                decoration: BoxDecoration(
                                  color: Colors.white,
                                  shape: BoxShape.circle,
                                  border: Border.all(
                                    color: GymLinkColors.blue,
                                    width: 4,
                                  ),
                                  boxShadow: const [
                                    BoxShadow(
                                      blurRadius: 8,
                                      color: Colors.black26,
                                    ),
                                  ],
                                ),
                                child: const Icon(
                                  Icons.fitness_center,
                                  color: GymLinkColors.blue,
                                ),
                              ),
                            ),
                          ),
                        ),
                      )
                      .toList(),
                ),
              ],
            ),
            const Positioned(
              right: 8,
              bottom: 6,
              child: DecoratedBox(
                decoration: BoxDecoration(color: Colors.white70),
                child: Padding(
                  padding: EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  child: Text(
                    '© OpenStreetMap contributors',
                    style: TextStyle(fontSize: 10),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _GymCard extends StatelessWidget {
  const _GymCard({required this.gym, required this.onOpen});
  final Map<String, dynamic> gym;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) => Card(
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onOpen,
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Row(
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(14),
              child: SizedBox.square(
                dimension: 82,
                child: gym['primaryImageUrl'] == null
                    ? const ColoredBox(
                        color: Color(0xFFE8EDF7),
                        child: Icon(
                          Icons.fitness_center,
                          color: GymLinkColors.blue,
                        ),
                      )
                    : Image.network(
                        gym['primaryImageUrl'].toString(),
                        fit: BoxFit.cover,
                        errorBuilder: (_, _, _) => const ColoredBox(
                          color: Color(0xFFE8EDF7),
                          child: Icon(Icons.broken_image_outlined),
                        ),
                      ),
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    gym['name'].toString(),
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text('${gym['address']}, ${gym['city']}'),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      const Icon(Icons.star, color: Colors.amber, size: 20),
                      Text(' ${gym['averageRating']} (${gym['reviewCount']})'),
                      const Spacer(),
                      if (gym['startingMembershipPrice'] != null)
                        Text(
                          '${gym['startingMembershipPrice']} ${gym['currency']}',
                          style: const TextStyle(
                            color: GymLinkColors.blue,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                    ],
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

class GymDetailsScreen extends StatefulWidget {
  const GymDetailsScreen({required this.gymId, super.key});
  final String gymId;

  @override
  State<GymDetailsScreen> createState() => _GymDetailsScreenState();
}

class _GymDetailsScreenState extends State<GymDetailsScreen> {
  Map<String, dynamic>? _gym;
  List<Map<String, dynamic>> _plans = const [];
  List<Map<String, dynamic>> _trainers = const [];
  List<Map<String, dynamic>> _reviews = const [];
  Object? _error;
  bool _loading = true;

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
        api.get('/api/gyms/${widget.gymId}', authenticated: false),
        api.list(
          '/api/gyms/${widget.gymId}/membership-plans',
          authenticated: false,
        ),
        api.list('/api/gyms/${widget.gymId}/trainers', authenticated: false),
        api.page('/api/gyms/${widget.gymId}/reviews', authenticated: false),
      ]);
      _gym = Map<String, dynamic>.from(results[0]! as Map);
      _plans = results[1] as List<Map<String, dynamic>>;
      _trainers = results[2] as List<Map<String, dynamic>>;
      _reviews = (results[3] as PagedData).items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _requestMembership(Map<String, dynamic> plan) async {
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Zahtjev za članstvo',
      message: 'Pošalji zahtjev za plan ${plan['name']}?',
      action: 'Pošalji',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/membership-requests',
        body: {'membershipPlanId': plan['id']},
      );
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Zahtjev je poslan.')));
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _reviewGym() async {
    final api = context.read<ApiClient>();
    final result = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (_) => const _ReviewDialog(title: 'Ocijenite teretanu'),
    );
    if (result == null) return;
    try {
      await api.post('/api/gyms/${widget.gymId}/reviews', body: result);
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
  Widget build(BuildContext context) => PageFrame(
    title: _gym?['name']?.toString() ?? 'Detalji teretane',
    child: AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: _gym == null
          ? const SizedBox.shrink()
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if ((_gym!['imageUrls'] as List? ?? const []).isNotEmpty)
                  ClipRRect(
                    borderRadius: BorderRadius.circular(18),
                    child: Image.network(
                      (_gym!['imageUrls'] as List).first.toString(),
                      height: 210,
                      fit: BoxFit.cover,
                      errorBuilder: (_, _, _) => const SizedBox.shrink(),
                    ),
                  ),
                const SizedBox(height: 14),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _gym!['name'].toString(),
                          style: Theme.of(context).textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        const SizedBox(height: 8),
                        Text('${_gym!['address']}, ${_gym!['city']}'),
                        const SizedBox(height: 8),
                        Row(
                          children: [
                            const Icon(Icons.star, color: Colors.amber),
                            Text(
                              ' ${_gym!['averageRating']} (${_gym!['reviewCount']} recenzija)',
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Text(_gym!['description'].toString()),
                      ],
                    ),
                  ),
                ),
                _sectionTitle('Članarine'),
                ..._plans.map(
                  (plan) => Card(
                    child: ListTile(
                      title: Text(plan['name'].toString()),
                      subtitle: Text('${plan['durationDays']} dana'),
                      trailing: FilledButton(
                        onPressed: () => _requestMembership(plan),
                        child: Text('${plan['price']} ${plan['currency']}'),
                      ),
                    ),
                  ),
                ),
                _sectionTitle('Treneri i termini'),
                if (_trainers.isEmpty)
                  const Text('Trenutno nema aktivnih trenera.')
                else
                  ..._trainers.map(
                    (trainer) => Card(
                      child: ListTile(
                        leading: const CircleAvatar(child: Icon(Icons.person)),
                        title: Text(trainer['displayName'].toString()),
                        subtitle: Text(
                          'Ocjena ${trainer['averageRating']} · ${trainer['reviewCount']} recenzija',
                        ),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => Navigator.push(
                          context,
                          MaterialPageRoute<void>(
                            builder: (_) => BookingScreen(trainer: trainer),
                          ),
                        ),
                      ),
                    ),
                  ),
                _sectionTitle('Recenzije'),
                OutlinedButton.icon(
                  onPressed: _reviewGym,
                  icon: const Icon(Icons.rate_review_outlined),
                  label: const Text('Napiši recenziju'),
                ),
                const SizedBox(height: 8),
                if (_reviews.isEmpty)
                  const Text('Još nema recenzija.')
                else
                  ..._reviews.map(
                    (review) => Card(
                      child: ListTile(
                        leading: Text('★ ${review['rating']}'),
                        title: Text(
                          review['comment']?.toString() ?? 'Bez komentara',
                        ),
                      ),
                    ),
                  ),
              ],
            ),
    ),
  );

  Widget _sectionTitle(String value) => Padding(
    padding: const EdgeInsets.only(top: 22, bottom: 10),
    child: Text(
      value,
      style: Theme.of(
        context,
      ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
    ),
  );
}

class BookingScreen extends StatefulWidget {
  const BookingScreen({required this.trainer, super.key});
  final Map<String, dynamic> trainer;

  @override
  State<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends State<BookingScreen> {
  List<Map<String, dynamic>> _offerings = const [];
  List<Map<String, dynamic>> _slots = const [];
  String? _offeringId;
  Map<String, dynamic>? _slot;
  DateTime _date = DateTime.now();
  bool _loading = true;
  Object? _error;

  Map<String, dynamic>? get _offering => _offeringId == null
      ? null
      : _offerings
            .where((item) => item['id']?.toString() == _offeringId)
            .firstOrNull;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
      _slot = null;
    });
    try {
      final api = context.read<ApiClient>();
      final offerings = await api.list(
        '/api/trainers/${widget.trainer['id']}/offerings',
        authenticated: false,
      );
      _offerings = offerings.where((item) => item['isActive'] == true).toList();
      if (!_offerings.any((item) => item['id']?.toString() == _offeringId)) {
        _offeringId = _offerings.firstOrNull?['id']?.toString();
      }
      final dayStart = DateTime(_date.year, _date.month, _date.day).toUtc();
      final slots = await api.page(
        '/api/trainers/${widget.trainer['id']}/availability',
        authenticated: false,
        query: {
          'trainerServiceOfferingId': _offering?['id'],
          'fromUtc': dayStart.toIso8601String(),
          'toUtc': dayStart.add(const Duration(days: 1)).toIso8601String(),
        },
      );
      _slots = slots.items;
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pickDate() async {
    final selected = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime.now(),
      lastDate: DateTime.now().add(const Duration(days: 180)),
    );
    if (selected != null) {
      _date = selected;
      await _load();
    }
  }

  Future<void> _book() async {
    if (_offering == null || _slot == null) return;
    final api = context.read<ApiClient>();
    if (!await confirmAction(
      context,
      title: 'Potvrda rezervacije',
      message:
          '${widget.trainer['displayName']}\n${DateFormat('dd.MM.yyyy. HH:mm').format(DateTime.parse(_slot!['startsAtUtc'].toString()).toLocal())}\n${_offering!['price']} ${_offering!['currency']}',
      action: 'Rezerviši',
    )) {
      return;
    }
    try {
      await api.post(
        '/api/reservations',
        body: {
          'trainerServiceOfferingId': _offering!['id'],
          'startsAtUtc': _slot!['startsAtUtc'],
        },
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Rezervacija je kreirana.')),
        );
        Navigator.pop(context);
      }
    } on ApiProblem catch (error) {
      if (error.status == 409) await _load();
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) => PageFrame(
    title: 'Rezervacija termina',
    child: AsyncPanel(
      loading: _loading,
      error: _error,
      onRetry: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: ListTile(
              leading: const CircleAvatar(child: Icon(Icons.person)),
              title: Text(widget.trainer['displayName'].toString()),
              subtitle: Text('★ ${widget.trainer['averageRating']}'),
            ),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            key: ValueKey(_offeringId),
            initialValue: _offeringId,
            decoration: const InputDecoration(labelText: 'Usluga'),
            items: _offerings
                .map(
                  (item) => DropdownMenuItem(
                    value: item['id'].toString(),
                    child: Text(
                      '${item['name']} · ${item['durationMinutes']} min',
                    ),
                  ),
                )
                .toList(),
            onChanged: (value) {
              _offeringId = value;
              _load();
            },
          ),
          const SizedBox(height: 14),
          OutlinedButton.icon(
            onPressed: _pickDate,
            icon: const Icon(Icons.calendar_today),
            label: Text(DateFormat('dd.MM.yyyy.').format(_date)),
          ),
          const SizedBox(height: 14),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Dostupni termini',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 14),
                  if (_slots.isEmpty)
                    const Text('Nema slobodnih termina za izabrani datum.')
                  else
                    Wrap(
                      spacing: 10,
                      runSpacing: 10,
                      children: _slots.map((slot) {
                        final selected = identical(_slot, slot);
                        final time = DateTime.parse(
                          slot['startsAtUtc'].toString(),
                        ).toLocal();
                        return ChoiceChip(
                          selected: selected,
                          selectedColor: GymLinkColors.blue,
                          labelStyle: TextStyle(
                            color: selected ? Colors.white : null,
                          ),
                          label: Text(DateFormat('HH:mm').format(time)),
                          onSelected: (_) => setState(() => _slot = slot),
                        );
                      }).toList(),
                    ),
                  const SizedBox(height: 16),
                  const Wrap(
                    spacing: 14,
                    children: [
                      _Legend(
                        color: GymLinkColors.warning,
                        label: 'Djelimično',
                      ),
                      _Legend(color: GymLinkColors.danger, label: 'Nedostupno'),
                      _Legend(color: GymLinkColors.blue, label: 'Izabrano'),
                    ],
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _slot == null ? null : _book,
            child: const Text('Potvrdi rezervaciju'),
          ),
        ],
      ),
    ),
  );
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label});
  final Color color;
  final String label;
  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 12,
        height: 12,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(3),
        ),
      ),
      const SizedBox(width: 5),
      Text(label),
    ],
  );
}

class _ReviewDialog extends StatefulWidget {
  const _ReviewDialog({required this.title});
  final String title;

  @override
  State<_ReviewDialog> createState() => _ReviewDialogState();
}

class _ReviewDialogState extends State<_ReviewDialog> {
  int _rating = 5;
  final _comment = TextEditingController();

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
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
          onSelectionChanged: (value) => setState(() => _rating = value.first),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: _comment,
          maxLength: 2000,
          maxLines: 4,
          decoration: const InputDecoration(labelText: 'Komentar (opcionalno)'),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Odustani'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(context, {
          'rating': _rating,
          'comment': _comment.text.trim().isEmpty ? null : _comment.text.trim(),
        }),
        child: const Text('Objavi'),
      ),
    ],
  );
}
