import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api.dart';
import '../../core/theme.dart';

class NotificationBell extends StatefulWidget {
  const NotificationBell({super.key});

  @override
  State<NotificationBell> createState() => _NotificationBellState();
}

class _NotificationBellState extends State<NotificationBell>
    with WidgetsBindingObserver {
  Timer? _timer;
  int _count = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _refresh();
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => _refresh());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _refresh();
  }

  Future<void> _refresh() async {
    try {
      final value = Map<String, dynamic>.from(
        (await context.read<ApiClient>().get(
              '/api/me/notifications/unread-count',
            ))!
            as Map,
      );
      if (mounted) {
        setState(() => _count = (value['count'] as num?)?.toInt() ?? 0);
      }
    } on Object {
      // Retain the last visible count while the API is unreachable.
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => IconButton(
    tooltip: 'Obavijesti',
    onPressed: () async {
      await context.push('/notifications');
      _refresh();
    },
    icon: Badge(
      isLabelVisible: _count > 0,
      label: Text(_count > 99 ? '99+' : '$_count'),
      child: const Icon(Icons.notifications_outlined),
    ),
  );
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
          _hasMore = page.page * page.pageSize < page.totalCount;
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
    initialIndex: _isRead == false ? 1 : 0,
    child: Scaffold(
      appBar: AppBar(
        title: const Text('Obavijesti'),
        bottom: TabBar.secondary(
          tabs: const [
            Tab(key: Key('notifications-all-tab'), text: 'Sve'),
            Tab(key: Key('notifications-unread-tab'), text: 'Nepročitane'),
          ],
          onTap: (index) {
            final next = index == 1 ? false : null;
            if (_isRead == next) return;
            setState(() => _isRead = next);
            _load(reset: true);
          },
        ),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 900),
          child: RefreshIndicator(
            onRefresh: () => _load(reset: true),
            child: ListView(
              padding: const EdgeInsets.all(28),
              children: [
                Align(
                  alignment: Alignment.centerRight,
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
                        : const Text('Označi sve kao pročitano'),
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
                    padding: EdgeInsets.all(48),
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
                        '${_notificationPreview(item['text'])}\n${_notificationDate(item['createdAtUtc'])}',
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
  late final Map<String, dynamic> _item;

  @override
  void initState() {
    super.initState();
    _item = Map<String, dynamic>.from(widget.item);
    WidgetsBinding.instance.addPostFrameCallback((_) => _markRead());
  }

  Future<void> _markRead() async {
    if (_item['isRead'] == true) return;
    try {
      final updated = Map<String, dynamic>.from(
        (await context.read<ApiClient>().post(
              '/api/me/notifications/${_item['id']}/read',
              body: {'concurrencyToken': _item['concurrencyToken']},
            ))!
            as Map,
      );
      if (mounted) setState(() => _item.addAll(updated));
    } on ApiProblem catch (error) {
      if (mounted) setState(() => _readError = error.message);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Detalji obavijesti')),
    body: Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 760),
        child: ListView(
          padding: const EdgeInsets.all(32),
          children: [
            Icon(
              Icons.notifications_outlined,
              size: 48,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(height: 20),
            Text(
              _item['title']?.toString() ?? 'Obavijest',
              style: Theme.of(
                context,
              ).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 8),
            Text(
              _notificationDate(_item['createdAtUtc']),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 18),
            Text(
              _item['text']?.toString() ?? '',
              style: Theme.of(context).textTheme.bodyLarge,
            ),
            if (_readError != null) ...[
              const SizedBox(height: 18),
              Text(
                _readError!,
                style: const TextStyle(color: GymLinkColors.danger),
              ),
            ],
          ],
        ),
      ),
    ),
  );
}

String _notificationPreview(Object? value) {
  final text = value?.toString().trim() ?? '';
  return text.length <= 110 ? text : '${text.substring(0, 107)}…';
}

String _notificationDate(Object? value) {
  final date = DateTime.tryParse(value?.toString() ?? '');
  return date == null
      ? ''
      : DateFormat('dd.MM.yyyy. HH:mm').format(date.toLocal());
}
