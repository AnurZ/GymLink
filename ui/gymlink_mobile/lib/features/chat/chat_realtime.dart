import 'dart:async';

import 'package:signalr_netcore/signalr_client.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import 'chat_models.dart';

abstract interface class ChatRealtimeGateway {
  Stream<ChatMessageModel> get messages;
  Stream<String> get conversationAvailable;
  Stream<ConversationReadEvent> get conversationReads;
  bool get isConnected;
  Future<void> connect();
  Future<void> join(String conversationId);
  Future<void> leave(String conversationId);
  Future<void> send(String conversationId, String clientMessageId, String text);
}

final class ChatRealtime
    implements AuthenticatedConnection, ChatRealtimeGateway {
  ChatRealtime(this._tokens);

  final AuthTokenSource _tokens;
  final _messages = StreamController<ChatMessageModel>.broadcast();
  final _conversationAvailable = StreamController<String>.broadcast();
  final _conversationReads =
      StreamController<ConversationReadEvent>.broadcast();
  final Map<String, int> _joinedConversations = {};
  HubConnection? _connection;
  Future<void>? _connectFuture;
  MethodInvocationFunc? _messageHandler;
  MethodInvocationFunc? _conversationAvailableHandler;
  MethodInvocationFunc? _conversationReadHandler;

  @override
  Stream<ChatMessageModel> get messages => _messages.stream;

  @override
  Stream<String> get conversationAvailable => _conversationAvailable.stream;

  @override
  Stream<ConversationReadEvent> get conversationReads =>
      _conversationReads.stream;

  @override
  bool get isConnected => _connection?.state == HubConnectionState.Connected;

  @override
  Future<void> connect() {
    if (_tokens.accessToken == null || isConnected) {
      return Future<void>.value();
    }
    return _connectFuture ??= _startConnection().whenComplete(() {
      _connectFuture = null;
    });
  }

  Future<void> _startConnection() async {
    if (ApiClient.baseUrl.isEmpty) {
      throw StateError('API_BASE_URL nije postavljen.');
    }
    final base = ApiClient.baseUrl.replaceFirst(RegExp(r'/$'), '');
    final connection = HubConnectionBuilder()
        .withUrl(
          '$base/hubs/chat',
          options: HttpConnectionOptions(
            accessTokenFactory: () async => _tokens.accessToken ?? '',
          ),
        )
        .withAutomaticReconnect()
        .build();
    connection.onreconnected(({String? connectionId}) => _rejoin());
    _connection = connection;
    _registerHandler();
    await connection.start();
    await _rejoin();
  }

  @override
  Future<void> join(String conversationId) async {
    final joinCount = _joinedConversations[conversationId] ?? 0;
    _joinedConversations[conversationId] = joinCount + 1;
    if (joinCount > 0) return;
    try {
      if (!isConnected) await connect();
      await _connection?.invoke('conversation:join', args: [conversationId]);
    } on Object {
      _joinedConversations.remove(conversationId);
      rethrow;
    }
  }

  @override
  Future<void> leave(String conversationId) async {
    final joinCount = _joinedConversations[conversationId] ?? 0;
    if (joinCount > 1) {
      _joinedConversations[conversationId] = joinCount - 1;
      return;
    }
    _joinedConversations.remove(conversationId);
    if (isConnected) {
      await _connection?.invoke('conversation:leave', args: [conversationId]);
    }
  }

  @override
  Future<void> send(
    String conversationId,
    String clientMessageId,
    String text,
  ) async {
    if (!isConnected) await connect();
    await _connection?.invoke(
      'message:send',
      args: [conversationId, clientMessageId, text],
    );
  }

  void _registerHandler() {
    final connection = _connection;
    if (connection == null) return;
    if (_messageHandler != null) {
      connection.off('message:new', method: _messageHandler);
    }
    _messageHandler = (arguments) {
      if (arguments == null || arguments.isEmpty || arguments.first is! Map) {
        return;
      }
      final payload = Map<String, dynamic>.from(arguments.first! as Map);
      final rawMessage = payload['message'];
      if (rawMessage is Map) {
        _messages.add(
          ChatMessageModel.fromJson(Map<String, dynamic>.from(rawMessage)),
        );
      }
    };
    connection.on('message:new', _messageHandler!);

    if (_conversationAvailableHandler != null) {
      connection.off(
        'conversation:available',
        method: _conversationAvailableHandler,
      );
    }
    _conversationAvailableHandler = (arguments) {
      if (arguments == null || arguments.isEmpty || arguments.first is! Map) {
        return;
      }
      final payload = Map<String, dynamic>.from(arguments.first! as Map);
      final conversationId = payload['conversationId']?.toString();
      if (conversationId != null && conversationId.isNotEmpty) {
        _conversationAvailable.add(conversationId);
      }
    };
    connection.on('conversation:available', _conversationAvailableHandler!);

    if (_conversationReadHandler != null) {
      connection.off('conversation:read', method: _conversationReadHandler);
    }
    _conversationReadHandler = (arguments) {
      if (arguments == null || arguments.isEmpty || arguments.first is! Map) {
        return;
      }
      final payload = Map<String, dynamic>.from(arguments.first! as Map);
      final conversationId = payload['conversationId']?.toString();
      final readerUserId = payload['readerUserId']?.toString();
      final readAtUtc = DateTime.tryParse(
        payload['readAtUtc']?.toString() ?? '',
      );
      if (conversationId != null &&
          conversationId.isNotEmpty &&
          readerUserId != null &&
          readerUserId.isNotEmpty &&
          readAtUtc != null) {
        _conversationReads.add(
          ConversationReadEvent(
            conversationId: conversationId,
            readerUserId: readerUserId,
            readAtUtc: readAtUtc.toUtc(),
          ),
        );
      }
    };
    connection.on('conversation:read', _conversationReadHandler!);
  }

  Future<void> _rejoin() async {
    for (final id in _joinedConversations.keys) {
      await _connection?.invoke('conversation:join', args: [id]);
    }
  }

  @override
  Future<void> disconnect() async {
    final connecting = _connectFuture;
    if (connecting != null) {
      try {
        await connecting;
      } on Object {
        // A failed connection has nothing to stop.
      }
    }
    _joinedConversations.clear();
    final connection = _connection;
    _connection = null;
    if (connection != null) await connection.stop();
  }
}
