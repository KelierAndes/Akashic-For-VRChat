import 'dart:async';

import 'package:flutter/material.dart';
import 'package:window_manager/window_manager.dart';

import 'src/shell/app_scope.dart';
import 'src/shell/app_shell.dart';
import 'src/state/app_model.dart';
import 'src/ui/theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await windowManager.ensureInitialized();

  const windowOptions = WindowOptions(
    size: Size(1360, 860),
    minimumSize: Size(1080, 680),
    center: true,
    title: 'Akashic for VRChat & Unity',
    backgroundColor: Colors.transparent,
    titleBarStyle: TitleBarStyle.hidden,
  );

  // The canvas is fully transparent so each piece casts its own shadow onto the
  // desktop; the runner needs the DWM "sheet of glass" margin for that to
  // actually be per-pixel alpha (see windows/runner/win32_window.cpp).
  unawaited(
    windowManager.waitUntilReadyToShow(windowOptions, () async {
      await windowManager.setBackgroundColor(Colors.transparent);
      await windowManager.setAsFrameless();
      await windowManager.show();
      await windowManager.focus();
    }),
  );

  runApp(const AkashicApp());
}

class AkashicApp extends StatefulWidget {
  const AkashicApp({super.key});

  @override
  State<AkashicApp> createState() => _AkashicAppState();
}

class _AkashicAppState extends State<AkashicApp> {
  final AppModel _model = AppModel();

  @override
  void initState() {
    super.initState();
    // The window is already up; the shell shows "connecting" until the sidecar
    // answers or refuses.
    unawaited(_model.connect());
  }

  @override
  void dispose() {
    _model.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Akashic for VRChat & Unity',
      home: AppScope(model: _model, child: const AppRoot()),
    );
  }
}

/// Owns the light / dark / system choice and hands the matching token set to the
/// widget tree. The choice comes from settings.json through the sidecar.
class AppRoot extends StatelessWidget {
  const AppRoot({super.key});

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final platformDark =
        MediaQuery.platformBrightnessOf(context) == Brightness.dark;
    final tokens = model.themeChoice == 1
        ? NeuTokens.dark
        : model.themeChoice == 2
            ? (platformDark ? NeuTokens.dark : NeuTokens.light)
            : NeuTokens.light;

    // Segoe UI has no Chinese glyphs, so without this list the CJK runs drop to Skia's
    // generic substitute — which is what made the whole app look thin and misaligned.
    const fallback = [
      'Microsoft YaHei UI',
      'Microsoft YaHei',
      'PingFang SC',
      'Noto Sans CJK SC',
    ];

    final base = ThemeData(
      useMaterial3: true,
      brightness: tokens.isDark ? Brightness.dark : Brightness.light,
      scaffoldBackgroundColor: Colors.transparent,
      fontFamily: 'Segoe UI',
      fontFamilyFallback: fallback,
      textSelectionTheme:
          const TextSelectionThemeData(cursorColor: Color(0xFF2E7D46)),
    );

    // Nearly every label in the app sets a size but no weight, so it inherits these
    // three. w500 is where 11-13 px Chinese stops looking shaved; the places that want
    // more already ask for w600/w700 themselves.
    final text = base.textTheme;

    return Theme(
      data: base.copyWith(
        textTheme: text.copyWith(
          bodyLarge: text.bodyLarge?.copyWith(fontWeight: FontWeight.w500),
          bodyMedium: text.bodyMedium?.copyWith(fontWeight: FontWeight.w500),
          bodySmall: text.bodySmall?.copyWith(fontWeight: FontWeight.w500),
        ),
      ),
      child: NeuTheme(
        tokens: tokens,
        child: const AppShell(),
      ),
    );
  }
}
