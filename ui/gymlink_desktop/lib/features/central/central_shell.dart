import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../desktop_frame.dart';
import '../profile/profile_screen.dart';
import 'central_screens.dart';

class CentralAdminShell extends StatelessWidget {
  const CentralAdminShell({super.key});

  @override
  Widget build(BuildContext context) => ChangeNotifierProvider(
    create: (_) => CentralAdminRefresh(),
    child: const DesktopFrame(
      heading: 'Upravljanje svim teretanama i računima',
      roleLabel: 'Centralni administrator',
      destinations: [
        DesktopDestination(
          'Pregled',
          Icons.home_outlined,
          CentralDashboardScreen(),
        ),
        DesktopDestination(
          'Teretane',
          Icons.apartment_outlined,
          GymManagementScreen(),
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
    ),
  );
}
