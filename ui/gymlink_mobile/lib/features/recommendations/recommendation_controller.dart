import 'package:flutter/foundation.dart';

import 'recommendation_repository.dart';

final class RecommendationController extends ChangeNotifier {
  RecommendationController(this._repository);
  final RecommendationRepository _repository;

  RecommendationFeed? feed;
  List<MemberPreference> preferences = const [];
  List<PreferenceLookup> cities = const [];
  List<PreferenceLookup> trainingTypes = const [];
  Object? error;
  Object? preferenceError;
  bool loading = false;
  bool preferenceLoading = false;
  bool preferenceLoaded = false;
  bool saving = false;

  RecommendationRepository get repository => _repository;

  Future<void> load({bool force = false}) async {
    if (loading) return;
    loading = true;
    error = null;
    notifyListeners();
    try {
      feed = await _repository.getFeed(force: force);
    } catch (value) {
      error = value;
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> loadPreferenceEditor() async {
    preferenceLoading = true;
    preferenceLoaded = false;
    preferenceError = null;
    notifyListeners();
    try {
      final results = await Future.wait([
        _repository.getPreferences(),
        _repository.getLookups(),
      ]);
      preferences = results[0] as List<MemberPreference>;
      final lookups =
          results[1]
              as ({
                List<PreferenceLookup> cities,
                List<PreferenceLookup> types,
              });
      cities = lookups.cities;
      trainingTypes = lookups.types;
      preferenceLoaded = true;
    } catch (value) {
      preferenceError = value;
    } finally {
      preferenceLoading = false;
      notifyListeners();
    }
  }

  Future<bool> savePreferences(
    List<({String cityId, String trainingTypeId})> items,
  ) async {
    saving = true;
    preferenceError = null;
    notifyListeners();
    try {
      preferences = await _repository.savePreferences(items);
      await load(force: true);
      return true;
    } catch (value) {
      preferenceError = value;
      return false;
    } finally {
      saving = false;
      notifyListeners();
    }
  }
}
