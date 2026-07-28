import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gymlink_desktop/core/theme.dart';
import 'package:gymlink_desktop/features/desktop_frame.dart';

void main() {
  testWidgets('desktop shell exposes role-specific navigation', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildGymLinkTheme(),
        home: const DesktopFrame(
          heading: 'Test',
          roleLabel: 'Centralni administrator',
          destinations: [
            DesktopDestination(
              'Pregled',
              Icons.home_outlined,
              Center(child: Text('Sadržaj')),
            ),
            DesktopDestination(
              'Korisnici',
              Icons.people_outline,
              Center(child: Text('Korisnici sadržaj')),
            ),
          ],
        ),
      ),
    );

    expect(find.text('GymLink Admin'), findsOneWidget);
    expect(find.text('Centralni administrator'), findsOneWidget);
    expect(find.text('Sadržaj'), findsOneWidget);
    await tester.tap(find.text('Korisnici'));
    await tester.pump();
    expect(find.text('Korisnici sadržaj'), findsOneWidget);
  });
}
