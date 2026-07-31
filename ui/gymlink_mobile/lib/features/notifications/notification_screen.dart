import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';
import '../chat/chat_screens.dart';
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
  bool? _isRead;
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
        query: {'page': _page, 'pageSize': 20, 'isRead': _isRead},
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

  Future<void> _markRead(Map<String, dynamic> item) async {
    if (item['isRead'] == true) return;
    try {
      final updated = Map<String, dynamic>.from(
        (await context.read<ApiClient>().post(
              '/api/me/notifications/${item['id']}/read',
              body: {'concurrencyToken': item['concurrencyToken']},
            ))!
            as Map,
      );
      if (mounted) {
        setState(() => item.addAll(updated));
        context.read<NotificationController>().notificationMarkedRead();
      }
    } on ApiProblem catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  Future<void> _markAllRead() async {
    await context.read<ApiClient>().post('/api/me/notifications/read-all');
    if (mounted) {
      context.read<NotificationController>().allNotificationsMarkedRead();
    }
    await _load(reset: true);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Obavijesti'),
      actions: [
        TextButton(onPressed: _markAllRead, child: const Text('Označi sve')),
      ],
    ),
    body: RefreshIndicator(
      onRefresh: () => _load(reset: true),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          SegmentedButton<bool?>(
            segments: const [
              ButtonSegment(value: null, label: Text('Sve')),
              ButtonSegment(value: false, label: Text('Nepročitane')),
              ButtonSegment(value: true, label: Text('Pročitane')),
            ],
            selected: {_isRead},
            onSelectionChanged: (value) {
              setState(() => _isRead = value.first);
              _load(reset: true);
            },
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
                subtitle: Text(item['text']?.toString() ?? ''),
                trailing: item['isRead'] == true
                    ? null
                    : const Icon(Icons.circle, size: 10),
                onTap: () async {
                  await _markRead(item);
                  if (!context.mounted) return;
                  if (item['category'] == 'chat' &&
                      item['targetType'] == 'conversation' &&
                      item['targetId'] != null) {
                    await openChatForConversation(
                      context,
                      item['targetId'].toString(),
                    );
                    return;
                  }
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(
                      content: Text(
                        'Cilj obavijesti više nije dostupan ili nema poseban prikaz.',
                      ),
                    ),
                  );
                },
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
  );
}
