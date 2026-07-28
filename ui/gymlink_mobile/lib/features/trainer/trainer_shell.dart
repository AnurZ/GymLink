import 'package:flutter/material.dart';

import '../profile/profile_screen.dart';
import 'trainer_screens.dart';

class TrainerShell extends StatefulWidget {
  const TrainerShell({super.key});

  @override
  State<TrainerShell> createState() => _TrainerShellState();
}

class _TrainerShellState extends State<TrainerShell> {
  int _index = 0;
  final _pages = const [
    TrainerAppointmentsScreen(),
    TrainerAvailabilityScreen(),
    TrainerOfferingsScreen(),
    TrainerReviewsScreen(),
    ProfileScreen(),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
        const [
          'Moji termini',
          'Dostupnost',
          'Usluge',
          'Recenzije',
          'Profil',
        ][_index],
        style: const TextStyle(fontWeight: FontWeight.w800),
      ),
    ),
    body: IndexedStack(index: _index, children: _pages),
    bottomNavigationBar: NavigationBar(
      selectedIndex: _index,
      onDestinationSelected: (value) => setState(() => _index = value),
      destinations: const [
        NavigationDestination(icon: Icon(Icons.event_note), label: 'Termini'),
        NavigationDestination(
          icon: Icon(Icons.calendar_month),
          label: 'Dostupnost',
        ),
        NavigationDestination(icon: Icon(Icons.sell_outlined), label: 'Usluge'),
        NavigationDestination(
          icon: Icon(Icons.star_outline),
          label: 'Recenzije',
        ),
        NavigationDestination(
          icon: Icon(Icons.person_outline),
          label: 'Profil',
        ),
      ],
    ),
  );
}
