import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/core/payments.dart';
import 'package:gymlink_mobile/core/theme.dart';
import 'package:gymlink_mobile/features/auth/auth_screens.dart';
import 'package:gymlink_mobile/features/member/gym_screens.dart';
import 'package:gymlink_mobile/features/member/membership_screen.dart';
import 'package:gymlink_mobile/features/member/reservation_screen.dart';
import 'package:gymlink_mobile/features/payments/payment_result_screen.dart';
import 'package:gymlink_mobile/features/reservations/reservation_refresh_controller.dart';
import 'package:gymlink_mobile/features/trainer/trainer_screens.dart';
import 'package:gymlink_mobile/shared/widgets.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets(
    'GymLink status and empty states render approved visual language',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: buildGymLinkTheme(),
          home: const Scaffold(
            body: Column(
              children: [
                StatusPill('Active'),
                Expanded(
                  child: EmptyState(
                    title: 'Nema rezultata',
                    message: 'Promijenite filter i pokušajte ponovo.',
                  ),
                ),
              ],
            ),
          ),
        ),
      );

      expect(find.text('Active'), findsOneWidget);
      expect(find.text('Nema rezultata'), findsOneWidget);
      expect(find.byIcon(Icons.inbox_outlined), findsOneWidget);
    },
  );

  testWidgets('trainer cancellation requires a reason without closing', (
    tester,
  ) async {
    String? submittedReason;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                submittedReason = await showDialog<String>(
                  context: context,
                  builder: (_) => const TrainerCancellationReasonDialog(),
                );
              },
              child: const Text('Otkaži'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Otkaži'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Nastavi'));
    await tester.pump();

    expect(find.text('Unesite razlog otkazivanja.'), findsOneWidget);
    expect(find.byType(TrainerCancellationReasonDialog), findsOneWidget);
    expect(submittedReason, isNull);

    await tester.enterText(find.byType(TextFormField), 'Bolest trenera');
    await tester.tap(find.text('Nastavi'));
    await tester.pumpAndSettle();

    expect(find.byType(TrainerCancellationReasonDialog), findsNothing);
    expect(submittedReason, 'Bolest trenera');
    expect(tester.takeException(), isNull);
  });

  testWidgets('trainer review accepts an empty optional comment', (
    tester,
  ) async {
    Map<String, Object?>? submittedReview;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                submittedReview = await showDialog<Map<String, Object?>>(
                  context: context,
                  builder: (_) => const TrainerReviewDialog(),
                );
              },
              child: const Text('Ocijeni'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Ocijeni'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Objavi'));
    await tester.pumpAndSettle();

    expect(find.byType(TrainerReviewDialog), findsNothing);
    expect(submittedReview, {'rating': 5, 'comment': null});
    expect(tester.takeException(), isNull);
  });

  testWidgets('reservation payment sheet explains both payment methods', (
    tester,
  ) async {
    ReservationPaymentMethod? selected;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                selected = await chooseReservationPaymentMethod(context);
              },
              child: const Text('Rezerviši termin'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Rezerviši termin'));
    await tester.pumpAndSettle();

    expect(find.text('Stripe'), findsOneWidget);
    expect(find.text('Plati uživo'), findsOneWidget);
    expect(find.textContaining('vanjski preglednik'), findsOneWidget);
    expect(find.textContaining('automatski ćete se vratiti'), findsOneWidget);

    await tester.tap(find.byKey(const Key('reservation-payment-in-person')));
    await tester.pumpAndSettle();

    expect(selected, ReservationPaymentMethod.payInPerson);
  });

  testWidgets('pay-in-person booking shows a confirmed success state', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(360, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final now = DateTime.now();
    final bookingDate = DateTime(now.year, now.month + 1);
    final startsAt = DateTime(
      bookingDate.year,
      bookingDate.month,
      bookingDate.day,
      10,
    ).toUtc();
    final calendarRequests = <String>[];
    final client = MockClient((request) async {
      if (request.method == 'GET' &&
          request.url.path == '/api/trainers/trainer-1/offerings') {
        return _jsonResponse([
          {
            'id': 'offering-1',
            'name': 'Individualni trening',
            'durationMinutes': 60,
            'price': 30,
            'currency': 'BAM',
            'isActive': true,
          },
        ]);
      }
      if (request.method == 'GET' &&
          request.url.path == '/api/trainers/trainer-1/availability-calendar') {
        final requestedMonth = DateTime.parse(
          request.url.queryParameters['fromLocalDate']!,
        );
        calendarRequests.add(request.url.queryParameters['fromLocalDate']!);
        final requestedDate = DateTime(
          requestedMonth.year,
          requestedMonth.month,
        );
        Map<String, Object> day(int offset, int availableSlots) {
          final date = requestedDate.add(Duration(days: offset));
          final firstStart =
              requestedDate.year == bookingDate.year &&
                  requestedDate.month == bookingDate.month &&
                  offset == 0
              ? startsAt
              : DateTime(date.year, date.month, date.day, 10).toUtc();
          return {
            'date': _testDateKey(date),
            'totalSlots': 4,
            'availableSlots': availableSlots,
            'slots': [
              for (var index = 0; index < 4; index++)
                {
                  'startsAtUtc': firstStart
                      .add(Duration(hours: index))
                      .toIso8601String(),
                  'endsAtUtc': firstStart
                      .add(Duration(hours: index + 1))
                      .toIso8601String(),
                  'isAvailable': index < availableSlots,
                },
            ],
          };
        }

        return _jsonResponse({
          'timeZoneId': 'Europe/Sarajevo',
          'bookingHorizonEndsOn': _testDateKey(
            DateUtils.dateOnly(now.add(const Duration(days: 56))),
          ),
          'days': [day(0, 1), day(1, 2), day(2, 0), day(3, 4)],
        });
      }
      if (request.method == 'GET' &&
          request.url.path == '/api/me/memberships') {
        return _jsonResponse(
          _page([
            {'id': 'membership-1'},
          ]),
        );
      }
      if (request.method == 'POST' && request.url.path == '/api/reservations') {
        return _jsonResponse({
          'id': 'reservation-in-person',
          'status': 1,
          'paymentMethod': 1,
        });
      }
      return _jsonResponse(const <Object>[]);
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    final reservations = ReservationRefreshController();
    var refreshPublished = false;
    reservations.addListener(() => refreshPublished = true);
    addTearDown(api.close);
    addTearDown(reservations.dispose);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: reservations),
        ],
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: BookingScreen(
            trainer: const {
              'id': 'trainer-1',
              'displayName': 'Trener Test',
              'averageRating': 5,
              'reviewCount': 12,
              'credentials': 'Personalni trener',
              'biography': 'Individualni treninzi i funkcionalna priprema.',
            },
            gymId: 'gym-1',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('TT'), findsOneWidget);
    expect(find.text('5 · 12 recenzija'), findsOneWidget);
    expect(find.text('Djelimično popunjeno'), findsOneWidget);
    expect(find.text('Termini popunjeni'), findsOneWidget);
    expect(find.text('Skoro popunjeno'), findsOneWidget);
    expect(tester.takeException(), isNull);

    tester.view.physicalSize = const Size(412, 1600);
    await tester.pump();
    expect(tester.takeException(), isNull);

    final nextMonth = find.byKey(const Key('booking-calendar-next'));
    await tester.ensureVisible(nextMonth);
    await tester.tap(nextMonth);
    await tester.pumpAndSettle();
    expect(calendarRequests.length, 2);
    final bookingDay = find.byKey(
      Key('booking-calendar-day-${_testDateKey(bookingDate)}'),
    );
    await tester.scrollUntilVisible(
      bookingDay,
      180,
      scrollable: find.byType(Scrollable).first,
    );
    final redInk = tester.widget<Ink>(
      find.descendant(of: bookingDay, matching: find.byType(Ink)),
    );
    expect(
      (redInk.decoration! as BoxDecoration).color,
      const Color(0xFFFFD7D7),
    );
    final partiallyFullDay = find.byKey(
      Key(
        'booking-calendar-day-${_testDateKey(bookingDate.add(const Duration(days: 1)))}',
      ),
    );
    final partiallyFullInk = tester.widget<Ink>(
      find.descendant(of: partiallyFullDay, matching: find.byType(Ink)),
    );
    expect(
      (partiallyFullInk.decoration! as BoxDecoration).color,
      const Color(0xFFFFF1A8),
    );
    final fullDay = find.byKey(
      Key(
        'booking-calendar-day-${_testDateKey(bookingDate.add(const Duration(days: 2)))}',
      ),
    );
    expect(tester.widget<InkWell>(fullDay).onTap, isNull);
    final fullInk = tester.widget<Ink>(
      find.descendant(of: fullDay, matching: find.byType(Ink)),
    );
    expect(
      (fullInk.decoration! as BoxDecoration).color,
      const Color(0xFFE5E7EB),
    );
    await tester.tap(bookingDay);
    await tester.pumpAndSettle();
    final selectedInk = tester.widget<Ink>(
      find.descendant(of: bookingDay, matching: find.byType(Ink)),
    );
    expect(
      (selectedInk.decoration! as BoxDecoration).color,
      GymLinkColors.blue,
    );
    await tester.scrollUntilVisible(
      find.byKey(const Key('booking-time-slots')),
      180,
      scrollable: find.byType(Scrollable).first,
    );
    final availableSlot = find.byWidgetPredicate(
      (widget) => widget is ChoiceChip && widget.onSelected != null,
    );
    await tester.scrollUntilVisible(
      availableSlot,
      120,
      scrollable: find.byType(Scrollable).first,
    );
    final chips = tester
        .widgetList<ChoiceChip>(find.byType(ChoiceChip))
        .toList();
    expect(chips, hasLength(4));
    expect(chips.where((chip) => chip.onSelected == null), hasLength(3));
    expect(
      tester.widget<Text>(find.byKey(const Key('booking-summary-date'))).data,
      '${bookingDate.day.toString().padLeft(2, '0')}.'
      '${bookingDate.month.toString().padLeft(2, '0')}.'
      '${bookingDate.year}.',
    );
    await tester.tap(availableSlot);
    await tester.pumpAndSettle();
    final confirmBooking = find.byKey(const Key('booking-confirm'));
    await tester.scrollUntilVisible(
      confirmBooking,
      160,
      scrollable: find.byType(Scrollable).first,
    );
    expect(
      tester.widget<Text>(find.byKey(const Key('booking-summary-time'))).data,
      '10:00',
    );
    expect(
      tester.widget<Text>(find.byKey(const Key('booking-summary-price'))).data,
      '30 BAM',
    );
    expect(tester.widget<FilledButton>(confirmBooking).onPressed, isNotNull);
    expect(tester.takeException(), isNull);
    await tester.tap(confirmBooking);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('reservation-payment-in-person')));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));

    expect(
      find.byKey(const Key('pay-in-person-success-dialog')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('pay-in-person-success-check')),
      findsOneWidget,
    );
    expect(find.byIcon(Icons.check_circle), findsOneWidget);
    expect(find.text('Termin je potvrđen'), findsOneWidget);
    expect(find.textContaining('sačuvana u Terminima'), findsOneWidget);
    expect(
      find.textContaining('razgovor s trenerom je spreman'),
      findsOneWidget,
    );
    expect(find.textContaining('plaćate uživo'), findsOneWidget);
    expect(refreshPublished, isTrue);
  });

  testWidgets('reservation cancellation uses appropriate confirmation copy', (
    tester,
  ) async {
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: MockClient(
        (_) async => _jsonResponse({
          'id': 'reservation-in-person',
          'trainerName': 'Trener Test',
          'gymName': 'GymLink Centar',
          'offeringName': 'Individualni trening',
          'startsAtUtc': '2030-08-01T10:00:00Z',
          'durationMinutes': 60,
          'price': 30,
          'currency': 'BAM',
          'paymentMethod': 1,
          'isPaid': false,
          'status': 1,
          'cancellationReason': null,
          'allowedActions': ['cancel'],
          'concurrencyToken': 'token-1',
        }),
      ),
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: const MaterialApp(
          home: ReservationDetailsScreen(
            reservationId: 'reservation-in-person',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Otkaži rezervaciju'));
    await tester.pumpAndSettle();

    expect(
      find.text(
        'Rezervacija će biti otkazana. Ovu radnju nije moguće poništiti.',
      ),
      findsOneWidget,
    );
    expect(find.textContaining('ponovo postati dostupan'), findsNothing);
  });

  testWidgets('Termini refresh shows a newly confirmed pay-in-person booking', (
    tester,
  ) async {
    var reservationLoads = 0;
    final client = MockClient((request) async {
      reservationLoads++;
      final items = reservationLoads == 1
          ? <Object>[]
          : <Object>[
              {
                'id': 'reservation-in-person',
                'trainerName': 'Trener Test',
                'gymName': 'GymLink Centar',
                'offeringName': 'Individualni trening',
                'startsAtUtc': '2026-08-01T10:00:00Z',
                'price': 30,
                'currency': 'BAM',
                'status': 1,
                'paymentMethod': 1,
              },
            ];
      return http.Response(
        jsonEncode(_page(items)),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    final controller = ReservationRefreshController();
    addTearDown(api.close);
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: Scaffold(
            body: MemberReservationsScreen(controller: controller),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Nema rezervacija'), findsOneWidget);

    controller.refresh();
    await tester.pumpAndSettle();

    expect(find.text('Trener Test · GymLink Centar'), findsOneWidget);
    expect(find.text('Confirmed'), findsOneWidget);
    expect(reservationLoads, 2);
  });

  testWidgets(
    'Member Termini status filter omits Pending and keeps enum values',
    (tester) async {
      final requestedStatuses = <String?>[];
      final client = MockClient((request) async {
        requestedStatuses.add(request.url.queryParameters['status']);
        return http.Response(
          jsonEncode(_page(const <Object>[])),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      final api = ApiClient(
        _TestTokenSource(),
        httpClient: client,
        baseUrlOverride: 'http://test.local',
      );
      addTearDown(api.close);
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: const Scaffold(body: MemberReservationsScreen()),
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
      expect(requestedStatuses, [null, '1']);
    },
  );

  testWidgets(
    'Termini details hide internal Stripe deadline and retry action',
    (tester) async {
      final client = MockClient((request) async {
        return http.Response(
          jsonEncode({
            'id': 'pending-stripe-reservation',
            'trainerName': 'Trener Test',
            'gymName': 'GymLink Centar',
            'offeringName': 'Individualni trening',
            'startsAtUtc': '2026-08-01T10:00:00Z',
            'durationMinutes': 60,
            'price': 30,
            'currency': 'BAM',
            'paymentMethod': 0,
            'paymentDueAtUtc': '2026-08-01T09:45:00Z',
            'isPaid': false,
            'status': 0,
            'cancellationReason': null,
            'canReview': false,
            'allowedActions': ['pay'],
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      final api = ApiClient(
        _TestTokenSource(),
        httpClient: client,
        baseUrlOverride: 'http://test.local',
      );
      addTearDown(api.close);
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: const ReservationDetailsScreen(
              reservationId: 'pending-stripe-reservation',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.textContaining('Platiti do'), findsNothing);
      expect(find.text('Nastavi Stripe plaćanje'), findsNothing);
    },
  );

  testWidgets('Trainer Termini refresh shows a newly confirmed booking', (
    tester,
  ) async {
    var reservationLoads = 0;
    final client = MockClient((request) async {
      reservationLoads++;
      final items = reservationLoads == 1
          ? <Object>[]
          : <Object>[
              {
                'id': 'reservation-in-person',
                'memberName': 'Član Test',
                'offeringName': 'Individualni trening',
                'startsAtUtc': '2026-08-01T10:00:00Z',
                'status': 1,
                'paymentMethod': 1,
              },
            ];
      return http.Response(
        jsonEncode(_page(items)),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    final controller = ReservationRefreshController();
    addTearDown(api.close);
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: Scaffold(
            body: TrainerAppointmentsScreen(controller: controller),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Nema termina'), findsOneWidget);

    controller.refresh();
    await tester.pumpAndSettle();

    expect(find.text('Član Test'), findsOneWidget);
    expect(find.text('Confirmed'), findsOneWidget);
    expect(reservationLoads, 2);
  });

  testWidgets(
    'Trainer availability is compact and responsive on a narrow phone',
    (tester) async {
      tester.view.physicalSize = const Size(320, 720);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _scheduleApi(
        shifts: const [
          {'dayOfWeek': 1, 'period': 0},
          {'dayOfWeek': 2, 'period': 1},
        ],
      );
      addTearDown(api.close);

      await tester.pumpWidget(_availabilityHarness(api));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('availability-day-1')), findsOneWidget);
      expect(find.byKey(const Key('availability-day-0')), findsOneWidget);
      final today = DateUtils.dateOnly(DateTime.now());
      final saturday = today.add(
        Duration(days: (DateTime.saturday - today.weekday + 7) % 7),
      );
      expect(
        find.text(
          'Subota - ${saturday.day.toString().padLeft(2, '0')}.${saturday.month.toString().padLeft(2, '0')}.',
        ),
        findsOneWidget,
      );
      expect(find.text('2 aktivna dana · 2 odabrane smjene'), findsOneWidget);
      expect(find.text('Jutarnja'), findsNWidgets(7));
      expect(find.text('Večernja'), findsNWidgets(7));
      expect(find.text('Raspored je sačuvan'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Trainer availability preserves edits, resets, and saves payload',
    (tester) async {
      tester.view.physicalSize = const Size(412, 915);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var putCount = 0;
      final submittedBodies = <Map<String, dynamic>>[];
      final client = MockClient((request) async {
        if (request.method == 'GET') {
          return _jsonResponse({
            'concurrencyToken': 'token-1',
            'shifts': [
              {'dayOfWeek': 1, 'period': 0},
            ],
          });
        }
        putCount++;
        final body = Map<String, dynamic>.from(jsonDecode(request.body) as Map);
        submittedBodies.add(body);
        if (putCount == 1) {
          return _jsonResponse(
            {
              'title': 'schedule_save_failed',
              'detail': 'Raspored trenutno nije moguće sačuvati.',
            },
            statusCode: 503,
            contentType: 'application/problem+json; charset=utf-8',
          );
        }
        return _jsonResponse({
          'concurrencyToken': 'token-2',
          'shifts': body['shifts'],
        });
      });
      final api = ApiClient(
        _TestTokenSource(),
        httpClient: client,
        baseUrlOverride: 'http://test.local',
      );
      addTearDown(api.close);
      await tester.pumpWidget(_availabilityHarness(api));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('availability-shift-1-1')));
      await tester.pumpAndSettle();
      expect(find.text('Nesačuvane izmjene'), findsOneWidget);
      expect(find.text('1 aktivan dan · 2 odabrane smjene'), findsOneWidget);
      expect(find.byKey(const Key('availability-reset')), findsOneWidget);

      await tester.drag(
        find.byKey(const Key('trainer-availability-scroll')),
        const Offset(0, 320),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
      expect(find.text('Odbaciti izmjene?'), findsOneWidget);
      await tester.tap(find.text('Odustani'));
      await tester.pumpAndSettle();
      expect(find.text('Nesačuvane izmjene'), findsOneWidget);

      await tester.tap(find.byKey(const Key('availability-save')));
      await tester.pumpAndSettle();
      expect(find.text('Nesačuvane izmjene'), findsOneWidget);
      expect(
        find.textContaining('Usluga trenutno nije dostupna'),
        findsOneWidget,
      );
      await tester.pump(const Duration(seconds: 5));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('availability-save')));
      await tester.pumpAndSettle();
      expect(find.text('Raspored je sačuvan'), findsOneWidget);
      expect(find.byKey(const Key('availability-reset')), findsNothing);
      expect(putCount, 2);
      expect(submittedBodies.last['trainerProfileId'], _emptyTestGuid);
      expect(submittedBodies.last['concurrencyToken'], 'token-1');
      expect(submittedBodies.last['shifts'], [
        {'dayOfWeek': 1, 'period': 0},
        {'dayOfWeek': 1, 'period': 1},
      ]);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Trainer availability conflict refreshes baseline but keeps edits',
    (tester) async {
      var getCount = 0;
      final client = MockClient((request) async {
        if (request.method == 'GET') {
          getCount++;
          return _jsonResponse({
            'concurrencyToken': getCount == 1 ? 'token-1' : 'token-2',
            'shifts': getCount == 1
                ? [
                    {'dayOfWeek': 1, 'period': 0},
                  ]
                : [
                    {'dayOfWeek': 5, 'period': 1},
                  ],
          });
        }
        return _jsonResponse(
          {
            'title': 'concurrency_conflict',
            'detail': 'Raspored je promijenjen na drugom uređaju.',
          },
          statusCode: 409,
          contentType: 'application/problem+json; charset=utf-8',
        );
      });
      final api = ApiClient(
        _TestTokenSource(),
        httpClient: client,
        baseUrlOverride: 'http://test.local',
      );
      addTearDown(api.close);
      await tester.pumpWidget(_availabilityHarness(api));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('availability-shift-2-0')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('availability-save')));
      await tester.pumpAndSettle();

      expect(getCount, 2);
      expect(find.textContaining('Vaše izmjene su zadržane'), findsOneWidget);
      expect(find.text('2 aktivna dana · 2 odabrane smjene'), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(const Key('availability-shift-2-0')),
          matching: find.byIcon(Icons.check_circle),
        ),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('availability-reset')));
      await tester.pumpAndSettle();
      expect(find.text('1 aktivan dan · 1 odabrana smjena'), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(const Key('availability-shift-5-1')),
          matching: find.byIcon(Icons.check_circle),
        ),
        findsOneWidget,
      );
    },
  );

  testWidgets('Mobile gym map controls zoom, recenter, and preserve markers', (
    tester,
  ) async {
    final gymQueries = <String?>[];
    final navigatorObserver = _RecordingNavigatorObserver();
    final client = MockClient((request) async {
      if (request.url.path == '/api/gyms') {
        gymQueries.add(request.url.queryParameters['query']);
        final mostar = request.url.queryParameters['query'] == 'Mostar';
        final noLocation =
            request.url.queryParameters['query'] == 'Bez lokacije';
        return _jsonResponse(
          _page([
            {
              'id': noLocation
                  ? 'gym-without-location'
                  : mostar
                  ? 'gym-mostar'
                  : 'gym-sarajevo',
              'name': noLocation
                  ? 'Gym bez lokacije'
                  : mostar
                  ? 'Gym Mostar'
                  : 'Gym Sarajevo',
              'latitude': noLocation ? null : (mostar ? 43.3438 : 43.8563),
              'longitude': noLocation ? null : (mostar ? 17.8078 : 18.4131),
            },
          ]),
        );
      }
      return _jsonResponse(const <Object>[]);
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          navigatorObservers: [navigatorObserver],
          home: const Scaffold(body: GymDiscoveryScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('gym-discovery-map-controls')), findsOneWidget);
    expect(find.byKey(const Key('gym-discovery-map-zoom-in')), findsOneWidget);
    expect(find.byKey(const Key('gym-discovery-map-zoom-out')), findsOneWidget);
    expect(find.byKey(const Key('gym-discovery-map-center')), findsOneWidget);
    expect(
      find.byKey(const Key('gym-map-marker-gym-sarajevo')),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('gym-map-marker-gym-sarajevo')));
    await tester.pump();
    expect(navigatorObserver.pushCount, 2);
    navigatorObserver.navigator!.pop();
    await tester.pumpAndSettle();

    final map = tester.widget<FlutterMap>(find.byType(FlutterMap));
    final controller = map.mapController!;
    expect(controller.camera.zoom, 13);
    await tester.tap(find.byKey(const Key('gym-discovery-map-zoom-in')));
    await tester.pump();
    expect(controller.camera.zoom, 14);
    controller.move(controller.camera.center, 19);
    await tester.tap(find.byKey(const Key('gym-discovery-map-zoom-in')));
    await tester.pump();
    expect(controller.camera.zoom, 19);
    controller.move(controller.camera.center, 6);
    await tester.tap(find.byKey(const Key('gym-discovery-map-zoom-out')));
    await tester.pump();
    expect(controller.camera.zoom, 6);

    controller.move(const LatLng(42, 16), 17);
    await tester.tap(find.byKey(const Key('gym-discovery-map-center')));
    await tester.pump();
    expect(controller.camera.zoom, 13);
    expect(controller.camera.center.latitude, closeTo(43.8563, 0.0001));
    expect(controller.camera.center.longitude, closeTo(18.4131, 0.0001));

    await tester.enterText(find.byKey(const Key('gym-search-field')), 'Mostar');
    await tester.tap(find.byKey(const Key('gym-search-submit')));
    await tester.pumpAndSettle();
    expect(gymQueries, [null, 'Mostar']);
    final updatedMap = tester.widget<FlutterMap>(find.byType(FlutterMap));
    final updatedController = updatedMap.mapController!;
    expect(updatedController.camera.center.latitude, closeTo(43.3438, 0.0001));
    expect(updatedController.camera.center.longitude, closeTo(17.8078, 0.0001));
    expect(
      find.byKey(const Key('gym-map-marker-gym-mostar'), skipOffstage: false),
      findsOneWidget,
    );

    await tester.enterText(
      find.byKey(const Key('gym-search-field')),
      'Bez lokacije',
    );
    await tester.tap(find.byKey(const Key('gym-search-submit')));
    await tester.pumpAndSettle();
    expect(gymQueries, [null, 'Mostar', 'Bez lokacije']);
    final fallbackMap = tester.widget<FlutterMap>(find.byType(FlutterMap));
    expect(fallbackMap.mapController!.camera.zoom, 13);
    expect(
      fallbackMap.mapController!.camera.center.latitude,
      closeTo(43.8563, 0.0001),
    );
    expect(
      fallbackMap.mapController!.camera.center.longitude,
      closeTo(18.4131, 0.0001),
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('password reset keeps the form open for empty values', (
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

  testWidgets('login exposes and opens password recovery', (tester) async {
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

  testWidgets('active gym membership is explained and purchase is disabled', (
    tester,
  ) async {
    final client = MockClient((request) async {
      final body = switch (request.url.path) {
        '/api/gyms/gym-1' => {
          'id': 'gym-1',
          'name': 'Test Gym',
          'description': 'Test description',
          'address': 'Testna 1',
          'city': 'Sarajevo',
          'averageRating': 4.5,
          'reviewCount': 2,
          'imageUrls': <String>[],
        },
        '/api/gyms/gym-1/membership-plans' => [
          {
            'id': 'plan-1',
            'name': 'Mjesečna',
            'durationDays': 30,
            'price': 50,
            'currency': 'BAM',
          },
        ],
        '/api/gyms/gym-1/trainers' => <Object>[],
        '/api/gyms/gym-1/reviews' => _page(<Object>[]),
        '/api/me/memberships' => _page([
          {
            'id': 'membership-1',
            'gymId': 'gym-1',
            'status': 1,
            'endsAtUtc': '2030-01-31T00:00:00Z',
          },
        ]),
        '/api/me/membership-requests' => _page(<Object>[]),
        _ => throw StateError('Unexpected request: ${request.url}'),
      };
      return http.Response(
        jsonEncode(body),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const GymDetailsScreen(gymId: 'gym-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final renderedText = tester
        .widgetList<Text>(find.byType(Text))
        .map((widget) => widget.data)
        .whereType<String>()
        .join(' | ');
    expect(
      find.byType(ListView),
      findsOneWidget,
      reason: 'Rendered text: $renderedText',
    );
    await tester.drag(find.byType(ListView), const Offset(0, -420));
    await tester.pumpAndSettle();

    expect(find.text('Status članstva'), findsOneWidget);
    expect(find.textContaining('Već imate aktivno članstvo'), findsOneWidget);
    final purchase = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, '50 BAM'),
    );
    expect(purchase.onPressed, isNull);
    expect(tester.takeException(), isNull);
  });

  testWidgets('membership details expose contextual cancellation', (
    tester,
  ) async {
    var cancelled = false;
    final client = MockClient((request) async {
      if (request.method == 'POST') cancelled = true;
      final membership = {
        'id': 'membership-1',
        'gymName': 'Test Gym',
        'planName': 'Mjesečna',
        'price': 50,
        'currency': 'BAM',
        'startsAtUtc': '2030-01-01T00:00:00Z',
        'endsAtUtc': '2030-01-31T00:00:00Z',
        'status': cancelled ? 3 : 1,
        'statusReason': cancelled ? 'Otkazao član' : null,
        'allowedActions': cancelled ? <String>[] : ['cancel'],
        'concurrencyToken': cancelled ? 'token-2' : 'token-1',
      };
      return http.Response(
        jsonEncode(membership),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const MembershipDetailsScreen(membershipId: 'membership-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Detalji članstva'), findsOneWidget);
    expect(find.text('Otkaži članstvo'), findsOneWidget);
    await tester.tap(find.text('Otkaži članstvo'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Otkaži').last);
    await tester.pumpAndSettle();

    expect(cancelled, isTrue);
    expect(find.text('Otkaži članstvo'), findsNothing);
  });

  testWidgets('payment result refreshes authoritative server state', (
    tester,
  ) async {
    final client = MockClient(
      (_) async => http.Response(
        jsonEncode({
          'id': 'payment-1',
          'status': 2,
          'isPaid': true,
          'purpose': 1,
          'targetId': 'reservation-1',
        }),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      ),
    );
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: client,
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    final reservations = ReservationRefreshController();
    var reservationRefreshes = 0;
    reservations.addListener(() => reservationRefreshes++);
    addTearDown(reservations.dispose);
    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: reservations),
        ],
        child: const MaterialApp(
          home: PaymentResultScreen(outcome: 'success', paymentId: 'payment-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Plaćanje uspješno'), findsOneWidget);
    expect(
      find.text('Termin je potvrđen i razgovor s trenerom je spreman.'),
      findsOneWidget,
    );
    expect(find.text('Osvježi status'), findsNothing);
    expect(find.byKey(const Key('payment-open-chat')), findsOneWidget);
    expect(reservationRefreshes, 1);
    expect(find.byKey(const Key('payment-return-home')), findsOneWidget);
    expect(
      tester.widget(find.byKey(const Key('payment-return-home'))),
      isA<OutlinedButton>(),
    );
    expect(
      tester.getSize(find.byKey(const Key('payment-open-chat'))).width,
      greaterThan(300),
    );
    expect(
      tester.getTopLeft(find.byKey(const Key('payment-return-home'))).dy,
      greaterThan(
        tester.getTopLeft(find.byKey(const Key('payment-open-chat'))).dy,
      ),
    );
  });
}

Map<String, Object> _page(List<Object> items) => {
  'items': items,
  'page': 1,
  'pageSize': 20,
  'totalCount': items.length,
};

String _testDateKey(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-'
    '${date.month.toString().padLeft(2, '0')}-'
    '${date.day.toString().padLeft(2, '0')}';

const _emptyTestGuid = '00000000-0000-0000-0000-000000000000';

ApiClient _scheduleApi({required List<Map<String, Object>> shifts}) {
  final client = MockClient(
    (_) async =>
        _jsonResponse({'concurrencyToken': 'token-1', 'shifts': shifts}),
  );
  return ApiClient(
    _TestTokenSource(),
    httpClient: client,
    baseUrlOverride: 'http://test.local',
  );
}

Widget _availabilityHarness(ApiClient api) => Provider<ApiClient>.value(
  value: api,
  child: MaterialApp(
    theme: buildGymLinkTheme(),
    home: const Scaffold(body: TrainerAvailabilityScreen()),
  ),
);

http.Response _jsonResponse(
  Object body, {
  int statusCode = 200,
  String contentType = 'application/json; charset=utf-8',
}) => http.Response(
  jsonEncode(body),
  statusCode,
  headers: {'content-type': contentType},
);

class _TestTokenSource implements AuthTokenSource {
  @override
  String? get accessToken => 'test-token';

  @override
  Future<void> invalidate() async {}

  @override
  Future<bool> refresh() async => false;
}

class _RecordingNavigatorObserver extends NavigatorObserver {
  int pushCount = 0;

  @override
  void didPush(Route<dynamic> route, Route<dynamic>? previousRoute) {
    pushCount++;
    super.didPush(route, previousRoute);
  }
}
