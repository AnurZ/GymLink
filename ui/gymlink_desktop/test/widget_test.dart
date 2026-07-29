import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:gymlink_desktop/core/api.dart';
import 'package:gymlink_desktop/core/theme.dart';
import 'package:gymlink_desktop/features/auth/login_screen.dart';
import 'package:gymlink_desktop/features/central/central_screens.dart';
import 'package:gymlink_desktop/features/desktop_frame.dart';
import 'package:gymlink_desktop/features/auth/password_reset_screens.dart';
import 'package:gymlink_desktop/features/gym_admin/gym_admin_screens.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('desktop shell exposes role-specific navigation', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: const DesktopFrame(
          heading: 'Test',
          roleLabel: 'Centralni administrator',
          destinations: [
            DesktopDestination(
              'Pregled',
              Icons.home_outlined,
              Center(child: Text('Sadržaj')),
            ),
            DesktopDestination(
              'Korisnici',
              Icons.people_outline,
              Center(child: Text('Korisnici sadržaj')),
            ),
          ],
        ),
      ),
    );

    expect(find.text('GymLink Admin'), findsOneWidget);
    expect(find.text('Centralni administrator'), findsOneWidget);
    expect(find.text('Sadržaj'), findsOneWidget);
    await tester.tap(find.text('Korisnici'));
    await tester.pump();
    expect(find.text('Korisnici sadržaj'), findsOneWidget);
  });
  testWidgets('password reset validates empty input without leaving the form', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: const ResetPasswordScreen(initialEmail: ''),
      ),
    );

    await tester.tap(find.text('Promijeni lozinku'));
    await tester.pump();

    expect(find.text('Unesite email.'), findsOneWidget);
    expect(find.text('Kod mora sadržavati šest cifara.'), findsOneWidget);
    expect(find.byType(ResetPasswordScreen), findsOneWidget);
  });

  testWidgets('desktop login exposes and opens password recovery', (
    tester,
  ) async {
    final router = GoRouter(
      initialLocation: '/login',
      routes: [
        GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
        GoRoute(
          path: '/forgot-password',
          builder: (_, _) => const ForgotPasswordScreen(),
        ),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      MaterialApp.router(theme: buildGymLinkTheme(), routerConfig: router),
    );

    expect(find.text('Zaboravili ste lozinku?'), findsOneWidget);
    await tester.tap(find.text('Zaboravili ste lozinku?'));
    await tester.pumpAndSettle();

    expect(find.byType(ForgotPasswordScreen), findsOneWidget);
    expect(find.text('Pošalji kod'), findsOneWidget);
  });

  testWidgets('gym creation searches locations only after explicit action', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi();
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Dodaj teretanu'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('gym-location-search')),
      'Sarajevo',
    );
    expect(api.lastLocationQuery, isNull);
    await tester.tap(find.byKey(const Key('gym-location-search-button')));
    await tester.pumpAndSettle();

    const label =
        'Grad Sarajevo, Kanton Sarajevo, Federacija Bosne i Hercegovine';
    expect(find.text(label), findsOneWidget);
    expect(api.lastLocationQuery?['query'], 'Sarajevo');
    await tester.tap(find.text(label));
    await tester.pump();
    expect(find.text('Grad/općina: Sarajevo'), findsOneWidget);
    expect(
      find.widgetWithText(TextFormField, 'Odabrana adresa'),
      findsOneWidget,
    );
  });

  testWidgets('complete gym wizard sends activation-ready payload', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1800, 1100);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi();
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Dodaj teretanu'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Naziv'),
      'Kompletna teretana',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Opis'),
      'Potpun opis nove teretane za aktivaciju.',
    );
    await tester.enterText(
      find.byKey(const Key('gym-location-search')),
      'Sarajevo',
    );
    await tester.tap(find.byKey(const Key('gym-location-search-button')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.text(
        'Grad Sarajevo, Kanton Sarajevo, Federacija Bosne i Hercegovine',
      ),
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();

    await tester.scrollUntilVisible(
      find.text('Oprema'),
      300,
      scrollable: find.byType(Scrollable).last,
    );
    expect(find.text('Oprema'), findsOneWidget);
    await tester.tap(find.text('Slobodni utezi'));
    await tester.tap(find.text('Funkcionalni trening'));
    await tester.scrollUntilVisible(
      find.widgetWithText(TextField, 'Naziv plana'),
      300,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.enterText(
      find.widgetWithText(TextField, 'Naziv plana'),
      'Standard',
    );
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('gym-admin-search')), 'owner');
    await tester.pump(const Duration(milliseconds: 350));
    await tester.pump();
    await tester.tap(find.text('Owner Account'));
    await tester.pump();
    await tester.enterText(
      find.widgetWithText(TextField, 'Razlog dodjele GymAdmin uloge'),
      'Vlasnik teretane',
    );
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(api.creationBody?['cityId'], 'city-sarajevo');
    expect(api.creationBody?['gymAdminUserId'], 'user-owner');
    expect(api.creationBody?['equipmentIds'], ['equipment-1']);
    expect(api.creationBody?['trainingTypeIds'], ['type-1']);
    expect((api.creationBody?['workingHours'] as List).length, 7);
    expect((api.creationBody?['membershipPlan'] as Map)['currency'], 'BAM');
  });

  testWidgets('gym row assigns one GymAdmin through candidate search', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi();
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    expect(find.text('Dodijeli GymAdmina'), findsOneWidget);
    expect(
      tester.getTopLeft(find.text('Dodijeli GymAdmina')).dy,
      lessThan(tester.getTopLeft(find.text('Aktivacija nije dostupna')).dy),
    );
    await tester.tap(find.text('Dodijeli GymAdmina'));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.widgetWithText(TextFormField, 'Registrovani korisnik'),
      'owner',
    );
    await tester.pump(const Duration(milliseconds: 350));
    await tester.pump();
    await tester.tap(find.text('Owner Account'));
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog dodjele'),
      'Vlasnik teretane',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Dodijeli'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(api.assignmentBody?['role'], 'GymAdmin');
    expect(api.assignmentBody?['tenantId'], 'tenant-1');
    expect(api.assignmentBody?['identifier'], 'owner@gymlink.local');

    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    final disabledItem = tester.widget<PopupMenuItem<String>>(
      find.widgetWithText(PopupMenuItem<String>, 'GymAdmin je već dodijeljen'),
    );
    expect(disabledItem.enabled, isFalse);
  });

  testWidgets('GymAdmin conflict remains inline and preserves dialog values', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(
      assignmentConflictCode: 'gym_admin_already_assigned',
    );
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dodijeli GymAdmina'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Registrovani korisnik'),
      'owner',
    );
    await tester.pump(const Duration(milliseconds: 350));
    await tester.pump();
    await tester.tap(find.text('Owner Account'));
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog dodjele'),
      'Sačuvani razlog',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Dodijeli'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(
      find.text(
        'Odabrani korisnik je već dodijeljen drugoj teretani. Prvo opozovite postojeću ulogu.',
      ),
      findsOneWidget,
    );
    expect(find.text('Sačuvani razlog'), findsOneWidget);
    expect(find.byType(AlertDialog), findsOneWidget);
  });

  testWidgets('stale activation conflict refreshes and shows blockers', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(
      activationConflictCode: 'tenant_catalog_incomplete',
    );
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Aktiviraj'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(find.text('Aktivacija nije moguća'), findsOneWidget);
    expect(find.textContaining('aktivan plan članstva'), findsWidgets);
    expect(api.activationAttempts, 1);
  });

  testWidgets('role assignment omits CentralAdmin and countries are hidden', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi();

    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: UserManagementScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dodijeli ulogu'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Član'));
    await tester.pumpAndSettle();

    expect(find.text('Centralni administrator'), findsNothing);
    await tester.tap(find.text('Trener').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Odustani'));
    await tester.pumpAndSettle();

    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: ReferenceDataScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Države'), findsNothing);
    expect(find.text('Gradovi'), findsOneWidget);
  });

  testWidgets('GymAdmin edits recurring shifts instead of manual slots', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _GymAdminScheduleApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TenantAvailabilityScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Jutarnja · 08:00–15:00'), findsWidgets);
    expect(find.text('Popodnevna · 15:00–22:00'), findsWidgets);
    expect(find.text('Dodaj termin'), findsNothing);
    await tester.tap(find.text('Jutarnja · 08:00–15:00').first);
    await tester.tap(find.text('Sačuvaj raspored'));
    await tester.pumpAndSettle();

    expect((api.savedSchedule?['shifts'] as List), isNotEmpty);
  });

  testWidgets('GymAdmin promotes an eligible active member to trainer', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _GymAdminTrainerApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TrainerManagementScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Dodaj trenera'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Active Member'), findsOneWidget);
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Biografija'),
      'Iskusni trener funkcionalnog treninga.',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog promocije'),
      'Odobrio GymAdmin',
    );
    await tester.tap(find.text('Funkcionalni trening'));
    await tester.tap(find.text('Promoviši u trenera'));
    await tester.pumpAndSettle();

    expect(api.promotionBody?['userId'], 'member-1');
    expect(api.promotionBody?['reason'], 'Odobrio GymAdmin');
    expect(api.promotionBody?['trainingTypeIds'], ['type-1']);
  });
}

Widget _centralHarness(_CentralAdminApi api) => MultiProvider(
  providers: [
    Provider<ApiClient>.value(value: api),
    ChangeNotifierProvider(create: (_) => CentralAdminRefresh()),
  ],
  child: MaterialApp(
    theme: buildGymLinkTheme(),
    home: const Scaffold(body: GymManagementScreen()),
  ),
);

class _TestTokens implements AuthTokenSource {
  @override
  String? get accessToken => 'test';

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}

class _CentralAdminApi extends ApiClient {
  _CentralAdminApi({this.assignmentConflictCode, this.activationConflictCode})
    : super(_TestTokens());

  final String? assignmentConflictCode;
  final String? activationConflictCode;
  Map<String, Object?>? lastLocationQuery;
  Map<String, dynamic>? assignmentBody;
  Map<String, dynamic>? creationBody;
  bool _assigned = false;
  int activationAttempts = 0;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/admin/gyms') {
      final activationReady =
          activationConflictCode != null && activationAttempts == 0;
      final hasAdmin = _assigned || activationConflictCode != null;
      return PagedData(
        items: [
          {
            'id': 'gym-1',
            'tenantId': 'tenant-1',
            'name': 'Nova teretana',
            'address': 'Testna 1',
            'cityName': 'Sarajevo',
            'status': 0,
            'activeGymAdminCount': hasAdmin ? 1 : 0,
            'canActivate': activationReady || _assigned,
            'missingActivationRequirements': activationReady || _assigned
                ? const <String>[]
                : activationConflictCode != null
                ? const ['membership_plan']
                : const ['gym_admin'],
          },
        ],
        page: 1,
        pageSize: 50,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/reference-data/countries') {
      return const PagedData(
        items: [
          {
            'id': 'country-bih',
            'code': 'BIH',
            'name': 'Bosna i Hercegovina',
            'isActive': true,
          },
        ],
        page: 1,
        pageSize: 10,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/reference-data/equipment') {
      return const PagedData(
        items: [
          {'id': 'equipment-1', 'name': 'Slobodni utezi', 'isActive': true},
        ],
        page: 1,
        pageSize: 100,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/reference-data/cities') {
      return const PagedData(
        items: [
          {
            'id': 'city-sarajevo',
            'countryId': 'country-bih',
            'countryName': 'Bosna i Hercegovina',
            'name': 'Sarajevo',
            'isActive': true,
          },
        ],
        page: 1,
        pageSize: 50,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/reference-data/training-types') {
      return const PagedData(
        items: [
          {'id': 'type-1', 'name': 'Funkcionalni trening', 'isActive': true},
        ],
        page: 1,
        pageSize: 100,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/users') {
      return const PagedData(
        items: [
          {
            'id': 'user-owner',
            'username': 'owner',
            'email': 'owner@gymlink.local',
            'displayName': 'Owner Account',
            'role': 'Member',
            'isActive': true,
          },
        ],
        page: 1,
        pageSize: 10,
        totalCount: 1,
      );
    }
    throw StateError('Unexpected page request: $path');
  }

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    if (path == '/api/admin/locations/search') {
      lastLocationQuery = Map<String, Object?>.from(query);
      return const [
        {
          'resultKey': 'relation:100',
          'displayName':
              'Grad Sarajevo, Kanton Sarajevo, Federacija Bosne i Hercegovine',
          'address':
              'Grad Sarajevo, Kanton Sarajevo, Federacija Bosne i Hercegovine',
          'cityId': 'city-sarajevo',
          'cityName': 'Sarajevo',
          'latitude': 43.8563,
          'longitude': 18.4131,
        },
      ];
    }
    throw StateError('Unexpected get request: $path');
  }

  @override
  Future<Object?> post(
    String path, {
    Object? body,
    bool authenticated = true,
  }) async {
    if (path == '/api/admin/tenants/tenant-1/activate') {
      activationAttempts++;
      throw ApiProblem(
        status: 409,
        code: activationConflictCode ?? 'tenant_catalog_incomplete',
        message: 'Backend conflict',
      );
    }
    if (path == '/api/admin/gyms') {
      creationBody = Map<String, dynamic>.from(body! as Map);
      _assigned = true;
      return const {};
    }
    if (path == '/api/admin/users/roles/assign') {
      assignmentBody = Map<String, dynamic>.from(body! as Map);
      if (assignmentConflictCode != null) {
        throw ApiProblem(
          status: 409,
          code: assignmentConflictCode!,
          message: 'Backend conflict',
        );
      }
      _assigned = true;
      return const {};
    }
    throw StateError('Unexpected post request: $path');
  }
}

class _GymAdminScheduleApi extends ApiClient {
  _GymAdminScheduleApi() : super(_TestTokens());

  Map<String, dynamic>? savedSchedule;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/trainers') {
      return const PagedData(
        items: [
          {'id': 'trainer-1', 'displayName': 'Test Trainer', 'isActive': true},
        ],
        page: 1,
        pageSize: 50,
        totalCount: 1,
      );
    }
    throw StateError('Unexpected page request: $path');
  }

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    if (path == '/api/tenant/trainer-availability/schedule') {
      return {
        'id': 'schedule-1',
        'trainerProfileId': 'trainer-1',
        'timeZoneId': 'Europe/Sarajevo',
        'bookingHorizonWeeks': 8,
        'shifts': <Object>[],
        'concurrencyToken': 'token-1',
      };
    }
    throw StateError('Unexpected get request: $path');
  }

  @override
  Future<Object?> put(String path, {Object? body}) async {
    if (path == '/api/tenant/trainer-availability/schedule') {
      savedSchedule = Map<String, dynamic>.from(body! as Map);
      return {...savedSchedule!, 'concurrencyToken': 'token-2'};
    }
    throw StateError('Unexpected put request: $path');
  }
}

class _GymAdminTrainerApi extends ApiClient {
  _GymAdminTrainerApi() : super(_TestTokens());

  Map<String, dynamic>? promotionBody;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/trainers' ||
        path == '/api/tenant/trainer-offerings') {
      return const PagedData(
        items: [],
        page: 1,
        pageSize: 50,
        totalCount: 0,
      );
    }
    if (path == '/api/tenant/trainer-candidates') {
      return const PagedData(
        items: [
          {
            'userId': 'member-1',
            'displayName': 'Active Member',
            'email': 'active@gymlink.local',
            'membershipPlan': 'Standard',
            'membershipEndsAtUtc': '2030-01-31T00:00:00Z',
          },
        ],
        page: 1,
        pageSize: 50,
        totalCount: 1,
      );
    }
    throw StateError('Unexpected page request: $path');
  }

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    if (path == '/api/reference-data/lookups') {
      return {
        'trainingTypes': [
          {'id': 'type-1', 'name': 'Funkcionalni trening'},
        ],
      };
    }
    throw StateError('Unexpected get request: $path');
  }

  @override
  Future<Object?> post(
    String path, {
    Object? body,
    bool authenticated = true,
  }) async {
    if (path == '/api/tenant/trainers') {
      promotionBody = Map<String, dynamic>.from(body! as Map);
      return const {};
    }
    throw StateError('Unexpected post request: $path');
  }
}
