import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';
import 'project_picker.dart';

/// The VPM registry: every package every configured source advertises, checked
/// against one target project so install state is readable per row.
class PackagesPage extends StatefulWidget {
  const PackagesPage({super.key});

  static const filters = ['全部包', '已安装', '未安装'];

  @override
  State<PackagesPage> createState() => _PackagesPageState();
}

class _Package {
  _Package(this.raw);

  final Map<String, dynamic> raw;

  String get name => '${raw['name']}';
  String get displayName =>
      '${raw['displayName']}'.isEmpty ? name : '${raw['displayName']}';
  String get description => '${raw['description']}';
  String get version => '${raw['version']}';
  String get unity => '${raw['unity'] ?? ''}';
  String get author => raw['author'] is Map ? '${raw['author']['name'] ?? ''}' : '';

  List<String> get dependencyLines {
    final out = <String>[];
    for (final map in [raw['vpmDependencies'], raw['dependencies']]) {
      if (map is Map) {
        map.forEach((key, value) => out.add('$key ≥ $value'));
      }
    }
    return out;
  }

  bool matches(String query) {
    if (query.isEmpty) return true;
    final needle = query.toLowerCase();
    return name.toLowerCase().contains(needle) ||
        displayName.toLowerCase().contains(needle) ||
        description.toLowerCase().contains(needle);
  }
}

class _PackagesPageState extends State<PackagesPage> {
  final TextEditingController _search = TextEditingController();
  List<_Package> _all = [];
  final Set<String> _selected = {};
  final Set<String> _installed = {};
  String _filter = PackagesPage.filters.first;
  String _status = '';
  String _target = '';
  String? _detail;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    // A package picked from the panel's search arrives as a query to apply.
    if (model.packageQuery.isNotEmpty) {
      _search.text = model.packageQuery;
      model.packageQuery = '';
    }

    setState(() {
      _busy = true;
      _status = '正在读取注册表…';
    });

    try {
      if (model.projects.isEmpty) await model.refreshProjects();
      _all = (await host.packages())
          .cast<Map<String, dynamic>>()
          .map(_Package.new)
          .toList()
        ..sort((a, b) =>
            a.displayName.toLowerCase().compareTo(b.displayName.toLowerCase()));

      _target = model.projects.any((p) => p.path == _target)
          ? _target
          : (model.projects.isEmpty ? '' : model.projects.first.path);
      await _matchTarget();
      _status = '注册表共 ${_all.length} 个包 · ${_installed.length} 个已在目标工程安装';
    } catch (error) {
      _status = '读取失败：$error';
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Install state comes from the project's own manifest, which is already parsed
  /// into `model.projects`, so this costs nothing extra.
  Future<void> _matchTarget() async {
    final model = AppScope.of(context);
    _installed
      ..clear()
      ..addAll(model.projects
          .where((p) => p.path == _target)
          .expand((p) => p.installedPackages)
          .map((i) => i.name.toLowerCase()));
  }

  List<_Package> get _shown {
    final query = _search.text.trim();
    return _all.where((p) {
      if (!p.matches(query)) return false;
      final has = _installed.contains(p.name.toLowerCase());
      if (_filter == PackagesPage.filters[1]) return has;
      if (_filter == PackagesPage.filters[2]) return !has;
      return true;
    }).toList();
  }

  Future<void> _install(List<_Package> packages, String projectPath) async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    setState(() {
      _busy = true;
      _target = projectPath;
    });

    for (var i = 0; i < packages.length; i++) {
      final package = packages[i];
      setState(() => _status = '正在安装 ${i + 1} / ${packages.length}：${package.displayName}');
      try {
        await host.installPackage(projectPath, package.name);
      } catch (error) {
        setState(() {
          _busy = false;
          _status = '${package.displayName} 安装失败：$error';
        });
        return;
      }
    }

    _selected.clear();
    await model.refreshProjects();
    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = '已安装 ${packages.length} 个包';
    });
    await _matchTarget();
    if (mounted) setState(() {});
  }

  Future<void> _remove(List<_Package> packages) async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null || _target.isEmpty) return;

    setState(() => _busy = true);
    for (var i = 0; i < packages.length; i++) {
      final package = packages[i];
      setState(() => _status = '正在移除 ${i + 1} / ${packages.length}：${package.displayName}');
      try {
        await host.removePackage(_target, package.name);
      } catch (error) {
        setState(() {
          _busy = false;
          _status = '${package.displayName} 移除失败：$error';
        });
        return;
      }
    }

    _selected.clear();
    await model.refreshProjects();
    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = '已移除 ${packages.length} 个包';
    });
    await _matchTarget();
    if (mounted) setState(() {});
  }

  void _pickAndInstall(List<_Package> packages) {
    final model = AppScope.of(context);
    if (model.projects.isEmpty) {
      setState(() => _status = '还没有登记任何 Unity 工程。');
      return;
    }
    model.showModal(ProjectPickerForm(
      title: '安装到工程',
      onPick: (path) => _install(packages, path),
    ));
  }

  String get _targetName {
    final matches =
        AppScope.of(context).projects.where((p) => p.path == _target).toList();
    return matches.isEmpty ? '目标工程' : matches.first.name;
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);
    final shown = _shown;
    final picked =
        shown.where((p) => _selected.contains(p.name)).toList();
    final detail = _detail == null
        ? null
        : _all.where((p) => p.name == _detail).toList();

    return NeuPageBody(
      title: '包浏览器',
      subtitle: _status,
      trailing: NeuActionButton(
        label: '刷新',
        icon: Icons.refresh_rounded,
        onTap: _busy ? null : _load,
      ),
      fixed: [
        Row(
          children: [
            Expanded(
              child: NeuSearchField(
                  hint: '搜索名称、显示名或描述…',
                  controller: _search,
                  onChanged: (_) => setState(() {})),
            ),
            const SizedBox(width: 14),
            NeuDropdown(
              value: _filter,
              items: PackagesPage.filters,
              icon: Icons.filter_alt_rounded,
              onChanged: (value) => setState(() => _filter = value),
            ),
            const SizedBox(width: 12),
            NeuDropdown(
              value: _targetName,
              items: [for (final p in model.projects) p.name],
              icon: Icons.account_tree_rounded,
              onChanged: (value) async {
                final chosen =
                    model.projects.firstWhere((p) => p.name == value);
                setState(() => _target = chosen.path);
                await _matchTarget();
                if (mounted) setState(() {});
              },
            ),
          ],
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            NeuCheckbox(
              value: shown.isNotEmpty &&
                  shown.every((p) => _selected.contains(p.name)),
              onTap: () => setState(() {
                final all = shown.every((p) => _selected.contains(p.name));
                _selected.clear();
                if (!all) _selected.addAll(shown.map((p) => p.name));
              }),
            ),
            const SizedBox(width: 9),
            Text('显示 ${shown.length} / ${_all.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const Spacer(),
            if (picked.isEmpty)
              Text('勾选包后可批量安装或移除',
                  style: TextStyle(fontSize: 12, color: t.inkTertiary))
            else ...[
              NeuActionButton(
                label: '移除所选（${picked.length}）',
                icon: Icons.delete_outline_rounded,
                onTap: _busy ? null : () => _remove(picked),
              ),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '安装所选（${picked.length}）',
                icon: Icons.install_mobile_rounded,
                emphasised: true,
                onTap: _busy ? null : () => _pickAndInstall(picked),
              ),
            ],
          ],
        ),
      ],
      content: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(
            flex: 5,
            child: shown.isEmpty
                ? const NeuEmptyState(
                    expand: true,
                    text: '注册表里没有匹配的包。\n检查「资源 · 存储库」中的仓库源是否可用。')
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final package = shown[index];
                      final checked = _selected.contains(package.name);
                      final installed =
                          _installed.contains(package.name.toLowerCase());

                      return NeuCard(
                        selected: package.name == _detail,
                        padding: const EdgeInsets.symmetric(
                            horizontal: 14, vertical: 11),
                        child: Row(
                          children: [
                            NeuCheckbox(
                              value: checked,
                              onTap: () => setState(() => checked
                                  ? _selected.remove(package.name)
                                  : _selected.add(package.name)),
                            ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: MouseRegion(
                                cursor: SystemMouseCursors.click,
                                child: GestureDetector(
                                  behavior: HitTestBehavior.opaque,
                                  onTap: () =>
                                      setState(() => _detail = package.name),
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(package.displayName,
                                          style: const TextStyle(
                                              fontSize: 13.5,
                                              fontWeight: FontWeight.w600),
                                          overflow: TextOverflow.ellipsis),
                                      const SizedBox(height: 2),
                                      Text(package.name,
                                          style: TextStyle(
                                              fontSize: 11,
                                              color: t.inkTertiary),
                                          overflow: TextOverflow.ellipsis),
                                    ],
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(width: 10),
                            NeuPill(package.version),
                            const SizedBox(width: 8),
                            NeuPill(
                              installed ? '已安装' : '未安装',
                              bg: installed
                                  ? const Color(0xFFBFE3C9)
                                  : Colors.black.withValues(alpha: 0.06),
                              fg: installed
                                  ? const Color(0xFF237A45)
                                  : null,
                            ),
                          ],
                        ),
                      );
                    },
                  ),
          ),
          const SizedBox(width: 16),
          SizedBox(
              width: 330,
              child: detail == null || detail.isEmpty
                  ? const NeuEmptyState(
                      expand: true, text: '选择左侧任一包查看详情与依赖。')
                  : _Detail(
                      package: detail.first,
                      installed:
                          _installed.contains(detail.first.name.toLowerCase()),
                      busy: _busy,
                      onInstall: () => _pickAndInstall([detail.first]),
                      onRemove: () => _remove([detail.first]),
                    )),
        ],
      ),
    );
  }
}

class _Detail extends StatelessWidget {
  const _Detail({
    required this.package,
    required this.installed,
    required this.busy,
    required this.onInstall,
    required this.onRemove,
  });

  final _Package package;
  final bool installed;
  final bool busy;
  final VoidCallback onInstall;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuBox(
      mode: NeuMode.inset,
      radius: 11,
      depth: 4,
      color: Colors.black.withValues(alpha: 0.05),
      border: Border.all(color: Colors.black.withValues(alpha: 0.1)),
      padding: const EdgeInsets.all(16),
      child: ListView(
        padding: EdgeInsets.zero,
        children: [
          Text(package.displayName,
              style: TextStyle(
                  fontSize: 16, fontWeight: FontWeight.w600, color: t.ink)),
          const SizedBox(height: 4),
          Text(package.name,
              style: TextStyle(fontSize: 11.5, color: t.inkTertiary)),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              NeuPill(package.version),
              if (package.unity.isNotEmpty) NeuPill('Unity ${package.unity}'),
              if (package.author.isNotEmpty) NeuPill(package.author),
            ],
          ),
          const SizedBox(height: 14),
          Text(package.description.isEmpty ? '这个包没有写描述。' : package.description,
              style: TextStyle(
                  fontSize: 12.5, height: 1.55, color: t.inkSecondary)),
          if (package.dependencyLines.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text('依赖',
                style: TextStyle(
                    fontSize: 12, fontWeight: FontWeight.w600, color: t.ink)),
            const SizedBox(height: 6),
            for (final line in package.dependencyLines)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Text(line,
                    style: TextStyle(fontSize: 11.5, color: t.inkSecondary)),
              ),
          ],
          const SizedBox(height: 18),
          NeuActionButton(
            label: installed ? '再次安装' : '安装到工程',
            icon: Icons.install_mobile_rounded,
            emphasised: true,
            onTap: busy ? null : onInstall,
          ),
          const SizedBox(height: 10),
          if (installed)
            NeuActionButton(
              label: '从工程移除',
              icon: Icons.delete_outline_rounded,
              onTap: busy ? null : onRemove,
            ),
        ],
      ),
    );
  }
}
