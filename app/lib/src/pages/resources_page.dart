import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../host/host_models.dart';
import '../shell/app_scope.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

/// The local package library: registry sources, the .unitypackage shelf, external
/// packages, and the two account feeds that already have files on disk.
class ResourcesPage extends StatefulWidget {
  const ResourcesPage({super.key, this.initialTab = 0});

  final int initialTab;

  static const tabs = ['存储库', '用户软件包', '外部包', 'Booth', 'Asset Store'];

  @override
  State<ResourcesPage> createState() => _ResourcesPageState();
}

class _ResourcesPageState extends State<ResourcesPage> {
  late int _tab = widget.initialTab;

  @override
  Widget build(BuildContext context) {
    return NeuPageBody(
      title: '资源',
      subtitle: ResourcesPage.tabs[_tab],
      fixed: [
        NeuTabs(
          tabs: ResourcesPage.tabs,
          index: _tab,
          onChanged: (i) => setState(() => _tab = i),
        ),
      ],
      content: switch (_tab) {
        0 => const _RepositoriesTab(),
        1 => const _UserPackagesTab(),
        2 => const _ExternalTab(),
        3 => const _BoothShelfTab(),
        _ => const _AssetStoreShelfTab(),
      },
    );
  }
}

// ---------- 1 · repositories ----------

class _RepositoriesTab extends StatefulWidget {
  const _RepositoriesTab();

  @override
  State<_RepositoriesTab> createState() => _RepositoriesTabState();
}

class _RepositoriesTabState extends State<_RepositoriesTab> {
  List<dynamic> _urls = [];
  final Map<String, String> _names = {};
  String _status = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    setState(() => _status = '正在读取仓库源…');
    final state = await host.state();
    if (!mounted) return;
    setState(() {
      _urls = state.registrySources;
      _status = '共 ${_urls.length} 个仓库源 · 顺序即解析优先级';
    });
    _resolveNames();
  }

  /// Names come from each registry, which is one request per source, so the derived
  /// label stays on screen until the real one lands.
  Future<void> _resolveNames() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    for (final url in _urls.toList()) {
      final name = await host.resolveRepositoryName(url);
      if (name.isNotEmpty && mounted) setState(() => _names[url] = name);
    }
  }

  String _derivedName(String url) {
    final resolved = _names[url];
    if (resolved != null) return resolved;
    if (url.contains('packages.vrchat.com/official')) return '官方';
    if (url.contains('packages.vrchat.com/curated')) return '精选';
    final uri = Uri.tryParse(url);
    final segments = (uri?.pathSegments ?? const <String>[])
        .where((s) => s.isNotEmpty && !s.endsWith('.json'))
        .toList();
    if (segments.isNotEmpty) return segments.last;
    return uri?.host ?? url;
  }

  Future<void> _add(String url) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    try {
      final next = await host.addRegistrySource(url);
      if (mounted) setState(() => _urls = next);
      _resolveNames();
    } catch (error) {
      if (mounted) setState(() => _status = '$error');
    }
  }

  Future<void> _move(int index, int delta) async {
    final host = AppScope.of(context).host;
    final target = index + delta;
    if (host == null || target < 0 || target >= _urls.length) return;

    final next = _urls.cast<String>().toList();
    next.insert(target, next.removeAt(index));
    setState(() => _urls = next);
    await host.orderRegistrySources(next);
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final model = AppScope.of(context);

    return Column(
      children: [
        Row(
          children: [
            Expanded(
                child: Text(_status,
                    style: TextStyle(fontSize: 12, color: t.inkTertiary),
                    overflow: TextOverflow.ellipsis)),
            NeuActionButton(
              label: '从 VCC 导入',
              icon: Icons.cloud_download_rounded,
              onTap: () async {
                final host = AppScope.of(context).host;
                if (host == null) return;
                final result = await host.importVccRepositories();
                if (mounted) {
                  setState(() {
                    _urls = result['urls'] as List<dynamic>;
                    _status = '已导入 ${result['imported']} 个 VCC 仓库源';
                  });
                }
              },
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '添加',
              icon: Icons.add_rounded,
              emphasised: true,
              onTap: () => model.showModal(NeuPromptForm(
                title: '添加仓库源',
                label: 'URL',
                hint: 'https://…',
                helper: '例：https://vpm.anatawa12.com/vpm.json',
                onSubmit: _add,
              )),
            ),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: ListView.separated(
            padding: EdgeInsets.zero,
            itemCount: _urls.length,
            separatorBuilder: (_, _) => const SizedBox(height: 10),
            itemBuilder: (context, index) {
              final url = _urls[index] as String;

              return NeuCard(
                padding:
                    const EdgeInsets.symmetric(horizontal: 16, vertical: 13),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(_derivedName(url),
                              style: const TextStyle(
                                  fontSize: 13.5,
                                  fontWeight: FontWeight.w600),
                              overflow: TextOverflow.ellipsis),
                          const SizedBox(height: 2),
                          Text(url,
                              style: TextStyle(
                                  fontSize: 11.5, color: t.inkTertiary),
                              overflow: TextOverflow.ellipsis),
                        ],
                      ),
                    ),
                    const SizedBox(width: 10),
                    NeuPill(_names[url] != null ? '仓库自报名称' : '按 URL 推导'),
                    const SizedBox(width: 8),
                    _IconPoke(
                        icon: Icons.arrow_upward_rounded,
                        tooltip: '上移',
                        enabled: index > 0,
                        onTap: () => _move(index, -1)),
                    const SizedBox(width: 6),
                    _IconPoke(
                        icon: Icons.arrow_downward_rounded,
                        tooltip: '下移',
                        enabled: index < _urls.length - 1,
                        onTap: () => _move(index, 1)),
                    const SizedBox(width: 6),
                    _IconPoke(
                        icon: Icons.delete_outline_rounded,
                        tooltip: '移除仓库源',
                        enabled: true,
                        onTap: () async {
                          final host = AppScope.of(context).host;
                          if (host == null) return;
                          final next = await host.removeRegistrySource(url);
                          if (mounted) setState(() => _urls = next);
                        }),
                  ],
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _IconPoke extends StatelessWidget {
  const _IconPoke({
    required this.icon,
    required this.tooltip,
    required this.enabled,
    required this.onTap,
  });

  final IconData icon;
  final String tooltip;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Tooltip(
      message: tooltip,
      child: NeuButton(
        width: 30,
        height: 30,
        radius: 8,
        depth: 4,
        color: t.raised,
        insetColor: t.inset,
        border: glassBorder(alpha: 0.45),
        onTap: enabled ? onTap : null,
        child: Icon(icon,
            size: 16,
            color: enabled
                ? t.inkSecondary
                : t.inkTertiary.withValues(alpha: 0.4)),
      ),
    );
  }
}

// ---------- 2 · user packages ----------

class _UserPackagesTab extends StatefulWidget {
  const _UserPackagesTab();

  @override
  State<_UserPackagesTab> createState() => _UserPackagesTabState();
}

class _UserPackagesTabState extends State<_UserPackagesTab> {
  List<dynamic> _files = [];
  List<dynamic> _folders = [];
  String _originFilter = '全部来源';

  /// Booth-imported packages belong to the Booth shelf tab, so they are not an option
  /// here — same as the old page, whose list excluded PackageOrigin.Booth outright.
  static const _origins = ['全部来源', '本地', 'Asset Store'];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    _files = await host.libraryFiles();
    _folders = await host.libraryFolders();
    if (mounted) setState(() {});
  }

  List<dynamic> get _shown => _files
      .where((f) {
        final origin = '${f['origin']}';
        if (_originFilter == '全部来源') return origin != 'Booth';
        return origin == originCodes[_originFilter];
      })
      .toList();

  String _size(int bytes) => bytes >= 1048576
      ? '${(bytes / 1048576).toStringAsFixed(1)} MB'
      : bytes >= 1024
          ? '${(bytes / 1024).toStringAsFixed(1)} KB'
          : '$bytes B';

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return Column(
      children: [
        Row(
          children: [
            NeuDropdown(
              value: _originFilter,
              items: _origins,
              icon: Icons.filter_alt_rounded,
              onChanged: (value) => setState(() => _originFilter = value),
            ),
            const SizedBox(width: 14),
            Expanded(
                child: Text(
                    '共 ${shown.length} 个包 · ${_folders.length} 个库目录',
                    style: TextStyle(fontSize: 12, color: t.inkTertiary))),
            NeuActionButton(
              label: '库目录',
              icon: Icons.create_new_folder_rounded,
              onTap: () async {
                final host = AppScope.of(context).host;
                final picked = await FilePicker.getDirectoryPath(
                    dialogTitle: '添加 .unitypackage 库目录');
                if (host == null || picked == null) return;
                await host.addLibraryFolder(picked);
                await _load();
              },
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '添加包文件',
              icon: Icons.add_rounded,
              emphasised: true,
              onTap: () async {
                final host = AppScope.of(context).host;
                final picked = await FilePicker.pickFiles(
                  dialogTitle: '选择 .unitypackage',
                  type: FileType.custom,
                  allowedExtensions: const ['unitypackage'],
                );
                if (host == null || picked.isEmpty) return;
                final path = picked.single.path;
                if (path == null) return;
                await host.addPackageFile(path);
                await _load();
              },
            ),
          ],
        ),
        const SizedBox(height: 14),
        if (_folders.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: NeuCard(
              padding:
                  const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text('扫描目录',
                      style: TextStyle(
                          fontSize: 12, fontWeight: FontWeight.w600)),
                  const SizedBox(height: 8),
                  for (final folder in _folders)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 4),
                      child: Row(
                        children: [
                          Expanded(
                              child: Text('$folder',
                                  style: TextStyle(
                                      fontSize: 12, color: t.inkSecondary),
                                  overflow: TextOverflow.ellipsis)),
                          _IconPoke(
                              icon: Icons.close_rounded,
                              tooltip: '移除库目录',
                              enabled: true,
                              onTap: () async {
                                final host = AppScope.of(context).host;
                                if (host == null) return;
                                await host.removeLibraryFolder('$folder');
                                await _load();
                              }),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ),
        Expanded(
          child: shown.isEmpty
              ? const NeuEmptyState(
                  height: 200,
                  text: '库里还没有 .unitypackage。\n添加一个扫描目录，或直接选择单个文件。')
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: shown.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final file = shown[index];
                    return NeuCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 16, vertical: 13),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('${file['displayName']}',
                                    style: const TextStyle(
                                        fontSize: 13.5,
                                        fontWeight: FontWeight.w600),
                                    overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 2),
                                Text('${file['filePath']}',
                                    style: TextStyle(
                                        fontSize: 11, color: t.inkTertiary),
                                    overflow: TextOverflow.ellipsis),
                              ],
                            ),
                          ),
                          const SizedBox(width: 10),
                          NeuPill('${file['version']}'.isEmpty
                              ? '未标注版本'
                              : '${file['version']}'),
                          const SizedBox(width: 8),
                          NeuPill(originLabel('${file['origin']}')),
                          const SizedBox(width: 8),
                          NeuPill(_size((file['size'] as num).toInt())),
                          const SizedBox(width: 8),
                          _IconPoke(
                              icon: Icons.delete_outline_rounded,
                              tooltip: '从列表移除（不删除文件）',
                              enabled: true,
                              onTap: () async {
                                final host = AppScope.of(context).host;
                                if (host == null) return;
                                await host.removePackageFile(
                                    '${file['filePath']}');
                                await _load();
                              }),
                        ],
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

// ---------- 3 · external ----------

class _ExternalTab extends StatefulWidget {
  const _ExternalTab();

  @override
  State<_ExternalTab> createState() => _ExternalTabState();
}

class _ExternalTabState extends State<_ExternalTab> {
  List<dynamic> _items = [];
  String _status = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    _items = await host.externalPackages();
    if (mounted) setState(() => _status = '共 ${_items.length} 个外部包');
  }

  Future<void> _addRepository(String url) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    try {
      await host.addExternalRepository(url, null);
      await _load();
    } catch (error) {
      if (mounted) setState(() => _status = '$error');
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final model = AppScope.of(context);

    return Column(
      children: [
        Row(
          children: [
            Expanded(
                child: Text(_status,
                    style: TextStyle(fontSize: 12, color: t.inkTertiary))),
            NeuActionButton(
              label: '添加 Git 仓库',
              icon: Icons.code_rounded,
              onTap: () => model.showModal(NeuPromptForm(
                title: '添加外部仓库',
                label: '仓库地址',
                hint: 'https://github.com/owner/repo',
                helper:
                    '支持 GitHub / GitLab / Gitee，按分支或标签下载归档',
                onSubmit: _addRepository,
              )),
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '添加本地文件夹',
              icon: Icons.folder_open_rounded,
              emphasised: true,
              onTap: () async {
                final host = AppScope.of(context).host;
                final picked = await FilePicker.getDirectoryPath(
                    dialogTitle: '选择包含 package.json 的包目录');
                if (host == null || picked == null) return;
                try {
                  await host.addExternalFolder(picked);
                  await _load();
                } catch (error) {
                  setState(() => _status = '$error');
                }
              },
            ),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: _items.isEmpty
              ? const NeuEmptyState(
                  height: 200,
                  text: '还没有外部包。\n本地包目录，或 GitHub / GitLab / Gitee 仓库都可以登记在这里。')
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: _items.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final item = _items[index];
                    return NeuCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 16, vertical: 13),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('${item['displayName']}',
                                    style: const TextStyle(
                                        fontSize: 13.5,
                                        fontWeight: FontWeight.w600),
                                    overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 2),
                                Text(
                                    '${item['name']} · ${item['location']}',
                                    style: TextStyle(
                                        fontSize: 11.5, color: t.inkTertiary),
                                    overflow: TextOverflow.ellipsis),
                              ],
                            ),
                          ),
                          const SizedBox(width: 10),
                          NeuPill('${item['kind']}'),
                          const SizedBox(width: 8),
                          _IconPoke(
                              icon: Icons.delete_outline_rounded,
                              tooltip: '移除（工程引用不受影响）',
                              enabled: true,
                              onTap: () async {
                                final host = AppScope.of(context).host;
                                if (host == null) return;
                                await host.removeExternal('${item['id']}');
                                await _load();
                              }),
                        ],
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

// ---------- 4 · booth shelf ----------

class _BoothShelfTab extends StatefulWidget {
  const _BoothShelfTab();

  @override
  State<_BoothShelfTab> createState() => _BoothShelfTabState();
}

class _BoothShelfTabState extends State<_BoothShelfTab> {
  List<dynamic> _imported = [];
  String _status = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    // This shelf is the imported side of Booth, same as the old page: only the
    // .unitypackage files tagged with the Booth origin, not the purchase list.
    _imported = (await host.libraryFiles())
        .where((f) => '${f['origin']}' == 'Booth')
        .toList();
    if (mounted) {
      setState(() => _status = '已入库 ${_imported.length} 个 Booth 软件包');
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return Column(
      children: [
        Row(
          children: [
            Expanded(
                child: Text(_status,
                    style: TextStyle(fontSize: 12, color: t.inkTertiary))),
            NeuActionButton(
                label: '去 Booth 已购',
                icon: Icons.shopping_bag_rounded,
                onTap: () => AppScope.of(context).jumpTo('booth')),
            const SizedBox(width: 10),
            NeuActionButton(
                label: '刷新',
                icon: Icons.refresh_rounded,
                onTap: _load),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: _imported.isEmpty
              ? const NeuEmptyState(
                  height: 200,
                  text: '还没有入库的 Booth 软件包。\n到「Booth 已购」页下载并入库后，会列在这里。')
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: _imported.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final file = _imported[index];
                    return NeuCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 16, vertical: 13),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('${file['displayName']}',
                                    style: const TextStyle(
                                        fontSize: 13.5,
                                        fontWeight: FontWeight.w600),
                                    overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 2),
                                Text('${file['filePath']}',
                                    style: TextStyle(
                                        fontSize: 11, color: t.inkTertiary),
                                    overflow: TextOverflow.ellipsis),
                              ],
                            ),
                          ),
                          const SizedBox(width: 10),
                          NeuPill('${file['version']}'.isEmpty
                              ? '未标注版本'
                              : '${file['version']}'),
                          const SizedBox(width: 8),
                          _IconPoke(
                              icon: Icons.delete_outline_rounded,
                              tooltip: '从列表移除（不删除文件）',
                              enabled: true,
                              onTap: () async {
                                final host = AppScope.of(context).host;
                                if (host == null) return;
                                await host.removePackageFile(
                                    '${file['filePath']}');
                                await _load();
                              }),
                        ],
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

// ---------- 5 · asset store shelf ----------

class _AssetStoreShelfTab extends StatefulWidget {
  const _AssetStoreShelfTab();

  @override
  State<_AssetStoreShelfTab> createState() => _AssetStoreShelfTabState();
}

class _AssetStoreShelfTabState extends State<_AssetStoreShelfTab> {
  List<dynamic> _cached = [];
  String _status = '';
  String _directory = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    final scripts = await host.assetStoreScripts();
    _directory = '${scripts['cacheDirectory']}';
    final all = (await host.assetStoreCache()).cast<Map<String, dynamic>>();
    _cached = all.where((a) => a['localPath'] != null).toList();
    if (mounted) {
      setState(() => _status =
          '缓存目录 ${_cached.length} / ${all.length} 项已下载 · $_directory');
    }
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return Column(
      children: [
        Row(
          children: [
            Expanded(
                child: Text(_status,
                    style: TextStyle(fontSize: 12, color: t.inkTertiary),
                    overflow: TextOverflow.ellipsis)),
            NeuActionButton(
                label: '重新扫描',
                icon: Icons.refresh_rounded,
                onTap: _load),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: _cached.isEmpty
              ? NeuEmptyState(
                  height: 200,
                  text: '缓存目录里还没有下载好的 .unitypackage。\n$_directory\n'
                      '可以在设置页改到这个目录，或在「Asset Store 导入」页直接下载。')
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: _cached.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final asset = _cached[index];
                    final inLibrary = asset['isInLibrary'] == true;

                    return NeuCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 16, vertical: 13),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('${asset['name']}',
                                    style: const TextStyle(
                                        fontSize: 13.5,
                                        fontWeight: FontWeight.w600),
                                    overflow: TextOverflow.ellipsis),
                                const SizedBox(height: 2),
                                Text(
                                    '${asset['publisher']} · ${asset['localPath']}',
                                    style: TextStyle(
                                        fontSize: 11, color: t.inkTertiary),
                                    overflow: TextOverflow.ellipsis),
                              ],
                            ),
                          ),
                          const SizedBox(width: 10),
                          NeuPill('${asset['version']}'.isEmpty
                              ? '未标注版本'
                              : '${asset['version']}'),
                          const SizedBox(width: 8),
                          NeuPill(inLibrary ? '已入库' : '未入库',
                              bg: inLibrary
                                  ? const Color(0xFFBFE3C9)
                                  : Colors.black.withValues(alpha: 0.06),
                              fg:
                                  inLibrary ? const Color(0xFF237A45) : null),
                          const SizedBox(width: 8),
                          if (!inLibrary)
                            NeuActionButton(
                              label: '入库',
                              icon: Icons.library_add_outlined,
                              onTap: () async {
                                final host = AppScope.of(context).host;
                                if (host == null) return;
                                await host.addPackageFile(
                                    '${asset['localPath']}',
                                    origin: 'AssetStore');
                                await _load();
                              },
                            ),
                        ],
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}
