import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:provider/provider.dart';

import '../profile/profile_screen.dart';
import '../notifications/notification_screen.dart';
import '../chat/chat_screens.dart';
import '../reservations/reservation_refresh_controller.dart';
import 'gym_screens.dart';
import 'membership_screen.dart';
import 'reservation_screen.dart';
import '../recommendations/recommendation_screen.dart';

class MemberShell extends StatefulWidget {
  const MemberShell({super.key});

  @override
  State<MemberShell> createState() => _MemberShellState();
}

class _MemberShellState extends State<MemberShell> {
  int _index = 0;
  int _chatUnreadCount = 0;
  ReservationRefreshController? _reservationsController;
  late List<Widget> _pages;

  void _openRecommendations() {
    Navigator.push<void>(
      context,
      MaterialPageRoute(builder: (_) => const RecommendationScreen()),
    );
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = context.read<ReservationRefreshController>();
    if (_reservationsController == controller) return;
    _reservationsController = controller;
    _pages = [
      const GymDiscoveryScreen(),
      const MembershipScreen(),
      MemberReservationsScreen(controller: controller),
      ConversationListScreen(onUnreadChanged: _setChatUnreadCount),
      const ProfileScreen(),
    ];
  }

  void _setChatUnreadCount(int value) {
    if (mounted && value != _chatUnreadCount) {
      setState(() => _chatUnreadCount = value);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        const [
          'Teretane',
          'Članstva',
          'Rezervacije',
          'Razgovori',
          'Profil',
        ][_index],
        style: const TextStyle(fontWeight: FontWeight.w800),
      ),
      actions: [
        RecommendationAttentionAction(onPressed: _openRecommendations),
        const NotificationBell(),
      ],
    ),
    body: IndexedStack(index: _index, children: _pages),
    bottomNavigationBar: NavigationBar(
      selectedIndex: _index,
      onDestinationSelected: (value) {
        if (value == 2) {
          _reservationsController?.refresh();
        }
        setState(() => _index = value);
      },
      destinations: [
        const NavigationDestination(
          icon: Icon(Icons.fitness_center),
          label: 'Teretane',
        ),
        const NavigationDestination(
          icon: Icon(Icons.card_membership),
          label: 'Članstva',
        ),
        const NavigationDestination(
          icon: Icon(Icons.event_note),
          label: 'Termini',
        ),
        NavigationDestination(
          icon: Badge(
            key: const Key('member-chat-unread'),
            isLabelVisible: _chatUnreadCount > 0,
            label: Text(_chatUnreadCount > 99 ? '99+' : '$_chatUnreadCount'),
            child: const Icon(Icons.forum_outlined),
          ),
          label: 'Razgovori',
        ),
        const NavigationDestination(
          icon: Icon(Icons.person_outline),
          label: 'Profil',
        ),
      ],
    ),
  );
}

abstract interface class RecommendationCueStorage {
  Future<bool> hasSeenRecommendations();
  Future<void> markRecommendationsSeen();
}

final class SecureRecommendationCueStorage implements RecommendationCueStorage {
  const SecureRecommendationCueStorage();

  static const _key = 'gymlink.recommendations_seen';
  static const _storage = FlutterSecureStorage();

  @override
  Future<bool> hasSeenRecommendations() async =>
      await _storage.read(key: _key) == 'true';

  @override
  Future<void> markRecommendationsSeen() =>
      _storage.write(key: _key, value: 'true');
}

class RecommendationAttentionAction extends StatefulWidget {
  const RecommendationAttentionAction({
    super.key,
    required this.onPressed,
    this.storage = const SecureRecommendationCueStorage(),
  });

  final VoidCallback onPressed;
  final RecommendationCueStorage storage;

  @override
  State<RecommendationAttentionAction> createState() =>
      _RecommendationAttentionActionState();
}

class _RecommendationAttentionActionState
    extends State<RecommendationAttentionAction> {
  bool _showDot = false;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    var show = true;
    try {
      show = !await widget.storage.hasSeenRecommendations();
    } catch (_) {
      show = true;
    }
    if (mounted) setState(() => _showDot = show);
  }

  Future<void> _rememberSeen() async {
    try {
      await widget.storage.markRecommendationsSeen();
    } catch (_) {
      // The cue is best-effort and must never block the destination.
    }
  }

  void _open() {
    if (_showDot) setState(() => _showDot = false);
    unawaited(_rememberSeen());
    widget.onPressed();
  }

  @override
  Widget build(BuildContext context) => IconButton(
    key: const Key('open-recommendations'),
    tooltip: 'Preporuke',
    onPressed: _open,
    icon: Badge(
      key: const Key('recommendation-attention-dot'),
      isLabelVisible: _showDot,
      backgroundColor: Colors.red,
      smallSize: 8,
      child: const Icon(Icons.auto_awesome_outlined),
    ),
  );
}
