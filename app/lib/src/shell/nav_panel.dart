import 'package:flutter/material.dart';

import '../app_nav.dart';
import '../state/app_model.dart';
import '../ui/app_colors.dart';
import '../ui/neumorphic.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';
import 'app_scope.dart';
import 'window_chrome.dart';

/// Side panel: window chrome, account tile, the full destination list with
/// counts, and the theme switch pinned at the bottom.
class NavPanel extends StatefulWidget {
  const NavPanel({
    super.key,
    required this.navIndex,
    required this.onNavChanged,
    required this.onToggle,
    required this.badges,
    required this.themeChoice,
    required this.onThemeChanged,
    this.searchHint = '搜索项目与包',
    this.accountName = 'Akashic',
    this.accountDetail = '尚未连接数据服务',
  });

  final int navIndex;
  final ValueChanged<int> onNavChanged;
  final VoidCallback onToggle;
  final Map<String, int> badges;
  final int themeChoice;
  final ValueChanged<int> onThemeChanged;
  final String searchHint;
  final String accountName;
  final String accountDetail;

  @override
  State<NavPanel> createState() => _NavPanelState();
}

class _NavPanelState extends State<NavPanel> {
  final TextEditingController _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final model = AppScope.of(context);
    return SizedBox(
      width: AppSpacing.sidePanelWidth,
      child: DecoratedBox(
        decoration: BoxDecoration(color: Colors.black.withValues(alpha: 0.04)),
        child: Column(
          children: [
            WindowChrome(
              trailing: NeuButton(
                width: 30,
                height: 30,
                radius: 8,
                depth: 4,
                onTap: widget.onToggle,
                child: Icon(Icons.picture_in_picture_alt_outlined,
                    size: 16, color: t.inkSecondary),
              ),
            ),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20, 8, 20, 16),
                children: [
                  _AccountTile(
                    name: widget.accountName,
                    detail: widget.accountDetail,
                    github: model.github,
                    sync: model.sync,
                    onTap: model.openGitHubLogin,
                  ),
                  const SizedBox(height: 14),
                  NeuSearchField(
                    hint: widget.searchHint,
                    controller: _search,
                    onChanged: model.runSearch,
                  ),
                  if (model.search.isNotEmpty) ...[
                    const SizedBox(height: 10),
                    _SearchResults(
                      model: model,
                      onPicked: () {
                        _search.clear();
                        model.clearSearch();
                      },
                    ),
                  ],
                  const SizedBox(height: 18),
                  for (var i = 0; i < AppNav.entries.length; i++) ...[
                    if (AppNav.entries[i].section != null) ...[
                      if (i > 0) const NeuDivider(margin: EdgeInsets.symmetric(vertical: 8, horizontal: 6)),
                      _SectionLabel(AppNav.entries[i].section!),
                    ],
                    _NavItem(
                      icon: AppNav.entries[i].icon,
                      label: AppNav.entries[i].label,
                      count: widget.badges[AppNav.entries[i].id],
                      selected: widget.navIndex == i,
                      onTap: () => widget.onNavChanged(i),
                    ),
                  ],
                ],
              ),
            ),
            const NeuDivider(
                margin:
                    EdgeInsets.symmetric(vertical: 8, horizontal: 14)),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
              child: Row(
                children: [
                  Icon(Icons.contrast_rounded, size: 16, color: t.inkTertiary),
                  const SizedBox(width: 10),
                  NeuSegmented(
                    labels: const ['浅色', '暗色', '跟随'],
                    index: widget.themeChoice,
                    onChanged: widget.onThemeChanged,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _AccountTile extends StatelessWidget {
  const _AccountTile({
    required this.name,
    required this.detail,
    this.github = const {},
    this.sync = const {},
    this.onTap,
  });

  final String name;
  final String detail;

  /// Tapping the card goes to the GitHub login on the settings page.
  final VoidCallback? onTap;

  /// Who the GitHub CLI is signed in as, and when the config last went up. The card shows
  /// the account rather than the app because that identity is what private external packages
  /// and the sync gist both run on.
  final Map<String, dynamic> github;
  final Map<String, dynamic> sync;

  String get _title =>
      github['signedIn'] == true ? '${github['login']}' : name;

  String get _subline {
    // Before the first probe lands, the card still says what is on screen.
    if (github.isEmpty) return detail;
    if (github['found'] != true) return 'GitHub CLI 未安装';
    if (github['signedIn'] != true) return 'GitHub 未登录';
    if (github['hasGistScope'] != true) return 'GitHub · 令牌缺少 gist 权限';

    final when = _stamp(sync['uploadedAt']);
    return when.isEmpty ? 'GitHub · 尚未同步' : 'GitHub · 同步于 $when';
  }

  /// The sidecar writes a full ISO stamp; the card only has room for the minute.
  static String _stamp(Object? raw) {
    final parsed = DateTime.tryParse('$raw');
    if (parsed == null) return '';
    final local = parsed.toLocal();
    String two(int v) => v.toString().padLeft(2, '0');
    return '${two(local.month)}/${two(local.day)} ${two(local.hour)}:${two(local.minute)}';
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final signedIn = github['signedIn'] == true;
    final avatar = '${github['avatarUrl'] ?? ''}';

    final content = Padding(
      padding: const EdgeInsets.only(top: 6),
      child: Row(
        children: [
          Container(
            width: 48,
            height: 48,
            clipBehavior: Clip.antiAlias,
            decoration: BoxDecoration(
              color: const Color(0xFF232423),
              shape: BoxShape.circle,
              boxShadow: neuRaised(depth: 5, dark: t.shadowDark),
            ),
            child: signedIn && avatar.isNotEmpty
                ? Image.network(avatar,
                    width: 48,
                    height: 48,
                    fit: BoxFit.cover,
                    errorBuilder: (_, _, _) => const Icon(Icons.person_rounded,
                        color: Colors.white, size: 26))
                : const Icon(Icons.person_rounded,
                    color: Colors.white, size: 26),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _title,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: t.ink,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
                const SizedBox(height: 2),
                Text(
                  _subline,
                  style: TextStyle(fontSize: 12, color: t.inkSecondary),
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
        ],
      ),
    );

    return Tooltip(
      message: 'GitHub 登录与配置同步',
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: content,
        ),
      ),
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(left: 8, top: 6, bottom: 8),
      child: Text(
        text,
        style: TextStyle(
          fontSize: 12.5,
          color: t.inkTertiary,
          fontWeight: FontWeight.w500,
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    required this.icon,
    required this.label,
    required this.selected,
    required this.onTap,
    this.count,
  });

  final IconData icon;
  final String label;
  final int? count;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: NeuButton(
        selected: selected,
        radius: 7,
        depth: 5,
        color: t.glassFillStrong,
        insetColor: t.inset,
        edge: selected ? const GlassEdgeStyle(top: 0.95, bottom: 0.25) : null,
        darkShadow: const Color(0x2E000000),
        lightShadow: const Color(0x40FFFFFF),
        padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 10),
        onTap: onTap,
        child: Row(
          children: [
            Icon(icon, size: 19, color: selected ? t.ink : t.inkSecondary),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                label,
                style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                  color: t.ink,
                ),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            if (count != null)
              NeuBox(
                mode: NeuMode.inset,
                radius: 20,
                depth: 2.5,
                padding:
                    const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                child: Text(
                  '$count',
                  style: TextStyle(fontSize: 11, color: t.inkSecondary),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// What the panel's search box matches: registered projects by name or path, and
/// registry packages by name, display name, description or keyword.
class _SearchResults extends StatelessWidget {
  const _SearchResults({required this.model, required this.onPicked});

  final AppModel model;
  final VoidCallback onPicked;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    if (!model.hasSearchResults) {
      return Padding(
        padding: const EdgeInsets.fromLTRB(8, 2, 8, 6),
        child: Text(
          model.searchBusy ? '正在搜索…' : '没有匹配的项目或软件包。',
          style: TextStyle(fontSize: 12, color: t.inkTertiary),
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (model.searchProjects.isNotEmpty) ...[
          _ResultGroup('项目'),
          for (final project in model.searchProjects)
            _ResultRow(
              icon: Icons.account_tree_rounded,
              title: project.name,
              detail: project.path,
              onTap: () {
                onPicked();
                model.openSearchProject(project);
              },
            ),
        ],
        if (model.searchPackages.isNotEmpty) ...[
          _ResultGroup('软件包'),
          for (final package in model.searchPackages)
            _ResultRow(
              icon: Icons.inventory_2_rounded,
              title: '${package['displayName']}'.isEmpty
                  ? '${package['name']}'
                  : '${package['displayName']}',
              detail: '${package['name']} · ${package['version']}',
              onTap: () {
                onPicked();
                model.openSearchPackage('${package['name']}');
              },
            ),
        ],
      ],
    );
  }
}

class _ResultGroup extends StatelessWidget {
  const _ResultGroup(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 4),
      child: Text(text,
          style: TextStyle(
              fontSize: 11.5,
              fontWeight: FontWeight.w600,
              color: t.inkTertiary)),
    );
  }
}

class _ResultRow extends StatelessWidget {
  const _ResultRow({
    required this.icon,
    required this.title,
    required this.detail,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String detail;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2.5),
      child: NeuTileButton(
        padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 8),
        onTap: onTap,
        child: Row(
          children: [
            Icon(icon, size: 16, color: t.inkSecondary),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title,
                      style: const TextStyle(
                          fontSize: 12.5, fontWeight: FontWeight.w600),
                      overflow: TextOverflow.ellipsis),
                  Text(detail,
                      style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
                      overflow: TextOverflow.ellipsis),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
