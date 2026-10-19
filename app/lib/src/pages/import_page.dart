import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';
import 'project_picker.dart';

class ImportPage extends StatefulWidget {
  const ImportPage({super.key});

  static const views = ['全部条目', '仅文件', '仅文件夹'];

  @override
  State<ImportPage> createState() => _ImportPageState();
}

class _ImportPageState extends State<ImportPage> {
  final Set<String> _excluded = {};
  Map<String, dynamic>? _preview;
  String _path = '';
  String _view = ImportPage.views.first;
  String _status = '';
  bool _busy = false;

  List<Map<String, dynamic>> get _entries =>
      ((_preview?['entries'] as List?) ?? const [])
          .cast<Map<String, dynamic>>();

  List<Map<String, dynamic>> get _shown => _entries.where((entry) {
        if (_view == ImportPage.views[1]) return entry['isDirectory'] != true;
        if (_view == ImportPage.views[2]) return entry['isDirectory'] == true;
        return true;
      }).toList();

  List<String> get _selectedPaths =>
      [for (final entry in _entries) if (!_excluded.contains('${entry['pathName']}')) '${entry['pathName']}'];

  String _size(Object? bytes) {
    final value = (bytes as num?)?.toInt() ?? 0;
    if (value >= 1048576) return '${(value / 1048576).toStringAsFixed(1)} MB';
    if (value >= 1024) return '${(value / 1024).toStringAsFixed(1)} KB';
    return '$value B';
  }

  Future<void> _pick() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    final picked = await FilePicker.pickFiles(
      dialogTitle: '选择 .unitypackage',
      type: FileType.custom,
      allowedExtensions: const ['unitypackage'],
    );
    if (picked.isEmpty) return;
    final path = picked.single.path;
    if (path == null) return;

    setState(() {
      _busy = true;
      _status = '正在解析 ${path.split(RegExp(r'[\\/]')).last}…';
    });

    try {
      _preview = await host.parsePackage(path);
      _path = path;
      _excluded.clear();
      _status = '${_preview?['name']} · ${_entries.length} 个条目';
    } catch (error) {
      _preview = null;
      _status = '解析失败：$error';
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _install() async {
    final model = AppScope.of(context);
    if (_preview == null || _selectedPaths.isEmpty) {
      setState(() => _status = '还没有勾选任何条目。');
      return;
    }
    if (model.projects.isEmpty) {
      setState(() => _status = '还没有登记任何 Unity 工程。');
      return;
    }

    model.showModal(ProjectPickerForm(title: '导入到工程', onPick: _installInto));
  }

  Future<void> _installInto(String projectPath) async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    final project = model.projects.firstWhere((p) => p.path == projectPath);
    setState(() {
      _busy = true;
      _status = '正在导入 ${project.name} · ${_selectedPaths.length} 个条目…';
    });

    try {
      await host.installPackageFile(projectPath, _path, _selectedPaths);
      final records =
          (await host.installRecords(projectPath)).cast<Map<String, dynamic>>();
      final hit = records
          .where((r) => '${r['filePath']}' == _path)
          .toList();
      final written = hit.isEmpty
          ? 0
          : ((hit.first['fileCount'] as num?)?.toInt() ?? 0);
      await model.refreshProjects();
      if (!mounted) return;
      setState(() {
        _busy = false;
        _status = '已导入 ${project.name} · 台账记录 $written 个文件';
      });
    } catch (error) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '导入失败：$error';
        });
      }
    }
  }

  Future<void> _register() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null || _path.isEmpty) return;

    setState(() => _busy = true);
    try {
      await host.addPackageFile(_path);
      if (mounted) setState(() => _status = '已把当前包加入软件包库。');
    } catch (error) {
      if (mounted) setState(() => _status = '入库失败：$error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;
    final total = _entries.length;
    final included = total - _excluded.length;
    final allIncluded = total > 0 && _excluded.isEmpty;

    return NeuPageBody(
      title: '导入 .unitypackage',
      subtitle: _path.isEmpty ? _status : '$_path · $_status',
      trailing: NeuActionButton(
        label: '选择包文件',
        icon: Icons.folder_open_rounded,
        emphasised: true,
        onTap: _busy ? null : _pick,
      ),
      fixed: [
        if (_preview != null)
          Row(
            children: [
              NeuDropdown(
                value: _view,
                items: ImportPage.views,
                icon: Icons.filter_alt_rounded,
                onChanged: (value) => setState(() => _view = value),
              ),
              const SizedBox(width: 14),
              NeuCheckbox(
                value: allIncluded,
                onTap: () => setState(() {
                  _excluded.clear();
                  if (!allIncluded) {
                    _excluded.addAll([for (final e in _entries) '${e['pathName']}']);
                  }
                }),
              ),
              const SizedBox(width: 9),
              Text('勾选 $included / $total 个条目',
                  style: TextStyle(fontSize: 12, color: t.inkTertiary)),
              const Spacer(),
              NeuActionButton(
                label: '加入软件包库',
                icon: Icons.library_add_outlined,
                onTap: _busy ? null : _register,
              ),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '导入到工程',
                icon: Icons.install_mobile_rounded,
                emphasised: true,
                onTap: _busy ? null : _install,
              ),
            ],
          ),
      ],
      content: _preview == null
          ? const NeuEmptyState(
              expand: true,
              text: '还没有选择 .unitypackage。\n右上角选择文件后，这里会列出包内条目，可以逐条取消勾选再导入。')
          : Column(
              children: [
                Row(
                  children: const [
                    NeuCell('路径', head: true, flex: 7),
                    NeuCell('大小', head: true, flex: 2),
                    NeuCell('类型', head: true, flex: 2),
                  ],
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 6),
                    itemBuilder: (context, index) {
                      final entry = shown[index];
                      final pathName = '${entry['pathName']}';
                      final included = !_excluded.contains(pathName);

                      return NeuCard(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 14, vertical: 9),
                        child: Row(
                          children: [
                            NeuCheckbox(
                              value: included,
                              onTap: () => setState(() => included
                                  ? _excluded.add(pathName)
                                  : _excluded.remove(pathName)),
                            ),
                            const SizedBox(width: 14),
                            NeuCell(pathName, flex: 7),
                            NeuCell(entry['isDirectory'] == true
                                ? ''
                                : _size(entry['size']), flex: 2),
                            NeuCell(entry['isDirectory'] == true
                                ? '文件夹'
                                : (entry['hasMeta'] == true ? '资源+meta' : '资源'), flex: 2),
                          ],
                        ),
                      );
                    },
                  ),
                ),
              ],
            ),
    );
  }
}
