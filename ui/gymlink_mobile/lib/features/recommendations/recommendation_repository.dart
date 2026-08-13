import '../../core/api.dart';

final class RecommendationItem {
  const RecommendationItem({
    required this.targetType,
    required this.targetId,
    required this.gymId,
    required this.name,
    required this.subtitle,
    required this.imageUrl,
    required this.ratingAverage,
    required this.ratingCount,
    required this.score,
    required this.reason,
  });

  final int targetType;
  final String targetId;
  final String gymId;
  final String name;
  final String subtitle;
  final String? imageUrl;
  final double ratingAverage;
  final int ratingCount;
  final double score;
  final String reason;

  bool get isGym => targetType == 0;

  factory RecommendationItem.fromJson(Map<String, dynamic> json) =>
      RecommendationItem(
        targetType: (json['targetType'] as num?)?.toInt() ?? -1,
        targetId: json['targetId']?.toString() ?? '',
        gymId: json['gymId']?.toString() ?? '',
        name: json['name']?.toString() ?? '',
        subtitle: json['subtitle']?.toString() ?? '',
        imageUrl: json['imageUrl']?.toString(),
        ratingAverage: (json['ratingAverage'] as num?)?.toDouble() ?? 0,
        ratingCount: (json['ratingCount'] as num?)?.toInt() ?? 0,
        score: (json['score'] as num?)?.toDouble() ?? 0,
        reason: json['reason']?.toString() ?? '',
      );
}

final class RecommendationSummary {
  const RecommendationSummary({
    this.mostFrequentTrainingType,
    required this.averageReservationsPerWeek,
    this.preferredCity,
  });

  final String? mostFrequentTrainingType;
  final double averageReservationsPerWeek;
  final String? preferredCity;

  factory RecommendationSummary.fromJson(Map<String, dynamic> json) =>
      RecommendationSummary(
        mostFrequentTrainingType: json['mostFrequentTrainingType']?.toString(),
        averageReservationsPerWeek:
            (json['averageReservationsPerWeek'] as num?)?.toDouble() ?? 0,
        preferredCity: json['preferredCity']?.toString(),
      );
}

final class RecommendationFeed {
  const RecommendationFeed({
    required this.items,
    required this.summary,
    required this.algorithmVersion,
    required this.generatedAtUtc,
  });

  final List<RecommendationItem> items;
  final RecommendationSummary summary;
  final String algorithmVersion;
  final DateTime? generatedAtUtc;

  factory RecommendationFeed.fromJson(
    Map<String, dynamic> json,
  ) => RecommendationFeed(
    items: (json['items'] as List? ?? const [])
        .whereType<Map>()
        .map(
          (item) =>
              RecommendationItem.fromJson(Map<String, dynamic>.from(item)),
        )
        .where((item) => item.reason.trim().isNotEmpty)
        .toList(growable: false),
    summary: RecommendationSummary.fromJson(
      Map<String, dynamic>.from(json['activitySummary'] as Map? ?? const {}),
    ),
    algorithmVersion: json['algorithmVersion']?.toString() ?? '',
    generatedAtUtc: DateTime.tryParse(json['generatedAtUtc']?.toString() ?? ''),
  );
}

final class MemberPreference {
  const MemberPreference({
    required this.rank,
    required this.cityId,
    required this.city,
    required this.trainingTypeId,
    required this.trainingType,
    required this.weight,
  });

  final int rank;
  final String cityId;
  final String city;
  final String trainingTypeId;
  final String trainingType;
  final double weight;

  factory MemberPreference.fromJson(Map<String, dynamic> json) =>
      MemberPreference(
        rank: (json['rank'] as num?)?.toInt() ?? 0,
        cityId: json['cityId']?.toString() ?? '',
        city: json['city']?.toString() ?? '',
        trainingTypeId: json['trainingTypeId']?.toString() ?? '',
        trainingType: json['trainingType']?.toString() ?? '',
        weight: (json['weight'] as num?)?.toDouble() ?? 0,
      );
}

final class PreferenceLookup {
  const PreferenceLookup({required this.id, required this.name});
  final String id;
  final String name;
}

final class RecommendationRepository {
  const RecommendationRepository(this._api);
  final ApiClient _api;

  Future<RecommendationFeed> getFeed({bool force = false}) async {
    final json = force
        ? await _api.post('/api/me/recommendations/refresh?limit=6')
        : await _api.get('/api/me/recommendations', query: {'limit': 6});
    return RecommendationFeed.fromJson(Map<String, dynamic>.from(json! as Map));
  }

  Future<List<MemberPreference>> getPreferences() async => (await _api.list(
    '/api/me/preferences',
  )).map(MemberPreference.fromJson).toList(growable: false);

  Future<List<MemberPreference>> savePreferences(
    List<({String cityId, String trainingTypeId})> items,
  ) async {
    final json = await _api.put(
      '/api/me/preferences',
      body: {
        'items': items
            .map(
              (item) => {
                'cityId': item.cityId,
                'trainingTypeId': item.trainingTypeId,
              },
            )
            .toList(growable: false),
      },
    );
    return mapListFromJson(
      json,
    ).map(MemberPreference.fromJson).toList(growable: false);
  }

  Future<({List<PreferenceLookup> cities, List<PreferenceLookup> types})>
  getLookups() async {
    final json = Map<String, dynamic>.from(
      (await _api.get('/api/reference-data/lookups', authenticated: false))!
          as Map,
    );
    List<PreferenceLookup> parse(String key) => (json[key] as List? ?? const [])
        .whereType<Map>()
        .map(
          (item) => PreferenceLookup(
            id: item['id'].toString(),
            name: item['name'].toString(),
          ),
        )
        .toList(growable: false);
    return (cities: parse('cities'), types: parse('trainingTypes'));
  }

  String? mediaUrl(String? value) => _api.mediaUrl(value);
}
