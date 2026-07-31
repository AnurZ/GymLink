import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/payments.dart';
import '../../core/theme.dart';
import '../../shared/widgets.dart';
import '../reservations/reservation_refresh_controller.dart';

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
                key: const Key('gym-search-field'),
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
              key: const Key('gym-search-submit'),
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

class _GymMap extends StatefulWidget {
  const _GymMap({required this.gyms, required this.onOpen});
  final List<Map<String, dynamic>> gyms;
  final ValueChanged<Map<String, dynamic>> onOpen;

  @override
  State<_GymMap> createState() => _GymMapState();
}

class _GymMapState extends State<_GymMap> {
  final _mapController = MapController();

  List<Map<String, dynamic>> get _validGyms => widget.gyms
      .where((gym) => gym['latitude'] is num && gym['longitude'] is num)
      .toList();

  LatLng get _resultCenter {
    final valid = _validGyms;
    return valid.isEmpty
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
  }

  @override
  void didUpdateWidget(covariant _GymMap oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (identical(oldWidget.gyms, widget.gyms)) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _centerMap();
    });
  }

  @override
  void dispose() {
    _mapController.dispose();
    super.dispose();
  }

  void _changeZoom(double delta) {
    final camera = _mapController.camera;
    _mapController.move(camera.center, (camera.zoom + delta).clamp(6.0, 19.0));
  }

  void _centerMap() => _mapController.move(_resultCenter, 13);

  Widget _mapControlButton({
    required Key key,
    required String tooltip,
    required VoidCallback onPressed,
    required IconData icon,
  }) => SizedBox.square(
    dimension: 40,
    child: IconButton(
      key: key,
      tooltip: tooltip,
      padding: EdgeInsets.zero,
      visualDensity: VisualDensity.compact,
      constraints: const BoxConstraints.tightFor(width: 40, height: 40),
      iconSize: 20,
      onPressed: onPressed,
      icon: Icon(icon),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final valid = _validGyms;
    return SizedBox(
      key: const Key('gym-discovery-map'),
      height: 560,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(18),
        child: Stack(
          children: [
            FlutterMap(
              mapController: _mapController,
              options: MapOptions(
                initialCenter: _resultCenter,
                initialZoom: 13,
                minZoom: 6,
                maxZoom: 19,
              ),
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
                            key: Key('gym-map-marker-${gym['id']}'),
                            button: true,
                            label: gym['name'].toString(),
                            child: GestureDetector(
                              onTap: () => widget.onOpen(gym),
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
            Positioned(
              top: 8,
              right: 8,
              child: Material(
                key: const Key('gym-discovery-map-controls'),
                color: Colors.white.withValues(alpha: 0.94),
                elevation: 2,
                borderRadius: BorderRadius.circular(6),
                clipBehavior: Clip.antiAlias,
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _mapControlButton(
                      key: const Key('gym-discovery-map-zoom-in'),
                      tooltip: 'Uvećaj mapu',
                      onPressed: () => _changeZoom(1),
                      icon: Icons.add,
                    ),
                    const SizedBox(
                      height: 22,
                      child: VerticalDivider(width: 1),
                    ),
                    _mapControlButton(
                      key: const Key('gym-discovery-map-zoom-out'),
                      tooltip: 'Umanji mapu',
                      onPressed: () => _changeZoom(-1),
                      icon: Icons.remove,
                    ),
                    const SizedBox(
                      height: 22,
                      child: VerticalDivider(width: 1),
                    ),
                    _mapControlButton(
                      key: const Key('gym-discovery-map-center'),
                      tooltip: 'Centriraj mapu',
                      onPressed: _centerMap,
                      icon: Icons.center_focus_strong,
                    ),
                  ],
                ),
              ),
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
  Map<String, dynamic>? _currentMembership;
  Map<String, dynamic>? _pendingRequest;
  Object? _error;
  bool _loading = true;
  String? _purchasingPlanId;

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
        api.page(
          '/api/me/memberships',
          query: {'gymId': widget.gymId, 'currentOnly': true},
        ),
        api.page(
          '/api/me/membership-requests',
          query: {'gymId': widget.gymId, 'status': 0},
        ),
      ]);
      _gym = Map<String, dynamic>.from(results[0]! as Map);
      _plans = results[1] as List<Map<String, dynamic>>;
      _trainers = results[2] as List<Map<String, dynamic>>;
      _reviews = (results[3] as PagedData).items;
      _currentMembership = (results[4] as PagedData).items.firstOrNull;
      _pendingRequest = (results[5] as PagedData).items.firstOrNull;
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
      title: 'Plaćanje članarine',
      message:
          'Otvori Stripe plaćanje za ${plan['name']} (${plan['price']} ${plan['currency']})?',
      action: 'Nastavi na plaćanje',
    )) {
      return;
    }
    setState(() => _purchasingPlanId = plan['id'].toString());
    try {
      await openHostedCheckout(
        api,
        '/api/payments/memberships/checkout',
        body: {'membershipPlanId': plan['id']},
      );
      await _load();
    } on ApiProblem catch (error) {
      if (error.status == 409) await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(_localizedMembershipError(error))),
        );
      }
    } on StateError catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _purchasingPlanId = null);
    }
  }

  String _localizedMembershipError(ApiProblem error) => switch (error.code) {
    'current_membership_exists' =>
      'Već imate trenutno članstvo u ovoj teretani.',
    'membership_request_already_pending' =>
      'Za ovu teretanu već imate zahtjev koji čeka obradu.',
    _ => error.message,
  };

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
                if (_membershipBlockerText != null)
                  Card(
                    color: GymLinkColors.blue.withValues(alpha: 0.08),
                    child: ListTile(
                      leading: const Icon(
                        Icons.info_outline,
                        color: GymLinkColors.blue,
                      ),
                      title: const Text('Status članstva'),
                      subtitle: Text(_membershipBlockerText!),
                    ),
                  ),
                ..._plans.map(
                  (plan) => Card(
                    child: ListTile(
                      title: Text(plan['name'].toString()),
                      subtitle: Text('${plan['durationDays']} dana'),
                      trailing: FilledButton(
                        onPressed:
                            _membershipBlocked || _purchasingPlanId != null
                            ? null
                            : () => _requestMembership(plan),
                        child: _purchasingPlanId == plan['id'].toString()
                            ? const SizedBox.square(
                                dimension: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : Text('${plan['price']} ${plan['currency']}'),
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
                            builder: (_) => BookingScreen(
                              trainer: trainer,
                              gymId: widget.gymId,
                            ),
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

  bool get _membershipBlocked =>
      _pendingRequest != null || _currentMembership != null;

  String? get _membershipBlockerText {
    if (_pendingRequest != null) {
      return 'Za ovu teretanu već imate zahtjev za članstvo koji čeka obradu.';
    }
    final membership = _currentMembership;
    if (membership == null) return null;
    final status = (membership['status'] as num?)?.toInt();
    final end = DateTime.tryParse(membership['endsAtUtc']?.toString() ?? '');
    final until = end == null
        ? ''
        : ' do ${DateFormat('dd.MM.yyyy.').format(end.toLocal())}';
    return switch (status) {
      0 => 'Članstvo za ovu teretanu čeka evidenciju plaćanja$until.',
      1 => 'Već imate aktivno članstvo u ovoj teretani$until.',
      4 => 'Vaše članstvo u ovoj teretani je trenutno suspendovano$until.',
      _ => 'Već imate važeći zapis članstva za ovu teretanu.',
    };
  }

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
  const BookingScreen({required this.trainer, required this.gymId, super.key});
  final Map<String, dynamic> trainer;
  final String gymId;

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
  bool _checkingMembership = false;
  bool _hasCoveringMembership = false;
  bool _booking = false;
  String? _membershipMessage;
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

  Future<void> _load({String? preserveStartAtUtc}) async {
    setState(() {
      _loading = true;
      _error = null;
      _slot = null;
      _hasCoveringMembership = false;
      _membershipMessage = null;
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
      if (preserveStartAtUtc != null) {
        _slot = _slots
            .where(
              (item) => item['startsAtUtc']?.toString() == preserveStartAtUtc,
            )
            .firstOrNull;
      }
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
    if (_slot != null) await _checkMembershipCoverage();
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
    if (_offering == null || _slot == null || !_hasCoveringMembership) return;
    final api = context.read<ApiClient>();
    final paymentMethod = await chooseReservationPaymentMethod(context);
    if (paymentMethod == null) return;
    try {
      setState(() => _booking = true);
      final reservation = Map<String, dynamic>.from(
        (await api.post(
              '/api/reservations',
              body: {
                'trainerServiceOfferingId': _offering!['id'],
                'startsAtUtc': _slot!['startsAtUtc'],
                'paymentMethod': paymentMethod.index,
              },
            ))!
            as Map,
      );
      if (paymentMethod == ReservationPaymentMethod.stripe) {
        await openHostedCheckout(
          api,
          '/api/payments/reservations/${reservation['id']}/checkout',
        );
      }
      if (mounted) {
        context.read<ReservationRefreshController>().refresh();
        if (paymentMethod == ReservationPaymentMethod.payInPerson) {
          await showPayInPersonReservationSuccess(context);
        }
        if (mounted) Navigator.pop(context, reservation);
      }
    } on ApiProblem catch (error) {
      if (error.status == 409) {
        final selectedStart = _slot?['startsAtUtc']?.toString();
        await _load(preserveStartAtUtc: selectedStart);
      }
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(_localizedBookingError(error))));
      }
    } on StateError catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _booking = false);
    }
  }

  Future<void> _selectSlot(Map<String, dynamic> slot) async {
    setState(() {
      _slot = slot;
      _hasCoveringMembership = false;
      _membershipMessage = null;
    });
    await _checkMembershipCoverage();
  }

  Future<void> _checkMembershipCoverage() async {
    final offering = _offering;
    final slot = _slot;
    if (offering == null || slot == null) return;
    final startsAt = DateTime.parse(slot['startsAtUtc'].toString()).toUtc();
    final duration = (offering['durationMinutes'] as num).toInt();
    final endsAt = startsAt.add(Duration(minutes: duration));
    setState(() => _checkingMembership = true);
    try {
      final result = await context.read<ApiClient>().page(
        '/api/me/memberships',
        query: {
          'gymId': widget.gymId,
          'status': 1,
          'coversFromUtc': startsAt.toIso8601String(),
          'coversToUtc': endsAt.toIso8601String(),
        },
      );
      if (!mounted || _slot != slot) return;
      setState(() {
        _hasCoveringMembership = result.items.isNotEmpty;
        _membershipMessage = _hasCoveringMembership
            ? null
            : 'Za ovaj termin je potrebno aktivno članstvo koje pokriva cijeli termin.';
      });
    } on ApiProblem catch (error) {
      if (!mounted || _slot != slot) return;
      setState(() {
        _hasCoveringMembership = false;
        _membershipMessage = error.message;
      });
    } finally {
      if (mounted && _slot == slot) {
        setState(() => _checkingMembership = false);
      }
    }
  }

  String _localizedBookingError(ApiProblem error) => switch (error.code) {
    'covering_membership_required' =>
      'Za ovaj termin je potrebno aktivno članstvo koje pokriva cijeli termin.',
    'reservation_overlap' =>
      'Već imate rezervaciju koja se preklapa sa izabranim terminom.',
    'reservation_conflict' =>
      'Termin je u međuvremenu zauzet. Odaberite drugi termin.',
    _ => error.message,
  };

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
                          onSelected: (_) => _selectSlot(slot),
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
          if (_checkingMembership)
            const LinearProgressIndicator()
          else if (_membershipMessage != null)
            Card(
              color: GymLinkColors.warning.withValues(alpha: 0.12),
              child: ListTile(
                leading: const Icon(Icons.card_membership_outlined),
                title: const Text('Članstvo je obavezno'),
                subtitle: Text(_membershipMessage!),
                trailing: TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: const Text('Nazad na članarine'),
                ),
              ),
            ),
          if (_checkingMembership || _membershipMessage != null)
            const SizedBox(height: 12),
          FilledButton(
            onPressed:
                _slot == null ||
                    _checkingMembership ||
                    !_hasCoveringMembership ||
                    _booking
                ? null
                : _book,
            child: _booking
                ? const SizedBox.square(
                    dimension: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Rezerviši termin'),
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
