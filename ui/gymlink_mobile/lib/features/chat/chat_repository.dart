import '../../core/api.dart';
import 'chat_models.dart';

abstract interface class ChatRepositoryGateway {
  Future<ConversationModel> open(String reservationId);
  Future<ConversationModel> get(String conversationId);
  Future<PagedData> search({int page = 1, String? search});
  Future<MessageHistoryModel> messages(
    String conversationId, {
    DateTime? beforeSentAtUtc,
    String? beforeId,
  });
  Future<ChatMessageModel> send(
    String conversationId,
    String clientMessageId,
    String text,
  );
  Future<void> markRead(String conversationId);
}

final class ChatRepository implements ChatRepositoryGateway {
  const ChatRepository(this._api);

  final ApiClient _api;

  @override
  Future<ConversationModel> open(String reservationId) async {
    final json = await _api.post(
      '/api/me/conversations',
      body: {'reservationId': reservationId},
    );
    return ConversationModel.fromJson(Map<String, dynamic>.from(json! as Map));
  }

  @override
  Future<ConversationModel> get(String conversationId) async {
    final json = await _api.get('/api/me/conversations/$conversationId');
    return ConversationModel.fromJson(Map<String, dynamic>.from(json! as Map));
  }

  @override
  Future<PagedData> search({int page = 1, String? search}) => _api.page(
    '/api/me/conversations',
    query: {'page': page, 'pageSize': 20, 'search': search},
  );

  @override
  Future<MessageHistoryModel> messages(
    String conversationId, {
    DateTime? beforeSentAtUtc,
    String? beforeId,
  }) async {
    final json = await _api.get(
      '/api/me/conversations/$conversationId/messages',
      query: {
        'take': 50,
        'beforeSentAtUtc': beforeSentAtUtc?.toIso8601String(),
        'beforeId': beforeId,
      },
    );
    return MessageHistoryModel.fromJson(
      Map<String, dynamic>.from(json! as Map),
    );
  }

  @override
  Future<ChatMessageModel> send(
    String conversationId,
    String clientMessageId,
    String text,
  ) async {
    final json = await _api.post(
      '/api/me/conversations/$conversationId/messages',
      body: {'clientMessageId': clientMessageId, 'text': text},
    );
    return ChatMessageModel.fromJson(Map<String, dynamic>.from(json! as Map));
  }

  @override
  Future<void> markRead(String conversationId) =>
      _api.post('/api/me/conversations/$conversationId/read');
}
