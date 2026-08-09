import 'package:flutter/material.dart';

import '../desktop_frame.dart';
import '../profile/profile_screen.dart';
import 'gym_admin_screens.dart';
import '../reporting/reporting_screens.dart';

class GymAdminShell extends StatelessWidget {
  const GymAdminShell({super.key});

  @override
  Widget build(BuildContext context) => const DesktopFrame(
    heading: 'Upravljanje odabranom teretanom',
    roleLabel: 'Administrator teretane',
    destinations: [
      DesktopDestination('Pregled', Icons.home_outlined, GymDashboardScreen()),
      DesktopDestination(
        'Članarine',
        Icons.card_membership_outlined,
        TenantMembershipRequestsScreen(),
      ),
      DesktopDestination(
        'Treneri i usluge',
        Icons.people_outline,
        TrainerManagementScreen(),
      ),
      DesktopDestination(
        'Raspored',
        Icons.calendar_month_outlined,
        TenantAvailabilityScreen(),
      ),
      DesktopDestination(
        'Rezervacije',
        Icons.event_note_outlined,
        TenantReservationsScreen(),
      ),
      DesktopDestination(
        'Izvještaji',
        Icons.bar_chart_outlined,
        GymAdminReportsScreen(),
      ),
      DesktopDestination(
        'Postavke teretane',
        Icons.settings_outlined,
        GymCatalogScreen(),
      ),
      DesktopDestination('Moj profil', Icons.person_outline, ProfileScreen()),
    ],
  );
}
