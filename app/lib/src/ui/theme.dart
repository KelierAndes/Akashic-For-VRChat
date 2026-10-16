import 'package:flutter/widgets.dart';

/// Theme-aware neumorphic tokens (light / dark variants of the same
/// physical materials).
class NeuTokens {
  const NeuTokens({
    required this.isDark,
    required this.ink,
    required this.inkSecondary,
    required this.inkTertiary,
    required this.raised,
    required this.raisedHi,
    required this.inset,
    required this.shadowDark,
    required this.shadowLight,
    required this.glassFill,
    required this.glassFillStrong,
    required this.sheetA,
    required this.sheetB,
    required this.sheetC,
    required this.warn,
    required this.edgeTop,
    required this.edgeBottom,
  });

  final bool isDark;
  final Color ink;
  final Color inkSecondary;
  final Color inkTertiary;
  final Color raised;
  final Color raisedHi;
  final Color inset;
  final Color shadowDark;
  final Color shadowLight;
  final Color glassFill;
  final Color glassFillStrong;
  final Color sheetA;
  final Color sheetB;
  final Color sheetC;

  /// Damage and refusal ink — restore warnings, failed tool output.
  final Color warn;
  final double edgeTop;
  final double edgeBottom;

  static const light = NeuTokens(
    isDark: false,
    ink: Color(0xE8000000),
    inkSecondary: Color(0xB3000000),
    inkTertiary: Color(0x8A000000),
    raised: Color(0xFFE1E3DE),
    raisedHi: Color(0xFFEAEDE7),
    inset: Color(0xFFC6C8C4),
    shadowDark: Color(0x2E000000),
    shadowLight: Color(0x42FFFFFF),
    glassFill: Color(0x40FFFFFF),
    glassFillStrong: Color(0x52FFFFFF),
    sheetA: Color(0xC6EDEFEA),
    sheetB: Color(0xAAD9DBD6),
    sheetC: Color(0xB8D2D4CF),
    warn: Color(0xFFA93B2C),
    edgeTop: 0.95,
    edgeBottom: 0.22,
  );

  static const dark = NeuTokens(
    isDark: true,
    ink: Color(0xF0FFFFFF),
    inkSecondary: Color(0xC8FFFFFF),
    inkTertiary: Color(0xA8FFFFFF),
    raised: Color(0xFF3F413F),
    raisedHi: Color(0xFF4A4C4A),
    inset: Color(0xFF202220),
    shadowDark: Color(0x99000000),
    shadowLight: Color(0x1AFFFFFF),
    glassFill: Color(0x1AFFFFFF),
    glassFillStrong: Color(0x26FFFFFF),
    sheetA: Color(0xE62A2C2A),
    sheetB: Color(0xD9242624),
    sheetC: Color(0xDF1F211F),
    warn: Color(0xFFFF9C86),
    edgeTop: 0.45,
    edgeBottom: 0.08,
  );
}

class NeuTheme extends InheritedWidget {
  const NeuTheme({super.key, required this.tokens, required super.child});

  final NeuTokens tokens;

  static NeuTokens of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<NeuTheme>()?.tokens ??
      NeuTokens.light;

  @override
  bool updateShouldNotify(NeuTheme oldWidget) => oldWidget.tokens != tokens;
}
