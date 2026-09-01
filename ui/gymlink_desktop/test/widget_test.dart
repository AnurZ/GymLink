import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fl_chart/fl_chart.dart';
import 'package:go_router/go_router.dart';
import 'package:gymlink_desktop/core/api.dart';
import 'package:gymlink_desktop/core/app_errors.dart';
import 'package:gymlink_desktop/core/theme.dart';
import 'package:gymlink_desktop/features/auth/login_screen.dart';
import 'package:gymlink_desktop/features/central/central_screens.dart';
import 'package:gymlink_desktop/features/desktop_frame.dart';
import 'package:gymlink_desktop/features/auth/password_reset_screens.dart';
import 'package:gymlink_desktop/features/gym_admin/gym_admin_screens.dart';
import 'package:gymlink_desktop/features/notifications/notification_screen.dart';
import 'package:gymlink_desktop/features/reporting/reporting_screens.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('global error banner resets its timeout and can be closed', (
    tester,
  ) async {
    addTearDown(AppErrorReporter.clear);
    await tester.pumpWidget(
      const MaterialApp(
        home: AppErrorBanner(child: Scaffold(body: SizedBox.expand())),
      ),
    );

    AppErrorReporter.reportUnexpected('Prva greška');
    await tester.pump();
    expect(find.text('Prva greška'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
    AppErrorReporter.reportUnexpected('Nova greška');
    await tester.pump();
    await tester.pump(const Duration(seconds: 4));
    expect(find.text('Nova greška'), findsOneWidget);

    await tester.pump(const Duration(seconds: 1));
    expect(find.text('Nova greška'), findsNothing);

    AppErrorReporter.reportUnexpected('Zatvori me');
    await tester.pump();
    await tester.tap(find.byTooltip('Zatvori'));
    await tester.pump();
    expect(find.text('Zatvori me'), findsNothing);
  });

  test('small statistics counts use an integer axis', () {
    final scale = reservationCountAxis(2);
    expect(scale.interval, 1);
    expect(scale.maximum, 3);
  });

  testWidgets('desktop notification opens detail before mark-read', (
    tester,
  ) async {
    final api = _NotificationApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const NotificationScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(api.readRequests, 0);
    expect(find.text('Označi sve kao pročitano'), findsOneWidget);
    expect(find.byKey(const Key('notifications-all-tab')), findsOneWidget);
    expect(find.byKey(const Key('notifications-unread-tab')), findsOneWidget);
    await tester.tap(find.text('Članstvo aktivirano'));
    await tester.pumpAndSettle();

    expect(api.readRequests, 1);
    expect(find.byType(NotificationDetailScreen), findsOneWidget);
    expect(find.text('Detalji obavijesti'), findsOneWidget);
    expect(find.text('Članarina je aktivirana.'), findsWidgets);
  });

  testWidgets(
    'Figure 7 statistics load independently and export both reports',
    (tester) async {
      tester.view.physicalSize = const Size(1500, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _ReportingApi();
      var saved = false;
      var printed = false;
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: Scaffold(
              body: GymAdminReportsScreen(
                saveReport: (_) async {
                  saved = true;
                  return true;
                },
                printReport: (_) async => printed = true,
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Izvještaji i statistika'), findsOneWidget);
      expect(find.text('Broj aktivnih članova'), findsOneWidget);
      expect(find.text('142'), findsWidgets);
      expect(
        find.text(
          'Periodi članstva: 150 (+20.0% prema kraju prethodnog mjeseca)',
        ),
        findsOneWidget,
      );
      expect(find.text('Broj članova po mjesecima'), findsOneWidget);
      expect(find.text('Tipovi članstva'), findsOneWidget);
      expect(statisticsPalette, const [
        Color(0xFF2864E8),
        Color(0xFF0F9D8A),
        Color(0xFFF59E0B),
        Color(0xFF7C3AED),
        Color(0xFFE85D75),
        Color(0xFF16A34A),
      ]);
      final bars = tester.widget<BarChart>(find.byType(BarChart));
      expect(
        bars.data.barGroups.map((group) => group.barRods.single.color).toSet(),
        hasLength(6),
      );
      expect(
        bars.data.barGroups.expand((group) => group.showingTooltipIndicators),
        isEmpty,
      );
      final firstGroup = bars.data.barGroups.first;
      final tooltip = bars.data.barTouchData.touchTooltipData.getTooltipItem(
        firstGroup,
        0,
        firstGroup.barRods.single,
        0,
      );
      expect(tooltip!.text, '30 članova');
      expect(find.text('4.0'), findsNothing);
      expect(find.text('8.0'), findsNothing);
      final pie = tester.widget<PieChart>(find.byType(PieChart));
      expect(
        pie.data.sections.map((section) => section.color),
        statisticsPalette.take(pie.data.sections.length),
      );
      api.requestedPaths.clear();
      await tester.tap(find.byKey(const Key('refresh-gym-statistics')));
      await tester.pumpAndSettle();
      expect(api.requestedPaths, [
        '/api/tenant/statistics/summary',
        '/api/tenant/statistics/members-by-month',
        '/api/tenant/statistics/membership-plan-distribution',
      ]);
      final exportTrigger = tester.widget<AnimatedContainer>(
        find.byKey(const Key('export-pdf-trigger')),
      );
      final exportContext = tester.element(
        find.byKey(const Key('export-pdf-trigger')),
      );
      expect(
        (exportTrigger.decoration! as BoxDecoration).color,
        Theme.of(exportContext).colorScheme.primary,
      );

      await tester.tap(find.byKey(const Key('export-pdf-menu')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('export-memberships')));
      await tester.pumpAndSettle();
      expect(api.downloadedPaths, ['/api/tenant/reports/memberships.pdf']);
      expect(find.text('PDF izvještaj je spreman'), findsOneWidget);

      await tester.tap(find.text('Sačuvaj'));
      await tester.pump();
      await tester.tap(find.text('Štampaj'));
      await tester.pump();
      expect(saved, isTrue);
      expect(printed, isTrue);

      await tester.tap(find.text('Zatvori'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('export-pdf-menu')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('export-reservations')));
      await tester.pumpAndSettle();
      expect(api.downloadedPaths, [
        '/api/tenant/reports/memberships.pdf',
        '/api/tenant/reports/reservations.pdf',
      ]);
    },
  );

  testWidgets(
    'membership period detail presents signed and zero-baseline changes',
    (tester) async {
      tester.view.physicalSize = const Size(1500, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      for (final scenario in [
        (
          api: _ReportingApi(
            membershipPeriodCount: 80,
            previousMonthEndMembershipPeriodCount: 100,
            membershipPeriodChangePercentage: -20,
          ),
          detail:
              'Periodi članstva: 80 (-20.0% prema kraju prethodnog mjeseca)',
        ),
        (
          api: _ReportingApi(
            membershipPeriodCount: 3,
            previousMonthEndMembershipPeriodCount: 0,
            membershipPeriodChangePercentage: 100,
          ),
          detail:
              'Periodi članstva: 3 (+100.0% prema kraju prethodnog mjeseca)',
        ),
      ]) {
        await tester.pumpWidget(
          Provider<ApiClient>.value(
            value: scenario.api,
            child: MaterialApp(
              theme: buildGymLinkTheme(),
              home: const Scaffold(body: GymAdminReportsScreen()),
            ),
          ),
        );
        await tester.pumpAndSettle();
        expect(find.text(scenario.detail), findsOneWidget);
        await tester.pumpWidget(const SizedBox.shrink());
        await tester.pump();
      }
    },
  );

  testWidgets('CentralAdmin reservations chart uses integer count scale', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: _CentralReportingApi(),
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: CentralStatisticsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    final scale = reservationCountAxis(31);
    expect(scale.interval, 10);
    expect(scale.maximum, 40);
    final chart = tester.widget<BarChart>(find.byType(BarChart));
    expect(chart.data.maxY, 40);
    expect(chart.data.gridData.horizontalInterval, 10);
    expect(
      chart.data.barGroups.map((group) => group.barRods.single.color).toSet(),
      {statisticsPalette.first},
    );
    expect(find.byKey(const Key('reservations-axis-title')), findsOneWidget);
    expect(find.text('Period: 01.03.2026. – 31.08.2026.'), findsOneWidget);
    expect(chart.data.barGroups.last.barRods.single.toY, 31);
    expect(find.textContaining('PDF'), findsNothing);
    expect(find.byKey(const Key('export-pdf-menu')), findsNothing);
  });

  testWidgets('CentralAdmin gym location is bounded and exposes full text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1366, 768);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    const address =
        'Ulica sa izuzetno dugim nazivom i dodatnim opisom ulaza broj 123, Sarajevo';
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(
      _centralHarness(_CentralAdminApi(address: address)),
    );
    await tester.pumpAndSettle();

    final text = tester.widget<Text>(find.text(address));
    expect(text.maxLines, 1);
    expect(text.overflow, TextOverflow.ellipsis);
    expect(tester.getSize(find.text(address)).width, 280);
    expect(find.byTooltip(address), findsOneWidget);
    final semantic = tester.widget<Semantics>(
      find
          .ancestor(of: find.text(address), matching: find.byType(Semantics))
          .first,
    );
    expect(semantic.properties.label, address);
    expect(tester.takeException(), isNull);
    semantics.dispose();
  });

  testWidgets(
    'report layout supports constrained width and explicit empty charts',
    (tester) async {
      tester.view.physicalSize = const Size(760, 720);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: _ReportingApi(emptyCharts: true),
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: const Scaffold(body: GymAdminReportsScreen()),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(
        find.text('Nema podataka za posljednjih šest mjeseci.'),
        findsOneWidget,
      );
      expect(find.text('Nema aktivnih članstava.'), findsOneWidget);
    },
  );

  testWidgets('report export prevents duplicate generation requests', (
    tester,
  ) async {
    final api = _DelayedReportingApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: GymAdminReportsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('export-pdf-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('export-memberships')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('export-pdf-menu')));
    await tester.pump();

    expect(api.downloadedPaths, ['/api/tenant/reports/memberships.pdf']);
    api.pending.complete(_ReportingApi.report);
    await tester.pumpAndSettle();
  });

  testWidgets(
    'report save cancellation and print failure have distinct messages',
    (tester) async {
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: _ReportingApi(),
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: Scaffold(
              body: GymAdminReportsScreen(
                saveReport: (_) async => false,
                printReport: (_) async => throw StateError('printer_failed'),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('export-pdf-menu')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('export-memberships')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Sačuvaj'));
      await tester.pump();
      expect(find.text('Spremanje izvještaja je otkazano.'), findsOneWidget);
      await tester.tap(find.text('Štampaj'));
      await tester.pump();
      expect(find.textContaining('PDF nije moguće štampati'), findsOneWidget);
    },
  );

  testWidgets('statistics chart failure keeps successful summary cards', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1500, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _ReportingApi(failMonths: true);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: GymAdminReportsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('142'), findsWidgets);
    expect(find.textContaining('monthly_failed'), findsOneWidget);
    expect(find.text('Pokušaj ponovo'), findsOneWidget);
  });

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

  testWidgets('password reset returns to desktop login', (tester) async {
    final router = GoRouter(
      initialLocation: '/reset-password',
      routes: [
        GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
        GoRoute(
          path: '/reset-password',
          builder: (_, _) => const ResetPasswordScreen(initialEmail: ''),
        ),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      MaterialApp.router(theme: buildGymLinkTheme(), routerConfig: router),
    );

    final returnButton = find.text('Nazad na prijavu');
    expect(returnButton, findsOneWidget);
    await tester.ensureVisible(returnButton);
    await tester.tap(returnButton);
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsOneWidget);
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

  testWidgets('GymAdmin Termini filter omits Pending and keeps enum values', (
    tester,
  ) async {
    final api = _GymAdminReservationsApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TenantReservationsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('reservation-status-filter')));
    await tester.pumpAndSettle();
    expect(find.text('Pending'), findsNothing);
    expect(find.text('Confirmed'), findsOneWidget);
    expect(find.text('Completed'), findsOneWidget);
    expect(find.text('Cancelled'), findsOneWidget);

    await tester.tap(find.text('Confirmed'));
    await tester.pumpAndSettle();
    expect(api.requestedStatuses, [null, 1]);
  });

  testWidgets(
    'unified memberships use request and membership columns/actions',
    (tester) async {
      tester.view.physicalSize = const Size(1920, 1080);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _GymAdminMembershipRequestsApi();
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: const Scaffold(body: TenantMembershipRequestsScreen()),
          ),
        ),
      );
      await tester.pumpAndSettle();

      for (final heading in [
        'Član',
        'Članarina',
        'Plaćanje',
        'Zahtjev',
        'Status članstva',
        'Period',
        'Akcije',
      ]) {
        expect(find.text(heading), findsWidgets);
      }
      expect(find.text('Cash Member'), findsOneWidget);
      expect(find.text('cash@gymlink.local'), findsOneWidget);
      expect(find.text('Plati uživo'), findsWidgets);
      expect(find.text('Stripe'), findsWidgets);
      expect(find.text('Stripe fallback'), findsNothing);
      await tester.tap(
        find.byKey(const Key('membership-payment-method-filter')),
      );
      await tester.pumpAndSettle();
      expect(find.text('Stripe fallback'), findsNothing);
      await tester.tap(find.text('Stripe').last);
      await tester.pumpAndSettle();
      expect(api.requestedPaymentCategories.last, 0);
      expect(find.byTooltip('Aktiviraj nakon naplate'), findsOneWidget);
      expect(find.byTooltip('Odbij'), findsOneWidget);
      expect(find.byTooltip('Detalji'), findsNWidgets(3));
      expect(find.text('09.08.2026'), findsNothing);
      expect(find.text('07.08.2026'), findsNothing);
      final detailButtons = find.byTooltip('Detalji');
      final detailCenters = List.generate(
        detailButtons.evaluate().length,
        (index) => tester.getCenter(detailButtons.at(index)).dx,
      );
      expect(detailCenters.toSet(), hasLength(1));
      expect(find.text('Active'), findsOneWidget);
      expect(find.byTooltip('Akcije članstva'), findsOneWidget);
      expect(tester.getRect(find.text('Akcije')).right, lessThan(1920));
      await tester.tap(find.byTooltip('Akcije članstva'));
      await tester.pumpAndSettle();
      expect(find.text('Suspenduj'), findsOneWidget);
      expect(find.text('Otkaži'), findsOneWidget);
      await tester.tap(find.text('Suspenduj'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Odustani'));
      await tester.pumpAndSettle();

      await tester.tap(
        find.byKey(const Key('linked-membership-status-filter')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Active').last);
      await tester.pumpAndSettle();
      expect(api.requestedMembershipStatuses.last, 1);
    },
  );

  testWidgets('membership table keeps horizontal scroll on narrow windows', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(800, 700);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: _GymAdminMembershipRequestsApi(),
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TenantMembershipRequestsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    final horizontal = find.byWidgetPredicate(
      (widget) =>
          widget is SingleChildScrollView &&
          widget.scrollDirection == Axis.horizontal,
    );
    expect(horizontal, findsOneWidget);
    await tester.drag(horizontal, const Offset(-600, 0));
    await tester.pumpAndSettle();
    expect(find.text('Akcije'), findsOneWidget);
  });

  testWidgets('reservation refresh preserves existing results on failure', (
    tester,
  ) async {
    final api = _GymAdminReservationsApi(withItem: true, failAfterFirst: true);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TenantReservationsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Test Member · Test Trainer'), findsOneWidget);

    await tester.tap(find.byKey(const Key('refresh-reservations')));
    await tester.pumpAndSettle();

    expect(api.requestedStatuses, [null, null]);
    expect(find.text('Test Member · Test Trainer'), findsOneWidget);
    expect(find.textContaining('prethodni podaci'), findsOneWidget);
  });

  testWidgets('GymAdmin completes reservation with localized confirmation', (
    tester,
  ) async {
    final api = _GymAdminReservationsApi(withItem: true);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: TenantReservationsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Označi završenom'));
    await tester.pumpAndSettle();
    expect(
      find.text('Želite li označiti rezervaciju završenom?'),
      findsOneWidget,
    );
    await tester.tap(find.text('Označi završenom'));
    await tester.pumpAndSettle();

    expect(api.completed, isTrue);
  });

  testWidgets('successful tenant mutation with failed refresh keeps the view', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(successfulActivationWithRefreshFailure: true);
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    expect(find.text('Naziv teretane'), findsOneWidget);
    expect(find.text('Lokacija'), findsOneWidget);
    expect(find.text('Broj članova'), findsOneWidget);
    expect(find.text('Status'), findsWidgets);
    expect(find.text('Akcije'), findsOneWidget);
    expect(find.text('12'), findsOneWidget);
    expect(find.byType(PopupMenuButton<String>), findsNothing);

    await tester.tap(find.byTooltip('Aktiviraj'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Spremna za rad',
    );
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(
      find.text(
        'Promjena je sačuvana, ali lista nije osvježena. Pokušajte ponovo.',
      ),
      findsOneWidget,
    );
    expect(find.text('Nova teretana'), findsOneWidget);
    expect(find.textContaining('Prikaz nije moguće učitati'), findsNothing);
    expect(find.text('Status teretane je uspješno promijenjen.'), findsNothing);
  });

  testWidgets('CentralAdmin gym table paginates and search resets the page', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(gymTotalCount: 21);
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    expect(find.text('Stranica 1 od 2'), findsOneWidget);
    await tester.tap(find.byTooltip('Sljedeća stranica'));
    await tester.pumpAndSettle();
    expect(api.gymQueries.last['page'], 2);

    await tester.enterText(find.byType(TextField).first, 'Arena');
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    expect(api.gymQueries.last['page'], 1);
    expect(api.gymQueries.last['query'], 'Arena');
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

    await _openGymWizardAtLocation(tester);
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

  testWidgets('map click resolves an editable nearest address', (tester) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi();
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await _openGymWizardAtLocation(tester);
    final map = find.byKey(const Key('gym-location-map'));
    await tester.scrollUntilVisible(
      map,
      250,
      scrollable: find.byType(Scrollable).last,
    );
    expect(tester.getSize(map).height, greaterThanOrEqualTo(400));
    await tester.tapAt(tester.getCenter(map));
    await tester.pump(const Duration(milliseconds: 301));
    await tester.pumpAndSettle();

    expect(api.lastReverseQuery, isNotNull);
    expect(find.text('Grad/općina: Sarajevo'), findsOneWidget);
    final addressField = find.widgetWithText(TextFormField, 'Odabrana adresa');
    expect(
      tester.widget<TextFormField>(addressField).controller!.text,
      'Zmaja od Bosne 12, Sarajevo, Bosna i Hercegovina',
    );
    await tester.enterText(addressField, 'Zmaja od Bosne 12, ulaz B');
    expect(
      tester.widget<TextFormField>(addressField).controller!.text,
      'Zmaja od Bosne 12, ulaz B',
    );
  });

  testWidgets('gym map remains substantially visible at 1366x768', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1366, 768);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(_centralHarness(_CentralAdminApi()));
    await tester.pumpAndSettle();

    await _openGymWizardAtLocation(tester);
    expect(
      find.text('Savjet: Pretražite adresu ili odaberite tačku na mapi.'),
      findsOneWidget,
    );
    final mapRect = tester.getRect(find.byKey(const Key('gym-location-map')));
    final viewportRect = tester.getRect(
      find.byKey(const Key('gym-location-scroll')),
    );
    final visibleMapHeight = mapRect.intersect(viewportRect).height;

    expect(mapRect.height, greaterThanOrEqualTo(400));
    expect(visibleMapHeight, greaterThanOrEqualTo(280));
    expect(find.byKey(const Key('gym-map-zoom-in')), findsOneWidget);
    expect(find.byKey(const Key('gym-map-zoom-out')), findsOneWidget);
    expect(find.byKey(const Key('gym-map-center')), findsOneWidget);
    final controlsRect = tester.getRect(
      find.byKey(const Key('gym-map-controls')),
    );
    expect(controlsRect.height, lessThanOrEqualTo(34));
    expect(controlsRect.width, lessThanOrEqualTo(100));
    expect(controlsRect.top - mapRect.top, lessThanOrEqualTo(10));
    expect(mapRect.right - controlsRect.right, lessThanOrEqualTo(10));
    await tester.tap(find.byKey(const Key('gym-map-zoom-in')));
    await tester.tap(find.byKey(const Key('gym-map-zoom-out')));
    await tester.tap(find.byKey(const Key('gym-map-center')));
    await tester.pump();
    expect(tester.takeException(), isNull);
  });

  testWidgets('latest map click wins and failed lookup enables manual fallback', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _DelayedReverseCentralAdminApi();
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await _openGymWizardAtLocation(tester);
    final map = find.byKey(const Key('gym-location-map'));
    await tester.scrollUntilVisible(
      map,
      250,
      scrollable: find.byType(Scrollable).last,
    );
    final rect = tester.getRect(map);
    await tester.tapAt(rect.center - const Offset(50, 0));
    await tester.pump(const Duration(milliseconds: 700));
    await tester.pump();
    expect(api.pending, hasLength(1));

    await tester.tapAt(rect.center + const Offset(50, 0));
    await tester.pump(const Duration(milliseconds: 700));
    await tester.pump();
    expect(api.pending, hasLength(2));
    api.pending[1].complete(const {
      'resultKey': 'way:second',
      'displayName': 'Druga adresa',
      'address': 'Druga adresa 2',
      'cityId': 'city-sarajevo',
      'cityName': 'Sarajevo',
    });
    await tester.pump();
    api.pending[0].complete(const {
      'resultKey': 'way:first',
      'displayName': 'Prva adresa',
      'address': 'Prva adresa 1',
      'cityId': 'city-sarajevo',
      'cityName': 'Sarajevo',
    });
    await tester.pump();

    final addressField = find.widgetWithText(TextFormField, 'Odabrana adresa');
    expect(
      tester.widget<TextFormField>(addressField).controller!.text,
      'Druga adresa 2',
    );

    api.problem = const ApiProblem(
      status: 404,
      code: 'location_not_resolved',
      message: 'Not resolved',
    );
    await tester.tapAt(rect.center);
    await tester.pump(const Duration(milliseconds: 700));
    await tester.pump();
    expect(
      find.text(
        'Adresa nije automatski pronađena. Unesite grad i adresu ručno; označene koordinate su sačuvane.',
      ),
      findsOneWidget,
    );
    expect(find.byKey(const Key('gym-manual-city')), findsOneWidget);
    expect(
      find.byKey(const Key('gym-manual-city-reference-hint')),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('gym-manual-city')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Sarajevo').last);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Adresa'),
      'Ručna adresa 12',
    );
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    expect(find.text('Radno vrijeme'), findsWidgets);
  });

  testWidgets('complete gym wizard sends activation-ready payload', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1800, 1100);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(reverseUnavailable: true);
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Dodaj teretanu'));
    await tester.pumpAndSettle();
    final initialStepper = tester.widget<Stepper>(find.byType(Stepper));
    expect(initialStepper.steps, hasLength(5));
    expect(
      initialStepper.steps
          .map((step) => (step.title as Text).data)
          .toList(growable: false),
      ['Osnovno', 'Lokacija', 'Radno vrijeme', 'Katalog', 'Pregled'],
    );
    expect(find.text('Razlog dodjele GymAdmin uloge'), findsNothing);
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Naziv'),
      'Kompletna teretana',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Opis'),
      'Potpun opis nove teretane za aktivaciju.',
    );
    await tester.enterText(find.byKey(const Key('gym-admin-search')), 'owner');
    await tester.pump(const Duration(milliseconds: 350));
    await tester.pump();
    await tester.tap(find.text('Owner Account'));
    await tester.pump();
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('gym-manual-location-toggle')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('gym-manual-city')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Sarajevo').last);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Adresa'),
      'Zmaja od Bosne 12, ulaz B',
    );
    final map = find.byKey(const Key('gym-location-map'));
    await tester.scrollUntilVisible(
      map,
      250,
      scrollable: find.byType(Scrollable).last,
    );
    await tester.tapAt(tester.getCenter(map));
    await tester.pump(const Duration(milliseconds: 301));
    await tester.pumpAndSettle();
    final stepper = tester.widget<Stepper>(find.byType(Stepper));
    expect(stepper.controller, isNotNull);
    stepper.controller!.jumpTo(stepper.controller!.position.maxScrollExtent);
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    expect(stepper.controller!.offset, 0);

    expect(find.text('Radno vrijeme'), findsWidgets);
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
    await tester.tap(find.byKey(const Key('gym-create-continue')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(api.creationBody?['cityId'], 'city-sarajevo');
    expect(api.creationBody?['address'], 'Zmaja od Bosne 12, ulaz B');
    expect(api.creationBody?['latitude'], api.lastReverseQuery?['latitude']);
    expect(api.creationBody?['longitude'], api.lastReverseQuery?['longitude']);
    expect(api.creationBody?['gymAdminUserId'], 'user-owner');
    expect(api.creationBody, isNot(contains('gymAdminAssignmentReason')));
    expect(api.creationBody?['equipmentIds'], ['equipment-1']);
    expect(api.creationBody?['trainingTypeIds'], ['type-1']);
    expect((api.creationBody?['workingHours'] as List).length, 7);
    expect((api.creationBody?['membershipPlan'] as Map)['currency'], 'BAM');
  });

  testWidgets(
    'gym creation conflict returns to GymAdmin selection with inline error',
    (tester) async {
      tester.view.physicalSize = const Size(1800, 1100);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _CentralAdminApi(
        creationConflictCode: 'gym_admin_already_assigned',
      );
      await tester.pumpWidget(_centralHarness(api));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Dodaj teretanu'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Naziv'),
        'Konflikt teretana',
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Opis'),
        'Potpun opis teretane za provjeru konflikta.',
      );
      await tester.enterText(
        find.byKey(const Key('gym-admin-search')),
        'owner',
      );
      await tester.pump(const Duration(milliseconds: 350));
      await tester.pump();
      await tester.tap(find.text('Owner Account'));
      await tester.pump();
      await tester.tap(find.byKey(const Key('gym-create-continue')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('gym-location-search')),
        'Sarajevo',
      );
      await tester.tap(find.byKey(const Key('gym-location-search-button')));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('Grad Sarajevo').first);
      await tester.tap(find.byKey(const Key('gym-create-continue')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('gym-create-continue')));
      await tester.pumpAndSettle();

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

      expect(find.text('Konflikt teretana'), findsOneWidget);
      await tester.tap(find.byKey(const Key('gym-create-continue')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Potvrdi'));
      await tester.pumpAndSettle();

      expect(find.text('GymAdmin'), findsWidgets);
      expect(
        find.text(
          'Odabrani račun ima aktivno članstvo ili drugu aktivnu dodjelu '
          'teretani. Registrujte novi Member račun bez aktivnog članstva.',
        ),
        findsOneWidget,
      );
      expect(
        tester
            .widget<TextField>(find.byKey(const Key('gym-admin-search')))
            .controller!
            .text,
        isEmpty,
      );
      expect(find.byType(AlertDialog), findsOneWidget);
    },
  );

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

    await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
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
    expect(api.assignmentBody?['identifier'], 'user-owner');

    expect(find.byTooltip('Upravljaj GymAdminom'), findsOneWidget);
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

    await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
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
        'Odabrani račun ima aktivno članstvo ili drugu aktivnu dodjelu '
        'teretani. Registrujte novi Member račun bez aktivnog članstva.',
      ),
      findsOneWidget,
    );
    expect(find.text('Sačuvani razlog'), findsOneWidget);
    expect(find.byType(AlertDialog), findsOneWidget);
  });

  testWidgets(
    'gym row shows the active admin and removes before assigning another',
    (tester) async {
      tester.view.physicalSize = const Size(1600, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _CentralAdminApi(initiallyAssigned: true);
      await tester.pumpWidget(_centralHarness(api));
      await tester.pumpAndSettle();

      await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
      await tester.pumpAndSettle();

      final currentAdmin = tester.widget<DropdownButtonFormField<String>>(
        find.byKey(const Key('current-gym-admin-dropdown')),
      );
      expect(currentAdmin.initialValue, 'user-owner');
      expect(find.text('Owner Account'), findsOneWidget);
      expect(find.text('owner@gymlink.local'), findsOneWidget);
      expect(find.text('Aktivan'), findsOneWidget);

      await tester.tap(find.byKey(const Key('remove-gym-admin')));
      await tester.pumpAndSettle();
      expect(find.textContaining('postati Member'), findsOneWidget);
      await tester.tap(find.widgetWithText(FilledButton, 'Nastavi'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Razlog'),
        'Promjena odgovorne osobe',
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Potvrdi'));
      await tester.pumpAndSettle();

      expect(api.revokeBody?['identifier'], 'user-owner');
      expect(api.revokeBody?['reason'], 'Promjena odgovorne osobe');
      expect(find.byKey(const Key('current-gym-admin-dropdown')), findsNothing);
      expect(find.byKey(const Key('gym-admin-search')), findsOneWidget);
      expect(
        find.text('GymAdmin je uklonjen. Sada možete dodijeliti novi račun.'),
        findsOneWidget,
      );
    },
  );

  testWidgets('GymAdmin removal error preserves the entered reason', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(
      initiallyAssigned: true,
      revokeConflictCode: 'role_change_failed',
    );
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('remove-gym-admin')));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Nastavi'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Sačuvani razlog opoziva',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Potvrdi'));
    await tester.pumpAndSettle();

    expect(find.text('Opoziv nije uspio.'), findsOneWidget);
    expect(find.text('Sačuvani razlog opoziva'), findsOneWidget);
    expect(find.byType(AlertDialog), findsNWidgets(2));
  });

  testWidgets('inactive gym explains why a missing admin cannot be assigned', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(gymStatus: 2);
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Dodjela je moguća samo'), findsOneWidget);
    expect(find.byKey(const Key('gym-admin-search')), findsNothing);
    expect(find.widgetWithText(FilledButton, 'Dodijeli'), findsNothing);
  });

  testWidgets('stale gym response blocks a conflicting admin assignment', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _CentralAdminApi(
      initiallyAssigned: true,
      omitActiveAdminSummary: true,
    );
    await tester.pumpWidget(_centralHarness(api));
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Upravljaj GymAdminom'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Ponovo pokrenite API'), findsOneWidget);
    expect(find.byKey(const Key('gym-admin-search')), findsNothing);
    expect(find.widgetWithText(FilledButton, 'Dodijeli'), findsNothing);
  });

  testWidgets(
    'CentralAdmin CRUD overviews use column tables and icon actions',
    (tester) async {
      tester.view.physicalSize = const Size(1000, 760);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _CentralCrudApi();
      Finder horizontalTableScroll() => find.byWidgetPredicate(
        (widget) =>
            widget is SingleChildScrollView &&
            widget.scrollDirection == Axis.horizontal,
      );

      Future<void> show(Widget screen) => tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: Scaffold(body: screen),
          ),
        ),
      );

      await show(const RegistrationManagementScreen());
      await tester.pumpAndSettle();
      expect(find.byType(DataTable), findsOneWidget);
      expect(horizontalTableScroll(), findsOneWidget);
      for (final heading in [
        'Teretana',
        'Lokacija',
        'Opis',
        'Status',
        'Akcije',
      ]) {
        expect(find.text(heading), findsOneWidget);
      }
      expect(find.byTooltip('Odobri'), findsOneWidget);
      expect(find.byTooltip('Odbij'), findsOneWidget);

      await show(const UserManagementScreen());
      await tester.pumpAndSettle();
      expect(find.byType(DataTable), findsOneWidget);
      expect(horizontalTableScroll(), findsOneWidget);
      for (final heading in [
        'Korisnik',
        'Email',
        'Teretana',
        'Uloga',
        'Status',
        'Akcije',
      ]) {
        expect(find.text(heading), findsOneWidget);
      }
      expect(find.byTooltip('Deaktiviraj račun'), findsOneWidget);

      await show(const ReferenceDataScreen());
      await tester.pumpAndSettle();
      expect(find.byType(DataTable), findsOneWidget);
      expect(horizontalTableScroll(), findsOneWidget);
      expect(find.text('Slobodni utezi'), findsOneWidget);
      expect(find.byTooltip('Uredi'), findsOneWidget);
      expect(find.byTooltip('Deaktiviraj'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('CentralAdmin refresh keeps prior gym and user rows on failure', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1500, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final gymGate = Completer<void>();
    final gymApi = _CentralCrudApi(
      failGymRefresh: true,
      gymRefreshGate: gymGate,
    );
    await tester.pumpWidget(_centralHarness(gymApi));
    await tester.pumpAndSettle();
    expect(find.text('Nova teretana'), findsOneWidget);

    await tester.tap(find.byKey(const Key('refresh-central-gyms')));
    await tester.pump();
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('refresh-central-gyms')))
          .onPressed,
      isNull,
    );
    gymGate.complete();
    await tester.pumpAndSettle();
    expect(find.text('Nova teretana'), findsOneWidget);
    expect(find.textContaining('prethodni podaci'), findsOneWidget);

    final userApi = _CentralCrudApi(failUserRefresh: true);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: userApi,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: UserManagementScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('refresh-central-users')));
    await tester.pumpAndSettle();
    expect(find.text('Owner Account'), findsOneWidget);
    expect(find.textContaining('prethodni podaci'), findsOneWidget);
  });

  testWidgets('user account action icons stay aligned between rows', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: _UserActionAlignmentApi(),
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: UserManagementScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    final memberAction = find.byKey(
      const Key('toggle-user-active-user-member'),
    );
    final gymAdminAction = find.byKey(
      const Key('toggle-user-active-user-gym-admin'),
    );
    expect(memberAction, findsOneWidget);
    expect(gymAdminAction, findsOneWidget);
    expect(
      tester.getCenter(memberAction).dx,
      tester.getCenter(gymAdminAction).dx,
    );
    expect(find.byKey(const Key('revoke-role-user-member')), findsNothing);
    expect(find.byKey(const Key('revoke-role-user-gym-admin')), findsOneWidget);
  });

  testWidgets('GymAdmin dashboard refresh preserves summaries on failure', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final gate = Completer<void>();
    final api = _GymDashboardApi(refreshGate: gate, failRefresh: true);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: GymDashboardScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Aktivni članovi'), findsOneWidget);

    await tester.tap(find.byKey(const Key('refresh-gym-dashboard')));
    await tester.pump();
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('refresh-gym-dashboard')))
          .onPressed,
      isNull,
    );
    gate.complete();
    await tester.pumpAndSettle();
    expect(find.text('Aktivni članovi'), findsOneWidget);
    expect(find.textContaining('prethodni podaci'), findsOneWidget);
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

    await tester.tap(find.byTooltip('Aktiviraj'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();
    expect(find.text('Unesite razlog (najmanje 2 znaka).'), findsOneWidget);
    expect(api.activationAttempts, 0);
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Spremno za aktivaciju',
    );
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(find.text('Razlog promjene statusa'), findsOneWidget);
    expect(find.textContaining('aktivan plan članstva'), findsWidgets);
    expect(find.byType(AlertDialog), findsOneWidget);
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
    expect(find.text('Trener'), findsNothing);
    await tester.tap(find.text('Administrator teretane').last);
    await tester.pumpAndSettle();
    expect(find.textContaining('bez aktivnog članstva'), findsOneWidget);
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
    expect(find.text('Oprema'), findsWidgets);
    expect(find.byType(DataTable), findsOneWidget);
    await tester.tap(find.text('Gradovi'));
    await tester.pumpAndSettle();
    expect(
      find.byKey(const Key('city-reference-manual-address-hint')),
      findsOneWidget,
    );
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
    await tester.tap(find.byKey(const Key('refresh-schedule')));
    await tester.pumpAndSettle();
    expect(api.scheduleLoads, 2);
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
    expect(find.text('Dodajte uslugu trenera'), findsOneWidget);
    expect(
      find.textContaining('aktivnu uslugu i kreiran raspored'),
      findsOneWidget,
    );
    expect(find.text('Dodaj uslugu'), findsWidgets);
    await tester.tap(find.text('Kasnije'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('add-offering-trainer-1')), findsOneWidget);
  });

  testWidgets('GymAdmin deactivates and reactivates a trainer with a reason', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _GymAdminTrainerApi(hasTrainer: true);
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

    await tester.tap(find.byTooltip('Radnje trenera'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Deaktiviraj trenera'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Privremeno van rasporeda',
    );
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(
      api.lifecycleRequests.single.$1,
      '/api/tenant/trainers/trainer-1/deactivate',
    );
    expect(
      api.lifecycleRequests.single.$2['reason'],
      'Privremeno van rasporeda',
    );
    await tester.tap(find.byTooltip('Radnje trenera'));
    await tester.pumpAndSettle();
    expect(find.text('Reaktiviraj trenera'), findsOneWidget);
    await tester.tap(find.text('Reaktiviraj trenera'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Ponovo dostupan',
    );
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(
      api.lifecycleRequests.last.$1,
      '/api/tenant/trainers/trainer-1/reactivate',
    );
    expect(api.lifecycleRequests.last.$2['reason'], 'Ponovo dostupan');
  });

  testWidgets('Trainer lifecycle API failures remain visible in the dialog', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _GymAdminTrainerApi(
      hasTrainer: true,
      lifecycleProblem: const ApiProblem(
        status: 409,
        code: 'trainer_lifecycle_conflict',
        message: 'Stanje trenera je u konfliktu.',
      ),
    );
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

    await tester.tap(find.byTooltip('Radnje trenera'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Deaktiviraj trenera'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Razlog'),
      'Administrativna odluka',
    );
    await tester.tap(find.text('Potvrdi'));
    await tester.pumpAndSettle();

    expect(find.text('Stanje trenera je u konfliktu.'), findsOneWidget);
    expect(find.byType(AlertDialog), findsOneWidget);
  });

  testWidgets('GymAdmin profile renders the ordered gallery controls', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1366, 768);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = _GymGalleryApi();
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: GymCatalogScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Galerija teretane'), findsOneWidget);
    expect(find.text('Naslovna'), findsOneWidget);
    expect(find.text('Slika 2'), findsOneWidget);
    expect(find.byKey(const Key('gym-gallery-add')), findsOneWidget);
    final save = tester.widget<FilledButton>(
      find.byKey(const Key('gym-gallery-save')),
    );
    expect(save.onPressed, isNull);

    await tester.tap(find.byTooltip('Pomjeri lijevo').last);
    await tester.pump();
    expect(find.text('Nesačuvane promjene'), findsOneWidget);
    expect(api.gallerySaveCalls, 0);

    await tester.tap(find.byKey(const Key('gym-gallery-save')));
    await tester.pumpAndSettle();
    expect(api.gallerySaveCalls, 1);
    expect(api.savedManifest?['items'], isA<List>());
    expect((api.savedManifest!['items'] as List).first['imageId'], 'image-2');
    expect(api.savedFiles, isEmpty);
    expect(find.text('Nesačuvane promjene'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('GymAdmin gallery save progress stays inside the save button', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1366, 768);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final saveGate = Completer<void>();
    final api = _GymGalleryApi(saveGate: saveGate);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(body: GymCatalogScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byTooltip('Pomjeri lijevo').last);
    await tester.pump();
    await tester.tap(find.byKey(const Key('gym-gallery-save')));
    await tester.pump();

    expect(find.byKey(const Key('gym-gallery-save-progress')), findsOneWidget);
    expect(find.text('Čuvanje...'), findsOneWidget);
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('gym-gallery-save')))
          .onPressed,
      isNull,
    );
    expect(
      tester
          .widget<OutlinedButton>(find.byKey(const Key('gym-gallery-discard')))
          .onPressed,
      isNull,
    );
    expect(tester.takeException(), isNull);

    saveGate.complete();
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('gym-gallery-save-progress')), findsNothing);
    expect(find.text('Nesačuvane promjene'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}

Future<void> _openGymWizardAtLocation(WidgetTester tester) async {
  await tester.tap(find.text('Dodaj teretanu'));
  await tester.pumpAndSettle();

  expect(find.text('Osnovni podaci'), findsOneWidget);
  expect(find.byKey(const Key('gym-location-map')), findsNothing);
  await tester.enterText(
    find.widgetWithText(TextFormField, 'Naziv'),
    'Testna teretana',
  );
  await tester.enterText(
    find.widgetWithText(TextFormField, 'Opis'),
    'Potpun opis testne teretane.',
  );
  await tester.enterText(find.byKey(const Key('gym-admin-search')), 'owner');
  await tester.pump(const Duration(milliseconds: 350));
  await tester.pump();
  await tester.ensureVisible(find.text('Owner Account'));
  await tester.pump();
  await tester.tap(find.text('Owner Account'));
  await tester.pump();
  await tester.tap(find.byKey(const Key('gym-create-continue')));
  await tester.pumpAndSettle();

  expect(find.byKey(const Key('gym-location-map')), findsOneWidget);
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

class _ReportingApi extends ApiClient {
  _ReportingApi({
    this.failMonths = false,
    this.emptyCharts = false,
    this.membershipPeriodCount = 150,
    this.previousMonthEndMembershipPeriodCount = 125,
    this.membershipPeriodChangePercentage = 20,
  }) : super(_TestTokens());

  final bool failMonths;
  final bool emptyCharts;
  final int membershipPeriodCount;
  final int previousMonthEndMembershipPeriodCount;
  final num membershipPeriodChangePercentage;
  final List<String> downloadedPaths = [];
  final List<String> requestedPaths = [];
  static const report = DownloadedFile(
    bytes: [0x25, 0x50, 0x44, 0x46],
    fileName: 'gymlink-clanstva.pdf',
    contentType: 'application/pdf',
    recordCount: 12,
  );

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    requestedPaths.add(path);
    if (path == '/api/tenant/statistics/summary') {
      return {
        'activeMemberCount': 142,
        'membershipPeriodCount': membershipPeriodCount,
        'previousMonthEndMembershipPeriodCount':
            previousMonthEndMembershipPeriodCount,
        'membershipPeriodChangePercentage': membershipPeriodChangePercentage,
        'reservationCount': 89,
        'reservationsToday': 8,
        'averageTrainerRating': 4.7,
      };
    }
    if (path == '/api/tenant/statistics/members-by-month') {
      if (failMonths) throw StateError('monthly_failed');
      if (emptyCharts) return {'items': <Object>[]};
      return {
        'items': [
          for (var month = 3; month <= 8; month++)
            {'year': 2026, 'month': month, 'count': month * 10},
        ],
      };
    }
    if (path == '/api/tenant/statistics/membership-plan-distribution') {
      if (emptyCharts) return {'total': 0, 'items': <Object>[]};
      return {
        'total': 142,
        'items': [
          {
            'membershipPlanId': 'monthly',
            'planName': 'Mjesečna',
            'count': 68,
            'percentage': 47.9,
          },
          {
            'membershipPlanId': 'quarterly',
            'planName': 'Tromjesečna',
            'count': 74,
            'percentage': 52.1,
          },
        ],
      };
    }
    throw StateError('Unexpected get request: $path');
  }

  @override
  Future<DownloadedFile> download(String path) async {
    downloadedPaths.add(path);
    return report;
  }
}

class _CentralReportingApi extends ApiClient {
  _CentralReportingApi() : super(_TestTokens());

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async => switch (path) {
    '/api/admin/statistics/summary' => {
      'totalGyms': 4,
      'activeUsers': 120,
      'pendingActivationGyms': 1,
      'reservationCount': 91,
    },
    '/api/admin/statistics/trends' => {
      'window': {
        'windowStart': '2026-03-01',
        'windowEnd': '2026-08-31',
        'timeZone': 'Europe/Sarajevo',
      },
      'reservationsByMonth': [
        for (var month = 3; month <= 8; month++)
          {'year': 2026, 'month': month, 'count': month == 8 ? 31 : month},
      ],
    },
    _ => throw StateError('Unexpected get request: $path'),
  };
}

class _NotificationApi extends ApiClient {
  _NotificationApi() : super(_TestTokens());

  int readRequests = 0;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async => const PagedData(
    items: [
      {
        'id': 'notice-1',
        'category': 'membership.approved',
        'title': 'Članstvo aktivirano',
        'text': 'Članarina je aktivirana.',
        'createdAtUtc': '2026-08-09T10:00:00Z',
        'isRead': false,
        'concurrencyToken': 'token-1',
      },
    ],
    page: 1,
    pageSize: 20,
    totalCount: 1,
  );

  @override
  Future<Object?> post(
    String path, {
    Object? body,
    bool authenticated = true,
  }) async {
    if (path == '/api/me/notifications/notice-1/read') {
      readRequests++;
      return {
        'id': 'notice-1',
        'category': 'membership.approved',
        'title': 'Članstvo aktivirano',
        'text': 'Članarina je aktivirana.',
        'createdAtUtc': '2026-08-09T10:00:00Z',
        'isRead': true,
        'concurrencyToken': 'token-2',
      };
    }
    return null;
  }
}

class _DelayedReportingApi extends _ReportingApi {
  final pending = Completer<DownloadedFile>();

  @override
  Future<DownloadedFile> download(String path) {
    downloadedPaths.add(path);
    return pending.future;
  }
}

class _GymGalleryApi extends ApiClient {
  _GymGalleryApi({this.saveGate}) : super(_TestTokens());

  final Completer<void>? saveGate;
  int gallerySaveCalls = 0;
  Map<String, dynamic>? savedManifest;
  List<MultipartUploadPart> savedFiles = const [];

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) async {
    if (path == '/api/tenant/gym') {
      return {
        'id': 'gym-1',
        'name': 'Test Gym',
        'description': 'Opis teretane',
        'address': 'Testna 1',
        'cityId': 'city-1',
        'city': 'Sarajevo',
        'equipment': ['Tegovi'],
        'equipmentIds': ['equipment-1'],
        'trainingTypes': ['Fitness'],
        'trainingTypeIds': ['type-1'],
        'workingHours': <Object>[],
        'imageGallery': {
          'maximumImages': 5,
          'images': [
            {
              'id': 'image-1',
              'imageUrl': null,
              'sortOrder': 0,
              'isPrimary': true,
              'concurrencyToken': 'token-1',
            },
            {
              'id': 'image-2',
              'imageUrl': null,
              'sortOrder': 1,
              'isPrimary': false,
              'concurrencyToken': 'token-2',
            },
          ],
        },
      };
    }
    if (path == '/api/reference-data/lookups') {
      return {
        'cities': <Object>[],
        'equipment': <Object>[],
        'trainingTypes': <Object>[],
      };
    }
    throw StateError('Unexpected get request: $path');
  }

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/membership-plans') {
      return const PagedData(items: [], page: 1, pageSize: 50, totalCount: 0);
    }
    throw StateError('Unexpected page request: $path');
  }

  @override
  Future<Object?> putMultipart(
    String path, {
    required Map<String, String> fields,
    required List<MultipartUploadPart> files,
  }) async {
    gallerySaveCalls++;
    savedManifest = Map<String, dynamic>.from(
      jsonDecode(fields['manifest']!) as Map,
    );
    savedFiles = files;
    await saveGate?.future;
    final items = savedManifest!['items'] as List;
    return {
      'maximumImages': 5,
      'images': [
        for (var index = 0; index < items.length; index++)
          {
            'id': (items[index] as Map)['imageId'],
            'imageUrl': null,
            'sortOrder': index,
            'isPrimary': index == 0,
            'concurrencyToken': 'saved-$index',
          },
      ],
    };
  }
}

class _CentralAdminApi extends ApiClient {
  _CentralAdminApi({
    this.assignmentConflictCode,
    this.revokeConflictCode,
    this.activationConflictCode,
    this.creationConflictCode,
    this.initiallyAssigned = false,
    this.omitActiveAdminSummary = false,
    this.gymStatus = 0,
    this.successfulActivationWithRefreshFailure = false,
    this.reverseUnavailable = false,
    this.gymTotalCount = 1,
    this.address = 'Testna 1, Sarajevo',
  }) : _assigned = initiallyAssigned,
       super(_TestTokens());

  final String? assignmentConflictCode;
  final String? revokeConflictCode;
  final String? activationConflictCode;
  final String? creationConflictCode;
  final bool initiallyAssigned;
  final bool omitActiveAdminSummary;
  final int gymStatus;
  final bool successfulActivationWithRefreshFailure;
  final bool reverseUnavailable;
  final int gymTotalCount;
  final String address;
  final List<Map<String, Object?>> gymQueries = [];
  Map<String, Object?>? lastLocationQuery;
  Map<String, Object?>? lastReverseQuery;
  Map<String, dynamic>? assignmentBody;
  Map<String, dynamic>? revokeBody;
  Map<String, dynamic>? creationBody;
  bool _assigned;
  int activationAttempts = 0;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/admin/gyms') {
      gymQueries.add(Map<String, Object?>.from(query));
      if (successfulActivationWithRefreshFailure && activationAttempts > 0) {
        throw ApiProblem(
          status: 503,
          code: 'refresh_failed',
          message: 'Refresh failed',
        );
      }
      final activationReady =
          activationConflictCode != null && activationAttempts == 0;
      final hasAdmin =
          _assigned ||
          activationConflictCode != null ||
          successfulActivationWithRefreshFailure;
      return PagedData(
        items: [
          {
            'id': 'gym-1',
            'tenantId': 'tenant-1',
            'name': 'Nova teretana',
            'address': address.endsWith(', Sarajevo')
                ? address.substring(0, address.length - 10)
                : address,
            'cityName': address.endsWith(', Sarajevo') ? 'Sarajevo' : '',
            'status': gymStatus,
            'memberCount': 12,
            'activeGymAdminCount': hasAdmin ? 1 : 0,
            'activeGymAdmin': hasAdmin && !omitActiveAdminSummary
                ? const {
                    'id': 'user-owner',
                    'displayName': 'Owner Account',
                    'email': 'owner@gymlink.local',
                    'isActive': true,
                  }
                : null,
            'canActivate':
                activationReady ||
                _assigned ||
                successfulActivationWithRefreshFailure,
            'missingActivationRequirements': activationReady || _assigned
                ? const <String>[]
                : activationConflictCode != null
                ? const ['membership_plan']
                : const ['gym_admin'],
          },
        ],
        page: 1,
        pageSize: 50,
        totalCount: gymTotalCount,
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
    if (path == '/api/admin/locations/reverse') {
      lastReverseQuery = Map<String, Object?>.from(query);
      if (reverseUnavailable) {
        throw const ApiProblem(
          status: 503,
          code: 'location_search_unavailable',
          message: 'Unavailable',
        );
      }
      return const {
        'resultKey': 'way:200',
        'displayName': 'Zmaja od Bosne 12, Sarajevo, Bosna i Hercegovina',
        'address': 'Zmaja od Bosne 12, Sarajevo, Bosna i Hercegovina',
        'cityId': 'city-sarajevo',
        'cityName': 'Sarajevo',
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
    if (path == '/api/admin/tenants/tenant-1/activate') {
      activationAttempts++;
      if (activationConflictCode != null) {
        throw ApiProblem(
          status: 409,
          code: activationConflictCode!,
          message: 'Backend conflict',
        );
      }
      return const {};
    }
    if (path == '/api/admin/gyms') {
      creationBody = Map<String, dynamic>.from(body! as Map);
      if (creationConflictCode != null) {
        throw ApiProblem(
          status: 409,
          code: creationConflictCode!,
          message: 'Backend conflict',
        );
      }
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
    if (path == '/api/admin/users/roles/revoke') {
      revokeBody = Map<String, dynamic>.from(body! as Map);
      if (revokeConflictCode != null) {
        throw ApiProblem(
          status: 409,
          code: revokeConflictCode!,
          message: 'Opoziv nije uspio.',
        );
      }
      _assigned = false;
      return const {};
    }
    throw StateError('Unexpected post request: $path');
  }
}

class _CentralCrudApi extends _CentralAdminApi {
  _CentralCrudApi({
    this.failGymRefresh = false,
    this.failUserRefresh = false,
    this.gymRefreshGate,
  });

  final bool failGymRefresh;
  final bool failUserRefresh;
  final Completer<void>? gymRefreshGate;
  int gymLoads = 0;
  int userLoads = 0;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/admin/gym-registration-requests') {
      return const PagedData(
        items: [
          {
            'id': 'registration-1',
            'gymName': 'Teretana u prijavi',
            'address': 'Prijavna 1',
            'cityName': 'Sarajevo',
            'description': 'Opis prijavljene teretane',
            'status': 1,
            'createdTenantId': null,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      );
    }
    if (path == '/api/admin/gyms') {
      gymLoads++;
      if (gymLoads > 1) {
        await gymRefreshGate?.future;
        if (failGymRefresh) {
          throw StateError('gym refresh failed');
        }
      }
    }
    if (path == '/api/admin/users') {
      userLoads++;
      if (userLoads > 1 && failUserRefresh) {
        throw StateError('user refresh failed');
      }
    }
    return super.page(path, query: query);
  }
}

class _UserActionAlignmentApi extends _CentralAdminApi {
  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/admin/users') {
      return const PagedData(
        items: [
          {
            'id': 'user-member',
            'username': 'member',
            'email': 'member@gymlink.local',
            'displayName': 'Member Account',
            'role': 'Member',
            'isActive': true,
          },
          {
            'id': 'user-gym-admin',
            'username': 'gym-admin',
            'email': 'gym-admin@gymlink.local',
            'displayName': 'GymAdmin Account',
            'role': 'GymAdmin',
            'isActive': true,
          },
        ],
        page: 1,
        pageSize: 10,
        totalCount: 2,
      );
    }
    return super.page(path, query: query);
  }
}

class _GymDashboardApi extends ApiClient {
  _GymDashboardApi({this.refreshGate, this.failRefresh = false})
    : super(_TestTokens());

  final Completer<void>? refreshGate;
  final bool failRefresh;
  int loadCycles = 0;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/membership-requests') loadCycles++;
    if (loadCycles > 1) {
      await refreshGate?.future;
      if (failRefresh) throw StateError('dashboard refresh failed');
    }
    return switch (path) {
      '/api/tenant/membership-requests' => const PagedData(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      ),
      '/api/tenant/memberships' => const PagedData(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 12,
      ),
      '/api/tenant/trainers' => const PagedData(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 3,
      ),
      '/api/tenant/reservations' => const PagedData(
        items: [
          {
            'memberName': 'Test Member',
            'trainerName': 'Test Trainer',
            'startsAtUtc': '2026-08-21T10:00:00Z',
            'status': 1,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      ),
      _ => throw StateError('Unexpected page request: $path'),
    };
  }
}

class _DelayedReverseCentralAdminApi extends _CentralAdminApi {
  final List<Completer<Object?>> pending = [];
  ApiProblem? problem;

  @override
  Future<Object?> get(
    String path, {
    Map<String, Object?> query = const {},
    bool authenticated = true,
  }) {
    if (path != '/api/admin/locations/reverse') {
      return super.get(path, query: query, authenticated: authenticated);
    }
    lastReverseQuery = Map<String, Object?>.from(query);
    if (problem case final value?) {
      return Future.error(value);
    }
    final completer = Completer<Object?>();
    pending.add(completer);
    return completer.future;
  }
}

class _GymAdminScheduleApi extends ApiClient {
  _GymAdminScheduleApi() : super(_TestTokens());

  Map<String, dynamic>? savedSchedule;
  int scheduleLoads = 0;

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
      scheduleLoads++;
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

class _GymAdminReservationsApi extends ApiClient {
  _GymAdminReservationsApi({this.withItem = false, this.failAfterFirst = false})
    : super(_TestTokens());

  final List<int?> requestedStatuses = [];
  final bool withItem;
  final bool failAfterFirst;
  bool completed = false;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/reservations') {
      requestedStatuses.add(query['status'] as int?);
      if (failAfterFirst && requestedStatuses.length > 1) {
        throw StateError('refresh failed');
      }
      return PagedData(
        items: withItem
            ? const [
                {
                  'id': 'reservation-1',
                  'memberName': 'Test Member',
                  'trainerName': 'Test Trainer',
                  'offeringName': 'Personalni trening',
                  'startsAtUtc': '2026-08-13T10:00:00Z',
                  'status': 1,
                  'allowedActions': ['complete'],
                  'concurrencyToken': 'token',
                },
              ]
            : const [],
        page: 1,
        pageSize: 20,
        totalCount: withItem ? 1 : 0,
      );
    }
    throw StateError('Unexpected page request: $path');
  }

  @override
  Future<Object?> post(
    String path, {
    Object? body,
    bool authenticated = true,
  }) async {
    if (path == '/api/tenant/reservations/reservation-1/complete') {
      completed = true;
      return const {};
    }
    throw StateError('Unexpected post request: $path');
  }
}

class _GymAdminMembershipRequestsApi extends ApiClient {
  _GymAdminMembershipRequestsApi() : super(_TestTokens());

  final List<int?> requestedMembershipStatuses = [];
  final List<int?> requestedPaymentCategories = [];

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/membership-requests') {
      requestedMembershipStatuses.add(query['membershipStatus'] as int?);
      requestedPaymentCategories.add(query['paymentCategory'] as int?);
      return const PagedData(
        items: [
          {
            'id': 'request-1',
            'memberDisplayName': 'Cash Member',
            'memberEmail': 'cash@gymlink.local',
            'planName': 'Mjesečna',
            'price': 50,
            'currency': 'BAM',
            'requestedAtUtc': '2026-08-09T10:00:00Z',
            'paymentMethod': 2,
            'status': 0,
            'allowedActions': ['approve', 'reject', 'view'],
            'concurrencyToken': 'token',
          },
          {
            'id': 'request-2',
            'memberDisplayName': 'Active Member',
            'memberEmail': 'active@gymlink.local',
            'planName': 'Godišnja',
            'price': 500,
            'currency': 'BAM',
            'requestedAtUtc': '2026-08-08T10:00:00Z',
            'paymentMethod': 1,
            'status': 1,
            'allowedActions': ['view'],
            'concurrencyToken': 'request-token',
            'membership': {
              'id': 'membership-1',
              'status': 1,
              'startsAtUtc': '2026-08-08T10:00:00Z',
              'endsAtUtc': '2027-08-08T10:00:00Z',
              'paymentStatus': null,
              'isPaid': false,
              'allowedActions': ['cancel', 'suspend'],
              'concurrencyToken': 'membership-token',
            },
          },
          {
            'id': 'request-3',
            'memberDisplayName': 'Rejected Member',
            'memberEmail': 'rejected@gymlink.local',
            'planName': 'Mjesečna',
            'price': 50,
            'currency': 'BAM',
            'requestedAtUtc': '2026-08-07T10:00:00Z',
            'paymentMethod': 2,
            'status': 2,
            'allowedActions': ['view'],
            'concurrencyToken': 'rejected-token',
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 3,
      );
    }
    throw StateError('Unexpected page request: $path');
  }
}

class _GymAdminTrainerApi extends ApiClient {
  _GymAdminTrainerApi({bool hasTrainer = false, this.lifecycleProblem})
    : _promoted = hasTrainer,
      super(_TestTokens());

  Map<String, dynamic>? promotionBody;
  final ApiProblem? lifecycleProblem;
  final List<(String, Map<String, dynamic>)> lifecycleRequests = [];
  bool _promoted;
  bool _active = true;

  @override
  Future<PagedData> page(
    String path, {
    Map<String, Object?> query = const {},
  }) async {
    if (path == '/api/tenant/trainers') {
      return PagedData(
        items: _promoted
            ? [
                {
                  'id': 'trainer-1',
                  'displayName': 'Active Member',
                  'credentials': null,
                  'averageRating': 0,
                  'isActive': _active,
                },
              ]
            : const [],
        page: 1,
        pageSize: 50,
        totalCount: _promoted ? 1 : 0,
      );
    }
    if (path == '/api/tenant/trainer-offerings') {
      return const PagedData(items: [], page: 1, pageSize: 50, totalCount: 0);
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
      _promoted = true;
      _active = true;
      return const {
        'id': 'trainer-1',
        'displayName': 'Active Member',
        'isActive': true,
      };
    }
    if (path == '/api/tenant/trainers/trainer-1/deactivate' ||
        path == '/api/tenant/trainers/trainer-1/reactivate') {
      lifecycleRequests.add((path, Map<String, dynamic>.from(body! as Map)));
      if (lifecycleProblem case final problem?) throw problem;
      _active = path.endsWith('/reactivate');
      return {
        'id': 'trainer-1',
        'displayName': 'Active Member',
        'isActive': _active,
      };
    }
    throw StateError('Unexpected post request: $path');
  }
}
