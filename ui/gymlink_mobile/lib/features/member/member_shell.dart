import 'package:flutter/material.dart';

import '../profile/profile_screen.dart';
import '../notifications/notification_screen.dart';
import 'gym_screens.dart';
import 'membership_screen.dart';
import 'reservation_screen.dart';

class MemberShell extends StatefulWidget {
  const MemberShell({super.key});

  @override
  State<MemberShell> createState() => _MemberShellState();
}

class _MemberShellState extends State<MemberShell> {
  int _index = 0;
  final _pages = const [
    GymDiscoveryScreen(),
    MembershipScreen(),
    MemberReservationsScreen(),
    ProfileScreen(),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        const ['Teretane', 'Članstva', 'Rezervacije', 'Profil'][_index],
        style: const TextStyle(fontWeight: FontWeight.w800),
      ),
      actions: const [NotificationBell()],
    ),
    body: IndexedStack(index: _index, children: _pages),
    bottomNavigationBar: NavigationBar(
      selectedIndex: _index,
      onDestinationSelected: (value) => setState(() => _index = value),
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.fitness_center),
          label: 'Teretane',
        ),
        NavigationDestination(
          icon: Icon(Icons.card_membership),
          label: 'Članstva',
        ),
        NavigationDestination(icon: Icon(Icons.event_note), label: 'Termini'),
        NavigationDestination(
          icon: Icon(Icons.person_outline),
          label: 'Profil',
        ),
      ],
    ),
  );
}
