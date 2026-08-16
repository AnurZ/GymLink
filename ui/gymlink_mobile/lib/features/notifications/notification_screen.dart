import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/auth.dart';
import '../../core/theme.dart';
import '../chat/chat_screens.dart';
import '../member/reservation_screen.dart';
import 'notification_controller.dart';

class NotificationBell extends StatelessWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context) {
    final count = context.watch<NotificationController>().unreadCount;
    return IconButton(
      tooltip: 'Obavijesti',
      onPressed: () async {
        await context.push('/notifications');
        if (context.mounted) {
          await context.read<NotificationController>().refresh();
        }
      },
      icon: Badge(
        key: const Key('notification-unread'),
        isLabelVisible: count > 0,
        label: Text(count > 99 ? '99+' : '$count'),
        child: const Icon(Icons.notifications_outlined),
      ),
    );
  }
}

class NotificationScreen extends StatefulWidget {
  const NotificationScreen({super.key});

  @override
  State<NotificationScreen> createState() => _NotificationScreenState();
}

class _NotificationScreenState extends State<NotificationScreen>
    with WidgetsBindingObserver {
  final List<Map<String, dynamic>> _items = [];
  Timer? _timer;
  int _page = 1;
  bool _busy = false;
  bool _hasMore = false;
  bool _markingAll = false;
  bool _unreadOnly = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _load(reset: true);
    _timer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _load(reset: true),
    );
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _load(reset: true);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load({required bool reset}) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
      if (reset) _page = 1;
    });
    try {
      final page = await context.read<ApiClient>().page(
        '/api/me/notifications',
        query: {
          'page': _page,
          'pageSize': 20,
          'isRead': _unreadOnly ? false : null,
        },
      );
      if (mounted) {
        setState(() {
          if (reset) _items.clear();
          _items.addAll(page.items);
          _hasMore = page.hasMore;
        });
      }
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _markAllRead() async {
    if (_markingAll) return;
    setState(() => _markingAll = true);
    try {
      await context.read<ApiClient>().post('/api/me/notifications/read-all');
      if (mounted) {
        context.read<NotificationController>().allNotificationsMarkedRead();
      }
      await _load(reset: true);
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _markingAll = false);
    }
  }

  Future<void> _openDetails(Map<String, dynamic> item) async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(builder: (_) => NotificationDetailScreen(item: item)),
    );
    if (mounted) await _load(reset: true);
  }

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 2,
    initialIndex: _unreadOnly ? 1 : 0,
    child: Scaffold(
      appBar: AppBar(
        title: const Text('Obavijesti'),
        bottom: TabBar.secondary(
          tabs: const [
            Tab(key: Key('notifications-all-tab'), text: 'Sve'),
            Tab(key: Key('notifications-unread-tab'), text: 'Nepročitane'),
          ],
          onTap: (index) {
            final unreadOnly = index == 1;
            if (_unreadOnly == unreadOnly) return;
            setState(() => _unreadOnly = unreadOnly);
            _load(reset: true);
          },
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () => _load(reset: true),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Align(
              alignment: Alignment.centerRight,
              child: Semantics(
                label: 'Označi sve kao pročitano',
                button: true,
                child: TextButton(
                  key: const Key('mark-all-notifications-read'),
                  onPressed:
                      _markingAll ||
                          !_items.any((item) => item['isRead'] != true)
                      ? null
                      : _markAllRead,
                  child: _markingAll
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text(
                          'Označi sve kao pročitano',
                          maxLines: 1,
                          overflow: TextOverflow.fade,
                          softWrap: false,
                        ),
                ),
              ),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.all(20),
                child: Text(
                  _error!,
                  style: const TextStyle(color: GymLinkColors.danger),
                ),
              ),
            if (!_busy && _items.isEmpty && _error == null)
              const Padding(
                padding: EdgeInsets.all(36),
                child: Center(child: Text('Nemate obavijesti.')),
              ),
            for (final item in _items)
              Card(
                color: item['isRead'] == true
                    ? null
                    : GymLinkColors.blue.withValues(alpha: 0.08),
                child: ListTile(
                  leading: const Icon(Icons.notifications_outlined),
                  title: Text(item['title']?.toString() ?? 'Obavijest'),
                  subtitle: Text(
                    '${_preview(item['text'])}\n${_notificationDate(item['createdAtUtc'])}',
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                  ),
                  trailing: item['isRead'] == true
                      ? null
                      : const Icon(Icons.circle, size: 10),
                  onTap: () => _openDetails(item),
                ),
              ),
            if (_hasMore)
              TextButton(
                onPressed: _busy
                    ? null
                    : () {
                        _page++;
                        _load(reset: false);
                      },
                child: const Text('Učitaj još'),
              ),
            if (_busy)
              const Padding(
                padding: EdgeInsets.all(20),
                child: Center(child: CircularProgressIndicator()),
              ),
          ],
        ),
      ),
    ),
  );
}

class NotificationDetailScreen extends StatefulWidget {
  const NotificationDetailScreen({required this.item, super.key});

  final Map<String, dynamic> item;

  @override
  State<NotificationDetailScreen> createState() =>
      _NotificationDetailScreenState();
}

class _NotificationDetailScreenState extends State<NotificationDetailScreen> {
  String? _readError;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _markRead());
  }

  Future<void> _markRead() async {
    if (widget.item['isRead'] == true) return;
    try {
      final updated = Map<String, dynamic>.from(
        (await context.read<ApiClient>().post(
              '/api/me/notifications/${widget.item['id']}/read',
              body: {'concurrencyToken': widget.item['concurrencyToken']},
            ))!
            as Map,
      );
      if (!mounted) return;
      setState(() => widget.item.addAll(updated));
      context.read<NotificationController>().notificationMarkedRead();
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _readError = error.message);
    }
  }

  bool get _canOpenChat =>
      widget.item['category'] == 'chat' &&
      widget.item['targetType'] == 'conversation' &&
      widget.item['targetId'] != null;

  bool get _canOpenCompletedReservation =>
      context.read<AuthController>().session?.role == 'Member' &&
      widget.item['category'] == 'reservation.completed' &&
      widget.item['targetType'] == 'reservation' &&
      widget.item['targetId'] != null;

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Detalji obavijesti')),
    body: ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Icon(
          Icons.notifications_outlined,
          size: 48,
          color: Theme.of(context).colorScheme.primary,
        ),
        const SizedBox(height: 20),
        Text(
          widget.item['title']?.toString() ?? 'Obavijest',
          style: Theme.of(
            context,
          ).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800),
        ),
        const SizedBox(height: 8),
        Text(
          _notificationDate(widget.item['createdAtUtc']),
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 24),
        Text(
          widget.item['text']?.toString() ?? '',
          style: Theme.of(context).textTheme.bodyLarge,
        ),
        if (_readError != null) ...[
          const SizedBox(height: 20),
          Text(
            _readError!,
            style: const TextStyle(color: GymLinkColors.danger),
          ),
        ],
        if (_canOpenChat) ...[
          const SizedBox(height: 28),
          FilledButton.icon(
            onPressed: () => openChatForConversation(
              context,
              widget.item['targetId'].toString(),
            ),
            icon: const Icon(Icons.chat_bubble_outline),
            label: const Text('Otvori razgovor'),
          ),
        ],
        if (_canOpenCompletedReservation) ...[
          const SizedBox(height: 28),
          FilledButton.icon(
            key: const Key('notification-open-completed-reservation'),
            onPressed: () => Navigator.push<void>(
              context,
              MaterialPageRoute(
                builder: (_) => ReservationDetailsScreen(
                  reservationId: widget.item['targetId'].toString(),
                ),
              ),
            ),
            icon: const Icon(Icons.event_available_outlined),
            label: const Text('Otvori termin'),
          ),
        ],
      ],
    ),
  );
}

String _preview(Object? value) {
  final text = value?.toString().trim() ?? '';
  return text.length <= 110 ? text : '${text.substring(0, 107)}…';
}

String _notificationDate(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '');
  return date == null
      ? ''
      : DateFormat('dd.MM.yyyy. HH:mm').format(date.toLocal());
}
