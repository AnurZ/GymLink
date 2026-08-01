import 'package:flutter/material.dart';
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
        IconButton(
          key: const Key('open-recommendations'),
          tooltip: 'Preporuke',
          onPressed: () => Navigator.push<void>(
            context,
            MaterialPageRoute(builder: (_) => const RecommendationScreen()),
          ),
          icon: const Icon(Icons.auto_awesome_outlined),
        ),
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
