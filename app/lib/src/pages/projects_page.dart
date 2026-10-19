import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../host/host_models.dart';
import '../pages/templates_page.dart';
import '../shell/app_scope.dart';
import '../state/app_model.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

class ProjectsPage extends StatelessWidget {
  const ProjectsPage({super.key});

  static IconData typeIcon(String display) => switch (display) {
        '世界' => Icons.public_rounded,
        '虚拟形象' => Icons.accessibility_new_rounded,
        'VRChat' => Icons.sports_esports_rounded,
        _ => Icons.extension_rounded,
      };

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return NeuPageBody(
      title: '项目',
      subtitle: model.listSummary,
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          NeuActionButton(
            label: '扫描目录',
            icon: Icons.folder_special_rounded,
            onTap: () async {
              final directory = await FilePicker.getDirectoryPath(
                  dialogTitle: '选择要扫描的文件夹');
              if (directory != null) await model.detectProjects(directory);
            },
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '新建项目',
            icon: Icons.add_rounded,
            onTap: () async {
              await model.loadTemplates();
              model.showModal(const NewProjectForm());
            },
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '添加项目',
            icon: Icons.create_new_folder_rounded,
            emphasised: true,
            onTap: () async {
              final directory = await FilePicker.getDirectoryPath(
                  dialogTitle: '选择 Unity 工程文件夹');
              if (directory != null) await model.addProject(directory);
            },
          ),
        ],
      ),
      content: _ProjectGrid(model: model),
      fixed: [
        Row(
          children: [
            NeuDropdown(
              value: model.category,
              items: AppModel.categories,
              icon: Icons.category_rounded,
              onChanged: (value) => model.applyFilters(category: value),
            ),
            const SizedBox(width: 10),
            NeuDropdown(
              value: model.cloudFilter,
              items: AppModel.cloudStates,
              icon: Icons.cloud_rounded,
              onChanged: (value) => model.applyFilters(cloudFilter: value),
            ),
            const SizedBox(width: 10),
            NeuDropdown(
              value: model.sortBy,
              items: AppModel.sortModes,
              icon: Icons.sort_rounded,
              onChanged: (value) => model.applyFilters(sortBy: value),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                model.cloudMatching
                    ? '云端匹配中 ${model.cloudMatched} / ${model.cloudTotal} · ${model.status}'
                    : model.status,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12, color: t.inkTertiary),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _ProjectGrid extends StatelessWidget {
  const _ProjectGrid({required this.model});

  final AppModel model;

  @override
  Widget build(BuildContext context) {
    if (model.loading) {
      return const NeuEmptyState(expand: true, text: '正在连接本地数据服务…');
    }

    final fatal = model.fatal;
    if (fatal != null) return NeuEmptyState(expand: true, text: fatal);

    if (model.projects.isEmpty) {
      return const NeuEmptyState(
        expand: true,
        text: '还没有登记任何 Unity 工程。\n'
            '用右上角「添加项目」选择现有工程，或「扫描目录」批量登记。',
      );
    }

    if (model.visible.isEmpty) {
      return Column(
        children: [
          const NeuEmptyState(expand: true, text: '当前筛选条件下没有匹配的工程。'),
          const SizedBox(height: 16),
          NeuActionButton(
            label: '重置筛选',
            icon: Icons.filter_alt_off_rounded,
            onTap: model.resetFilters,
          ),
        ],
      );
    }

    return SingleChildScrollView(
      child: LayoutBuilder(
        builder: (context, constraints) => Wrap(
          spacing: 14,
          runSpacing: 14,
          children: [
            for (final project in model.visible)
              SizedBox(
                width: (constraints.maxWidth - 14) / 2,
                child: _ProjectTile(project: project, model: model),
              ),
          ],
        ),
      ),
    );
  }
}

class _ProjectTile extends StatelessWidget {
  const _ProjectTile({required this.project, required this.model});

  final UnityProject project;
  final AppModel model;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return NeuTileButton(
      padding: const EdgeInsets.all(18),
      onTap: () => model.openProject(project.path),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  color: t.glassFillStrong,
                  borderRadius: BorderRadius.circular(12),
                  border: glassEdgeBorderFallback(),
                ),
                child: Icon(ProjectsPage.typeIcon(project.projectTypeDisplayName),
                    size: 20, color: t.inkSecondary),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(project.name,
                        style: const TextStyle(
                            fontSize: 14, fontWeight: FontWeight.w600),
                        overflow: TextOverflow.ellipsis),
                    const SizedBox(height: 2),
                    Text(project.path,
                        style:
                            TextStyle(fontSize: 11.5, color: t.inkTertiary),
                        overflow: TextOverflow.ellipsis),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          _PillRow([
            NeuPill(project.projectTypeDisplayName),
            NeuPill('${project.installedPackages.length} 个包'),
            project.hasCloudLink
                ? NeuPill(project.cloudLinkLabel,
                    bg: const Color(0xFFBFE3C9),
                    fg: const Color(0xFF237A45),
                    tooltip: project.cloudLinkTip,
                    onTap: () =>
                        model.jumpToVrchatContent(project.cloudBlueprintIds))
                : const NeuPill('未上传'),
          ]),
          const SizedBox(height: 8),
          _PillRow([
            NeuPill(project.unityVersion),
            ...hostingPills(model, project.path),
            _backupPill(model, project.path),
          ]),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: Text(project.lastModifiedDisplay,
                    style:
                        TextStyle(fontSize: 11.5, color: t.inkTertiary),
                    overflow: TextOverflow.ellipsis),
              ),
              _MiniButton(
                icon: Icons.play_arrow_rounded,
                tooltip: '启动 Unity',
                onTap: () => model.launch(project),
              ),
              const SizedBox(width: 8),
              _MiniButton(
                icon: Icons.cloud_upload_outlined,
                tooltip: '备份工程',
                onTap: () => model.backupProject(project),
              ),
              const SizedBox(width: 8),
              _MiniButton(
                icon: Icons.delete_outline_rounded,
                tooltip: '从列表移除',
                onTap: () => model.removeProject(project),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

List<Widget> hostingPills(AppModel model, String path) {
  final kind = model.hostingOf(path);
  if (kind.isEmpty) return const [];

  final (label, bg, fg) = switch (kind) {
    'git' => ('Git 同步', const Color(0xFFC7DCF3), const Color(0xFF1F5A8A)),
    'uvcs' => ('UVCS 同步', const Color(0xFFE0D3F2), const Color(0xFF553980)),
    _ => ('Git + UVCS', const Color(0xFFD6E4E2), const Color(0xFF2C5A55)),
  };

  return [NeuPill(label, bg: bg, fg: fg)];
}

Widget _backupPill(AppModel model, String path) {
  final count = model.backupCounts[path] ?? 0;
  return NeuPill(
    count == 0 ? '无备份' : '本地备份 $count',
    bg: count == 0 ? null : const Color(0xFFD8E8CF),
    fg: count == 0 ? null : const Color(0xFF41692F),
  );
}

class _PillRow extends StatelessWidget {
  const _PillRow(this.pills);

  final List<Widget> pills;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          for (var i = 0; i < pills.length; i++) ...[
            if (i > 0) const SizedBox(width: 8),
            Flexible(child: pills[i]),
          ],
        ],
      );
}

class _MiniButton extends StatelessWidget {
  const _MiniButton({
    required this.icon,
    required this.tooltip,
    required this.onTap,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Tooltip(
      message: tooltip,
      child: NeuButton(
        width: 32,
        height: 32,
        radius: 9,
        depth: 4,
        color: t.raised,
        insetColor: t.inset,
        border: glassBorder(alpha: 0.45),
        onTap: onTap,
        child: Icon(icon, size: 17, color: t.inkSecondary),
      ),
    );
  }
}
