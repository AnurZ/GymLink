import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/payments.dart';
import '../../core/theme.dart';
import '../../shared/cached_network_image_view.dart';
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

  static const _fallbackCenter = LatLng(43.8563, 18.4131);
  static const _fallbackZoom = 8.0;
  static const _fitPadding = EdgeInsets.all(40);

  List<Map<String, dynamic>> get _validGyms => widget.gyms
      .where((gym) => gym['latitude'] is num && gym['longitude'] is num)
      .toList();

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

  CameraFit get _resultCameraFit => CameraFit.coordinates(
    coordinates: _validGyms
        .map(
          (gym) => LatLng(
            (gym['latitude'] as num).toDouble(),
            (gym['longitude'] as num).toDouble(),
          ),
        )
        .toList(growable: false),
    padding: _fitPadding,
    minZoom: 6,
    maxZoom: 13,
  );

  void _centerMap() {
    if (_validGyms.isEmpty) {
      _mapController.move(_fallbackCenter, _fallbackZoom);
      return;
    }
    _mapController.fitCamera(_resultCameraFit);
  }

  Widget _markerContent(BuildContext context, Map<String, dynamic> gym) {
    final imageUrl = context.read<ApiClient>().mediaUrl(gym['primaryImageUrl']);
    const fallback = ColoredBox(
      color: Colors.white,
      child: Icon(Icons.fitness_center, color: GymLinkColors.blue),
    );
    return ClipOval(
      child: CachedNetworkImageView(
        key: Key('gym-map-marker-image-${gym['id']}'),
        imageUrl: imageUrl,
        fallback: fallback,
        decodeWidth: 96,
        decodeHeight: 96,
      ),
    );
  }

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
                initialCenter: _fallbackCenter,
                initialZoom: _fallbackZoom,
                initialCameraFit: valid.isEmpty ? null : _resultCameraFit,
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
                                padding: const EdgeInsets.all(3),
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
                                child: _markerContent(context, gym),
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
  Widget build(BuildContext context) {
    final imageUrl = context.read<ApiClient>().mediaUrl(gym['primaryImageUrl']);
    return Card(
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
                  child: CachedNetworkImageView(
                    imageUrl: imageUrl,
                    decodeWidth: 164,
                    decodeHeight: 164,
                    fallback: const ColoredBox(
                      color: Color(0xFFE8EDF7),
                      child: Icon(
                        Icons.fitness_center,
                        color: GymLinkColors.blue,
                      ),
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
                        Text(
                          ' ${gym['averageRating']} (${gym['reviewCount']})',
                        ),
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
}

class _GymPhotoCarousel extends StatefulWidget {
  const _GymPhotoCarousel({required this.imageUrls});

  final List<String> imageUrls;

  @override
  State<_GymPhotoCarousel> createState() => _GymPhotoCarouselState();
}

class _GymPhotoCarouselState extends State<_GymPhotoCarousel> {
  int _current = 0;

  @override
  void didUpdateWidget(covariant _GymPhotoCarousel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!listEquals(oldWidget.imageUrls, widget.imageUrls)) {
      _current = 0;
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      ClipRRect(
        borderRadius: BorderRadius.circular(18),
        child: SizedBox(
          height: 210,
          width: double.infinity,
          child: widget.imageUrls.isEmpty
              ? const ColoredBox(
                  color: Color(0xFFE8EDF7),
                  child: Icon(
                    Icons.fitness_center,
                    size: 54,
                    color: GymLinkColors.blue,
                  ),
                )
              : PageView.builder(
                  key: const Key('gym-image-carousel'),
                  itemCount: widget.imageUrls.length,
                  onPageChanged: (value) => setState(() => _current = value),
                  itemBuilder: (_, index) => CachedNetworkImageView(
                    imageUrl: widget.imageUrls[index],
                    decodeWidth: 1200,
                    decodeHeight: 700,
                    fallback: const ColoredBox(
                      color: Color(0xFFE8EDF7),
                      child: Icon(Icons.broken_image_outlined),
                    ),
                  ),
                ),
        ),
      ),
      if (widget.imageUrls.length > 1) ...[
        const SizedBox(height: 9),
        Row(
          key: const Key('gym-image-dots'),
          mainAxisAlignment: MainAxisAlignment.center,
          children: List.generate(
            widget.imageUrls.length,
            (index) => AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              width: index == _current ? 20 : 8,
              height: 8,
              margin: const EdgeInsets.symmetric(horizontal: 3),
              decoration: BoxDecoration(
                color: index == _current
                    ? GymLinkColors.blue
                    : const Color(0xFFCCD4E3),
                borderRadius: BorderRadius.circular(4),
              ),
            ),
          ),
        ),
      ],
    ],
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
    final paymentMethod = await chooseMembershipPaymentMethod(context);
    if (paymentMethod == null) return;
    if (!mounted) return;
    final isFallback = paymentMethod == MembershipPaymentMethod.stripeFallback;
    final isPayInPerson = paymentMethod == MembershipPaymentMethod.payInPerson;
    if (paymentMethod == MembershipPaymentMethod.stripe) {
      if (!await confirmAction(
        context,
        title: 'Plaćanje članarine',
        message:
            'Otvori Stripe plaćanje za ${plan['name']} '
            '(${plan['price']} ${plan['currency']})?',
        action: 'Nastavi na plaćanje',
      )) {
        return;
      }
    }
    setState(() => _purchasingPlanId = plan['id'].toString());
    try {
      if (isPayInPerson) {
        await api.post(
          '/api/membership-requests',
          body: {'membershipPlanId': plan['id'], 'paymentMethod': 2},
        );
      } else if (isFallback) {
        await api.post(
          '/api/payments/manual/memberships/pay',
          body: {'membershipPlanId': plan['id']},
        );
      } else {
        await openHostedCheckout(
          api,
          '/api/payments/memberships/checkout',
          body: {'membershipPlanId': plan['id']},
        );
      }
      await _load();
      if ((isFallback || isPayInPerson) && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              isPayInPerson
                  ? 'Zahtjev je poslan. GymAdmin će ga potvrditi nakon plaćanja uživo.'
                  : 'Testno plaćanje je uspješno evidentirano.',
            ),
          ),
        );
      }
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
    'validation_failed' when _usesOutdatedMembershipContract(error) =>
      'API nije ažuriran. Ponovo pokrenite najnoviju verziju API-ja i pokušajte ponovo.',
    'validation_failed' when error.firstFieldError != null =>
      error.firstFieldError!,
    'unsupported_membership_payment_method' =>
      'Odabrani način plaćanja nije podržan. Ažurirajte aplikaciju i pokušajte ponovo.',
    _ => error.message,
  };

  bool _usesOutdatedMembershipContract(ApiProblem error) =>
      error.fieldErrors.keys.any((key) {
        final normalized = key.trim().toLowerCase();
        return normalized == 'request' || normalized.startsWith(r'$.');
      });

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
                _GymPhotoCarousel(
                  imageUrls: (_gym!['imageUrls'] as List? ?? const [])
                      .map(context.read<ApiClient>().mediaUrl)
                      .whereType<String>()
                      .toList(),
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
                        leading: TrainerImageAvatar(
                          name: trainer['displayName'].toString(),
                          imageUrl: context.read<ApiClient>().mediaUrl(
                            trainer['imageUrl'],
                          ),
                        ),
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
  Map<String, Map<String, dynamic>> _calendarDays = const {};
  String? _offeringId;
  Map<String, dynamic>? _slot;
  DateTime _focusedMonth = DateTime(DateTime.now().year, DateTime.now().month);
  DateTime? _selectedDate;
  DateTime _bookingHorizonEnd = DateTime.now().add(const Duration(days: 56));
  bool _loading = true;
  bool _calendarLoading = false;
  bool _checkingMembership = false;
  bool _hasCoveringMembership = false;
  bool _booking = false;
  String? _membershipMessage;
  Object? _error;
  Object? _calendarError;

  Map<String, dynamic>? get _offering => _offeringId == null
      ? null
      : _offerings
            .where((item) => item['id']?.toString() == _offeringId)
            .firstOrNull;

  Map<String, dynamic>? get _selectedDay =>
      _selectedDate == null ? null : _calendarDays[_dateKey(_selectedDate!)];

  List<Map<String, dynamic>> get _slots =>
      (_selectedDay?['slots'] as List? ?? const [])
          .whereType<Map>()
          .map(Map<String, dynamic>.from)
          .toList();

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
      _selectedDate = null;
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
    } catch (error) {
      _error = error;
    } finally {
      if (mounted) setState(() => _loading = false);
    }
    if (_error == null && _offering != null) await _loadCalendar();
  }

  Future<void> _loadCalendar({
    DateTime? preserveDate,
    String? preserveStartAtUtc,
  }) async {
    final offering = _offering;
    if (offering == null) return;
    setState(() {
      _calendarLoading = true;
      _calendarError = null;
      _slot = null;
      _hasCoveringMembership = false;
      _membershipMessage = null;
    });
    try {
      final first = DateTime(_focusedMonth.year, _focusedMonth.month);
      final last = DateTime(_focusedMonth.year, _focusedMonth.month + 1, 0);
      final result = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/trainers/${widget.trainer['id']}/availability-calendar',
              authenticated: false,
              query: {
                'trainerServiceOfferingId': offering['id'],
                'fromLocalDate': _dateKey(first),
                'toLocalDate': _dateKey(last),
              },
            ))!
            as Map,
      );
      final days = <String, Map<String, dynamic>>{};
      for (final item
          in (result['days'] as List? ?? const []).whereType<Map>()) {
        days[item['date'].toString()] = Map<String, dynamic>.from(item);
      }
      final horizon = DateTime.tryParse(
        result['bookingHorizonEndsOn']?.toString() ?? '',
      );
      _calendarDays = days;
      if (horizon != null) _bookingHorizonEnd = DateUtils.dateOnly(horizon);
      final retainedDay = preserveDate == null
          ? null
          : days[_dateKey(preserveDate)];
      if (retainedDay != null && _availableSlots(retainedDay) > 0) {
        _selectedDate = DateUtils.dateOnly(preserveDate!);
        if (preserveStartAtUtc != null) {
          _slot = (retainedDay['slots'] as List? ?? const [])
              .whereType<Map>()
              .map(Map<String, dynamic>.from)
              .where(
                (item) =>
                    item['isAvailable'] == true &&
                    item['startsAtUtc']?.toString() == preserveStartAtUtc,
              )
              .firstOrNull;
        }
      } else if (preserveDate != null) {
        _selectedDate = null;
      }
    } catch (error) {
      _calendarError = error;
    } finally {
      if (mounted) setState(() => _calendarLoading = false);
    }
    if (_slot != null) await _checkMembershipCoverage();
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
                'paymentMethod':
                    paymentMethod == ReservationPaymentMethod.payInPerson
                    ? ReservationPaymentMethod.payInPerson.index
                    : ReservationPaymentMethod.stripe.index,
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
      if (paymentMethod == ReservationPaymentMethod.manual) {
        await api.post(
          '/api/payments/manual/reservations/${reservation['id']}/pay',
        );
      }
      if (mounted) {
        context.read<ReservationRefreshController>().refresh();
        if (paymentMethod == ReservationPaymentMethod.payInPerson) {
          await showPayInPersonReservationSuccess(context);
          if (!mounted) return;
        }
        if (paymentMethod == ReservationPaymentMethod.manual) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('Testno plaćanje je uspješno evidentirano.'),
            ),
          );
        }
        if (mounted) Navigator.pop(context, reservation);
      }
    } on ApiProblem catch (error) {
      if (error.status == 409) {
        final selectedStart = _slot?['startsAtUtc']?.toString();
        await _loadCalendar(
          preserveDate: _selectedDate,
          preserveStartAtUtc: selectedStart,
        );
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
    if (slot['isAvailable'] != true) return;
    setState(() {
      _slot = slot;
      _hasCoveringMembership = false;
      _membershipMessage = null;
    });
    await _checkMembershipCoverage();
  }

  void _selectDate(DateTime date) {
    final day = _calendarDays[_dateKey(date)];
    if (day == null || _availableSlots(day) == 0) return;
    setState(() {
      _selectedDate = DateUtils.dateOnly(date);
      _slot = null;
      _hasCoveringMembership = false;
      _membershipMessage = null;
    });
  }

  Future<void> _changeMonth(int offset) async {
    final target = DateTime(_focusedMonth.year, _focusedMonth.month + offset);
    if (target.isBefore(_currentMonth) || target.isAfter(_lastMonth)) return;
    setState(() {
      _focusedMonth = target;
      _selectedDate = null;
      _slot = null;
      _hasCoveringMembership = false;
      _membershipMessage = null;
    });
    await _loadCalendar();
  }

  DateTime get _currentMonth {
    final now = DateTime.now();
    return DateTime(now.year, now.month);
  }

  DateTime get _lastMonth =>
      DateTime(_bookingHorizonEnd.year, _bookingHorizonEnd.month);

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
          _trainerCard(context),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            key: const Key('booking-offering'),
            isExpanded: true,
            initialValue: _offeringId,
            decoration: const InputDecoration(labelText: 'Usluga'),
            items: _offerings
                .map(
                  (item) => DropdownMenuItem(
                    value: item['id'].toString(),
                    child: Text(
                      '${item['name']} · ${item['durationMinutes']} min',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                )
                .toList(),
            onChanged: (value) {
              setState(() {
                _offeringId = value;
                _selectedDate = null;
                _slot = null;
                _hasCoveringMembership = false;
                _membershipMessage = null;
              });
              _loadCalendar();
            },
          ),
          const SizedBox(height: 14),
          _calendarCard(context),
          const SizedBox(height: 14),
          _timeSlotsCard(context),
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
          _summaryCard(context),
          const SizedBox(height: 16),
          SizedBox(
            height: 52,
            child: FilledButton(
              key: const Key('booking-confirm'),
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
          ),
        ],
      ),
    ),
  );

  Widget _trainerCard(BuildContext context) {
    final name = widget.trainer['displayName']?.toString() ?? 'Trener';
    final credentials = widget.trainer['credentials']?.toString().trim();
    final biography = widget.trainer['biography']?.toString().trim();
    final reviewCount = (widget.trainer['reviewCount'] as num?)?.toInt() ?? 0;
    return Card(
      key: const Key('booking-trainer-card'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TrainerImageAvatar(
              name: name,
              imageUrl: context.read<ApiClient>().mediaUrl(
                widget.trainer['imageUrl'],
              ),
              radius: 29,
            ),
            const SizedBox(width: 13),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    name,
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  Text(
                    credentials == null || credentials.isEmpty
                        ? 'Personalni trener'
                        : credentials,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      const Icon(
                        Icons.star,
                        size: 18,
                        color: Color(0xFFF4B400),
                      ),
                      const SizedBox(width: 4),
                      Expanded(
                        child: Text(
                          '${widget.trainer['averageRating'] ?? 0} · $reviewCount recenzija',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                      ),
                    ],
                  ),
                  if (biography != null && biography.isNotEmpty) ...[
                    const SizedBox(height: 6),
                    Text(
                      biography,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _calendarCard(BuildContext context) => Card(
    key: const Key('booking-calendar'),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                'Izaberi datum',
                style: Theme.of(
                  context,
                ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
              ),
              const Spacer(),
              const Icon(Icons.calendar_month_outlined),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              IconButton(
                key: const Key('booking-calendar-previous'),
                tooltip: 'Prethodni mjesec',
                onPressed: _focusedMonth.isAfter(_currentMonth)
                    ? () => _changeMonth(-1)
                    : null,
                icon: const Icon(Icons.chevron_left),
              ),
              Expanded(
                child: Text(
                  '${_months[_focusedMonth.month - 1]} ${_focusedMonth.year}',
                  key: const Key('booking-calendar-month'),
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontWeight: FontWeight.w800),
                ),
              ),
              IconButton(
                key: const Key('booking-calendar-next'),
                tooltip: 'Sljedeći mjesec',
                onPressed: _focusedMonth.isBefore(_lastMonth)
                    ? () => _changeMonth(1)
                    : null,
                icon: const Icon(Icons.chevron_right),
              ),
            ],
          ),
          Row(
            children: [
              for (final label in ['P', 'U', 'S', 'Č', 'P', 'S', 'N'])
                Expanded(
                  child: Text(
                    label,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: Colors.blueGrey,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 6),
          if (_calendarLoading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 56),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_calendarError != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 24),
              child: Center(
                child: Column(
                  children: [
                    Text(
                      _calendarError is ApiProblem
                          ? (_calendarError! as ApiProblem).message
                          : 'Došlo je do neočekivane greške.',
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 8),
                    OutlinedButton.icon(
                      onPressed: _loadCalendar,
                      icon: const Icon(Icons.refresh),
                      label: const Text('Pokušaj ponovo'),
                    ),
                  ],
                ),
              ),
            )
          else
            _calendarGrid(context),
          const SizedBox(height: 12),
          const Wrap(
            spacing: 12,
            runSpacing: 8,
            children: [
              _Legend(color: Color(0xFFFFF1A8), label: 'Djelimično popunjeno'),
              _Legend(color: Color(0xFFFFD7D7), label: 'Skoro popunjeno'),
              _Legend(color: Color(0xFFE5E7EB), label: 'Termini popunjeni'),
              _Legend(color: GymLinkColors.blue, label: 'Izabrano'),
            ],
          ),
        ],
      ),
    ),
  );

  Widget _calendarGrid(BuildContext context) {
    final first = DateTime(_focusedMonth.year, _focusedMonth.month);
    final daysInMonth = DateTime(
      _focusedMonth.year,
      _focusedMonth.month + 1,
      0,
    ).day;
    final leading = first.weekday - DateTime.monday;
    final cellCount = ((leading + daysInMonth + 6) ~/ 7) * 7;
    return GridView.builder(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      itemCount: cellCount,
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 7,
        mainAxisSpacing: 5,
        crossAxisSpacing: 5,
      ),
      itemBuilder: (context, index) {
        final dayNumber = index - leading + 1;
        if (dayNumber < 1 || dayNumber > daysInMonth) {
          return const SizedBox.shrink();
        }
        final date = DateTime(
          _focusedMonth.year,
          _focusedMonth.month,
          dayNumber,
        );
        return _calendarDay(context, date);
      },
    );
  }

  Widget _calendarDay(BuildContext context, DateTime date) {
    final day = _calendarDays[_dateKey(date)];
    final total = day == null ? 0 : _totalSlots(day);
    final available = day == null ? 0 : _availableSlots(day);
    final disabled = available == 0;
    final selected =
        _selectedDate != null && DateUtils.isSameDay(_selectedDate, date);
    final partiallyFull = available > 0 && available < total;
    final almostFull = partiallyFull && available / total <= 0.25;
    final background = selected
        ? GymLinkColors.blue
        : disabled
        ? const Color(0xFFE5E7EB)
        : almostFull
        ? const Color(0xFFFFD7D7)
        : partiallyFull
        ? const Color(0xFFFFF1A8)
        : Colors.white;
    final foreground = selected
        ? Colors.white
        : disabled
        ? Theme.of(context).disabledColor
        : almostFull
        ? const Color(0xFFB42318)
        : GymLinkColors.ink;
    final today = DateUtils.isSameDay(DateTime.now(), date);
    return Semantics(
      button: !disabled,
      selected: selected,
      label:
          '${date.day}. ${_months[date.month - 1]}, $available slobodnih termina',
      child: InkWell(
        key: Key('booking-calendar-day-${_dateKey(date)}'),
        onTap: disabled ? null : () => _selectDate(date),
        borderRadius: BorderRadius.circular(10),
        child: Ink(
          decoration: BoxDecoration(
            color: background,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(
              color: today && !selected
                  ? GymLinkColors.blue
                  : Colors.transparent,
            ),
          ),
          child: Center(
            child: Text(
              '${date.day}',
              style: TextStyle(color: foreground, fontWeight: FontWeight.w700),
            ),
          ),
        ),
      ),
    );
  }

  Widget _timeSlotsCard(BuildContext context) => Card(
    key: const Key('booking-time-slots'),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                'Dostupni termini',
                style: Theme.of(
                  context,
                ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
              ),
              const Spacer(),
              const Icon(Icons.schedule_outlined, color: Colors.blueGrey),
            ],
          ),
          const SizedBox(height: 14),
          if (_selectedDate == null)
            const Text('Odaberite datum da biste vidjeli termine.')
          else if (_slots.isEmpty)
            const Text('Nema termina za izabrani datum.')
          else
            LayoutBuilder(
              builder: (context, constraints) {
                final width = (constraints.maxWidth - 24) / 4;
                return Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: _slots.map((slot) {
                    final available = slot['isAvailable'] == true;
                    final selected =
                        _slot?['startsAtUtc']?.toString() ==
                        slot['startsAtUtc']?.toString();
                    final time = DateTime.parse(
                      slot['startsAtUtc'].toString(),
                    ).toLocal();
                    return SizedBox(
                      width: width,
                      child: ChoiceChip(
                        key: Key('booking-slot-${slot['startsAtUtc']}'),
                        selected: selected,
                        showCheckmark: false,
                        selectedColor: GymLinkColors.blue,
                        disabledColor: Theme.of(
                          context,
                        ).colorScheme.surfaceContainerHighest,
                        labelStyle: TextStyle(
                          color: selected
                              ? Colors.white
                              : available
                              ? GymLinkColors.ink
                              : Theme.of(context).disabledColor,
                          fontWeight: FontWeight.w700,
                        ),
                        label: SizedBox(
                          width: double.infinity,
                          child: Text(
                            DateFormat('HH:mm').format(time),
                            textAlign: TextAlign.center,
                          ),
                        ),
                        onSelected: available ? (_) => _selectSlot(slot) : null,
                      ),
                    );
                  }).toList(),
                );
              },
            ),
        ],
      ),
    ),
  );

  Widget _summaryCard(BuildContext context) => Card(
    key: const Key('booking-summary'),
    color: GymLinkColors.blue.withValues(alpha: 0.06),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Odabrani termin',
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 14),
          _summaryRow(
            'Datum',
            _selectedDate == null
                ? '—'
                : DateFormat('dd.MM.yyyy.').format(_selectedDate!),
            const Key('booking-summary-date'),
          ),
          _summaryRow(
            'Vrijeme',
            _slot == null
                ? '—'
                : DateFormat('HH:mm').format(
                    DateTime.parse(_slot!['startsAtUtc'].toString()).toLocal(),
                  ),
            const Key('booking-summary-time'),
          ),
          _summaryRow(
            'Trener',
            widget.trainer['displayName']?.toString() ?? '—',
            const Key('booking-summary-trainer'),
          ),
          _summaryRow(
            'Usluga',
            _offering == null
                ? '—'
                : '${_offering!['name']} · ${_offering!['durationMinutes']} min',
            const Key('booking-summary-service'),
          ),
          _summaryRow(
            'Cijena',
            _offering == null
                ? '—'
                : '${_offering!['price']} ${_offering!['currency']}',
            const Key('booking-summary-price'),
          ),
        ],
      ),
    ),
  );

  Widget _summaryRow(String label, String value, Key key) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 3),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 76,
          child: Text(
            label,
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
        Expanded(
          child: Text(value, key: key, textAlign: TextAlign.end),
        ),
      ],
    ),
  );

  static const _months = [
    'Januar',
    'Februar',
    'Mart',
    'April',
    'Maj',
    'Juni',
    'Juli',
    'August',
    'Septembar',
    'Oktobar',
    'Novembar',
    'Decembar',
  ];

  int _totalSlots(Map<String, dynamic> day) =>
      (day['totalSlots'] as num?)?.toInt() ?? 0;

  int _availableSlots(Map<String, dynamic> day) =>
      (day['availableSlots'] as num?)?.toInt() ?? 0;

  String _dateKey(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}';
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
      Flexible(child: Text(label)),
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
