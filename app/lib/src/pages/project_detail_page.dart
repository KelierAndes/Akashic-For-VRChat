import 'package:flutter/material.dart';

import '../host/host_models.dart';
import '../pages/project_picker.dart';
import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';

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
    required this.category,
    required this.cover,
    required this.filePath,
  });

  final String id;
  final String name;
  final String group;
  final String installed;
  final String available;
  final String origin;
  final String category;
  final String cover;
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
  String _origin = '全部来源';
  String _category = '全部分类';
  String _status = '';
  bool _busy = false;

  static const _filters = ['全部', '已安装', '可升级', '未安装'];

  List<String> get _origins => [
        '全部来源',
        ...{for (final row in _rows) if (row.origin.isNotEmpty) row.origin},
      ];

  List<String> get _categories => [
        '全部分类',
        '未分类',
        ...{for (final row in _rows) if (row.category.isNotEmpty) row.category},
      ];

  static String _key(String raw) {
    var text = raw.toLowerCase();
    text = text.replaceAll(RegExp(r'\d+(?:\.\d+){1,3}[0-9a-z.+\-]*'), ' ');
    return text.replaceAll(RegExp(r'[^a-z0-9\u3040-\u30ff\u4e00-\u9fff]'), '');
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load({bool force = false, bool reloadRegistry = false}) async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    setState(() {
      _busy = true;
      _status = '正在读取…';
    });

    try {
      if (force || model.projects.isEmpty) await model.refreshProjects();
      final project = model.projects.firstWhere((p) => p.path == widget.projectPath);
      final registry = await model.packages(force: reloadRegistry);
      final library = await model.libraryFiles(force: force);
      final records = await model.installRecords(project.path, force: force);

      final categoryByKey = <String, String>{};
      final coverByKey = <String, String>{};
      final sourceByKey = <String, String>{};
      for (final row in await model.resourceRows(force: force)) {
        final metaKey = '${row['metaKey']}';
        if (metaKey.isEmpty) continue;

        final category = '${row['category']}';
        if (category.isNotEmpty) categoryByKey[metaKey] = category;

        final cover = '${row['coverUrl']}';
        if (cover.isNotEmpty) coverByKey[metaKey] = cover;

        final source = '${row['matchSource']}';
        if (source.isNotEmpty) sourceByKey[metaKey] = source;
      }

      String categoryOf(String name, String id) =>
          categoryByKey[_key(name)] ?? categoryByKey[_key(id)] ?? '';
      String coverOf(String name, String id) =>
          coverByKey[_key(name)] ?? coverByKey[_key(id)] ?? '';
      String originOf(String name, String id, String fallback) {
        final source = sourceByKey[_key(name)] ?? sourceByKey[_key(id)] ?? '';
        return switch (source) {
          'Booth' => 'Booth',
          'AssetStore' => 'Asset Store',
          _ => fallback,
        };
      }

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
        final display = '${package['displayName']}'.isEmpty
            ? name
            : '${package['displayName']}';
        registryNames.add(name.toLowerCase());
        rows.add(_Row(
          id: name,
          name: display,
          group: 'VPM 包',
          installed: installedByName[name.toLowerCase()]?.version ?? '无',
          available: '${package['version']}',
          origin: '注册表',
          category: categoryOf(display, name),
          cover: coverOf(display, name),
          filePath: '',
        ));
      }

      for (final entry in installedByName.values) {
        if (registryNames.contains(entry.name.toLowerCase())) continue;
        final display = entry.displayName.isEmpty ? entry.name : entry.displayName;
        rows.add(_Row(
          id: entry.name,
          name: display,
          group: switch (entry.source) {
            'Local' => '本地包',
            'Git' => 'Git 仓库',
            _ => '外部',
          },
          installed: entry.version,
          available: entry.version,
          origin: switch (entry.source) {
            'Local' => '本地',
            'Git' => 'Git',
            'Registry' => '注册表',
            'Embedded' => '内嵌',
            _ => entry.source,
          },
          category: categoryOf(display, entry.name),
          cover: coverOf(display, entry.name),
          filePath: '',
        ));
      }

      for (final file in library) {
        final path = '${file['filePath']}';
        final display =
            '${file['displayName']}'.isEmpty ? path : '${file['displayName']}';
        rows.add(_Row(
          id: 'unitypackage:$path',
          name: display,
          group: '.unitypackage',
          installed: recordByPath[path]?['version'] ?? '无',
          available: '${file['version']}',
          origin: originOf(display, path, originLabel('${file['origin']}')),
          category: categoryOf(display, path),
          cover: coverOf(display, path),
          filePath: path,
        ));      }

      _rows = rows;
      if (!_origins.contains(_origin)) _origin = '全部来源';
      if (!_categories.contains(_category)) _category = '全部分类';
      _status = '共 ${rows.length} 项 · ${installedByName.length} 项已安装 · '
          '${recordByPath.length} 个 .unitypackage 台账';
    } catch (error) {
      _status = '读取失败：$error';
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  List<_Row> get _shown => _rows.where((row) {
        final matchesState = switch (_filter) {
          '已安装' => row.isInstalled,
          '可升级' => row.upgradable,
          '未安装' => !row.isInstalled,
          _ => true,
        };
        if (!matchesState) return false;
        if (_origin != '全部来源' && row.origin != _origin) return false;
        if (_category == '未分类') {
          if (row.category.isNotEmpty) return false;
        } else if (_category != '全部分类' && row.category != _category) {
          return false;
        }
        return true;
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
    await _load(force: true);
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
    await _load(force: true);
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
              onChanged: (value) => setState(() {
                _filter = value;
                _selected.clear();
              }),
            ),
            const SizedBox(width: 10),
            NeuDropdown(
              value: _origin,
              items: _origins,
              icon: Icons.source_rounded,
              onChanged: (value) => setState(() {
                _origin = value;
                _selected.clear();
              }),
            ),
            const SizedBox(width: 10),
            NeuDropdown(
              value: _category,
              items: _categories,
              icon: Icons.category_rounded,
              onChanged: (value) => setState(() {
                _category = value;
                _selected.clear();
              }),
            ),
            const Spacer(),
            if (_selected.isEmpty)
              NeuActionButton(
                label: '刷新',
                icon: Icons.refresh_rounded,
                onTap: _busy ? null : () => _load(force: true, reloadRegistry: true),
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
        const SizedBox(height: 10),
        Row(
          children: [
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
            Text('全选 ${shown.length} / ${_rows.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
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
              NeuCell('分类', head: true, flex: 2),
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
                Expanded(
                  flex: 5,
                  child: Row(
                    children: [
                      NeuCover(row.cover, width: 44, height: 32),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Text(row.name,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w500,
                                color: t.ink)),
                      ),
                    ],
                  ),
                ),
                NeuCell(row.installed, flex: 2),
                NeuCell(row.available, flex: 2),
                NeuCell(row.origin, flex: 2),
                NeuCell(row.category.isEmpty ? '—' : row.category, flex: 2),
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
