import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../shared/widgets.dart';
import '../member/gym_screens.dart';
import 'chat_controller.dart';
import 'chat_models.dart';
import 'chat_repository.dart';

final _chatTime = DateFormat('dd.MM. HH:mm');

final class NormalizedChatImage {
  const NormalizedChatImage({
    required this.bytes,
    required this.fileName,
    required this.contentType,
  });

  final Uint8List bytes;
  final String fileName;
  final String contentType;
}

NormalizedChatImage? normalizeChatImage(Uint8List bytes, String originalName) {
  final String? contentType;
  final String extension;
  if (bytes.length >= 3 &&
      bytes[0] == 0xff &&
      bytes[1] == 0xd8 &&
      bytes[2] == 0xff) {
    contentType = 'image/jpeg';
    extension = 'jpg';
  } else if (bytes.length >= 8 &&
      bytes[0] == 0x89 &&
      bytes[1] == 0x50 &&
      bytes[2] == 0x4e &&
      bytes[3] == 0x47 &&
      bytes[4] == 0x0d &&
      bytes[5] == 0x0a &&
      bytes[6] == 0x1a &&
      bytes[7] == 0x0a) {
    contentType = 'image/png';
    extension = 'png';
  } else if (bytes.length >= 12 &&
      String.fromCharCodes(bytes.sublist(0, 4)) == 'RIFF' &&
      String.fromCharCodes(bytes.sublist(8, 12)) == 'WEBP') {
    contentType = 'image/webp';
    extension = 'webp';
  } else {
    return null;
  }

  final rawBaseName = originalName.trim().split(RegExp(r'[/\\]')).last;
  final dot = rawBaseName.lastIndexOf('.');
  final baseName = (dot > 0 ? rawBaseName.substring(0, dot) : rawBaseName)
      .replaceAll(RegExp(r'[^A-Za-z0-9_-]'), '_');
  return NormalizedChatImage(
    bytes: bytes,
    fileName: '${baseName.isEmpty ? 'chat' : baseName}.$extension',
    contentType: contentType,
  );
}

Future<void> openChatForReservation(
  BuildContext context,
  String reservationId,
) async {
  try {
    final conversation = await ChatRepository(
      context.read<ApiClient>(),
    ).open(reservationId);
    if (!context.mounted) return;
    await _openConversation(context, conversation);
  } on ApiProblem catch (error) {
    if (context.mounted) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(error.message)));
    }
  }
}

Future<void> openChatForConversation(
  BuildContext context,
  String conversationId,
) async {
  try {
    final conversation = await ChatRepository(
      context.read<ApiClient>(),
    ).get(conversationId);
    if (!context.mounted) return;
    await _openConversation(context, conversation);
  } on ApiProblem catch (error) {
    if (context.mounted) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(error.message)));
    }
  }
}

Future<void> _openConversation(
  BuildContext context,
  ConversationModel conversation,
) => Navigator.push(
  context,
  MaterialPageRoute<void>(
    builder: (_) => ChangeNotifierProvider.value(
      value: context.read<ChatController>(),
      child: ChatDetailScreen(conversation: conversation),
    ),
  ),
);

class ConversationListScreen extends StatelessWidget {
  const ConversationListScreen({this.onUnreadChanged, super.key});

  final ValueChanged<int>? onUnreadChanged;

  @override
  Widget build(BuildContext context) =>
      _ConversationListBody(onUnreadChanged: onUnreadChanged);
}

class _ConversationListBody extends StatefulWidget {
  const _ConversationListBody({this.onUnreadChanged});

  final ValueChanged<int>? onUnreadChanged;

  @override
  State<_ConversationListBody> createState() => _ConversationListBodyState();
}

class _ConversationListBodyState extends State<_ConversationListBody>
    with WidgetsBindingObserver {
  final _search = TextEditingController();
  Timer? _debounce;
  int _reportedUnread = -1;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        unawaited(context.read<ChatController>().initializeList());
      }
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(context.read<ChatController>().resume());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  void _searchChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (mounted) {
        context.read<ChatController>().loadConversations(search: value.trim());
      }
    });
  }

  void _reportUnread(int value) {
    if (_reportedUnread == value) return;
    _reportedUnread = value;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) widget.onUnreadChanged?.call(value);
    });
  }

  @override
  Widget build(BuildContext context) => Consumer<ChatController>(
    builder: (context, controller, _) {
      _reportUnread(controller.unreadCount);
      final error = controller.listError;
      return RefreshIndicator(
        onRefresh: () =>
            controller.loadConversations(search: _search.text.trim()),
        child: ListView(
          key: const Key('conversation-list'),
          padding: const EdgeInsets.all(16),
          children: [
            TextField(
              controller: _search,
              onChanged: _searchChanged,
              decoration: const InputDecoration(
                labelText: 'Pretraži razgovore',
                prefixIcon: Icon(Icons.search),
              ),
            ),
            const SizedBox(height: 12),
            if (controller.listLoading && controller.conversations.isEmpty)
              const SizedBox(
                height: 420,
                child: Center(child: CircularProgressIndicator()),
              )
            else if (error != null && controller.conversations.isEmpty)
              SizedBox(
                height: 420,
                child: _ChatError(
                  message: error,
                  retry: () =>
                      controller.loadConversations(search: _search.text.trim()),
                ),
              )
            else if (controller.conversations.isEmpty)
              const SizedBox(
                height: 420,
                child: EmptyState(
                  title: 'Nema razgovora',
                  message:
                      'Razgovori se pojavljuju nakon potvrđene rezervacije.',
                  icon: Icons.forum_outlined,
                ),
              )
            else ...[
              for (final conversation in controller.conversations)
                _ConversationTile(
                  conversation: conversation,
                  onTap: () => Navigator.push(
                    context,
                    MaterialPageRoute<void>(
                      builder: (_) => ChangeNotifierProvider.value(
                        value: controller,
                        child: ChatDetailScreen(conversation: conversation),
                      ),
                    ),
                  ),
                ),
              if (error != null)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    error,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ),
              if (controller.hasMoreConversations)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: OutlinedButton(
                    onPressed: controller.listLoading
                        ? null
                        : () => controller.loadMoreConversations(
                            search: _search.text.trim(),
                          ),
                    child: controller.listLoading
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Učitaj još'),
                  ),
                ),
            ],
          ],
        ),
      );
    },
  );
}

class _ConversationTile extends StatelessWidget {
  const _ConversationTile({required this.conversation, required this.onTap});

  final ConversationModel conversation;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final lastAt = conversation.lastMessageAtUtc ?? conversation.createdAtUtc;
    return Card(
      child: ListTile(
        key: Key('conversation-${conversation.id}'),
        onTap: onTap,
        leading: TrainerImageAvatar(
          name: conversation.counterpartDisplayName,
          imageUrl: context.read<ApiClient>().mediaUrl(
            conversation.counterpartImageUrl,
          ),
        ),
        title: Text(
          conversation.counterpartDisplayName,
          style: const TextStyle(fontWeight: FontWeight.w700),
        ),
        subtitle: Text(
          '${conversation.gymName}\n'
          '${conversation.lastMessageText ?? 'Razgovor je otvoren.'}',
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
        ),
        isThreeLine: true,
        trailing: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Text(
              _chatTime.format(lastAt.toLocal()),
              style: Theme.of(context).textTheme.labelSmall,
            ),
            const SizedBox(height: 6),
            if (conversation.unreadCount > 0)
              Badge(
                key: Key('unread-${conversation.id}'),
                label: Text(
                  conversation.unreadCount > 99
                      ? '99+'
                      : '${conversation.unreadCount}',
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class ChatDetailScreen extends StatefulWidget {
  const ChatDetailScreen({
    required this.conversation,
    this.pickImage,
    super.key,
  });

  final ConversationModel conversation;
  final Future<XFile?> Function()? pickImage;

  @override
  State<ChatDetailScreen> createState() => _ChatDetailScreenState();
}

class _ChatDetailScreenState extends State<ChatDetailScreen>
    with WidgetsBindingObserver {
  final _draft = TextEditingController();
  late ChatController _chatController;
  bool _openingTrainerProfile = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        unawaited(_chatController.openConversation(widget.conversation));
      }
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _chatController = context.read<ChatController>();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_chatController.resume());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _draft.dispose();
    unawaited(_chatController.closeConversation());
    super.dispose();
  }

  Future<void> _send() async {
    final sent = await _chatController.send(_draft.text);
    if (sent && mounted) _draft.clear();
  }

  Future<void> _sendImage() async {
    try {
      final image =
          await (widget.pickImage?.call() ??
              ImagePicker().pickImage(
                source: ImageSource.gallery,
                maxWidth: 1600,
                maxHeight: 1600,
                imageQuality: 80,
              ));
      if (image == null || !mounted) return;
      final bytes = await image.readAsBytes();
      if (!mounted) return;
      if (bytes.length > 5 * 1024 * 1024) {
        _showImageError('Slika mora biti manja od 5 MB.');
        return;
      }
      final normalized = normalizeChatImage(bytes, image.name);
      if (normalized == null) {
        _showImageError(
          'Odabranu sliku nije moguće obraditi. Odaberite JPG, PNG ili WebP fotografiju.',
        );
        return;
      }
      final sent = await _chatController.sendImage(
        normalized.bytes,
        normalized.fileName,
        normalized.contentType,
      );
      if (!sent && mounted) {
        _showImageError(
          _chatController.imageUploadError ??
              'Slika trenutno nije poslana. Pokušajte ponovo.',
        );
      }
    } catch (_) {
      if (mounted) {
        _showImageError(
          'Sliku nije moguće otvoriti. Odaberite drugu fotografiju.',
        );
      }
    }
  }

  void _showImageError(String message) => ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _openTrainerProfile(ConversationModel conversation) async {
    if (_openingTrainerProfile || conversation.counterpartRole != 'Trainer') {
      return;
    }
    setState(() => _openingTrainerProfile = true);
    try {
      final trainers = await context.read<ApiClient>().page(
        '/api/gyms/${conversation.gymId}/trainers',
        query: const {'page': 1, 'pageSize': 100},
        authenticated: false,
      );
      Map<String, dynamic>? trainer;
      for (final candidate in trainers.items) {
        if (candidate['userId']?.toString() == conversation.counterpartUserId) {
          trainer = candidate;
          break;
        }
      }
      if (!mounted) return;
      if (trainer == null) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Profil trenera trenutno nije dostupan.'),
          ),
        );
        return;
      }
      await Navigator.push<void>(
        context,
        MaterialPageRoute(
          builder: (_) =>
              BookingScreen(trainer: trainer!, gymId: conversation.gymId),
        ),
      );
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Profil trenera trenutno nije dostupan.'),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _openingTrainerProfile = false);
    }
  }

  Widget _conversationTitle(ConversationModel conversation) {
    final details = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          conversation.counterpartDisplayName,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        Text(
          conversation.gymName,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.labelMedium,
        ),
      ],
    );
    if (conversation.counterpartRole != 'Trainer') return details;
    return Tooltip(
      message: 'Otvori profil trenera',
      child: InkWell(
        key: const Key('chat-trainer-profile'),
        borderRadius: BorderRadius.circular(8),
        onTap: _openingTrainerProfile
            ? null
            : () => _openTrainerProfile(conversation),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Row(
            children: [
              Expanded(child: details),
              const SizedBox(width: 8),
              if (_openingTrainerProfile)
                const SizedBox.square(
                  dimension: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              else
                const Icon(Icons.person_outline, size: 20),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Consumer<ChatController>(
    builder: (context, controller, _) {
      final conversation = controller.activeConversation ?? widget.conversation;
      return Scaffold(
        appBar: AppBar(title: _conversationTitle(conversation)),
        body: SafeArea(
          child: Column(
            children: [
              if (controller.detailLoading && controller.messages.isEmpty)
                const Expanded(
                  child: Center(child: CircularProgressIndicator()),
                )
              else if (controller.detailError != null &&
                  controller.messages.isEmpty)
                Expanded(
                  child: _ChatError(
                    message: controller.detailError!,
                    retry: () => controller.openConversation(conversation),
                  ),
                )
              else
                Expanded(
                  child: Column(
                    children: [
                      if (controller.hasMoreMessages)
                        TextButton.icon(
                          onPressed: controller.detailLoading
                              ? null
                              : controller.loadOlder,
                          icon: const Icon(Icons.history),
                          label: const Text('Učitaj starije poruke'),
                        ),
                      Expanded(
                        child: controller.messages.isEmpty
                            ? const EmptyState(
                                title: 'Nema poruka',
                                message: 'Pošaljite prvu poruku.',
                                icon: Icons.chat_bubble_outline,
                              )
                            : ListView.builder(
                                key: const Key('chat-message-list'),
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 12,
                                  vertical: 8,
                                ),
                                itemCount: controller.messages.length,
                                itemBuilder: (context, index) {
                                  final message = controller.messages[index];
                                  return _MessageBubble(
                                    message: message,
                                    image: controller.imageFor(message),
                                    mine:
                                        message.senderUserId ==
                                        controller.currentUserId,
                                    retry: () => controller.retry(message),
                                  );
                                },
                              ),
                      ),
                    ],
                  ),
                ),
              if (controller.detailError != null &&
                  controller.messages.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Text(
                    controller.detailError!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ),
              _Composer(
                controller: _draft,
                sending: controller.sending,
                sendingImage: controller.sendingImage,
                onSend: _send,
                onImage: _sendImage,
              ),
            ],
          ),
        ),
      );
    },
  );
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({
    required this.message,
    required this.image,
    required this.mine,
    required this.retry,
  });

  final ChatMessageModel message;
  final Future<Uint8List?> image;
  final bool mine;
  final VoidCallback retry;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: GestureDetector(
        onTap: message.delivery == MessageDeliveryState.failed ? retry : null,
        child: Container(
          key: Key('message-${message.clientMessageId}'),
          constraints: const BoxConstraints(maxWidth: 320),
          margin: const EdgeInsets.symmetric(vertical: 4),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          decoration: BoxDecoration(
            color: mine ? scheme.primaryContainer : scheme.surfaceContainerHigh,
            borderRadius: BorderRadius.circular(16),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: message.imageUrl == null
                    ? Text(message.text)
                    : FutureBuilder<Uint8List?>(
                        future: image,
                        builder: (context, snapshot) {
                          final bytes = snapshot.data;
                          if (bytes != null) {
                            return ClipRRect(
                              borderRadius: BorderRadius.circular(12),
                              child: Image.memory(
                                bytes,
                                key: Key('chat-image-${message.id}'),
                                width: 240,
                                fit: BoxFit.cover,
                              ),
                            );
                          }
                          if (snapshot.connectionState ==
                              ConnectionState.waiting) {
                            return const SizedBox(
                              width: 240,
                              height: 160,
                              child: Center(child: CircularProgressIndicator()),
                            );
                          }
                          return const SizedBox(
                            width: 240,
                            height: 120,
                            child: Center(
                              child: Icon(Icons.broken_image_outlined),
                            ),
                          );
                        },
                      ),
              ),
              const SizedBox(height: 4),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    DateFormat('HH:mm').format(message.sentAtUtc.toLocal()),
                    style: Theme.of(context).textTheme.labelSmall,
                  ),
                  if (mine) ...[
                    const SizedBox(width: 4),
                    Icon(
                      switch (message.delivery) {
                        MessageDeliveryState.pending => Icons.schedule,
                        MessageDeliveryState.sent => Icons.done_all,
                        MessageDeliveryState.failed => Icons.error_outline,
                      },
                      key: Key(
                        'delivery-${message.clientMessageId}-'
                        '${message.delivery.name}',
                      ),
                      size: 15,
                      color: message.delivery == MessageDeliveryState.failed
                          ? scheme.error
                          : null,
                    ),
                  ],
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.sending,
    required this.sendingImage,
    required this.onSend,
    required this.onImage,
  });

  final TextEditingController controller;
  final bool sending;
  final bool sendingImage;
  final VoidCallback onSend;
  final VoidCallback onImage;

  @override
  Widget build(BuildContext context) {
    return Material(
      elevation: 8,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            IconButton(
              key: const Key('chat-image-picker'),
              tooltip: 'PoÅ¡alji sliku',
              onPressed: sending || sendingImage ? null : onImage,
              icon: sendingImage
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.attach_file),
            ),
            Expanded(
              child: TextField(
                key: const Key('chat-draft'),
                controller: controller,
                enabled: !sending && !sendingImage,
                minLines: 1,
                maxLines: 5,
                maxLength: 2000,
                textInputAction: TextInputAction.newline,
                decoration: const InputDecoration(
                  hintText: 'Napišite poruku...',
                  counterText: '',
                ),
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filled(
              key: const Key('chat-send'),
              onPressed: sending || sendingImage ? null : onSend,
              icon: sending
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.send),
            ),
          ],
        ),
      ),
    );
  }
}

class _ChatError extends StatelessWidget {
  const _ChatError({required this.message, required this.retry});

  final String message;
  final VoidCallback retry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.cloud_off_outlined, size: 42),
          const SizedBox(height: 12),
          Text(message, textAlign: TextAlign.center),
          const SizedBox(height: 16),
          OutlinedButton(onPressed: retry, child: const Text('Pokušaj ponovo')),
        ],
      ),
    ),
  );
}
