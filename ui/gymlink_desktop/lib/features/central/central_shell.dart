import 'package:flutter/material.dart';

import '../desktop_frame.dart';
import '../profile/profile_screen.dart';
import 'central_screens.dart';

class CentralAdminShell extends StatelessWidget {
  const CentralAdminShell({super.key});

  @override
  Widget build(BuildContext context) => const DesktopFrame(
    heading: 'Upravljanje svim teretanama i računima',
    roleLabel: 'Centralni administrator',
    destinations: [
      DesktopDestination(
        'Pregled',
        Icons.home_outlined,
        CentralDashboardScreen(),
      ),
      DesktopDestination(
        'Registracije teretana',
        Icons.apartment_outlined,
        RegistrationManagementScreen(),
      ),
      DesktopDestination(
        'Korisnici i uloge',
        Icons.manage_accounts_outlined,
        UserManagementScreen(),
      ),
      DesktopDestination(
        'Referentni podaci',
        Icons.dataset_outlined,
        ReferenceDataScreen(),
      ),
      DesktopDestination('Moj profil', Icons.person_outline, ProfileScreen()),
    ],
  );
}
