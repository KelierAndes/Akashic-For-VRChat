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
