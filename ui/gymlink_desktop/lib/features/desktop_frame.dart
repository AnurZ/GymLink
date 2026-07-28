import 'package:flutter/material.dart';

import '../core/theme.dart';
import 'notifications/notification_screen.dart';

class DesktopDestination {
  const DesktopDestination(this.label, this.icon, this.page);
  final String label;
  final IconData icon;
  final Widget page;
}

class DesktopFrame extends StatefulWidget {
  const DesktopFrame({
    required this.heading,
    required this.roleLabel,
    required this.destinations,
    super.key,
  });
  final String heading;
  final String roleLabel;
  final List<DesktopDestination> destinations;

  @override
  State<DesktopFrame> createState() => _DesktopFrameState();
}

class _DesktopFrameState extends State<DesktopFrame> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final selected = widget.destinations[_index];
    return Scaffold(
      body: Row(
        children: [
          Material(
            color: Colors.white,
            child: SizedBox(
              width: 260,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(22, 24, 18, 22),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text(
                          'GymLink Admin',
                          style: TextStyle(
                            color: GymLinkColors.blue,
                            fontSize: 23,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        const SizedBox(height: 6),
                        Text(widget.roleLabel),
                      ],
                    ),
                  ),
                  const Divider(height: 1),
                  const SizedBox(height: 12),
                  Expanded(
                    child: ListView.builder(
                      padding: const EdgeInsets.symmetric(horizontal: 12),
                      itemCount: widget.destinations.length,
                      itemBuilder: (context, index) {
                        final item = widget.destinations[index];
                        return Padding(
                          padding: const EdgeInsets.only(bottom: 5),
                          child: ListTile(
                            selected: _index == index,
                            selectedTileColor: const Color(0xFFEAF2FF),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(10),
                            ),
                            leading: Icon(item.icon),
                            title: Text(
                              item.label,
                              style: const TextStyle(
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            onTap: () => setState(() => _index = index),
                          ),
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
          ),
          const VerticalDivider(width: 1),
          Expanded(
            child: Column(
              children: [
                Container(
                  height: 92,
                  padding: const EdgeInsets.symmetric(horizontal: 34),
                  color: Colors.white,
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              selected.label,
                              style: Theme.of(context).textTheme.headlineMedium
                                  ?.copyWith(fontWeight: FontWeight.w800),
                            ),
                            Text(widget.heading),
                          ],
                        ),
                      ),
                      const NotificationBell(),
                    ],
                  ),
                ),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.all(28),
                    child: IndexedStack(
                      index: _index,
                      children: widget.destinations
                          .map((destination) => destination.page)
                          .toList(growable: false),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
