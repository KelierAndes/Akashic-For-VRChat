import 'package:flutter/material.dart';

import '../host/host_models.dart';
import '../pages/project_picker.dart';
import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';

/// One project's package table: registry packages, whatever else is installed, and
/// the local .unitypackage shelf matched against this project's import ledger.
class ProjectDetailPage extends StatefulWidget {
  const ProjectDetailPage({super.key, required this.projectPath});

  final String projectPath;

  @override
  State<ProjectDetailPage> createState() => _ProjectDetailPageState();
}

class _Row {
  _Row({
    required this.id,
    required this.name,
    required this.group,
    required this.installed,
    required this.available,
    required this.origin,
    required this.filePath,
  });

  final String id;
  final String name;
  final String group;
  final String installed;
  final String available;
  final String origin;
  final String filePath;

  bool get isInstalled => installed != '无';
  bool get isUnityPackage => group == '.unitypackage';
  bool get isRegistry => origin == '注册表';
  bool get upgradable => isInstalled && available != installed;
}

class _ProjectDetailPageState extends State<ProjectDetailPage> {
  List<_Row> _rows = [];
  final Set<String> _selected = {};
  String _filter = '全部';
  String _status = '';
  bool _busy = false;

  static const _filters = ['全部', '已安装', '可升级', '未安装'];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    setState(() {
      _busy = true;
      _status = '正在读取…';
    });

    try {
      await model.refreshProjects();
      final project = model.projects.firstWhere((p) => p.path == widget.projectPath);
      final registry = (await host.packages()).cast<Map<String, dynamic>>();
      final library = (await host.libraryFiles()).cast<Map<String, dynamic>>();
      final records =
          (await host.installRecords(project.path)).cast<Map<String, dynamic>>();

      final installedByName = <String, InstalledPackage>{
        for (final p in project.installedPackages) p.name.toLowerCase(): p
      };
      final recordByPath = {
        for (final r in records) '${r['filePath']}': r
      };

      final rows = <_Row>[];
      final registryNames = <String>{};

      for (final package in registry) {
        final name = '${package['name']}';
        registryNames.add(name.toLowerCase());
        rows.add(_Row(
          id: name,
          name: '${package['displayName']}'.isEmpty ? name : '${package['displayName']}',
          group: 'VPM 包',
          installed: installedByName[name.toLowerCase()]?.version ?? '无',
          available: '${package['version']}',
          origin: '注册表',
          filePath: '',
        ));
      }

      for (final entry in installedByName.values) {
        if (registryNames.contains(entry.name.toLowerCase())) continue;
        rows.add(_Row(
          id: entry.name,
          name: entry.displayName.isEmpty ? entry.name : entry.displayName,
          group: switch (entry.source) {
            'Local' => '本地包',
            'Git' => 'Git 仓库',
            _ => '外部',
          },
          installed: entry.version,
          available: entry.version,
          origin: entry.source,
          filePath: '',
        ));
      }

      for (final file in library) {
        final path = '${file['filePath']}';
        rows.add(_Row(
          id: 'unitypackage:$path',
          name: '${file['displayName']}'.isEmpty ? path : '${file['displayName']}',
          group: '.unitypackage',
          installed: recordByPath[path]?['version'] ?? '无',
          available: '${file['version']}',
          origin: originLabel('${file['origin']}'),
          filePath: path,
        ));
      }

      _rows = rows;
      _status = '共 ${rows.length} 项 · ${installedByName.length} 项已安装 · '
          '${recordByPath.length} 个 .unitypackage 台账';
    } catch (error) {
      _status = '读取失败：$error';
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  List<_Row> get _shown => _rows.where((row) {
        switch (_filter) {
          case '已安装':
            return row.isInstalled;
          case '可升级':
            return row.upgradable;
          case '未安装':
            return !row.isInstalled;
          default:
            return true;
        }
      }).toList()
        ..sort((a, b) {
          final byInstalled =
              (b.isInstalled ? 1 : 0).compareTo(a.isInstalled ? 1 : 0);
          if (byInstalled != 0) return byInstalled;
          return a.name.toLowerCase().compareTo(b.name.toLowerCase());
        });

  Future<void> _installSelected() async {
    AppScope.of(context).showModal(
        ProjectPickerForm(title: '安装到工程', onPick: _installInto));
  }

  Future<void> _installInto(String projectPath) async {
    final host = AppScope.of(context).host;
    if (host == null || _selected.isEmpty) return;

    setState(() => _busy = true);
    final picked = _rows.where((r) => _selected.contains(r.id)).toList();

    for (var i = 0; i < picked.length; i++) {
      final row = picked[i];
      setState(() => _status = '正在处理 ${i + 1} / ${picked.length}：${row.name}');

      try {
        if (row.isUnityPackage) {
          await host.installPackageFile(projectPath, row.filePath, null);
        } else if (row.isRegistry) {
          await host.installPackage(projectPath, row.id);
        } else {
          setState(() {
            _busy = false;
            _status = '${row.name} 不是可安装的来源，请从资源页安装。';
          });
          return;
        }
      } catch (error) {
        setState(() {
          _busy = false;
          _status = '${row.name} 失败：$error';
        });
        return;
      }
    }

    _selected.clear();
    if (!mounted) return;
    setState(() => _busy = false);
    await _load();
  }

  Future<void> _removeSelected() async {
    final host = AppScope.of(context).host;
    if (host == null || _selected.isEmpty) return;

    setState(() => _busy = true);
    final picked = _rows.where((r) => _selected.contains(r.id)).toList();

    for (final row in picked) {
      try {
        if (row.isUnityPackage) {
          final records = (await host.installRecords(widget.projectPath))
              .cast<Map<String, dynamic>>();
          final hit = records
              .where((r) => '${r['filePath']}' == row.filePath)
              .toList();
          if (hit.isEmpty) {
            _status = '${row.name} 没有导入台账，无法精确撤销。';
            continue;
          }
          await host.uninstallPackageFile(
              widget.projectPath, '${hit.first['packageId']}');
        } else {
          await host.removePackage(widget.projectPath, row.id);
        }
      } catch (error) {
        setState(() {
          _busy = false;
          _status = '${row.name} 移除失败：$error';
        });
        return;
      }
    }

    _selected.clear();
    if (!mounted) return;
    setState(() => _busy = false);
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);
    final matches =
        model.projects.where((p) => p.path == widget.projectPath).toList();
    final current = matches.isEmpty ? null : matches.first;
    final shown = _shown;

    if (current == null) {
      return NeuPageBody(
        title: '项目详情',
        subtitle: '找不到这个工程，它可能已被移除。',
        fixed: [
          NeuActionButton(
            label: '返回项目列表',
            icon: Icons.arrow_back_rounded,
            onTap: model.closeProject,
          ),
        ],
      );
    }

    return NeuPageBody(
      title: current.name,
      subtitle: '${current.path} · ${current.projectTypeDisplayName} · '
          '${current.unityVersion} · $_status',
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          NeuActionButton(
            label: '返回',
            icon: Icons.arrow_back_rounded,
            onTap: model.closeProject,
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '启动 Unity',
            icon: Icons.play_arrow_rounded,
            onTap: () => model.launch(current),
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '备份',
            icon: Icons.cloud_upload_outlined,
            onTap: () => model.backupProject(current),
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '打开目录',
            icon: Icons.folder_open_rounded,
            onTap: () => model.openFolder(current.path),
          ),
        ],
      ),
      fixed: [
        Row(
          children: [
            NeuDropdown(
              value: _filter,
              items: _filters,
              icon: Icons.filter_alt_rounded,
              onChanged: (value) => setState(() => _filter = value),
            ),
            const SizedBox(width: 14),
            NeuCheckbox(
              value: shown.isNotEmpty &&
                  shown.every((r) => _selected.contains(r.id)),
              onTap: () => setState(() {
                final all = shown.every((r) => _selected.contains(r.id));
                _selected.clear();
                if (!all) _selected.addAll(shown.map((r) => r.id));
              }),
            ),
            const SizedBox(width: 9),
            Text('全选 ${shown.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const Spacer(),
            if (_selected.isEmpty)
              NeuActionButton(
                label: '刷新',
                icon: Icons.refresh_rounded,
                onTap: _busy ? null : _load,
              )
            else ...[
              NeuActionButton(
                label: '移除所选（${_selected.length}）',
                icon: Icons.delete_outline_rounded,
                onTap: _busy ? null : _removeSelected,
              ),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '安装所选（${_selected.length}）',
                icon: Icons.install_mobile_rounded,
                emphasised: true,
                onTap: _busy ? null : _installSelected,
              ),
            ],
          ],
        ),
        const SizedBox(height: 12),
        Padding(
          padding: const EdgeInsets.only(left: 16),
          child: Row(
            children: const [
              NeuCell('软件包', head: true, flex: 5),
              NeuCell('已安装', head: true, flex: 2),
              NeuCell('可用', head: true, flex: 2),
              NeuCell('来源', head: true, flex: 2),
              NeuCell('类型', head: true, flex: 2),
            ],
          ),
        ),
      ],
      content: ListView.separated(
        padding: EdgeInsets.zero,
        itemCount: shown.length,
        separatorBuilder: (_, _) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          final row = shown[index];
          final checked = _selected.contains(row.id);

          return NeuCard(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 11),
            child: Row(
              children: [
                NeuCheckbox(
                  value: checked,
                  onTap: () => setState(() => checked
                      ? _selected.remove(row.id)
                      : _selected.add(row.id)),
                ),
                const SizedBox(width: 14),
                NeuCell(row.name, flex: 5),
                NeuCell(row.installed, flex: 2),
                NeuCell(row.available, flex: 2),
                NeuCell(row.origin, flex: 2),
                NeuCell(row.group, flex: 2),
                if (row.upgradable)
                  const NeuPill('可升级',
                      bg: Color(0xFFBFE3C9), fg: Color(0xFF237A45)),
              ],
            ),
          );
        },
      ),
    );
  }
}
