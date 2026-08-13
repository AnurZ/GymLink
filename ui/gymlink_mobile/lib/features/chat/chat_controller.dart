import 'dart:async';
import 'dart:math';

import 'package:flutter/foundation.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../notifications/notification_controller.dart';
import 'chat_models.dart';
import 'chat_realtime.dart';
import 'chat_repository.dart';

final class ChatController extends ChangeNotifier {
  ChatController(
    this._repository,
    this._realtime,
    this._auth, [
    this._notifications,
  ]) {
    _messageSubscription = _realtime.messages.listen(_receive);
    _availableSubscription = _realtime.conversationAvailable.listen(
      _conversationAvailable,
    );
    _readSubscription = _realtime.conversationReads.listen(_conversationRead);
    _sessionUserId = currentUserId;
    _auth.addListener(_authChanged);
  }

  final ChatRepositoryGateway _repository;
  final ChatRealtimeGateway _realtime;
  final AuthController _auth;
  final NotificationController? _notifications;
  late final StreamSubscription<ChatMessageModel> _messageSubscription;
  late final StreamSubscription<String> _availableSubscription;
  late final StreamSubscription<ConversationReadEvent> _readSubscription;
  final List<ConversationModel> conversations = [];
  final List<ChatMessageModel> messages = [];
  final Set<String> _seenMessageKeys = {};
  final Set<String> _listJoinedConversationIds = {};
  final Map<String, Future<Uint8List?>> _imageLoads = {};
  String? _detailJoinedConversationId;
  ConversationModel? activeConversation;
  bool listLoading = false;
  bool detailLoading = false;
  bool sending = false;
  bool sendingImage = false;
  bool hasMoreConversations = false;
  bool hasMoreMessages = false;
  int _conversationPage = 1;
  DateTime? _beforeSentAtUtc;
  String? _beforeId;
  String? listError;
  String? detailError;
  String? imageUploadError;
  String _sessionUserId = '';

  String get currentUserId => _auth.session?.user['id']?.toString() ?? '';
  int get unreadCount => conversations.fold(
    0,
    (total, conversation) => total + conversation.unreadCount,
  );

  Future<void> initializeList() async {
    try {
      await _realtime.connect();
    } on Object {
      // REST remains available when realtime cannot connect.
    }
    await loadConversations();
  }

  Future<void> resume() async {
    try {
      await _realtime.connect();
    } on Object {
      // REST reconciliation below remains available while the hub is offline.
    }
    if (activeConversation == null) {
      await loadConversations();
    } else {
      final conversation = activeConversation!;
      await openConversation(conversation);
    }
  }

  Future<void> loadConversations({String? search}) async {
    listLoading = true;
    listError = null;
    notifyListeners();
    try {
      final page = await _repository.search(search: search);
      conversations
        ..clear()
        ..addAll(page.items.map(ConversationModel.fromJson));
      _conversationPage = page.page;
      hasMoreConversations = page.hasMore;
      await _joinListConversations(conversations);
    } on ApiProblem catch (error) {
      listError = error.message;
    } finally {
      listLoading = false;
      notifyListeners();
    }
  }

  Future<void> loadMoreConversations({String? search}) async {
    if (listLoading || !hasMoreConversations) return;
    listLoading = true;
    listError = null;
    notifyListeners();
    try {
      final page = await _repository.search(
        page: _conversationPage + 1,
        search: search,
      );
      final knownIds = conversations.map((item) => item.id).toSet();
      conversations.addAll(
        page.items
            .map(ConversationModel.fromJson)
            .where((item) => knownIds.add(item.id)),
      );
      _conversationPage = page.page;
      hasMoreConversations = page.hasMore;
      await _joinListConversations(conversations);
    } on ApiProblem catch (error) {
      listError = error.message;
    } finally {
      listLoading = false;
      notifyListeners();
    }
  }

  Future<void> openConversation(ConversationModel conversation) async {
    activeConversation = conversation;
    _notifications?.setActiveConversation(conversation.id);
    messages.clear();
    _imageLoads.clear();
    detailError = null;
    detailLoading = true;
    notifyListeners();
    try {
      final history = await _repository.messages(conversation.id);
      messages.addAll(history.items);
      _seenMessageKeys.addAll(history.items.map(_messageKey));
      hasMoreMessages = history.hasMore;
      _beforeSentAtUtc = history.nextBeforeSentAtUtc;
      _beforeId = history.nextBeforeId;
      activeConversation = _withCanSend(conversation, history.canSend);
      await _joinDetailConversation(conversation.id);
      await _repository.markRead(conversation.id);
      _markConversationRead(conversation.id);
      _notifications?.conversationRead(conversation.id);
    } on ApiProblem catch (error) {
      detailError = error.message;
    } finally {
      detailLoading = false;
      notifyListeners();
    }
  }

  Future<void> closeConversation() async {
    final id = _detailJoinedConversationId;
    if (id != null) {
      try {
        await _realtime.leave(id);
      } on Object {
        // The server will discard the connection group on disconnect.
      }
    }
    _detailJoinedConversationId = null;
    activeConversation = null;
    _notifications?.setActiveConversation(null);
  }

  Future<void> _joinDetailConversation(String conversationId) async {
    if (_detailJoinedConversationId == conversationId) return;
    final previous = _detailJoinedConversationId;
    if (previous != null) {
      await _realtime.leave(previous);
    }
    try {
      await _realtime.join(conversationId);
      _detailJoinedConversationId = conversationId;
    } on Object {
      // The REST send path is the required fallback.
    }
  }

  Future<void> _joinListConversations(
    Iterable<ConversationModel> values,
  ) async {
    for (final conversation in values) {
      if (!_listJoinedConversationIds.add(conversation.id)) continue;
      try {
        await _realtime.join(conversation.id);
      } on Object {
        _listJoinedConversationIds.remove(conversation.id);
      }
    }
  }

  Future<void> _releaseRealtimeSubscriptions() async {
    await closeConversation();
    for (final id in _listJoinedConversationIds.toList(growable: false)) {
      try {
        await _realtime.leave(id);
      } on Object {
        // A disconnected hub has already discarded its server subscriptions.
      }
    }
    _listJoinedConversationIds.clear();
  }

  Future<void> loadOlder() async {
    final conversation = activeConversation;
    if (conversation == null || !hasMoreMessages || detailLoading) return;
    detailLoading = true;
    notifyListeners();
    try {
      final history = await _repository.messages(
        conversation.id,
        beforeSentAtUtc: _beforeSentAtUtc,
        beforeId: _beforeId,
      );
      messages.insertAll(0, history.items);
      _seenMessageKeys.addAll(history.items.map(_messageKey));
      hasMoreMessages = history.hasMore;
      _beforeSentAtUtc = history.nextBeforeSentAtUtc;
      _beforeId = history.nextBeforeId;
      activeConversation = _withCanSend(conversation, history.canSend);
    } on ApiProblem catch (error) {
      detailError = error.message;
    } finally {
      detailLoading = false;
      notifyListeners();
    }
  }

  Future<bool> send(String rawText) async {
    final conversation = activeConversation;
    final text = rawText.trim();
    if (conversation == null || text.isEmpty) {
      return false;
    }
    return _sendWithId(conversation, _newGuid(), text);
  }

  Future<bool> sendImage(
    List<int> bytes,
    String fileName,
    String contentType,
  ) async {
    final conversation = activeConversation;
    if (conversation == null || bytes.isEmpty || sendingImage) return false;
    sendingImage = true;
    imageUploadError = null;
    notifyListeners();
    try {
      final saved = await _repository.sendImage(
        conversation.id,
        _newGuid(),
        bytes,
        fileName,
        contentType,
      );
      _acceptSaved(saved);
      return true;
    } on ApiProblem catch (error) {
      imageUploadError = error.code == 'invalid_chat_image'
          ? 'Odabranu sliku nije moguće poslati. Odaberite JPG, PNG ili WebP fotografiju do 5 MB.'
          : error.message;
      return false;
    } finally {
      sendingImage = false;
      notifyListeners();
    }
  }

  Future<Uint8List?> imageFor(ChatMessageModel message) {
    final imageUrl = message.imageUrl;
    if (imageUrl == null) return Future.value();
    return _imageLoads.putIfAbsent(message.id, () async {
      try {
        return await _repository.imageBytes(imageUrl);
      } on ApiProblem catch (error) {
        detailError = error.message;
        notifyListeners();
        return null;
      }
    });
  }

  Future<bool> _sendWithId(
    ConversationModel conversation,
    String clientMessageId,
    String text,
  ) async {
    final pending = ChatMessageModel(
      id: 'local-$clientMessageId',
      conversationId: conversation.id,
      senderUserId: currentUserId,
      clientMessageId: clientMessageId,
      text: text,
      sentAtUtc: DateTime.now().toUtc(),
      delivery: MessageDeliveryState.pending,
    );
    _seenMessageKeys.add(_messageKey(pending));
    _replace(pending);
    sending = true;
    detailError = null;
    notifyListeners();
    try {
      if (_realtime.isConnected) {
        await _realtime.send(conversation.id, clientMessageId, text);
      }
      final saved = await _repository.send(
        conversation.id,
        clientMessageId,
        text,
      );
      _acceptSaved(saved);
      return true;
    } on ApiProblem catch (error) {
      detailError = error.message;
      _replace(pending.withDelivery(MessageDeliveryState.failed));
      return false;
    } on Object {
      try {
        final saved = await _repository.send(
          conversation.id,
          clientMessageId,
          text,
        );
        _acceptSaved(saved);
        return true;
      } on ApiProblem catch (error) {
        detailError = error.message;
        _replace(pending.withDelivery(MessageDeliveryState.failed));
        return false;
      }
    } finally {
      sending = false;
      notifyListeners();
    }
  }

  Future<void> retry(ChatMessageModel message) async {
    if (message.delivery != MessageDeliveryState.failed) return;
    final conversation = activeConversation;
    if (conversation == null) return;
    await _sendWithId(conversation, message.clientMessageId, message.text);
  }

  void _receive(ChatMessageModel message) {
    final isNew = _seenMessageKeys.add(_messageKey(message));
    final active = activeConversation;
    if (active?.id == message.conversationId) {
      _replace(message);
      if (message.senderUserId != currentUserId) {
        unawaited(_repository.markRead(message.conversationId));
      }
    }
    final index = conversations.indexWhere(
      (item) => item.id == message.conversationId,
    );
    if (index >= 0) {
      conversations[index] = conversations[index].withMessage(
        message,
        unread:
            isNew &&
            active?.id != message.conversationId &&
            message.senderUserId != currentUserId,
      );
      conversations.sort(_conversationOrder);
    } else {
      unawaited(loadConversations());
    }
    notifyListeners();
  }

  void _conversationAvailable(String conversationId) {
    if (conversations.any((item) => item.id == conversationId) || listLoading) {
      return;
    }
    unawaited(loadConversations());
  }

  void _conversationRead(ConversationReadEvent event) {
    if (event.readerUserId != currentUserId) return;
    _markConversationRead(event.conversationId);
    _notifications?.conversationRead(event.conversationId);
    notifyListeners();
  }

  void _acceptSaved(ChatMessageModel message) {
    _seenMessageKeys.add(_messageKey(message));
    _replace(message);
    _recordConversationMessage(message, unread: false);
  }

  void _recordConversationMessage(
    ChatMessageModel message, {
    required bool unread,
  }) {
    final index = conversations.indexWhere(
      (item) => item.id == message.conversationId,
    );
    if (index < 0) return;
    conversations[index] = conversations[index].withMessage(
      message,
      unread: unread,
    );
    conversations.sort(_conversationOrder);
  }

  void _replace(ChatMessageModel message) {
    final index = messages.indexWhere(
      (item) => item.clientMessageId == message.clientMessageId,
    );
    if (index >= 0) {
      messages[index] = message;
    } else {
      messages.add(message);
    }
    messages.sort((a, b) => a.sentAtUtc.compareTo(b.sentAtUtc));
    notifyListeners();
  }

  void _markConversationRead(String id) {
    final index = conversations.indexWhere((item) => item.id == id);
    if (index >= 0) conversations[index] = conversations[index].markRead();
  }

  static int _conversationOrder(
    ConversationModel first,
    ConversationModel second,
  ) => (second.lastMessageAtUtc ?? second.createdAtUtc).compareTo(
    first.lastMessageAtUtc ?? first.createdAtUtc,
  );

  static ConversationModel _withCanSend(
    ConversationModel value,
    bool canSend,
  ) => ConversationModel(
    id: value.id,
    originatingReservationId: value.originatingReservationId,
    counterpartUserId: value.counterpartUserId,
    counterpartDisplayName: value.counterpartDisplayName,
    counterpartRole: value.counterpartRole,
    counterpartImageUrl: value.counterpartImageUrl,
    gymId: value.gymId,
    gymName: value.gymName,
    lastMessageText: value.lastMessageText,
    lastMessageAtUtc: value.lastMessageAtUtc,
    unreadCount: value.unreadCount,
    canSend: canSend,
    createdAtUtc: value.createdAtUtc,
    closedAtUtc: value.closedAtUtc,
  );

  static String _messageKey(ChatMessageModel message) =>
      '${message.conversationId}:${message.senderUserId}:'
      '${message.clientMessageId}';

  static String _newGuid() {
    final random = Random.secure();
    final bytes = List<int>.generate(16, (_) => random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    final hex = bytes
        .map((byte) => byte.toRadixString(16).padLeft(2, '0'))
        .join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
        '${hex.substring(12, 16)}-${hex.substring(16, 20)}-'
        '${hex.substring(20)}';
  }

  void _authChanged() {
    final nextUserId = currentUserId;
    if (nextUserId == _sessionUserId) return;
    _sessionUserId = nextUserId;
    unawaited(_resetForSession());
  }

  Future<void> _resetForSession() async {
    await _releaseRealtimeSubscriptions();
    conversations.clear();
    messages.clear();
    _imageLoads.clear();
    _seenMessageKeys.clear();
    activeConversation = null;
    listError = null;
    detailError = null;
    notifyListeners();
    if (_sessionUserId.isNotEmpty) {
      await initializeList();
    }
  }

  @override
  void dispose() {
    _auth.removeListener(_authChanged);
    unawaited(_messageSubscription.cancel());
    unawaited(_availableSubscription.cancel());
    unawaited(_readSubscription.cancel());
    unawaited(_releaseRealtimeSubscriptions());
    super.dispose();
  }
}
