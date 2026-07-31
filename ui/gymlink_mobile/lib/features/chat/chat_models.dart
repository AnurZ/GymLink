enum MessageDeliveryState { pending, sent, failed }

final class ConversationReadEvent {
  const ConversationReadEvent({
    required this.conversationId,
    required this.readerUserId,
    required this.readAtUtc,
  });

  final String conversationId;
  final String readerUserId;
  final DateTime readAtUtc;
}

final class ConversationModel {
  const ConversationModel({
    required this.id,
    required this.originatingReservationId,
    required this.counterpartUserId,
    required this.counterpartDisplayName,
    required this.counterpartRole,
    required this.gymId,
    required this.gymName,
    required this.lastMessageText,
    required this.lastMessageAtUtc,
    required this.unreadCount,
    required this.canSend,
    required this.createdAtUtc,
    required this.closedAtUtc,
  });

  final String id;
  final String? originatingReservationId;
  final String counterpartUserId;
  final String counterpartDisplayName;
  final String counterpartRole;
  final String gymId;
  final String gymName;
  final String? lastMessageText;
  final DateTime? lastMessageAtUtc;
  final int unreadCount;
  final bool canSend;
  final DateTime createdAtUtc;
  final DateTime? closedAtUtc;

  factory ConversationModel.fromJson(Map<String, dynamic> json) =>
      ConversationModel(
        id: json['id'].toString(),
        originatingReservationId: json['originatingReservationId']?.toString(),
        counterpartUserId: json['counterpartUserId'].toString(),
        counterpartDisplayName: json['counterpartDisplayName'].toString(),
        counterpartRole: json['counterpartRole'].toString(),
        gymId: json['gymId'].toString(),
        gymName: json['gymName'].toString(),
        lastMessageText: json['lastMessageText']?.toString(),
        lastMessageAtUtc: _date(json['lastMessageAtUtc']),
        unreadCount: (json['unreadCount'] as num?)?.toInt() ?? 0,
        canSend: json['canSend'] == true,
        createdAtUtc: DateTime.parse(json['createdAtUtc'].toString()).toUtc(),
        closedAtUtc: _date(json['closedAtUtc']),
      );

  ConversationModel withMessage(
    ChatMessageModel message, {
    required bool unread,
  }) => ConversationModel(
    id: id,
    originatingReservationId: originatingReservationId,
    counterpartUserId: counterpartUserId,
    counterpartDisplayName: counterpartDisplayName,
    counterpartRole: counterpartRole,
    gymId: gymId,
    gymName: gymName,
    lastMessageText: message.text,
    lastMessageAtUtc: message.sentAtUtc,
    unreadCount: unread ? unreadCount + 1 : unreadCount,
    canSend: canSend,
    createdAtUtc: createdAtUtc,
    closedAtUtc: closedAtUtc,
  );

  ConversationModel markRead() => ConversationModel(
    id: id,
    originatingReservationId: originatingReservationId,
    counterpartUserId: counterpartUserId,
    counterpartDisplayName: counterpartDisplayName,
    counterpartRole: counterpartRole,
    gymId: gymId,
    gymName: gymName,
    lastMessageText: lastMessageText,
    lastMessageAtUtc: lastMessageAtUtc,
    unreadCount: 0,
    canSend: canSend,
    createdAtUtc: createdAtUtc,
    closedAtUtc: closedAtUtc,
  );
}

final class ChatMessageModel {
  const ChatMessageModel({
    required this.id,
    required this.conversationId,
    required this.senderUserId,
    required this.clientMessageId,
    required this.text,
    required this.sentAtUtc,
    this.delivery = MessageDeliveryState.sent,
  });

  final String id;
  final String conversationId;
  final String senderUserId;
  final String clientMessageId;
  final String text;
  final DateTime sentAtUtc;
  final MessageDeliveryState delivery;

  factory ChatMessageModel.fromJson(Map<String, dynamic> json) =>
      ChatMessageModel(
        id: json['id'].toString(),
        conversationId: json['conversationId'].toString(),
        senderUserId: json['senderUserId'].toString(),
        clientMessageId: json['clientMessageId'].toString(),
        text: json['text'].toString(),
        sentAtUtc: DateTime.parse(json['sentAtUtc'].toString()).toUtc(),
      );

  ChatMessageModel withDelivery(MessageDeliveryState value) => ChatMessageModel(
    id: id,
    conversationId: conversationId,
    senderUserId: senderUserId,
    clientMessageId: clientMessageId,
    text: text,
    sentAtUtc: sentAtUtc,
    delivery: value,
  );
}

final class MessageHistoryModel {
  const MessageHistoryModel({
    required this.items,
    required this.hasMore,
    required this.nextBeforeSentAtUtc,
    required this.nextBeforeId,
    required this.canSend,
  });

  final List<ChatMessageModel> items;
  final bool hasMore;
  final DateTime? nextBeforeSentAtUtc;
  final String? nextBeforeId;
  final bool canSend;

  factory MessageHistoryModel.fromJson(Map<String, dynamic> json) =>
      MessageHistoryModel(
        items: (json['items'] as List? ?? const [])
            .whereType<Map>()
            .map(
              (item) =>
                  ChatMessageModel.fromJson(Map<String, dynamic>.from(item)),
            )
            .toList(growable: false),
        hasMore: json['hasMore'] == true,
        nextBeforeSentAtUtc: _date(json['nextBeforeSentAtUtc']),
        nextBeforeId: json['nextBeforeId']?.toString(),
        canSend: json['canSend'] == true,
      );
}

DateTime? _date(Object? value) =>
    value == null ? null : DateTime.parse(value.toString()).toUtc();
