import 'package:flutter/widgets.dart';

/// Palette matched to the reference mock:
/// mid-gray canvas, lighter translucent window card, soft single-source
/// shadows (light from top-left). No heavy white halos.
class AppColors {
  AppColors._();

  // Canvas (outside the window card)
  static const Color canvas = Color(0xFFBDBEBB);
  static const Color canvasTop = Color(0xFFC7C8C5);
  static const Color canvasBottom = Color(0xFFB1B3AF);

  // Base surfaces (glass tints these further at render time)
  static const Color surface = Color(0xFFD3D5D1); // side panel zone
  static const Color content = Color(0xFFDADCD7); // main content zone
  static const Color raised = Color(0xFFE1E3DE); // convex cards / hover
  static const Color raisedHi = Color(0xFFEAEDE7); // selected pills, stat card
  static const Color inset = Color(0xFFC6C8C4); // concave fields

  // Dark rail
  static const Color rail = Color(0xFF2A2B2A);
  static const Color railTile = Color(0xFF3E3F3E);
  static const Color railTileInset = Color(0xFF1D1E1D);

  // Soft neumorphic shadows: single light source, top-left
  static const Color shadowDark = Color(0x2E000000); // ~18% black
  static const Color shadowLight = Color(0x42FFFFFF); // ~26% white

  // Ink
  static const Color ink = Color(0xDE000000);
  static const Color inkSecondary = Color(0x99000000);
  static const Color inkTertiary = Color(0x66000000);

  // Accents
  static const Color greenBg = Color(0xFFBFE3C9);
  static const Color greenFg = Color(0xFF237A45);
  static const Color close = Color(0xFFFF5F57);
  static const Color minimize = Color(0xFFFEBC2E);
  static const Color maximize = Color(0xFF28C840);
}

class AppSpacing {
  AppSpacing._();

  static const double railWidth = 62;
  static const double sidePanelWidth = 300;
}
