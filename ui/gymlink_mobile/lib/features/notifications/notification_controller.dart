import 'dart:async';

import 'package:flutter/widgets.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../chat/chat_models.dart';
import '../chat/chat_realtime.dart';

final class NotificationController extends ChangeNotifier
    with WidgetsBindingObserver {
  NotificationController(this._api, this._auth, this._realtime) {
    _messageSubscription = _realtime.messages.listen(_receiveMessage);
    _auth.addListener(_authChanged);
  }

  final ApiClient _api;
  final AuthController _auth;
  final ChatRealtimeGateway _realtime;
  late final StreamSubscription<ChatMessageModel> _messageSubscription;
  final Map<String, Set<String>> _optimisticChatMessages = {};
  Timer? _pollTimer;
  Timer? _reconcileTimer;
  String? _activeConversationId;
  int unreadCount = 0;
  bool _initialized = false;

  void initialize() {
    if (_initialized) return;
    _initialized = true;
    WidgetsBinding.instance.addObserver(this);
    unawaited(refresh());
    unawaited(_connectRealtime());
    _pollTimer = Timer.periodic(const Duration(seconds: 30), (_) => refresh());
  }

  void setActiveConversation(String? conversationId) {
    _activeConversationId = conversationId;
    if (conversationId != null) {
      conversationRead(conversationId);
    }
  }

  void conversationRead(String conversationId) {
    final removed = _optimisticChatMessages.remove(conversationId)?.length ?? 0;
    if (removed > 0) {
      unreadCount = (unreadCount - removed).clamp(0, 1 << 31).toInt();
      notifyListeners();
    }
    _scheduleReconciliation();
  }

  Future<void> refresh() async {
    if (!_auth.isAuthenticated) {
      _reset();
      return;
    }
    try {
      final data = Map<String, dynamic>.from(
        (await _api.get('/api/me/notifications/unread-count'))! as Map,
      );
      final persisted = (data['count'] as num?)?.toInt() ?? 0;
      final optimistic = _optimisticChatMessages.values.fold<int>(
        0,
        (total, values) => total + values.length,
      );
      unreadCount = persisted + optimistic;
      notifyListeners();
    } on Object {
      // Preserve the last known and optimistic count while offline.
    }
  }

  void notificationMarkedRead() {
    if (unreadCount > 0) {
      unreadCount--;
      notifyListeners();
    }
    _scheduleReconciliation();
  }

  void allNotificationsMarkedRead() {
    _optimisticChatMessages.clear();
    unreadCount = 0;
    notifyListeners();
    _scheduleReconciliation();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(refresh());
      unawaited(_connectRealtime());
    }
  }

  void _receiveMessage(ChatMessageModel message) {
    final currentUserId = _auth.session?.user['id']?.toString();
    if (currentUserId == null ||
        message.senderUserId == currentUserId ||
        message.conversationId == _activeConversationId) {
      return;
    }

    final key =
        '${message.conversationId}:${message.senderUserId}:'
        '${message.clientMessageId}';
    final messages = _optimisticChatMessages.putIfAbsent(
      message.conversationId,
      () => {},
    );
    if (!messages.add(key)) return;
    unreadCount++;
    notifyListeners();
    _scheduleReconciliation();
  }

  void _scheduleReconciliation() {
    _reconcileTimer?.cancel();
    _reconcileTimer = Timer(const Duration(seconds: 5), () async {
      _optimisticChatMessages.clear();
      await refresh();
    });
  }

  void _authChanged() {
    if (!_auth.isAuthenticated) {
      _reset();
    } else {
      unawaited(refresh());
      unawaited(_connectRealtime());
    }
  }

  Future<void> _connectRealtime() async {
    if (!_auth.isAuthenticated) return;
    try {
      await _realtime.connect();
    } on Object {
      // Periodic REST reconciliation remains authoritative while offline.
    }
  }

  void _reset() {
    _optimisticChatMessages.clear();
    _activeConversationId = null;
    if (unreadCount != 0) {
      unreadCount = 0;
      notifyListeners();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _pollTimer?.cancel();
    _reconcileTimer?.cancel();
    _auth.removeListener(_authChanged);
    unawaited(_messageSubscription.cancel());
    super.dispose();
  }
}
