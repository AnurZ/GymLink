import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/core/auth.dart';
import 'package:gymlink_mobile/core/payments.dart';
import 'package:gymlink_mobile/core/theme.dart';
import 'package:gymlink_mobile/features/auth/auth_screens.dart';
import 'package:gymlink_mobile/features/member/gym_screens.dart';
import 'package:gymlink_mobile/features/member/membership_screen.dart';
import 'package:gymlink_mobile/features/member/reservation_screen.dart';
import 'package:gymlink_mobile/features/notifications/notification_screen.dart';
import 'package:gymlink_mobile/features/notifications/notification_controller.dart';
import 'package:gymlink_mobile/features/chat/chat_models.dart';
import 'package:gymlink_mobile/features/chat/chat_realtime.dart';
import 'package:gymlink_mobile/features/payments/payment_result_screen.dart';
import 'package:gymlink_mobile/features/profile/profile_screen.dart';
import 'package:gymlink_mobile/features/reservations/reservation_refresh_controller.dart';
import 'package:gymlink_mobile/features/trainer/trainer_screens.dart';
import 'package:gymlink_mobile/shared/widgets.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';

void main() {
  testWidgets('notification tabs stay compact and map all/unread queries', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 700);
    tester.view.devicePixelRatio = 1;
    tester.platformDispatcher.textScaleFactorTestValue = 1.6;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    final isReadQueries = <bool?>[];
    final markAllGate = Completer<void>();
    var markAllRequests = 0;
    final api = ApiClient(
      _TestTokenSource(),
      baseUrlOverride: 'http://test.local',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/me/notifications/read-all') {
          markAllRequests++;
          await markAllGate.future;
          return _jsonResponse({});
        }
        if (request.url.path == '/api/me/notifications') {
          isReadQueries.add(
            request.url.queryParameters['isRead'] == null
                ? null
                : request.url.queryParameters['isRead'] == 'false'
                ? false
                : true,
          );
          return _jsonResponse({
            'items': [
              {
                'id': 'notice-1',
                'title': 'Nova obavijest',
                'text': 'Sadržaj obavijesti',
                'createdAtUtc': '2026-08-14T08:00:00Z',
                'isRead': false,
                'concurrencyToken': 'token-1',
              },
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
        }
        return _jsonResponse({}, statusCode: 404);
      }),
    );
    final auth = AuthController();
    final realtime = _SilentRealtime();
    final notifications = NotificationController(api, auth, realtime);
    addTearDown(notifications.dispose);
    addTearDown(realtime.dispose);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: notifications),
        ],
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: const NotificationScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(isReadQueries, [null]);
    expect(find.text('Pročitane'), findsNothing);
    expect(find.byKey(const Key('notifications-all-tab')), findsOneWidget);
    expect(find.byKey(const Key('notifications-unread-tab')), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.byKey(const Key('notifications-unread-tab')));
    await tester.pumpAndSettle();
    expect(isReadQueries.last, false);

    final markAll = find.byKey(const Key('mark-all-notifications-read'));
    expect(find.text('Označi sve kao pročitano'), findsOneWidget);
    expect(tester.widget<TextButton>(markAll).onPressed, isNotNull);
    await tester.tap(markAll);
    await tester.pump();
    expect(
      find.descendant(
        of: markAll,
        matching: find.byType(CircularProgressIndicator),
      ),
      findsOneWidget,
    );
    expect(tester.widget<TextButton>(markAll).onPressed, isNull);
    markAllGate.complete();
    await tester.pumpAndSettle();
    expect(markAllRequests, 1);
    expect(tester.takeException(), isNull);
    await tester.pump(const Duration(seconds: 5));
  });

  testWidgets('notification detail marks read only after it opens', (
    tester,
  ) async {
    var readRequests = 0;
    final api = ApiClient(
      _TestTokenSource(),
      baseUrlOverride: 'https://api.test',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/me/notifications/notice-1/read') {
          readRequests++;
          return http.Response(
            jsonEncode({
              'title': 'concurrency_conflict',
              'detail': 'Obavijest je promijenjena. Osvježite listu.',
            }),
            409,
            headers: {'content-type': 'application/problem+json'},
          );
        }
        return http.Response('', 404);
      }),
    );
    final item = <String, dynamic>{
      'id': 'notice-1',
      'title': 'Članstvo aktivirano',
      'text': 'Teretana je odobrila aktivaciju članstva.',
      'createdAtUtc': '2026-08-09T10:00:00Z',
      'isRead': false,
      'concurrencyToken': 'token',
    };

    expect(readRequests, 0);
    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider(create: (_) => AuthController()),
        ],
        child: MaterialApp(home: NotificationDetailScreen(item: item)),
      ),
    );
    await tester.pumpAndSettle();

    expect(readRequests, 1);
    expect(find.text('Detalji obavijesti'), findsOneWidget);
    expect(
      find.text('Teretana je odobrila aktivaciju članstva.'),
      findsOneWidget,
    );
    expect(item['isRead'], false);
  });

  testWidgets('completed reservation notification opens review dialog', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    var reservationLoads = 0;
    final auth = AuthController();
    final api = ApiClient(
      auth,
      baseUrlOverride: 'https://api.test',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/auth/login') {
          return _jsonResponse({
            'accessToken': 'access-token',
            'refreshToken': 'refresh-token',
            'user': {
              'id': 'member-1',
              'displayName': 'Član Test',
              'role': 'Member',
            },
          });
        }
        if (request.url.path == '/api/me/reservations/reservation-1') {
          reservationLoads++;
          return _jsonResponse({
            'id': 'reservation-1',
            'trainerName': 'Trener Test',
            'gymName': 'GymLink Centar',
            'offeringName': 'Individualni trening',
            'startsAtUtc': '2026-08-01T10:00:00Z',
            'durationMinutes': 60,
            'price': 30,
            'currency': 'BAM',
            'paymentMethod': 1,
            'isPaid': false,
            'status': 2,
            'canReview': true,
            'allowedActions': <String>[],
            'concurrencyToken': 'token-1',
          });
        }
        return _jsonResponse({}, statusCode: 404);
      }),
    );
    auth.attachApi(api);
    await auth.login('member', 'Test123!');
    addTearDown(api.close);
    addTearDown(auth.dispose);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: auth),
        ],
        child: MaterialApp(
          home: NotificationDetailScreen(
            item: {
              'id': 'notice-1',
              'category': 'reservation.completed',
              'title': 'Rezervacija',
              'text': 'Termin je završen.',
              'createdAtUtc': '2026-08-01T11:00:00Z',
              'isRead': true,
              'targetType': 'reservation',
              'targetId': 'reservation-1',
              'concurrencyToken': 'notice-token',
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(const Key('notification-open-completed-reservation')),
    );
    await tester.pumpAndSettle();

    expect(reservationLoads, 1);
    expect(find.byType(TrainerReviewDialog), findsOneWidget);
  });

  testWidgets('trainer reservation notification opens appointment details', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    final auth = AuthController();
    final api = ApiClient(
      auth,
      baseUrlOverride: 'https://api.test',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/auth/login') {
          return _jsonResponse({
            'accessToken': 'access-token',
            'refreshToken': 'refresh-token',
            'user': {
              'id': 'trainer-user-1',
              'displayName': 'Trener Test',
              'role': 'Trainer',
            },
          });
        }
        if (request.url.path == '/api/me/trainer-reservations/reservation-1') {
          return _jsonResponse({
            'id': 'reservation-1',
            'memberName': 'Član Test',
            'gymName': 'GymLink Centar',
            'offeringName': 'Individualni trening',
            'startsAtUtc': '2026-08-01T10:00:00Z',
            'durationMinutes': 60,
            'paymentMethod': 1,
            'status': 1,
            'allowedActions': <String>[],
            'concurrencyToken': 'token-1',
          });
        }
        return _jsonResponse({}, statusCode: 404);
      }),
    );
    auth.attachApi(api);
    await auth.login('trainer', 'Test123!');
    addTearDown(api.close);
    addTearDown(auth.dispose);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: auth),
        ],
        child: MaterialApp(
          home: NotificationDetailScreen(
            item: {
              'id': 'notice-1',
              'category': 'reservation.confirmed',
              'title': 'Rezervacija',
              'text': 'Termin je potvrđen.',
              'createdAtUtc': '2026-08-01T09:00:00Z',
              'isRead': true,
              'targetType': 'reservation',
              'targetId': 'reservation-1',
              'concurrencyToken': 'notice-token',
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(const Key('notification-open-trainer-reservation')),
    );
    await tester.pumpAndSettle();

    expect(find.byType(TrainerAppointmentDetails), findsOneWidget);
    expect(find.text('Detalji termina'), findsOneWidget);
  });

  testWidgets('trainer image avatar falls back to initials', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(body: TrainerImageAvatar(name: 'Anur Zjakić')),
      ),
    );

    expect(find.text('AZ'), findsOneWidget);
  });

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
                await showDialog<bool>(
                  context: context,
                  builder: (_) => TrainerCancellationReasonDialog(
                    onSubmit: (reason) async => submittedReason = reason,
                  ),
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
    final submit = find.descendant(
      of: find.byType(TrainerCancellationReasonDialog),
      matching: find.widgetWithText(FilledButton, 'Otkaži'),
    );
    await tester.tap(submit);
    await tester.pump();

    expect(find.text('Unesite razlog otkazivanja.'), findsOneWidget);
    expect(find.byType(TrainerCancellationReasonDialog), findsOneWidget);
    expect(submittedReason, isNull);

    await tester.enterText(find.byType(TextFormField), 'Bolest trenera');
    await tester.tap(submit);
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
                await showDialog<bool>(
                  context: context,
                  builder: (_) => TrainerReviewDialog(
                    onSubmit: (body) async => submittedReview = body,
                  ),
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

  testWidgets(
    'trainer offering validates fields inline and keeps dialog open',
    (tester) async {
      var postCount = 0;
      final api = ApiClient(
        _TestTokenSource(),
        baseUrlOverride: 'http://test.local',
        httpClient: MockClient((request) async {
          if (request.url.path == '/api/tenant/trainer-offerings' &&
              request.method == 'GET') {
            return _jsonResponse(_page(const []));
          }
          if (request.url.path == '/api/reference-data/lookups') {
            return _jsonResponse({
              'trainingTypes': [
                {'id': 'type-1', 'name': 'Individualni trening'},
              ],
            });
          }
          if (request.url.path == '/api/tenant/trainer-offerings' &&
              request.method == 'POST') {
            postCount++;
            return _jsonResponse({}, statusCode: 201);
          }
          return _jsonResponse({}, statusCode: 404);
        }),
      );
      addTearDown(api.close);
      await tester.pumpWidget(
        Provider<ApiClient>.value(
          value: api,
          child: MaterialApp(
            theme: buildGymLinkTheme(),
            home: const Scaffold(body: TrainerOfferingsScreen()),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Dodaj uslugu'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Sačuvaj'));
      await tester.pump();

      expect(find.text('Unesite naziv usluge.'), findsOneWidget);
      expect(find.text('Nova usluga'), findsOneWidget);
      expect(postCount, 0);

      await tester.enterText(
        find.widgetWithText(TextFormField, 'Trajanje (min)'),
        '1441',
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Cijena (BAM)'),
        '1000001',
      );
      await tester.tap(find.text('Sačuvaj'));
      await tester.pump();
      expect(find.text('Unesite cijeli broj od 1 do 1440.'), findsOneWidget);
      expect(find.text('Unesite cijenu od 0 do 1.000.000.'), findsOneWidget);
    },
  );

  testWidgets('trainer offering displays server field error without closing', (
    tester,
  ) async {
    var postCount = 0;
    final api = ApiClient(
      _TestTokenSource(),
      baseUrlOverride: 'http://test.local',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/tenant/trainer-offerings' &&
            request.method == 'GET') {
          return _jsonResponse(_page(const []));
        }
        if (request.url.path == '/api/reference-data/lookups') {
          return _jsonResponse({
            'trainingTypes': [
              {'id': 'type-1', 'name': 'Individualni trening'},
            ],
          });
        }
        postCount++;
        return _jsonResponse(
          {
            'title': 'One or more validation errors occurred.',
            'errors': {
              'Name': ['Naziv usluge već postoji.'],
            },
          },
          statusCode: 400,
          contentType: 'application/problem+json; charset=utf-8',
        );
      }),
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: const MaterialApp(
          home: Scaffold(body: TrainerOfferingsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dodaj uslugu'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Naziv'),
      'Individualni',
    );
    await tester.tap(find.text('Sačuvaj'));
    await tester.pumpAndSettle();

    expect(postCount, 1);
    expect(find.text('Naziv usluge već postoji.'), findsWidgets);
    expect(find.text('Nova usluga'), findsOneWidget);
    expect(find.text('One or more validation errors occurred.'), findsNothing);
  });

  testWidgets('reservation payment sheet offers Stripe and pay in person', (
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
    expect(find.byKey(const Key('reservation-payment-manual')), findsNothing);
    expect(find.textContaining('vanjski preglednik'), findsOneWidget);
    expect(find.textContaining('automatski ćete se vratiti'), findsOneWidget);

    final payInPerson = find.byKey(const Key('reservation-payment-in-person'));
    await tester.ensureVisible(payInPerson);
    await tester.tap(payInPerson);
    await tester.pumpAndSettle();

    expect(selected, ReservationPaymentMethod.payInPerson);
  });

  testWidgets('membership payment sheet explains pay in person', (
    tester,
  ) async {
    MembershipPaymentMethod? selected;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                selected = await chooseMembershipPaymentMethod(context);
              },
              child: const Text('Plati članarinu'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Plati članarinu'));
    await tester.pumpAndSettle();

    expect(find.text('Stripe'), findsOneWidget);
    expect(find.text('Plati uživo'), findsOneWidget);
    expect(find.byKey(const Key('membership-payment-manual')), findsNothing);
    expect(
      find.text(
        'Nakon što platite članarinu u teretani, administrator će vam odobriti članstvo.',
      ),
      findsOneWidget,
    );
    expect(find.textContaining('ALLOW_FAKE_PAYMENTS'), findsNothing);

    final payInPerson = find.byKey(const Key('membership-payment-in-person'));
    await tester.ensureVisible(payInPerson);
    await tester.tap(payInPerson);
    await tester.pumpAndSettle();

    expect(selected, MembershipPaymentMethod.payInPerson);
  });

  testWidgets(
    'pay in person sends compatible method and shows field validation',
    (tester) async {
      tester.view.physicalSize = const Size(400, 1600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      Map<String, dynamic>? submittedBody;
      var postAttempts = 0;
      var created = false;
      final client = MockClient((request) async {
        if (request.method == 'POST' &&
            request.url.path == '/api/membership-requests') {
          submittedBody = Map<String, dynamic>.from(
            jsonDecode(request.body) as Map,
          );
          postAttempts++;
          if (postAttempts == 1) {
            return _jsonResponse({
              'title': 'validation_failed',
              'detail': 'One or more validation errors occurred.',
              'errors': {
                'PaymentMethod': ['Odaberite podržan način plaćanja.'],
              },
            }, statusCode: 400);
          }
          created = true;
          return _jsonResponse({
            'id': 'request-1',
            'status': 0,
            'paymentMethod': 2,
          }, statusCode: 201);
        }
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
          '/api/gyms/gym-1/membership-plans' => _page([
            {
              'id': 'plan-1',
              'name': 'Mjesečna',
              'durationDays': 30,
              'price': 50,
              'currency': 'BAM',
            },
          ]),
          '/api/gyms/gym-1/trainers' => _page(<Object>[]),
          '/api/gyms/gym-1/reviews' => _page(<Object>[]),
          '/api/me/memberships' => _page(<Object>[]),
          '/api/me/membership-requests' => _page(
            created
                ? [
                    {
                      'id': 'request-1',
                      'gymId': 'gym-1',
                      'status': 0,
                      'paymentMethod': 2,
                    },
                  ]
                : <Object>[],
          ),
          _ => throw StateError('Unexpected request: ${request.url}'),
        };
        return _jsonResponse(body);
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
      await tester.drag(find.byType(ListView), const Offset(0, -420));
      await tester.pumpAndSettle();

      Future<void> selectPayInPerson() async {
        await tester.tap(find.text('50 BAM'));
        await tester.pumpAndSettle();
        final payInPerson = find.byKey(
          const Key('membership-payment-in-person'),
        );
        await tester.ensureVisible(payInPerson);
        await tester.tap(payInPerson);
        await tester.pumpAndSettle();
      }

      await selectPayInPerson();
      expect(find.text('Odaberite podržan način plaćanja.'), findsOneWidget);
      expect(find.textContaining('One or more validation'), findsNothing);

      await selectPayInPerson();
      expect(submittedBody?['paymentMethod'], 2);
      expect(postAttempts, 2);
      expect(
        find.textContaining(
          'zahtjev za članstvo koji čeka obradu',
          findRichText: true,
        ),
        findsWidgets,
      );
    },
  );

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
        expect(request.url.queryParameters['page'], '1');
        expect(request.url.queryParameters['pageSize'], '100');
        return _jsonResponse(
          _page([
            {
              'id': 'offering-1',
              'name': 'Individualni trening',
              'durationMinutes': 60,
              'price': 30,
              'currency': 'BAM',
              'isActive': true,
            },
          ]),
        );
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
              {
                'id': 'reservation-cancelled',
                'trainerName': 'Trener Otkazan',
                'gymName': 'GymLink Centar',
                'offeringName': 'Individualni trening',
                'startsAtUtc': '2026-08-02T10:00:00Z',
                'price': 30,
                'currency': 'BAM',
                'status': 3,
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
    expect(
      find.byKey(const Key('member-reservation-chat-reservation-in-person')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('member-reservation-chat-reservation-cancelled')),
      findsNothing,
    );
    expect(reservationLoads, 2);
  });

  testWidgets(
    'Member Termini status filter includes pending payment and keeps enum values',
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
      expect(find.text('Čeka plaćanje'), findsOneWidget);
      expect(find.text('Confirmed'), findsOneWidget);
      expect(find.text('Completed'), findsOneWidget);
      expect(find.text('Cancelled'), findsOneWidget);

      await tester.tap(find.text('Confirmed'));
      await tester.pumpAndSettle();
      expect(requestedStatuses, [null, '1']);
    },
  );

  testWidgets('Clanarine filters memberships and requests independently', (
    tester,
  ) async {
    final membershipStatuses = <String?>[];
    final requestStatuses = <String?>[];
    final client = MockClient((request) async {
      if (request.url.path == '/api/me/memberships') {
        membershipStatuses.add(request.url.queryParameters['status']);
      } else if (request.url.path == '/api/me/membership-requests') {
        requestStatuses.add(request.url.queryParameters['status']);
      }
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
          home: const Scaffold(body: MembershipScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Još nemate članstvo'), findsOneWidget);
    expect(find.text('Još nemate zahtjeva'), findsOneWidget);

    await tester.tap(find.byKey(const Key('membership-status-filter')));
    await tester.pumpAndSettle();
    expect(find.text('PendingPayment'), findsOneWidget);
    expect(find.text('Active'), findsOneWidget);
    expect(find.text('Expired'), findsOneWidget);
    expect(find.text('Cancelled'), findsOneWidget);
    expect(find.text('Suspended'), findsOneWidget);
    await tester.tap(find.text('Active'));
    await tester.pumpAndSettle();

    expect(find.text('Nema članstava'), findsOneWidget);
    expect(membershipStatuses, [null, '1']);
    expect(requestStatuses, [null, null]);

    final requestFilter = find.byKey(
      const Key('membership-request-status-filter'),
    );
    await tester.ensureVisible(requestFilter);
    await tester.tap(requestFilter);
    await tester.pumpAndSettle();
    expect(find.text('Pending'), findsOneWidget);
    expect(find.text('Approved'), findsOneWidget);
    expect(find.text('Rejected'), findsOneWidget);
    await tester.tap(find.text('Rejected'));
    await tester.pumpAndSettle();

    expect(find.text('Nema zahtjeva'), findsOneWidget);
    expect(membershipStatuses, [null, '1', '1']);
    expect(requestStatuses, [null, null, '2']);

    final membershipFilter = find.byKey(const Key('membership-status-filter'));
    await tester.drag(find.byType(ListView), const Offset(0, 600));
    await tester.pumpAndSettle();
    await tester.tap(membershipFilter);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Svi statusi').last);
    await tester.pumpAndSettle();

    expect(membershipStatuses, [null, '1', '1', null]);
    expect(requestStatuses, [null, null, '2', '2']);

    await tester.drag(find.byType(ListView), const Offset(0, 300));
    await tester.pumpAndSettle();
    expect(membershipStatuses, hasLength(5));
    expect(requestStatuses, hasLength(5));
    expect(membershipStatuses.last, isNull);
    expect(requestStatuses.last, '2');
  });

  testWidgets('Termini details show active Stripe deadline and retry action', (
    tester,
  ) async {
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
          'paymentDueAtUtc': '2999-08-01T09:45:00Z',
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

    expect(find.textContaining('Platiti do'), findsOneWidget);
    expect(find.text('Nastavi Stripe plaćanje'), findsOneWidget);
  });

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
              {
                'id': 'reservation-cancelled',
                'memberName': 'Član Otkazan',
                'offeringName': 'Individualni trening',
                'startsAtUtc': '2026-08-02T10:00:00Z',
                'status': 3,
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
    expect(
      find.text('Sortirano po datumu: najnoviji termini prvo.'),
      findsOneWidget,
    );

    controller.refresh();
    await tester.pumpAndSettle();

    expect(find.text('Član Test'), findsOneWidget);
    expect(find.text('Confirmed'), findsOneWidget);
    expect(
      find.byKey(const Key('trainer-appointment-chat-reservation-in-person')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('trainer-appointment-chat-reservation-cancelled')),
      findsNothing,
    );
    expect(reservationLoads, 2);
  });

  testWidgets(
    'Trainer Termini status filter preserves range and selected status',
    (tester) async {
      final requestedStatuses = <String?>[];
      final requestedRanges = <String?>[];
      final client = MockClient((request) async {
        requestedStatuses.add(request.url.queryParameters['status']);
        requestedRanges.add(request.url.queryParameters['fromUtc']);
        final items = request.url.queryParameters['status'] == '2'
            ? <Object>[
                {
                  'id': 'reservation-completed',
                  'memberName': 'Član Test',
                  'gymName': 'GymLink Centar',
                  'offeringName': 'Individualni trening',
                  'startsAtUtc': '2026-08-01T10:00:00Z',
                  'endsAtUtc': '2026-08-01T11:00:00Z',
                  'durationMinutes': 60,
                  'price': 30,
                  'currency': 'BAM',
                  'status': 2,
                  'paymentMethod': 1,
                  'allowedActions': <Object>[],
                  'concurrencyToken': 'token-1',
                },
              ]
            : const <Object>[];
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

      await tester.tap(
        find.byKey(const Key('trainer-appointment-status-filter')),
      );
      await tester.pumpAndSettle();
      expect(find.text('Confirmed'), findsOneWidget);
      expect(find.text('Completed'), findsOneWidget);
      expect(find.text('Cancelled'), findsOneWidget);
      expect(find.text('Pending'), findsNothing);
      await tester.tap(find.text('Completed'));
      await tester.pumpAndSettle();

      expect(requestedStatuses, [null, '2']);
      expect(requestedRanges, everyElement(isNotNull));
      expect(find.text('Član Test'), findsOneWidget);

      await tester.tap(find.text('Član Test'));
      await tester.pumpAndSettle();
      expect(find.byType(TrainerAppointmentDetails), findsOneWidget);
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(requestedStatuses, hasLength(3));
      expect(requestedStatuses.last, '2');

      controller.refresh();
      await tester.pumpAndSettle();
      expect(requestedStatuses, hasLength(4));
      expect(requestedStatuses.last, '2');

      await tester.drag(find.byType(ListView).first, const Offset(0, 300));
      await tester.pumpAndSettle();
      expect(requestedStatuses, hasLength(5));
      expect(requestedStatuses.last, '2');
      expect(requestedRanges, everyElement(isNotNull));
    },
  );

  testWidgets('trainer completion uses user-facing confirmation copy', (
    tester,
  ) async {
    final api = ApiClient(
      _TestTokenSource(),
      httpClient: MockClient((_) async => _jsonResponse({})),
      baseUrlOverride: 'http://test.local',
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp(
          home: TrainerAppointmentDetails(
            item: {
              'id': 'reservation-1',
              'memberName': 'Član Test',
              'gymName': 'GymLink Centar',
              'offeringName': 'Individualni trening',
              'startsAtUtc': '2026-08-01T10:00:00Z',
              'durationMinutes': 60,
              'paymentMethod': 1,
              'status': 1,
              'allowedActions': ['complete'],
              'concurrencyToken': 'token-1',
            },
          ),
        ),
      ),
    );

    await tester.tap(find.text('Označi završenim'));
    await tester.pumpAndSettle();

    expect(find.text('Završetak treninga'), findsOneWidget);
    expect(find.text('Želite li označiti trening završenim?'), findsOneWidget);
    expect(find.textContaining('complete'), findsNothing);
  });

  testWidgets('trainer opens a privacy-preserving review detail', (
    tester,
  ) async {
    final api = ApiClient(
      _TestTokenSource(),
      baseUrlOverride: 'http://test.local',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/profile') {
          return _jsonResponse({'trainerProfileId': 'trainer-1'});
        }
        if (request.url.path == '/api/trainers/trainer-1/reviews') {
          return _jsonResponse(
            _page([
              {
                'id': 'review-1',
                'rating': 5,
                'comment': 'Odličan trening i komunikacija.',
                'createdAtUtc': '2026-08-16T10:30:00Z',
                'reviewerName': 'Skriveni korisnik',
              },
            ]),
          );
        }
        return _jsonResponse({}, statusCode: 404);
      }),
    );
    addTearDown(api.close);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: const MaterialApp(home: TrainerReviewsScreen()),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Odličan trening i komunikacija.'));
    await tester.pumpAndSettle();

    expect(find.byType(TrainerReviewDetailsScreen), findsOneWidget);
    expect(find.text('Detalji recenzije'), findsOneWidget);
    expect(find.text('5 od 5'), findsOneWidget);
    expect(find.text('Anonimna recenzija'), findsOneWidget);
    expect(find.text('Skriveni korisnik'), findsNothing);
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
    tester.view.physicalSize = const Size(800, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final gymQueries = <String?>[];
    final navigatorObserver = _RecordingNavigatorObserver();
    final client = MockClient((request) async {
      if (request.url.path == '/api/gyms') {
        gymQueries.add(request.url.queryParameters['query']);
        final mostar = request.url.queryParameters['query'] == 'Mostar';
        final noLocation =
            request.url.queryParameters['query'] == 'Bez lokacije';
        if (!mostar && !noLocation) {
          return _jsonResponse(
            _page([
              {
                'id': 'gym-sarajevo',
                'name': 'Gym Sarajevo',
                'address': 'Zmaja od Bosne 12',
                'city': 'Sarajevo',
                'latitude': 43.8563,
                'longitude': 18.4131,
                'primaryImageUrl': 'http://invalid.test/gym.jpg',
                'startingMembershipPrice': 50,
                'currency': 'KM',
                'averageRating': 4.8,
              },
              {
                'id': 'gym-mostar-initial',
                'name': 'Gym Mostar',
                'latitude': 43.3438,
                'longitude': 17.8078,
              },
              {
                'id': 'gym-bihac',
                'name': 'Gym Bihać',
                'latitude': 44.8169,
                'longitude': 15.8708,
              },
            ]),
          );
        }
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
    final searchButton = find.byKey(const Key('gym-search-submit'));
    expect(
      tester.widget<IconButton>(searchButton).tooltip,
      'Pretraži teretane',
    );
    expect(
      find.descendant(of: searchButton, matching: find.byIcon(Icons.search)),
      findsOneWidget,
    );
    expect(find.byKey(const Key('gym-discovery-map-zoom-in')), findsOneWidget);
    expect(find.byKey(const Key('gym-discovery-map-zoom-out')), findsOneWidget);
    expect(find.byKey(const Key('gym-discovery-map-center')), findsOneWidget);
    expect(
      find.byKey(const Key('gym-map-marker-gym-sarajevo')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('gym-map-marker-image-gym-sarajevo')),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byKey(const Key('gym-map-marker-gym-bihac')),
        matching: find.byIcon(Icons.fitness_center),
      ),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('gym-map-marker-gym-sarajevo')));
    await tester.pump();
    expect(navigatorObserver.pushCount, 1);
    expect(
      find.byKey(const Key('gym-map-preview-gym-sarajevo')),
      findsOneWidget,
    );
    expect(find.text('Zmaja od Bosne 12, Sarajevo'), findsOneWidget);
    expect(find.text('4.8'), findsOneWidget);
    expect(find.text('50 KM/mjesec'), findsOneWidget);

    final selectedMap = tester.widget<FlutterMap>(find.byType(FlutterMap));
    selectedMap.options.onTap!(
      const TapPosition(Offset.zero, Offset.zero),
      const LatLng(43.85, 18.41),
    );
    await tester.pump();
    expect(find.byKey(const Key('gym-map-preview-gym-sarajevo')), findsNothing);

    await tester.tap(find.byKey(const Key('gym-map-marker-gym-sarajevo')));
    await tester.pump();
    final detailsButton = find.byKey(const Key('gym-map-preview-details'));
    await tester.ensureVisible(detailsButton);
    await tester.tap(detailsButton);
    await tester.pump();
    expect(navigatorObserver.pushCount, 2);
    navigatorObserver.navigator!.pop();
    await tester.pumpAndSettle();
    await tester.ensureVisible(
      find.byKey(const Key('gym-discovery-map-zoom-in')),
    );

    final map = tester.widget<FlutterMap>(find.byType(FlutterMap));
    final controller = map.mapController!;
    expect(controller.camera.zoom, inInclusiveRange(6, 10));
    final initialZoom = controller.camera.zoom;
    await tester.tap(find.byKey(const Key('gym-discovery-map-zoom-in')));
    await tester.pump();
    expect(controller.camera.zoom, closeTo(initialZoom + 1, 0.0001));
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
    expect(controller.camera.zoom, inInclusiveRange(6, 10));
    expect(
      controller.camera.visibleBounds.contains(const LatLng(43.8563, 18.4131)),
      isTrue,
    );
    expect(
      controller.camera.visibleBounds.contains(const LatLng(43.3438, 17.8078)),
      isTrue,
    );
    expect(
      controller.camera.visibleBounds.contains(const LatLng(44.8169, 15.8708)),
      isTrue,
    );

    await tester.enterText(
      find.byKey(const Key('gym-search-field')),
      'Sarajevo',
    );
    await tester.tap(find.byKey(const Key('gym-search-submit')));
    await tester.pumpAndSettle();
    expect(
      find.byKey(const Key('gym-map-preview-gym-sarajevo')),
      findsOneWidget,
    );

    await tester.enterText(find.byKey(const Key('gym-search-field')), 'Mostar');
    await tester.tap(find.byKey(const Key('gym-search-submit')));
    await tester.pumpAndSettle();
    expect(gymQueries, [null, 'Sarajevo', 'Mostar']);
    final updatedMap = tester.widget<FlutterMap>(find.byType(FlutterMap));
    final updatedController = updatedMap.mapController!;
    expect(updatedController.camera.zoom, 13);
    expect(updatedController.camera.center.latitude, closeTo(43.3438, 0.0001));
    expect(updatedController.camera.center.longitude, closeTo(17.8078, 0.0001));
    expect(
      find.byKey(const Key('gym-map-marker-gym-mostar'), skipOffstage: false),
      findsOneWidget,
    );
    expect(find.byKey(const Key('gym-map-preview-gym-sarajevo')), findsNothing);
    await tester.tap(find.byKey(const Key('gym-map-marker-gym-mostar')));
    await tester.pump();
    expect(find.text('Cijena članarine nije dostupna'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('gym-search-field')),
      'Bez lokacije',
    );
    await tester.tap(find.byKey(const Key('gym-search-submit')));
    await tester.pumpAndSettle();
    expect(gymQueries, [null, 'Sarajevo', 'Mostar', 'Bez lokacije']);
    final fallbackMap = tester.widget<FlutterMap>(find.byType(FlutterMap));
    expect(fallbackMap.mapController!.camera.zoom, 8);
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

  testWidgets('password reset can return to login without submitting', (
    tester,
  ) async {
    var resetRequests = 0;
    final api = ApiClient(
      _TestTokenSource(),
      baseUrlOverride: 'http://test.local',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/auth/reset-password') resetRequests++;
        return _jsonResponse({});
      }),
    );
    final router = GoRouter(
      initialLocation: '/reset-password',
      routes: [
        GoRoute(
          path: '/reset-password',
          builder: (_, _) => const ResetPasswordScreen(initialEmail: ''),
        ),
        GoRoute(
          path: '/login',
          builder: (_, _) => const Scaffold(body: Text('Prijava test')),
        ),
      ],
    );
    addTearDown(api.close);
    addTearDown(router.dispose);
    await tester.pumpWidget(
      Provider<ApiClient>.value(
        value: api,
        child: MaterialApp.router(routerConfig: router),
      ),
    );

    final backToLogin = find.byKey(const Key('reset-password-back-to-login'));
    await tester.ensureVisible(backToLogin);
    await tester.tap(backToLogin);
    await tester.pumpAndSettle();

    expect(find.text('Prijava test'), findsOneWidget);
    expect(resetRequests, 0);
  });

  testWidgets('mobile profile shows username as read-only identity', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    final auth = AuthController();
    final api = ApiClient(
      auth,
      baseUrlOverride: 'http://test.local',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/auth/login') {
          return _jsonResponse({
            'accessToken': 'access-token',
            'refreshToken': 'refresh-token',
            'user': {
              'id': 'member-1',
              'displayName': 'Član Test',
              'role': 'Member',
            },
          });
        }
        if (request.url.path == '/api/profile') {
          return _jsonResponse({
            'id': 'member-1',
            'username': 'clan.test',
            'displayName': 'Član Test',
            'email': 'clan@example.test',
            'phoneNumber': null,
          });
        }
        return _jsonResponse({}, statusCode: 404);
      }),
    );
    auth.attachApi(api);
    await auth.login('clan.test', 'Test123!');
    addTearDown(api.close);
    addTearDown(auth.dispose);

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          Provider<ApiClient>.value(value: api),
          ChangeNotifierProvider.value(value: auth),
        ],
        child: const MaterialApp(home: Scaffold(body: ProfileScreen())),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('profile-username')), findsOneWidget);
    expect(find.text('@clan.test'), findsOneWidget);
    expect(find.widgetWithText(TextFormField, 'clan.test'), findsNothing);
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
          'imageUrls': <String>[
            '/uploads/gym-images/cover.jpg',
            '/uploads/gym-images/inside.webp',
          ],
        },
        '/api/gyms/gym-1/membership-plans' => _page([
          {
            'id': 'plan-1',
            'name': 'Mjesečna',
            'durationDays': 30,
            'price': 50,
            'currency': 'BAM',
          },
        ]),
        '/api/gyms/gym-1/trainers' => _page(<Object>[]),
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
    expect(find.byKey(const Key('gym-image-carousel')), findsOneWidget);
    expect(find.byKey(const Key('gym-image-dots')), findsOneWidget);
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

  testWidgets('gym details show a locked map at the API coordinates', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 1200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final client = MockClient((request) async {
      final body = switch (request.url.path) {
        '/api/gyms/gym-map' => {
          'id': 'gym-map',
          'name': 'Map Gym',
          'description': 'Teretana sa lokacijom.',
          'address': 'Zmaja od Bosne 12',
          'city': 'Sarajevo',
          'latitude': 43.8563,
          'longitude': 18.4131,
          'averageRating': 4.8,
          'reviewCount': 12,
          'imageUrls': <String>[],
        },
        '/api/gyms/gym-map/membership-plans' => _page(<Object>[]),
        '/api/gyms/gym-map/trainers' => _page(<Object>[]),
        '/api/gyms/gym-map/reviews' => _page(<Object>[]),
        '/api/me/memberships' => _page(<Object>[]),
        '/api/me/membership-requests' => _page(<Object>[]),
        _ => throw StateError('Unexpected request: ${request.url}'),
      };
      return _jsonResponse(body);
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
          home: const GymDetailsScreen(gymId: 'gym-map'),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final locationMap = find.byKey(const Key('gym-details-location-map'));
    await tester.scrollUntilVisible(locationMap, 250);
    await tester.pump();

    expect(find.text('Lokacija'), findsOneWidget);
    expect(locationMap, findsOneWidget);
    expect(
      find.byKey(const Key('gym-details-location-marker')),
      findsOneWidget,
    );
    final flutterMap = tester.widget<FlutterMap>(
      find.descendant(of: locationMap, matching: find.byType(FlutterMap)),
    );
    expect(flutterMap.options.initialCenter, const LatLng(43.8563, 18.4131));
    expect(flutterMap.options.initialZoom, 15);
    expect(flutterMap.options.interactionOptions.flags, InteractiveFlag.none);
    expect(tester.getSize(locationMap).height, 220);
    expect(tester.takeException(), isNull);
  });

  testWidgets('membership details hide membership cancellation action', (
    tester,
  ) async {
    var requestCount = 0;
    final client = MockClient((request) async {
      final membership = {
        'id': 'membership-1',
        'gymId': 'gym-1',
        'gymName': 'Test Gym',
        'planName': 'Mjesečna',
        'price': 50,
        'currency': 'BAM',
        'startsAtUtc': '2030-01-01T00:00:00Z',
        'endsAtUtc': '2030-01-31T00:00:00Z',
        'status': 1,
        'statusReason': null,
        'allowedActions': ['cancel'],
        'concurrencyToken': 'token-1',
      };
      if (request.url.path == '/api/me/memberships/membership-1') {
        requestCount++;
        return _jsonResponse(membership);
      }
      if (request.url.path == '/api/gyms/gym-1') {
        return _jsonResponse({
          'id': 'gym-1',
          'name': 'Test Gym',
          'description': 'Opis teretane',
          'address': 'Testna 1',
          'city': 'Sarajevo',
          'averageRating': 4.5,
          'reviewCount': 2,
          'imageUrls': <String>[],
          'workingHours': <Object>[],
          'equipment': <String>[],
          'trainingTypes': <String>[],
        });
      }
      if (request.url.path == '/api/gyms/gym-1/membership-plans' ||
          request.url.path == '/api/gyms/gym-1/trainers') {
        return _jsonResponse(_page(const <Object>[]));
      }
      if (request.url.path == '/api/gyms/gym-1/reviews' ||
          request.url.path == '/api/me/memberships' ||
          request.url.path == '/api/me/membership-requests') {
        return _jsonResponse(_page(const <Object>[]));
      }
      return http.Response('', 404);
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
    expect(find.text('Otkaži članstvo'), findsNothing);
    expect(find.text('Otvori teretanu'), findsOneWidget);
    expect(requestCount, 1);

    await tester.tap(find.byKey(const Key('membership-open-gym')));
    await tester.pumpAndSettle();

    expect(find.byType(GymDetailsScreen), findsOneWidget);
    expect(find.text('Test Gym'), findsWidgets);
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

class _SilentRealtime implements ChatRealtimeGateway {
  final _messages = StreamController<ChatMessageModel>.broadcast();
  final _available = StreamController<String>.broadcast();
  final _reads = StreamController<ConversationReadEvent>.broadcast();

  @override
  bool get isConnected => false;
  @override
  Stream<ChatMessageModel> get messages => _messages.stream;
  @override
  Stream<String> get conversationAvailable => _available.stream;
  @override
  Stream<ConversationReadEvent> get conversationReads => _reads.stream;
  @override
  Future<void> connect() async {}
  @override
  Future<void> join(String conversationId) async {}
  @override
  Future<void> leave(String conversationId) async {}
  @override
  Future<void> send(
    String conversationId,
    String clientMessageId,
    String text,
  ) async {}

  Future<void> dispose() async {
    await _messages.close();
    await _available.close();
    await _reads.close();
  }
}

class _RecordingNavigatorObserver extends NavigatorObserver {
  int pushCount = 0;

  @override
  void didPush(Route<dynamic> route, Route<dynamic>? previousRoute) {
    pushCount++;
    super.didPush(route, previousRoute);
  }
}
