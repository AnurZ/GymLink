import 'package:flutter/material.dart';

abstract final class GymLinkColors {
  static const blue = Color(0xFF2864E8);
  static const ink = Color(0xFF111827);
  static const canvas = Color(0xFFF6F7F9);
  static const line = Color(0xFFE3E6EB);
  static const success = Color(0xFF16A34A);
  static const danger = Color(0xFFEF4444);
}

ThemeData buildGymLinkTheme() => ThemeData(
  useMaterial3: true,
  colorScheme: ColorScheme.fromSeed(seedColor: GymLinkColors.blue),
  scaffoldBackgroundColor: GymLinkColors.canvas,
  cardTheme: CardThemeData(
    color: Colors.white,
    elevation: 0,
    shape: RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(18),
      side: const BorderSide(color: GymLinkColors.line),
    ),
  ),
  inputDecorationTheme: InputDecorationTheme(
    filled: true,
    fillColor: const Color(0xFFF1F3F6),
    border: OutlineInputBorder(
      borderRadius: BorderRadius.circular(12),
      borderSide: BorderSide.none,
    ),
  ),
  filledButtonTheme: FilledButtonThemeData(
    style: FilledButton.styleFrom(
      backgroundColor: const Color(0xFF020617),
      foregroundColor: Colors.white,
      minimumSize: const Size(120, 46),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
    ),
  ),
);
