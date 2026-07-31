import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../profile/profile_screen.dart';
import '../notifications/notification_screen.dart';
import '../chat/chat_screens.dart';
import '../reservations/reservation_refresh_controller.dart';
import 'trainer_screens.dart';

class TrainerShell extends StatefulWidget {
  const TrainerShell({super.key});

  @override
  State<TrainerShell> createState() => _TrainerShellState();
}

class _TrainerShellState extends State<TrainerShell> {
  int _index = 0;
  int _chatUnreadCount = 0;
  ReservationRefreshController? _appointmentsController;
  late List<Widget> _pages;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = context.read<ReservationRefreshController>();
    if (_appointmentsController == controller) return;
    _appointmentsController = controller;
    _pages = [
      TrainerAppointmentsScreen(controller: controller),
      const TrainerAvailabilityScreen(),
      const TrainerOfferingsScreen(),
      const TrainerReviewsScreen(),
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
          'Moji termini',
          'Dostupnost',
          'Usluge',
          'Recenzije',
          'Razgovori',
          'Profil',
        ][_index],
        style: const TextStyle(fontWeight: FontWeight.w800),
      ),
      actions: const [NotificationBell()],
    ),
    body: IndexedStack(index: _index, children: _pages),
    bottomNavigationBar: NavigationBar(
      selectedIndex: _index,
      onDestinationSelected: (value) {
        if (value == 0) {
          _appointmentsController?.refresh();
        }
        setState(() => _index = value);
      },
      destinations: [
        const NavigationDestination(
          icon: Icon(Icons.event_note),
          label: 'Termini',
        ),
        const NavigationDestination(
          icon: Icon(Icons.calendar_month),
          label: 'Dostupnost',
        ),
        const NavigationDestination(
          icon: Icon(Icons.sell_outlined),
          label: 'Usluge',
        ),
        const NavigationDestination(
          icon: Icon(Icons.star_outline),
          label: 'Recenzije',
        ),
        NavigationDestination(
          icon: Badge(
            key: const Key('trainer-chat-unread'),
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
