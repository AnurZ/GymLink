import 'dart:async';
import 'dart:typed_data';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_mobile/core/api.dart';
import 'package:gymlink_mobile/core/auth.dart';
import 'package:gymlink_mobile/core/theme.dart';
import 'package:gymlink_mobile/features/chat/chat_controller.dart';
import 'package:gymlink_mobile/features/chat/chat_models.dart';
import 'package:gymlink_mobile/features/chat/chat_realtime.dart';
import 'package:gymlink_mobile/features/chat/chat_repository.dart';
import 'package:gymlink_mobile/features/chat/chat_screens.dart';
import 'package:gymlink_mobile/features/member/member_shell.dart';
import 'package:gymlink_mobile/features/notifications/notification_controller.dart';
import 'package:gymlink_mobile/features/reservations/reservation_refresh_controller.dart';
import 'package:gymlink_mobile/features/trainer/trainer_shell.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';

void main() {
  test('conversation events update unread state and ordering', () async {
    final older = _conversation('older', at: DateTime.utc(2026, 1));
    final newer = _conversation('newer', at: DateTime.utc(2026, 2));
    final repository = _FakeChatRepository(conversations: [newer, older]);
    final realtime = _FakeChatRealtime();
    final auth = AuthController();
    final controller = ChatController(repository, realtime, auth);
    addTearDown(controller.dispose);
    addTearDown(realtime.close);

    await controller.initializeList();
    expect(realtime.joined, containsAll([newer.id, older.id]));
    final event = _message(
      conversationId: older.id,
      clientMessageId: 'event-1',
      senderUserId: 'counterpart',
      at: DateTime.utc(2026, 3),
    );
    realtime.emit(event);
    realtime.emit(event);
    await Future<void>.delayed(Duration.zero);

    expect(controller.conversations.first.id, older.id);
    expect(controller.conversations.first.unreadCount, 1);
    expect(controller.conversations.first.lastMessageText, 'Poruka');
  });

  test('conversation availability reloads the authoritative list', () async {
    final repository = _FakeChatRepository(conversations: []);
    final realtime = _FakeChatRealtime();
    final controller = ChatController(repository, realtime, AuthController());
    addTearDown(controller.dispose);
    addTearDown(realtime.close);

    await controller.initializeList();
    expect(controller.conversations, isEmpty);

    repository.conversations.add(_conversation('available-conversation'));
    realtime.emitAvailable('available-conversation');
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);

    expect(controller.conversations.single.id, 'available-conversation');
    expect(realtime.joined, contains('available-conversation'));
  });

  test('authenticated app scope joins existing conversations', () async {
    FlutterSecureStorage.setMockInitialValues({});
    final auth = AuthController();
    final api = ApiClient(
      auth,
      httpClient: MockClient(
        (_) async => http.Response(
          jsonEncode({
            'accessToken': 'access-token',
            'refreshToken': 'refresh-token',
            'user': {
              'id': 'recipient',
              'role': 'Member',
              'displayName': 'Recipient',
            },
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        ),
      ),
      baseUrlOverride: 'http://test.local',
    );
    auth.attachApi(api);
    final conversation = _conversation('existing-conversation');
    final realtime = _FakeChatRealtime();
    final controller = ChatController(
      _FakeChatRepository(conversations: [conversation]),
      realtime,
      auth,
    );
    addTearDown(realtime.close);
    addTearDown(controller.dispose);

    await auth.login('recipient', 'Test123!');
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);

    expect(controller.conversations.single.id, conversation.id);
    expect(realtime.joined, contains(conversation.id));
  });

  test(
    'notification badge deduplicates incoming messages and ignores active chat',
    () async {
      FlutterSecureStorage.setMockInitialValues({});
      final auth = AuthController();
      final client = MockClient((request) async {
        final body = request.url.path == '/api/auth/login'
            ? {
                'accessToken': 'access-token',
                'refreshToken': 'refresh-token',
                'user': {
                  'id': 'recipient',
                  'role': 'Member',
                  'displayName': 'Recipient',
                },
              }
            : {'count': 0};
        return http.Response(
          jsonEncode(body),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      final api = ApiClient(
        auth,
        httpClient: client,
        baseUrlOverride: 'http://test.local',
      );
      auth.attachApi(api);
      await auth.login('recipient', 'Test123!');
      final realtime = _FakeChatRealtime();
      final notifications = NotificationController(api, auth, realtime);
      addTearDown(realtime.close);
      addTearDown(notifications.dispose);
      notifications.initialize();
      await Future<void>.delayed(Duration.zero);

      final first = _message(
        conversationId: 'conversation',
        clientMessageId: 'message-1',
        senderUserId: 'counterpart',
      );
      realtime.emit(first);
      realtime.emit(first);
      await Future<void>.delayed(Duration.zero);
      expect(notifications.unreadCount, 1);

      notifications.setActiveConversation('conversation');
      expect(notifications.unreadCount, 0);
      realtime.emit(
        _message(
          conversationId: 'conversation',
          clientMessageId: 'message-2',
          senderUserId: 'counterpart',
        ),
      );
      await Future<void>.delayed(Duration.zero);
      expect(notifications.unreadCount, 0);
    },
  );

  test('failed retry preserves the original client message id', () async {
    final conversation = _conversation('conversation');
    final repository = _FakeChatRepository(
      conversations: [conversation],
      failSend: true,
    );
    final realtime = _FakeChatRealtime();
    final controller = ChatController(repository, realtime, AuthController());
    addTearDown(controller.dispose);
    addTearDown(realtime.close);

    await controller.initializeList();
    await controller.openConversation(conversation);
    expect(await controller.send('  Pozdrav  '), isFalse);
    final failed = controller.messages.single;
    expect(failed.delivery, MessageDeliveryState.failed);

    repository.failSend = false;
    await controller.retry(failed);

    expect(repository.sentClientIds, [
      failed.clientMessageId,
      failed.clientMessageId,
    ]);
    expect(controller.messages, hasLength(1));
    expect(controller.messages.single.delivery, MessageDeliveryState.sent);
    expect(controller.messages.single.text, 'Pozdrav');
  });

  test(
    'list realtime subscription survives opening and closing detail',
    () async {
      final conversation = _conversation('conversation');
      final realtime = _FakeChatRealtime();
      final controller = ChatController(
        _FakeChatRepository(conversations: [conversation]),
        realtime,
        AuthController(),
      );
      addTearDown(controller.dispose);
      addTearDown(realtime.close);

      await controller.initializeList();
      await controller.openConversation(conversation);
      expect(
        realtime.joined.where((id) => id == conversation.id),
        hasLength(2),
      );

      await controller.closeConversation();
      expect(
        realtime.joined.where((id) => id == conversation.id),
        hasLength(1),
      );
    },
  );

  test(
    'history uses the stable cursor and REST remains the send fallback',
    () async {
      final conversation = _conversation('conversation');
      final newest = _message(
        conversationId: conversation.id,
        clientMessageId: 'newest',
        at: DateTime.utc(2026, 2),
      );
      final oldest = _message(
        conversationId: conversation.id,
        clientMessageId: 'oldest',
        at: DateTime.utc(2026, 1),
      );
      final repository = _FakeChatRepository(
        conversations: [conversation],
        history: [
          MessageHistoryModel(
            items: [newest],
            hasMore: true,
            nextBeforeSentAtUtc: newest.sentAtUtc,
            nextBeforeId: newest.id,
            canSend: true,
          ),
          MessageHistoryModel(
            items: [oldest],
            hasMore: false,
            nextBeforeSentAtUtc: null,
            nextBeforeId: null,
            canSend: true,
          ),
        ],
      );
      final realtime = _FakeChatRealtime();
      final controller = ChatController(repository, realtime, AuthController());
      addTearDown(controller.dispose);
      addTearDown(realtime.close);

      await controller.initializeList();
      await controller.openConversation(conversation);
      await controller.loadOlder();
      expect(repository.messageCursors.single, (newest.sentAtUtc, newest.id));
      expect(controller.messages.map((message) => message.clientMessageId), [
        'oldest',
        'newest',
      ]);

      realtime.connected = false;
      expect(await controller.send('REST fallback'), isTrue);
      expect(realtime.sentClientIds, isEmpty);
      expect(repository.sentClientIds, hasLength(1));
      expect(controller.conversations.single.lastMessageText, 'REST fallback');
    },
  );

  testWidgets('failed send preserves the draft and exposes retry state', (
    tester,
  ) async {
    final conversation = _conversation('conversation');
    final repository = _FakeChatRepository(
      conversations: [conversation],
      failSend: true,
    );
    final realtime = _FakeChatRealtime();
    final controller = ChatController(repository, realtime, AuthController());
    addTearDown(realtime.close);

    await tester.pumpWidget(
      ChangeNotifierProvider.value(
        value: controller,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: ChatDetailScreen(conversation: conversation),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('chat-draft')), 'Sačuvaj me');
    await tester.tap(find.byKey(const Key('chat-send')));
    await tester.pumpAndSettle();

    expect(
      tester
          .widget<TextField>(find.byKey(const Key('chat-draft')))
          .controller!
          .text,
      'Sačuvaj me',
    );
    expect(find.byIcon(Icons.error_outline), findsOneWidget);
    expect(find.text('Mrežna greška'), findsOneWidget);

    repository.failSend = false;
    final failedMessage = controller.messages.single;
    await tester.tap(
      find.byKey(Key('message-${failedMessage.clientMessageId}')),
    );
    await tester.pumpAndSettle();
    expect(find.byIcon(Icons.done_all), findsOneWidget);
  });

  testWidgets('stored participant can send when compatibility flag is false', (
    tester,
  ) async {
    final conversation = _conversation('conversation');
    final repository = _FakeChatRepository(
      conversations: [conversation],
      history: [
        const MessageHistoryModel(
          items: [],
          hasMore: false,
          nextBeforeSentAtUtc: null,
          nextBeforeId: null,
          canSend: false,
        ),
      ],
    );
    final realtime = _FakeChatRealtime();
    final controller = ChatController(repository, realtime, AuthController());
    addTearDown(realtime.close);

    await tester.pumpWidget(
      ChangeNotifierProvider.value(
        value: controller,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: ChatDetailScreen(conversation: conversation),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('chat-read-only')), findsNothing);
    expect(find.byKey(const Key('chat-send')), findsOneWidget);
    await tester.enterText(find.byKey(const Key('chat-draft')), 'Pozdrav');
    await tester.tap(find.byKey(const Key('chat-send')));
    await tester.pumpAndSettle();
    expect(repository.sentClientIds, hasLength(1));
  });

  testWidgets('paperclip picks and renders a standalone gallery image', (
    tester,
  ) async {
    final conversation = _conversation('conversation');
    final repository = _FakeChatRepository(conversations: [conversation]);
    final realtime = _FakeChatRealtime();
    final controller = ChatController(repository, realtime, AuthController());
    addTearDown(realtime.close);

    await tester.pumpWidget(
      ChangeNotifierProvider.value(
        value: controller,
        child: MaterialApp(
          theme: buildGymLinkTheme(),
          home: ChatDetailScreen(
            conversation: conversation,
            pickImage: () async => XFile.fromData(
              _pngBytes(),
              name: 'chat.png',
              mimeType: 'image/png',
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('chat-image-picker')));
    await tester.pumpAndSettle();

    expect(repository.sentImageCount, 1);
    expect(find.byKey(const Key('chat-image-picker')), findsOneWidget);
    expect(find.byType(Image), findsOneWidget);
    expect(find.text('Slika'), findsNothing);
  });

  testWidgets('Member and Trainer shells expose the conversation feature', (
    tester,
  ) async {
    final memberRealtime = await _pumpShell(
      tester,
      const MemberShell(),
      withConversation: true,
    );
    memberRealtime.emit(
      _message(
        conversationId: 'shell-conversation',
        clientMessageId: 'member-event',
        senderUserId: 'trainer',
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<Badge>(find.byKey(const Key('member-chat-unread')))
          .isLabelVisible,
      isTrue,
    );
    await tester.tap(find.text('Razgovori'));
    await tester.pumpAndSettle();
    expect(find.byType(ConversationListScreen), findsOneWidget);
    expect(find.byKey(const Key('unread-shell-conversation')), findsOneWidget);

    final trainerRealtime = await _pumpShell(
      tester,
      const TrainerShell(),
      withConversation: true,
    );
    trainerRealtime.emit(
      _message(
        conversationId: 'shell-conversation',
        clientMessageId: 'trainer-event',
        senderUserId: 'member',
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<Badge>(find.byKey(const Key('trainer-chat-unread')))
          .isLabelVisible,
      isTrue,
    );
    await tester.tap(find.text('Razgovori'));
    await tester.pumpAndSettle();
    expect(find.byType(ConversationListScreen), findsOneWidget);
    expect(find.byKey(const Key('unread-shell-conversation')), findsOneWidget);
  });
}

Future<_FakeChatRealtime> _pumpShell(
  WidgetTester tester,
  Widget shell, {
  bool withConversation = false,
}) async {
  final shellConversation = _conversation('shell-conversation');
  final client = MockClient((request) async {
    final Object body = switch (request.url.path) {
      '/api/tenant/trainer-availability/schedule' => {
        'concurrencyToken': 'token',
        'shifts': <Object>[],
      },
      '/api/profile' => {
        'id': 'user',
        'displayName': 'Test User',
        'email': 'test@gymlink.ba',
        'phoneNumber': null,
        'trainerProfileId': 'trainer',
      },
      '/api/me/notifications/unread-count' => {'count': 0},
      '/api/me/conversations' => {
        'items': withConversation
            ? <Object>[_conversationJson(shellConversation)]
            : <Object>[],
        'page': 1,
        'pageSize': 20,
        'totalCount': withConversation ? 1 : 0,
      },
      _ => {'items': <Object>[], 'page': 1, 'pageSize': 20, 'totalCount': 0},
    };
    return http.Response(
      jsonEncode(body),
      200,
      headers: {'content-type': 'application/json; charset=utf-8'},
    );
  });
  final auth = AuthController();
  final api = ApiClient(
    auth,
    httpClient: client,
    baseUrlOverride: 'http://test.local',
  );
  final realtime = _FakeChatRealtime();
  addTearDown(realtime.close);
  final notifications = NotificationController(api, auth, realtime);
  final reservations = ReservationRefreshController();
  final chat = ChatController(
    ChatRepository(api),
    realtime,
    auth,
    notifications,
  );
  addTearDown(chat.dispose);
  addTearDown(notifications.dispose);
  addTearDown(reservations.dispose);
  auth.attachApi(api);
  await tester.pumpWidget(
    MultiProvider(
      providers: [
        ChangeNotifierProvider.value(value: auth),
        ChangeNotifierProvider.value(value: notifications),
        ChangeNotifierProvider.value(value: chat),
        ChangeNotifierProvider.value(value: reservations),
        Provider.value(value: api),
        Provider<ChatRealtimeGateway>.value(value: realtime),
      ],
      child: MaterialApp(theme: buildGymLinkTheme(), home: shell),
    ),
  );
  await tester.pumpAndSettle();
  return realtime;
}

ConversationModel _conversation(String id, {DateTime? at}) => ConversationModel(
  id: id,
  originatingReservationId: 'reservation-$id',
  counterpartUserId: 'counterpart-$id',
  counterpartDisplayName: 'Osoba $id',
  counterpartRole: 'Trainer',
  gymId: 'gym',
  gymName: 'GymLink Gym',
  lastMessageText: null,
  lastMessageAtUtc: at,
  unreadCount: 0,
  canSend: true,
  createdAtUtc: at ?? DateTime.utc(2026),
  closedAtUtc: null,
);

ChatMessageModel _message({
  required String conversationId,
  required String clientMessageId,
  DateTime? at,
  String senderUserId = 'sender',
}) => ChatMessageModel(
  id: 'message-$clientMessageId',
  conversationId: conversationId,
  senderUserId: senderUserId,
  clientMessageId: clientMessageId,
  text: 'Poruka',
  sentAtUtc: at ?? DateTime.utc(2026),
);

Map<String, dynamic> _conversationJson(ConversationModel value) => {
  'id': value.id,
  'originatingReservationId': value.originatingReservationId,
  'counterpartUserId': value.counterpartUserId,
  'counterpartDisplayName': value.counterpartDisplayName,
  'counterpartRole': value.counterpartRole,
  'gymId': value.gymId,
  'gymName': value.gymName,
  'lastMessageText': value.lastMessageText,
  'lastMessageAtUtc': value.lastMessageAtUtc?.toIso8601String(),
  'unreadCount': value.unreadCount,
  'canSend': value.canSend,
  'createdAtUtc': value.createdAtUtc.toIso8601String(),
  'closedAtUtc': value.closedAtUtc?.toIso8601String(),
};

final class _FakeChatRepository implements ChatRepositoryGateway {
  _FakeChatRepository({
    required this.conversations,
    this.history = const [],
    this.failSend = false,
  });

  final List<ConversationModel> conversations;
  final List<MessageHistoryModel> history;
  bool failSend;
  int _historyIndex = 0;
  final List<String> sentClientIds = [];
  int sentImageCount = 0;
  final List<(DateTime?, String?)> messageCursors = [];

  @override
  Future<ConversationModel> open(String reservationId) async =>
      conversations.first;

  @override
  Future<ConversationModel> get(String conversationId) async =>
      conversations.firstWhere((item) => item.id == conversationId);

  @override
  Future<PagedData> search({int page = 1, String? search}) async => PagedData(
    items: conversations.map(_conversationJson).toList(),
    page: page,
    pageSize: 20,
    totalCount: conversations.length,
  );

  @override
  Future<MessageHistoryModel> messages(
    String conversationId, {
    DateTime? beforeSentAtUtc,
    String? beforeId,
  }) async {
    if (beforeId != null) {
      messageCursors.add((beforeSentAtUtc, beforeId));
    }
    if (history.isEmpty) {
      return const MessageHistoryModel(
        items: [],
        hasMore: false,
        nextBeforeSentAtUtc: null,
        nextBeforeId: null,
        canSend: true,
      );
    }
    return history[_historyIndex++];
  }

  @override
  Future<ChatMessageModel> send(
    String conversationId,
    String clientMessageId,
    String text,
  ) async {
    sentClientIds.add(clientMessageId);
    if (failSend) {
      throw ApiProblem(
        status: 0,
        code: 'network_error',
        message: 'Mrežna greška',
      );
    }
    return ChatMessageModel(
      id: 'saved-$clientMessageId',
      conversationId: conversationId,
      senderUserId: '',
      clientMessageId: clientMessageId,
      text: text,
      sentAtUtc: DateTime.utc(2026, 4),
    );
  }

  @override
  Future<void> markRead(String conversationId) async {}

  @override
  Future<ChatMessageModel> sendImage(
    String conversationId,
    String clientMessageId,
    List<int> bytes,
    String fileName,
    String contentType,
  ) async {
    sentImageCount++;
    return ChatMessageModel(
      id: 'saved-$clientMessageId',
      conversationId: conversationId,
      senderUserId: '',
      clientMessageId: clientMessageId,
      text: 'Slika',
      imageUrl: '/image/$clientMessageId',
      sentAtUtc: DateTime.utc(2026, 4),
    );
  }

  @override
  Future<Uint8List> imageBytes(String imageUrl) async => _pngBytes();
}

Uint8List _pngBytes() => Uint8List.fromList(
  base64Decode(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  ),
);

final class _FakeChatRealtime implements ChatRealtimeGateway {
  final _messages = StreamController<ChatMessageModel>.broadcast();
  final _available = StreamController<String>.broadcast();
  final _reads = StreamController<ConversationReadEvent>.broadcast();
  bool connected = false;
  final List<String> joined = [];
  final List<String> sentClientIds = [];

  @override
  Stream<ChatMessageModel> get messages => _messages.stream;

  @override
  Stream<String> get conversationAvailable => _available.stream;

  @override
  Stream<ConversationReadEvent> get conversationReads => _reads.stream;

  @override
  bool get isConnected => connected;

  @override
  Future<void> connect() async {
    connected = true;
  }

  @override
  Future<void> join(String conversationId) async {
    connected = true;
    joined.add(conversationId);
  }

  @override
  Future<void> leave(String conversationId) async {
    joined.remove(conversationId);
  }

  @override
  Future<void> send(
    String conversationId,
    String clientMessageId,
    String text,
  ) async {
    sentClientIds.add(clientMessageId);
  }

  void emit(ChatMessageModel message) => _messages.add(message);

  void emitAvailable(String conversationId) => _available.add(conversationId);

  Future<void> close() async {
    await _messages.close();
    await _available.close();
    await _reads.close();
  }
}
