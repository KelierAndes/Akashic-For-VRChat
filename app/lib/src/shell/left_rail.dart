import 'package:flutter/material.dart';

import '../app_nav.dart';
import '../state/app_model.dart';
import '../ui/app_colors.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';
import 'app_scope.dart';

class LeftRail extends StatelessWidget {
  const LeftRail({super.key, required this.model, required this.index});

  final AppModel model;
  final int index;

  static const double _tileSize = 36;
  static const double _tileGap = 5;

  static const double _trayGap = 10;

  BoxDecoration _baseStrip(NeuTokens t) {
    final a = model.sheetOpacity;
    return BoxDecoration(
      borderRadius: BorderRadius.circular(16),
      gradient: LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [
          t.sheetA.withValues(alpha: a),
          t.sheetB.withValues(alpha: a),
          t.sheetC.withValues(alpha: a),
        ],
        stops: const [0.0, 0.55, 1.0],
      ),
      border: Border.all(
          color: Colors.white.withValues(alpha: (t.isDark ? 0.16 : 0.35) * a)),
    );
  }

  bool _isSelected(String raw) {
    if (AppNav.isProjectTile(raw)) {
      return index == AppNav.indexOf('projects') &&
          model.openProjectPath == AppNav.tileValue(raw);
    }
    return index == AppNav.indexOf(AppNav.tileValue(raw));
  }

  String _labelOf(String raw) {
    if (!AppNav.isProjectTile(raw)) {
      final entry = AppNav.entries
          .where((e) => e.id == AppNav.tileValue(raw))
          .toList();
      return entry.isEmpty ? raw : entry.first.label;
    }
    final path = AppNav.tileValue(raw);
    final match = model.projects.where((p) => p.path == path).toList();
    return match.isEmpty ? '已移除的工程' : match.first.name;
  }

  static String initialsOf(String name) {
    final raw = name.trim();
    if (raw.isEmpty) return '?';
    if (raw.codeUnitAt(0) >= 0x2E80) return raw.characters.first;

    final cleaned = raw.replaceAll(RegExp(r'[^A-Za-z0-9]'), '');
    if (cleaned.isEmpty) return raw.characters.first;
    if (cleaned.length == 1) return cleaned.toUpperCase();
    return cleaned[0].toUpperCase() + cleaned[1].toLowerCase();
  }

  Widget _tile(BuildContext context, String raw) {
    final isProject = AppNav.isProjectTile(raw);
    final selected = _isSelected(raw);
    final label = _labelOf(raw);
    final editing = model.railEditing;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: _tileGap / 2),
      child: Tooltip(
        message: editing ? '从快捷栏移除：$label' : label,
        child: NeuButton(
          selected: selected,
          width: _tileSize,
          height: _tileSize,
          radius: 9,
          depth: 4,
          color: editing ? const Color(0xFF4A2326) : AppColors.railTile,
          insetColor: AppColors.railTileInset,
          darkShadow: const Color(0x40000000),
          lightShadow: const Color(0x14FFFFFF),
          onTap: () => editing
              ? model.removeRailTile(raw)
              : _activate(context, raw),
          child: Stack(
            alignment: Alignment.center,
            children: [
              if (isProject)
                Text(
                  initialsOf(label),
                  style: TextStyle(
                    fontSize: label.characters.first.codeUnitAt(0) >= 0x2E80
                        ? 15
                        : 13.5,
                    height: 1,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.2,
                    color: Colors.white.withValues(alpha: selected ? 1 : 0.85),
                  ),
                )
              else
                Icon(
                  AppNav.entries
                      .firstWhere((e) => e.id == AppNav.tileValue(raw),
                          orElse: () => AppNav.entries.first)
                      .icon,
                  size: 17,
                  color:
                      Colors.white.withValues(alpha: selected ? 1 : 0.8),
                ),
              if ((model.badges[AppNav.tileValue(raw)] ?? 0) > 0 &&
                  !isProject)
                Positioned(
                  top: 2,
                  right: 2,
                  child: Container(
                    width: 7,
                    height: 7,
                    decoration: BoxDecoration(
                      color: AppColors.greenFg,
                      shape: BoxShape.circle,
                      border: Border.all(color: AppColors.rail, width: 1.5),
                    ),
                  ),
                ),
              if (editing)
                Positioned(
                  top: -1,
                  right: -1,
                  child: Icon(Icons.cancel_rounded,
                      size: 14, color: Colors.white.withValues(alpha: 0.9)),
                ),
            ],
          ),
        ),
      ),
    );
  }

  void _activate(BuildContext context, String raw) {
    if (AppNav.isProjectTile(raw)) {
      final path = AppNav.tileValue(raw);
      if (!model.projects.any((p) => p.path == path)) {
        model.setStatus('这个工程已经不在列表里了，可以从快捷栏移除。');
        return;
      }
      model.openProject(path);
      model.jumpTo('projects');
      return;
    }
    final target = AppNav.indexOf(AppNav.tileValue(raw));
    if (target >= 0) model.selectNav(target);
  }

  Widget _miniTile(
      BuildContext context, IconData icon, String tooltip, VoidCallback onTap,
      {bool active = false}) {
    return Padding(
      padding: const EdgeInsets.only(top: _tileGap),
      child: Tooltip(
        message: tooltip,
        child: NeuButton(
          selected: active,
          width: _tileSize,
          height: _tileSize - 8,
          radius: 8,
          depth: 3.5,
          color: AppColors.railTile,
          insetColor: AppColors.railTileInset,
          darkShadow: const Color(0x40000000),
          lightShadow: const Color(0x14FFFFFF),
          onTap: onTap,
          child: Icon(icon, size: 15, color: Colors.white70),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final tiles = model.railTiles;

    return SizedBox(
      width: AppSpacing.railWidth,
      child: LayoutBuilder(builder: (context, constraints) {
        final rows = tiles.length.clamp(1, AppModel.maxRailTiles);
        final wanted = rows * (_tileSize + _tileGap);

        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.only(bottom: _trayGap),
              child: GlassEdgeOverlay(
                radius: 16,
                style: const GlassEdgeStyle(top: 0.8, bottom: 0.15),
                child: Container(
                  decoration: _baseStrip(t),
                  child: Padding(
                    padding: const EdgeInsets.all(8),
                    child: Container(
                      height: 46,
                      decoration: BoxDecoration(
                        color: AppColors.rail,
                        borderRadius: BorderRadius.circular(12),
                        boxShadow: const [
                          BoxShadow(
                            color: Color(0x4D000000),
                            blurRadius: 12,
                            offset: Offset(2, 5),
                          ),
                        ],
                      ),
                      clipBehavior: Clip.antiAlias,
                      child: Image.asset('assets/brand/akashic-light.png',
                          fit: BoxFit.cover),
                    ),
                  ),
                ),
              ),
            ),
            ConstrainedBox(
              constraints: BoxConstraints(
                maxHeight: (constraints.maxHeight - 242).clamp(180.0, double.infinity),
              ),
              child: GlassEdgeOverlay(
                radius: 16,
                style: const GlassEdgeStyle(top: 0.8, bottom: 0.15),
                child: Container(
                  decoration: _baseStrip(t),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
                    child: Container(
                      decoration: BoxDecoration(
                        color: AppColors.rail,
                        borderRadius: BorderRadius.circular(12),
                        boxShadow: const [
                          BoxShadow(
                            color: Color(0x4D000000),
                            blurRadius: 12,
                            offset: Offset(2, 5),
                          ),
                        ],
                      ),
                      padding: const EdgeInsets.symmetric(vertical: 9),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Flexible(
                            child: ConstrainedBox(
                              constraints: BoxConstraints(maxHeight: wanted),
                              child: SingleChildScrollView(
                                padding:
                                    const EdgeInsets.symmetric(horizontal: 9),
                                child: Column(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    for (final raw in tiles)
                                      _tile(context, raw),
                                  ],
                                ),
                              ),
                            ),
                          ),
                          Padding(
                            padding: const EdgeInsets.fromLTRB(9, 0, 9, 2),
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                _miniTile(
                                    context,
                                    model.railEditing
                                        ? Icons.check_rounded
                                        : Icons.edit_rounded,
                                    model.railEditing ? '完成编辑' : '编辑快捷栏',
                                    () => model
                                        .setRailEditing(!model.railEditing),
                                    active: model.railEditing),
                                if (!model.railEditing)
                                  _miniTile(
                                      context,
                                      Icons.add_rounded,
                                      '添加到此栏',
                                      () => model
                                          .showModal(const RailPickerForm())),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
            const SizedBox(height: _trayGap),
            Expanded(
              child: GlassEdgeOverlay(
                radius: 16,
                style: const GlassEdgeStyle(top: 0.8, bottom: 0.15),
                child: Container(
                  decoration: _baseStrip(t),
                  child: Column(
                    children: [
                      Expanded(
                        child: Center(
                          child: RotatedBox(
                            quarterTurns: 3,
                            child: Text(
                              'AKASHIC  FOR  VRCHAT',
                              maxLines: 1,
                              softWrap: false,
                              overflow: TextOverflow.clip,
                              style: TextStyle(
                                color: t.inkTertiary,
                                fontSize: 10,
                                letterSpacing: 2.5,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        ),
                      ),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: Tooltip(
                          message: '设置',
                          child: NeuButton(
                            selected: index == AppNav.settingsIndex,
                            width: 40,
                            height: 40,
                            radius: 10,
                            depth: 5,
                            color: AppColors.railTile,
                            insetColor: AppColors.railTileInset,
                            darkShadow: const Color(0x4D000000),
                            lightShadow: const Color(0x1FFFFFFF),
                            onTap: () => model.selectNav(AppNav.settingsIndex),
                            child: Stack(
                              clipBehavior: Clip.none,
                              alignment: Alignment.center,
                              children: [
                                const Icon(Icons.settings_rounded,
                                    size: 18, color: Colors.white),
                                if (model.updateAvailable)
                                  Positioned(
                                    right: -3,
                                    top: -3,
                                    child: Container(
                                      width: 9,
                                      height: 9,
                                      decoration: BoxDecoration(
                                        color: const Color(0xFF5BD68B),
                                        shape: BoxShape.circle,
                                        border: Border.all(
                                            color: AppColors.railTile, width: 1.5),
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
        );
      }),
    );
  }
}

class RailPickerForm extends StatelessWidget {
  const RailPickerForm({super.key});

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);
    final pinned = model.railTiles;
    final full = pinned.length >= AppModel.maxRailTiles;

    final pages = AppNav.entries
        .where((e) =>
            e.id != 'settings' &&
            !pinned.contains(AppNav.navTile(e.id)))
        .toList();
    final projects = model.projects
        .where((p) => !pinned.contains(AppNav.projectTile(p.path)))
        .toList();

    return NeuModalScaffold(
      title: '快捷栏',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text('已钉选 ${pinned.length} / ${AppModel.maxRailTiles}',
                    style: TextStyle(
                        fontSize: 12.5,
                        fontWeight: FontWeight.w600,
                        color: full ? AppColors.greenFg : t.inkSecondary)),
              ),
              if (full)
                Text('已达上限，先移除一个',
                    style: TextStyle(fontSize: 11.5, color: t.inkTertiary)),
            ],
          ),
          const SizedBox(height: 12),
          Text('页面', style: TextStyle(fontSize: 12, color: t.inkTertiary)),
          const SizedBox(height: 6),
          if (pages.isEmpty)
            _None('标签页都已经在这条栏上了。'),
          for (final entry in pages)
            _PickRow(
              icon: entry.icon,
              label: entry.label,
              detail: '标签页',
              enabled: !full,
              onTap: () => model.toggleRailTile(AppNav.navTile(entry.id)),
            ),
          const SizedBox(height: 14),
          Text('项目', style: TextStyle(fontSize: 12, color: t.inkTertiary)),
          const SizedBox(height: 6),
          if (projects.isEmpty)
            _None(model.projects.isEmpty ? '还没有登记任何工程。' : '工程都已经钉上了。'),
          for (final project in projects)
            _PickRow(
              icon: Icons.account_tree_rounded,
              label: project.name,
              detail: project.path,
              enabled: !full,
              onTap: () =>
                  model.toggleRailTile(AppNav.projectTile(project.path)),
            ),
          const SizedBox(height: 10),
          NeuDialogActions(
            confirmLabel: '完成',
            onCancel: model.closeModal,
            onConfirm: model.closeModal,
          ),
        ],
      ),
    );
  }
}

class _None extends StatelessWidget {
  const _None(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Text(text,
          style: TextStyle(fontSize: 12.5, color: t.inkSecondary)),
    );
  }
}

class _PickRow extends StatelessWidget {
  const _PickRow({
    required this.icon,
    required this.label,
    required this.detail,
    required this.enabled,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final String detail;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Opacity(
        opacity: enabled ? 1 : 0.45,
        child: NeuTileButton(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
          onTap: enabled ? onTap : null,
          child: Row(
            children: [
              Icon(icon, size: 17, color: t.inkSecondary),
              const SizedBox(width: 11),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label,
                        style: const TextStyle(
                            fontSize: 13, fontWeight: FontWeight.w600),
                        overflow: TextOverflow.ellipsis),
                    Text(detail,
                        style: TextStyle(fontSize: 11, color: t.inkTertiary),
                        overflow: TextOverflow.ellipsis),
                  ],
                ),
              ),
              Icon(Icons.add_rounded, size: 16, color: t.inkTertiary),
            ],
          ),
        ),
      ),
    );
  }
}
