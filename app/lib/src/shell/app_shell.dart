import 'package:flutter/material.dart';

import '../app_nav.dart';
import '../pages/about_page.dart';
import '../pages/assetstore_page.dart';
import '../pages/import_page.dart';
import '../pages/packages_page.dart';
import '../pages/project_detail_page.dart';
import '../pages/booth_page.dart';
import '../pages/projects_page.dart';
import '../pages/resources_page.dart';
import '../pages/restore_page.dart';
import '../pages/settings_page.dart';
import '../state/app_model.dart';
import '../pages/stub_page.dart';
import '../pages/templates_page.dart';
import '../pages/vrchat_page.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';
import 'app_scope.dart';
import 'left_rail.dart';
import 'nav_panel.dart';

/// Three pieces resting on the desktop: the icon rail, its settings card, and the
/// glass sheet that holds the nav panel plus the recessed page tray.
class AppShell extends StatefulWidget {
  const AppShell({super.key});

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _navIndex = 0;
  bool _sideVisible = true;

  Widget _page(String id, AppModel model) {
    switch (id) {
      case 'projects':
        final open = model.openProjectPath;
        return open == null
            ? const ProjectsPage()
            : ProjectDetailPage(key: ValueKey(open), projectPath: open);
      case 'templates':
        return const TemplatesPage();
      case 'settings':
        return const SettingsPage();
      case 'about':
        return const AboutPage();
      case 'booth':
        return const BoothPage();
      case 'assetstore':
        return const AssetStorePage();
      case 'vrchatsync':
        return const VRChatSyncPage();
      case 'resources':
        return const ResourcesPage();
      case 'restore':
        return const RestorePage();
      case 'packages':
        return const PackagesPage();
      case 'import':
        return const ImportPage();
      default:
        return StubPage(entry: AppNav.entries.firstWhere((e) => e.id == id));
    }
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final state = model.state;
    // Pages can ask the shell to change destination; the shell owns the index.
    model.navJump = (id) => setState(() {
          final index = AppNav.indexOf(id);
          if (index >= 0) _navIndex = index;
        });
    model.navSelect = (i) => setState(() => _navIndex = i);
    final badges = model.badges;
    final accountDetail = state == null
        ? (model.fatal == null ? '正在连接本地数据服务…' : '本地数据服务未连接')
        : '${state.projectCount} 个项目 · ${state.editors.length} 个 Unity';

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(10),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              LeftRail(
                model: model,
                index: _navIndex,
              ),
              // Same seam as the gaps between the rail's own pieces.
              const SizedBox(width: 10),
              Expanded(
                child: GlassSheet(
                  opacity: model.sheetOpacity,
                  child: Padding(
                    padding: const EdgeInsets.all(1.5),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (_sideVisible)
                          NavPanel(
                            navIndex: _navIndex,
                            onNavChanged: (i) => setState(() => _navIndex = i),
                            onToggle: () =>
                                setState(() => _sideVisible = false),
                            badges: badges,
                            accountDetail: accountDetail,
                            themeChoice: model.themeChoice,
                            onThemeChanged: model.chooseTheme,
                          ),
                        Expanded(
                          child: Padding(
                            padding: const EdgeInsets.fromLTRB(10, 14, 14, 14),
                            child: NeuBox(
                              // Page tray: recessed, thin uniform rim.
                              mode: NeuMode.inset,
                              radius: 12,
                              depth: 4,
                              color: Colors.black.withValues(alpha: 0.045),
                              border: Border.all(
                                  color: Colors.black.withValues(alpha: 0.09)),
                              darkShadow: const Color(0x26000000),
                              lightShadow: const Color(0x4DFFFFFF),
                              child: Stack(
                                children: [
                                  Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.stretch,
                                    children: [
                                      if (!_sideVisible)
                                        Padding(
                                          padding: const EdgeInsets.fromLTRB(
                                              28, 14, 28, 0),
                                          child: Row(
                                            children: [
                                              NeuButton(
                                                width: 32,
                                                height: 32,
                                                radius: 10,
                                                depth: 4,
                                                onTap: () => setState(() =>
                                                    _sideVisible = true),
                                                child: Icon(
                                                    Icons
                                                        .format_list_bulleted_rounded,
                                                    size: 17,
                                                    color:
                                                        NeuTheme.of(context)
                                                            .inkSecondary),
                                              ),
                                              const Spacer(),
                                            ],
                                          ),
                                        ),
                                      Expanded(
                                          child: _page(AppNav.entries[_navIndex].id,
                                              model)),
                                    ],
                                  ),
                                  if (model.modal != null)
                                    Positioned.fill(
                                      child: GestureDetector(
                                        behavior: HitTestBehavior.opaque,
                                        onTap: model.closeModal,
                                        child: ColoredBox(
                                          color: Colors.black
                                              .withValues(alpha: 0.32),
                                          child: Center(
                                            child: ConstrainedBox(
                                              constraints: const BoxConstraints(
                                                  maxWidth: 520,
                                                  maxHeight: 620),
                                              child: Padding(
                                                padding:
                                                    const EdgeInsets.all(22),
                                                child: model.modal,
                                              ),
                                            ),
                                          ),
                                        ),
                                      ),
                                    ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
