import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../../shared/widgets.dart';
import '../member/gym_screens.dart';
import 'recommendation_controller.dart';
import 'recommendation_repository.dart';

class RecommendationScreen extends StatefulWidget {
  const RecommendationScreen({super.key});

  @override
  State<RecommendationScreen> createState() => _RecommendationScreenState();
}

class _RecommendationScreenState extends State<RecommendationScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
      (_) => context.read<RecommendationController>().load(),
    );
  }

  Future<void> _editPreferences() async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(builder: (_) => const PreferenceEditorScreen()),
    );
  }

  void _open(RecommendationItem item) {
    if (item.isGym) {
      Navigator.push<void>(
        context,
        MaterialPageRoute(builder: (_) => GymDetailsScreen(gymId: item.gymId)),
      );
      return;
    }
    Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => BookingScreen(
          gymId: item.gymId,
          trainer: {
            'id': item.targetId,
            'displayName': item.name,
            'imageUrl': item.imageUrl,
            'averageRating': item.ratingAverage,
            'reviewCount': item.ratingCount,
          },
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<RecommendationController>();
    return Scaffold(
      appBar: AppBar(
        title: const Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.auto_awesome, color: GymLinkColors.blue),
            SizedBox(width: 10),
            Text('Preporuke', style: TextStyle(fontWeight: FontWeight.w800)),
          ],
        ),
        actions: [
          IconButton(
            key: const Key('edit-recommendation-preferences'),
            tooltip: 'Uredi preference',
            onPressed: _editPreferences,
            icon: const Icon(Icons.tune),
          ),
        ],
      ),
      body: controller.loading && controller.feed == null
          ? const _RecommendationSkeleton()
          : AsyncPanel(
              loading: false,
              error: controller.feed == null ? controller.error : null,
              onRetry: controller.load,
              child: _feed(context, controller),
            ),
    );
  }

  Widget _feed(BuildContext context, RecommendationController controller) {
    final feed = controller.feed;
    if (feed == null || feed.items.isEmpty) {
      return RefreshIndicator(
        onRefresh: () => controller.load(force: true),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          children: const [
            SizedBox(height: 100),
            EmptyState(
              title: 'Još nema preporuka',
              message:
                  'Postavite preference ili koristite aplikaciju kako bismo pripremili personalizovane preporuke.',
              icon: Icons.auto_awesome_outlined,
            ),
          ],
        ),
      );
    }
    return RefreshIndicator(
      onRefresh: () => controller.load(force: true),
      child: ListView(
        key: const Key('recommendation-feed'),
        padding: const EdgeInsets.fromLTRB(20, 22, 20, 32),
        children: [
          Text(
            'Preporučeno za vas',
            style: Theme.of(
              context,
            ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 14),
          for (final item in feed.items) ...[
            _RecommendationCard(item: item, onOpen: () => _open(item)),
            const SizedBox(height: 14),
          ],
          const SizedBox(height: 40),
          _ActivitySummary(summary: feed.summary, onEdit: _editPreferences),
        ],
      ),
    );
  }
}

class _RecommendationSkeleton extends StatelessWidget {
  const _RecommendationSkeleton();

  @override
  Widget build(BuildContext context) => ListView(
    key: const Key('recommendation-loading-skeleton'),
    padding: const EdgeInsets.fromLTRB(20, 22, 20, 32),
    children: [
      _bar(context, 180, 24),
      const SizedBox(height: 16),
      for (var index = 0; index < 3; index++) ...[
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    _bar(context, 90, 90),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          _bar(context, double.infinity, 18),
                          const SizedBox(height: 10),
                          _bar(context, 120, 14),
                          const SizedBox(height: 10),
                          _bar(context, 80, 14),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                _bar(context, double.infinity, 48),
              ],
            ),
          ),
        ),
        const SizedBox(height: 14),
      ],
    ],
  );

  static Widget _bar(BuildContext context, double width, double height) =>
      Container(
        width: width,
        height: height,
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
        ),
      );
}

class _RecommendationCard extends StatelessWidget {
  const _RecommendationCard({required this.item, required this.onOpen});
  final RecommendationItem item;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) => Card(
    key: Key('recommendation-${item.targetType}-${item.targetId}'),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              _image(context),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      item.subtitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(color: Colors.blueGrey),
                    ),
                    const SizedBox(height: 5),
                    Row(
                      children: [
                        const Icon(Icons.star, color: Colors.amber, size: 20),
                        const SizedBox(width: 4),
                        Flexible(
                          child: Text(
                            '${item.ratingAverage.toStringAsFixed(1)} (${item.ratingCount})',
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(fontWeight: FontWeight.w700),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
          DecoratedBox(
            decoration: BoxDecoration(
              color: GymLinkColors.blue.withValues(alpha: .08),
              borderRadius: BorderRadius.circular(14),
              border: Border.all(
                color: GymLinkColors.blue.withValues(alpha: .18),
              ),
            ),
            child: Padding(
              padding: const EdgeInsets.all(13),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Icon(
                    Icons.auto_awesome,
                    size: 20,
                    color: GymLinkColors.blue,
                  ),
                  const SizedBox(width: 9),
                  Expanded(
                    child: Text(
                      item.reason,
                      style: const TextStyle(color: Color(0xFF164EA6)),
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),
          FilledButton(
            key: Key('recommendation-open-${item.targetId}'),
            onPressed: onOpen,
            child: const Text('Pogledaj detalje'),
          ),
        ],
      ),
    ),
  );

  Widget _image(BuildContext context) {
    final url = context.read<RecommendationController>().repository.mediaUrl(
      item.imageUrl,
    );
    if (!item.isGym) {
      return TrainerImageAvatar(name: item.name, imageUrl: url, radius: 45);
    }
    final fallback = ColoredBox(
      color: GymLinkColors.blue.withValues(alpha: .1),
      child: const Icon(Icons.fitness_center, color: GymLinkColors.blue),
    );
    return ClipRRect(
      borderRadius: BorderRadius.circular(15),
      child: SizedBox.square(
        dimension: 90,
        child: url == null
            ? fallback
            : Image.network(
                url,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => fallback,
              ),
      ),
    );
  }
}

class _ActivitySummary extends StatelessWidget {
  const _ActivitySummary({required this.summary, required this.onEdit});
  final RecommendationSummary summary;
  final VoidCallback onEdit;

  @override
  Widget build(BuildContext context) => Card(
    key: const Key('recommendation-activity-summary'),
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Na osnovu vaših aktivnosti',
            style: Theme.of(
              context,
            ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 14),
          _row(
            'Najčešći tip treninga:',
            summary.mostFrequentTrainingType ?? 'Još nema podataka',
          ),
          _row(
            'Prosječno rezervacija:',
            '${summary.averageReservationsPerWeek.toStringAsFixed(1)}x sedmično',
          ),
          _row(
            'Preferirana lokacija:',
            summary.preferredCity ?? 'Nije izabrana',
          ),
          const SizedBox(height: 8),
          const Text(
            'Preporuke se ažuriraju na osnovu vaših aktivnosti i preferencija.',
            style: TextStyle(color: Colors.blueGrey),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: onEdit,
            icon: const Icon(Icons.tune),
            label: const Text('Uredi preference'),
          ),
        ],
      ),
    ),
  );

  Widget _row(String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 9),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(child: Text(label)),
        const SizedBox(width: 12),
        Flexible(
          child: Text(
            value,
            textAlign: TextAlign.end,
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
      ],
    ),
  );
}

class PreferenceEditorScreen extends StatefulWidget {
  const PreferenceEditorScreen({super.key});

  @override
  State<PreferenceEditorScreen> createState() => _PreferenceEditorScreenState();
}

class _PreferenceEditorScreenState extends State<PreferenceEditorScreen> {
  final List<_EditablePreference> _items = [];
  bool _initialized = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
      (_) => context.read<RecommendationController>().loadPreferenceEditor(),
    );
  }

  void _sync(RecommendationController controller) {
    if (_initialized || !controller.preferenceLoaded) return;
    _initialized = true;
    _items.addAll(
      controller.preferences.map(
        (item) => _EditablePreference(
          key: UniqueKey(),
          cityId: item.cityId,
          trainingTypeId: item.trainingTypeId,
        ),
      ),
    );
  }

  Future<void> _save(RecommendationController controller) async {
    if (_items.any(
      (item) => item.cityId == null || item.trainingTypeId == null,
    )) {
      setState(() {});
      return;
    }
    final saved = await controller.savePreferences(
      _items
          .map(
            (item) =>
                (cityId: item.cityId!, trainingTypeId: item.trainingTypeId!),
          )
          .toList(growable: false),
    );
    if (saved && mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<RecommendationController>();
    _sync(controller);
    return Scaffold(
      appBar: AppBar(
        title: const Text(
          'Preference',
          style: TextStyle(fontWeight: FontWeight.w800),
        ),
      ),
      body: AsyncPanel(
        loading:
            !_initialized &&
            (controller.preferenceLoading ||
                (!controller.preferenceLoaded &&
                    controller.preferenceError == null)),
        error: !_initialized ? controller.preferenceError : null,
        onRetry: controller.loadPreferenceEditor,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 12),
              child: Text(
                'Dodajte do tri preference i povucite ih u željeni redoslijed. Prioritet određuje težinu preporuke.',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ),
            Expanded(
              child: ReorderableListView.builder(
                key: const Key('ranked-preference-list'),
                padding: const EdgeInsets.symmetric(horizontal: 20),
                itemCount: _items.length,
                onReorderItem: (oldIndex, newIndex) {
                  setState(() {
                    final item = _items.removeAt(oldIndex);
                    _items.insert(newIndex, item);
                  });
                },
                itemBuilder: (context, index) => Padding(
                  key: _items[index].key,
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        children: [
                          Row(
                            children: [
                              const Icon(Icons.drag_handle),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  const ['Glavna', 'Druga', 'Treća'][index],
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w800,
                                  ),
                                ),
                              ),
                              IconButton(
                                tooltip: 'Ukloni',
                                onPressed: () =>
                                    setState(() => _items.removeAt(index)),
                                icon: const Icon(Icons.close),
                              ),
                            ],
                          ),
                          const SizedBox(height: 10),
                          DropdownButtonFormField<String>(
                            initialValue: _items[index].cityId,
                            decoration: const InputDecoration(
                              labelText: 'Grad',
                            ),
                            items: controller.cities
                                .map(
                                  (item) => DropdownMenuItem(
                                    value: item.id,
                                    child: Text(item.name),
                                  ),
                                )
                                .toList(growable: false),
                            onChanged: (value) => _items[index].cityId = value,
                          ),
                          const SizedBox(height: 12),
                          DropdownButtonFormField<String>(
                            initialValue: _items[index].trainingTypeId,
                            decoration: const InputDecoration(
                              labelText: 'Tip treninga',
                            ),
                            items: controller.trainingTypes
                                .map(
                                  (item) => DropdownMenuItem(
                                    value: item.id,
                                    child: Text(item.name),
                                  ),
                                )
                                .toList(growable: false),
                            onChanged: (value) =>
                                _items[index].trainingTypeId = value,
                          ),
                          if (_items[index].cityId == null ||
                              _items[index].trainingTypeId == null) ...[
                            const SizedBox(height: 8),
                            const Align(
                              alignment: Alignment.centerLeft,
                              child: Text(
                                'Izaberite grad i tip treninga.',
                                style: TextStyle(color: GymLinkColors.danger),
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
            if (controller.preferenceError != null)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                child: Text(
                  controller.preferenceError is ApiProblem
                      ? (controller.preferenceError! as ApiProblem).message
                      : 'Preference nije moguće sačuvati.',
                  style: const TextStyle(color: GymLinkColors.danger),
                ),
              ),
            SafeArea(
              top: false,
              minimum: const EdgeInsets.fromLTRB(20, 12, 20, 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (_items.length < 3)
                    OutlinedButton.icon(
                      key: const Key('add-ranked-preference'),
                      onPressed: () => setState(
                        () => _items.add(_EditablePreference(key: UniqueKey())),
                      ),
                      icon: const Icon(Icons.add),
                      label: const Text('Dodaj preferencu'),
                    ),
                  const SizedBox(height: 8),
                  FilledButton(
                    key: const Key('save-ranked-preferences'),
                    onPressed: controller.saving
                        ? null
                        : () => _save(controller),
                    child: controller.saving
                        ? const SizedBox.square(
                            dimension: 20,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Sačuvaj preference'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

final class _EditablePreference {
  _EditablePreference({required this.key, this.cityId, this.trainingTypeId});
  final Key key;
  String? cityId;
  String? trainingTypeId;
}
