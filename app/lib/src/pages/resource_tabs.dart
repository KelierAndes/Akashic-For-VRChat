import 'dart:async';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';
import 'preview_page.dart';

const resourceCategories = [
  '服饰',
  '模型',
  '场景',
  '道具',
  '纹理',
  '动画',
  '音频',
  '插画',
  '文档',
  '工具',
  '其他',
];

bool resourceRowMatches(Map<String, dynamic> row, String needle) {
  if (needle.isEmpty) return true;
  for (final field in const [
    'name',
    'displayName',
    'relativePath',
    'filePath',
    'boothShop',
    'category',
  ]) {
    if ('${row[field]}'.toLowerCase().contains(needle)) return true;
  }
  final children = (row['children'] as List?) ?? const [];
  return children.any(
    (child) =>
        child is Map &&
        resourceRowMatches(child.cast<String, dynamic>(), needle),
  );
}

class UserPackagesTab extends StatefulWidget {
  const UserPackagesTab({super.key, this.query = '', this.onOpenPackage});

  final String query;
  final void Function(Map<String, dynamic> target)? onOpenPackage;

  @override
  State<UserPackagesTab> createState() => _UserPackagesTabState();
}

class _UserPackagesTabState extends State<UserPackagesTab> {
  static const filters = [
    '未匹配',
    '全部',
    'Booth',
    'Asset Store',
    ...resourceCategories,
  ];

  List<Map<String, dynamic>> _rows = [];
  List<dynamic> _folders = [];
  final Set<String> _open = {};
  String _filter = filters.first;
  String _status = '';
  bool _matching = false;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _load({bool force = false}) async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    try {
      final rows = await model.resourceRows(force: force);
      final folders = await host.libraryFolders();
      if (!mounted) return;
      setState(() {
        _rows = rows.cast<Map<String, dynamic>>();
        _folders = folders;
        _status = _summary;
      });
    } catch (error) {
      if (mounted) setState(() => _status = '读取失败：$error');
    }
  }

  String get _summary {
    final unmatched = _rows.where((row) => row['matched'] != true).length;
    final booth = _rows
        .where((row) => '${row['matchSource']}' == 'Booth')
        .length;
    final store = _rows
        .where((row) => '${row['matchSource']}' == 'AssetStore')
        .length;
    return '共 ${_rows.length} 个包 · 未匹配 $unmatched · Booth $booth · Asset Store $store';
  }

  List<Map<String, dynamic>> get _shown {
    final needle = widget.query.toLowerCase();
    if (needle.isNotEmpty) {
      return _rows.where((row) => resourceRowMatches(row, needle)).toList();
    }
    return _rows.where((row) {
      final source = '${row['matchSource']}';
      return switch (_filter) {
        '未匹配' => row['matched'] != true,
        '全部' => true,
        'Booth' => source == 'Booth',
        'Asset Store' => source == 'AssetStore',
        _ => '${row['category']}' == _filter,
      };
    }).toList();
  }

  Future<void> _startMatch({bool force = false}) async {
    final host = AppScope.of(context).host;
    if (host == null || _matching) return;

    setState(() {
      _matching = true;
      _status = force ? '正在全量重新匹配…' : '正在匹配新增的包…';
    });

    await host.startResourceMatch(force: force);
    _poll?.cancel();
    _poll = Timer.periodic(const Duration(milliseconds: 900), (_) async {
      final status = await host.resourceMatchStatus();
      if (!mounted) return;

      final scanned = status['scanned'] ?? 0;
      final total = status['total'] ?? 0;
      final current = '${status['current']}';

      setState(() {
        _status = status['running'] == true
            ? '匹配中 $scanned / $total${current.isEmpty ? '' : '：$current'}'
            : '${status['message']}';
      });

      if (status['running'] == true) return;

      _poll?.cancel();
      _poll = null;
      setState(() => _matching = false);
      await _load(force: true);
    });
  }

  Future<void> _edit(Map<String, dynamic> row) async {
    final model = AppScope.of(context);
    model.showModal(
      ResourceMetaForm(
        row: row,
        onReset: () async {
          final host = model.host;
          if (host == null) return;
          await host.resetResourceMeta('${row['metaKey']}');
          await _load(force: true);
        },
        onSubmit: (update) async {
          final host = model.host;
          if (host == null) return;
          final rows = await host.saveResourceMeta(update);
          if (!mounted) return;
          setState(() {
            _rows = rows.cast<Map<String, dynamic>>();
            _status = _summary;
          });
        },
      ),
    );
  }

  Future<void> _extract(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    setState(() => _status = '正在解压 ${row['name']}…');
    try {
      final paths = await host.extractArchive('${row['filePath']}');
      await _load(force: true);
      if (mounted) {
        setState(() => _status = '已解压 ${paths.length} 个 .unitypackage');
      }
    } catch (error) {
      if (mounted) setState(() => _status = '解压失败：$error');
    }
  }

  void _preview(
    Map<String, dynamic> row, {
    String? resourceKey,
    String? entry,
  }) {
    final host = AppScope.of(context).host;
    if (host == null) return;

    var target = row;
    final children = ((row['children'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    if (resourceKey == null && children.isNotEmpty) {
      target = children.firstWhere(
        (child) => '${child['kind']}' == 'unitypackage',
        orElse: () => children.first,
      );
    }
    if ('${target['kind']}' != 'unitypackage') return;

    final key = resourceKey ?? '${target['key']}';
    final open = widget.onOpenPackage;
    if (open != null) {
      open({
        'resourceKey': key,
        'resourceName': '${target['name']}',
        'entry': entry,
      });
      return;
    }

    final tokens = NeuTheme.of(context);
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => NeuTheme(
          tokens: tokens,
          child: PreviewPage(
            host: host,
            resourceKey: key,
            resourceName: '${target['name']}',
            entry: entry,
          ),
        ),
      ),
    );
  }

  Future<void> _addPackage() async {
    final host = AppScope.of(context).host;
    final picked = await FilePicker.pickFiles(
      dialogTitle: '选择 .unitypackage 或 .zip',
      type: FileType.custom,
      allowedExtensions: const ['unitypackage', 'zip'],
    );
    if (host == null || picked.isEmpty) return;

    final path = picked.single.path;
    if (path == null) return;
    await host.addPackageFile(path);
    await _load(force: true);
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return Column(
      children: [
        Row(
          children: [
            NeuDropdown(
              value: _filter,
              items: filters,
              icon: Icons.filter_alt_rounded,
              onChanged: (value) => setState(() => _filter = value),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                _status,
                style: TextStyle(fontSize: 12, color: t.inkTertiary),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            NeuActionButton(
              label: _matching ? '匹配中…' : '匹配 Booth',
              icon: Icons.auto_awesome_rounded,
              onTap: _matching ? null : _startMatch,
            ),
            const SizedBox(width: 6),
            _IconPoke(
              icon: Icons.restart_alt_rounded,
              tooltip: '全量重新匹配（包括已尝试过的包）',
              enabled: !_matching,
              onTap: () => _startMatch(force: true),
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '库目录',
              icon: Icons.create_new_folder_rounded,
              onTap: () async {
                final host = AppScope.of(context).host;
                final picked = await FilePicker.getDirectoryPath(
                  dialogTitle: '添加 .unitypackage 库目录',
                );
                if (host == null || picked == null) return;
                await host.addLibraryFolder(picked);
                await _load(force: true);
              },
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '添加包文件',
              icon: Icons.add_rounded,
              emphasised: true,
              onTap: _addPackage,
            ),
          ],
        ),
        const SizedBox(height: 14),
        if (_folders.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: NeuCard(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    '扫描目录',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                  ),
                  const SizedBox(height: 8),
                  for (final folder in _folders)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 4),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              '$folder',
                              style: TextStyle(
                                fontSize: 12,
                                color: t.inkSecondary,
                              ),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                          _IconPoke(
                            icon: Icons.close_rounded,
                            tooltip: '移除库目录',
                            enabled: true,
                            onTap: () async {
                              final host = AppScope.of(context).host;
                              if (host == null) return;
                              await host.removeLibraryFolder('$folder');
                              await _load(force: true);
                            },
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ),
        Expanded(
          child: shown.isEmpty
              ? NeuEmptyState(
                  height: 200,
                  text: _filter == '未匹配'
                      ? '没有未匹配的资源。\n点「匹配 Booth」让本地包按名称去找对应商品，'
                            '或切到「全部」查看已关联的资源。'
                      : '这个筛选下没有资源。',
                )
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: shown.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) => _rowCard(shown[index]),
                ),
        ),
      ],
    );
  }

  Widget _rowCard(Map<String, dynamic> row) {
    final t = NeuTheme.of(context);
    final key = '${row['key']}';
    final kind = '${row['kind']}';
    final kids = ((row['children'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    final isArchive = kind == 'archive';
    final isContainer = kind == 'archive' || kind == 'folder';
    final open = _open.contains(key);
    final tags = ((row['tags'] as List?) ?? const []).cast<String>();
    final source = '${row['matchSource']}';
    final shop = '${row['boothShop']}';
    final category = '${row['category']}';
    final matchedKids = isContainer
        ? kids.where((child) => '${child['matchSource']}'.isNotEmpty).length
        : 0;

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: () => _preview(row),
      child: NeuCard(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 13),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                NeuCover('${row['coverUrl']}'),
                const SizedBox(width: 13),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Flexible(
                            child: Text(
                              '${row['name']}',
                              style: const TextStyle(
                                fontSize: 13.5,
                                fontWeight: FontWeight.w600,
                              ),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                          if ('${row['version']}'.isNotEmpty) ...[
                            const SizedBox(width: 8),
                            NeuPill('${row['version']}'),
                          ],
                        ],
                      ),
                      const SizedBox(height: 3),
                      Text(
                        isContainer
                            ? '${isArchive ? '压缩包' : '文件夹'} · 含 ${row['childCount']} 个 .unitypackage · ${row['originLabel']}'
                            : '${shop.isNotEmpty ? '$shop · ' : ''}${row['filePath']}',
                        style: TextStyle(fontSize: 11, color: t.inkTertiary),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      const SizedBox(height: 7),
                      Wrap(
                        spacing: 7,
                        runSpacing: 6,
                        children: [
                          NeuPill(category.isEmpty ? '其他' : category),
                          if (source.isNotEmpty)
                            NeuPill(
                              source == 'Booth' ? 'Booth' : 'Asset Store',
                              bg: const Color(0xFFBFE3C9),
                              fg: const Color(0xFF237A45),
                            ),
                          if (source.isEmpty && matchedKids > 0)
                            NeuPill(
                              '包内 $matchedKids 项已匹配',
                              bg: const Color(0xFFDCE6F6),
                              fg: const Color(0xFF2C4A78),
                            ),
                          if (source.isEmpty && matchedKids == 0)
                            const NeuPill(
                              '未关联',
                              bg: Color(0xFFEBD9B4),
                              fg: Color(0xFF7A5514),
                            ),
                          if (source.isEmpty && row['attempted'] == true)
                            const NeuPill('已尝试'),
                          if (row['isManual'] == true) const NeuPill('已自定义'),
                          for (final tag in tags) NeuPill('#$tag'),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 10),
                if (source.isNotEmpty)
                  _IconPoke(
                    icon: Icons.open_in_new_rounded,
                    tooltip: '打开来源页面',
                    enabled: '${row['sourceUrl']}'.isNotEmpty,
                    onTap: () {
                      final url = '${row['sourceUrl']}';
                      if (url.isNotEmpty) AppScope.of(context).openUrl(url);
                    },
                  ),
                const SizedBox(width: 6),
                _IconPoke(
                  icon: Icons.view_in_ar_outlined,
                  tooltip: isContainer ? '预览容器里的第一个包' : '预览包内容',
                  enabled: true,
                  onTap: () => _preview(row),
                ),
                const SizedBox(width: 6),
                _IconPoke(
                  icon: Icons.edit_outlined,
                  tooltip: '编辑分类、标签、源链接与封面',
                  enabled: true,
                  onTap: () => _edit(row),
                ),
                if (isArchive) ...[
                  const SizedBox(width: 6),
                  _IconPoke(
                    icon: Icons.unarchive_outlined,
                    tooltip: '解压出 .unitypackage 并登记入库',
                    enabled: true,
                    onTap: () => _extract(row),
                  ),
                ],
                if (isContainer) ...[
                  const SizedBox(width: 6),
                  _IconPoke(
                    icon: open
                        ? Icons.expand_less_rounded
                        : Icons.expand_more_rounded,
                    tooltip: open ? '收起包内容' : '展开包内容',
                    enabled: true,
                    onTap: () => setState(() {
                      if (open) {
                        _open.remove(key);
                      } else {
                        _open.add(key);
                      }
                    }),
                  ),
                ],
                if (!isContainer) ...[
                  const SizedBox(width: 6),
                  _IconPoke(
                    icon: Icons.delete_outline_rounded,
                    tooltip: '从列表移除（不删除文件）',
                    enabled: true,
                    onTap: () async {
                      final host = AppScope.of(context).host;
                      if (host == null) return;
                      await host.removePackageFile('${row['filePath']}');
                      await _load(force: true);
                    },
                  ),
                ],
              ],
            ),
            if (open)
              Padding(
                padding: const EdgeInsets.only(left: 77, top: 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [for (final child in kids) _childRow(child)],
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _childRow(Map<String, dynamic> child) {
    final t = NeuTheme.of(context);
    final source = '${child['matchSource']}';
    final category = '${child['category']}';
    final relative = '${child['relativePath']}';

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: () => _preview(child),
        child: Row(
          children: [
            NeuCover('${child['coverUrl']}'),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          '${child['name']}',
                          style: const TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w600,
                          ),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      if ('${child['version']}'.isNotEmpty) ...[
                        const SizedBox(width: 7),
                        NeuPill('${child['version']}'),
                      ],
                    ],
                  ),
                  if (relative.contains('/'))
                    Text(
                      relative,
                      style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  const SizedBox(height: 5),
                  Wrap(
                    spacing: 6,
                    runSpacing: 5,
                    children: [
                      NeuPill(category.isEmpty ? '其他' : category),
                      if (source.isNotEmpty)
                        NeuPill(
                          source == 'Booth' ? 'Booth' : 'Asset Store',
                          bg: const Color(0xFFBFE3C9),
                          fg: const Color(0xFF237A45),
                        )
                      else
                        const NeuPill(
                          '未关联',
                          bg: Color(0xFFEBD9B4),
                          fg: Color(0xFF7A5514),
                        ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Text(
              _size((child['size'] as num?)?.toInt() ?? 0),
              style: TextStyle(fontSize: 11, color: t.inkTertiary),
            ),
            const SizedBox(width: 6),
            if (source.isNotEmpty)
              _IconPoke(
                icon: Icons.open_in_new_rounded,
                tooltip: '打开来源页面',
                enabled: '${child['sourceUrl']}'.isNotEmpty,
                onTap: () {
                  final url = '${child['sourceUrl']}';
                  if (url.isNotEmpty) AppScope.of(context).openUrl(url);
                },
              ),
            _IconPoke(
              icon: Icons.view_in_ar_outlined,
              tooltip: '预览这个包',
              enabled: true,
              onTap: () => _preview(child),
            ),
            const SizedBox(width: 6),
            _IconPoke(
              icon: Icons.edit_outlined,
              tooltip: '编辑这个包的分类、标签、源链接与封面',
              enabled: true,
              onTap: () => _edit(child),
            ),
          ],
        ),
      ),
    );
  }

  static String _size(int bytes) => bytes >= 1048576
      ? '${(bytes / 1048576).toStringAsFixed(1)} MB'
      : bytes >= 1024
      ? '${(bytes / 1024).toStringAsFixed(1)} KB'
      : '$bytes B';
}

class BoothShelfTab extends StatefulWidget {
  const BoothShelfTab({super.key, this.query = '', this.onOpenPackage});

  final String query;
  final void Function(Map<String, dynamic> target)? onOpenPackage;

  @override
  State<BoothShelfTab> createState() => _BoothShelfTabState();
}

class _BoothShelfTabState extends State<BoothShelfTab> {
  List<Map<String, dynamic>> _rows = [];
  final Set<String> _open = {};
  String _category = '全部分类';
  String _status = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  Future<void> _load({bool force = false}) async {
    final model = AppScope.of(context);
    if (model.host == null) return;

    final rows = (await model.resourceRows(force: force))
        .cast<Map<String, dynamic>>();
    final booth = rows
        .where((row) => '${row['matchSource']}' == 'Booth')
        .toList();

    if (!mounted) return;
    setState(() {
      _rows = booth;
      if (!_categories.contains(_category)) _category = '全部分类';
      _status =
          'Booth 分类下 ${booth.length} 个资源 · '
          '${booth.where((row) => '${row['coverUrl']}'.isNotEmpty).length} 个有封面';
    });
  }

  List<String> get _categories => [
    '全部分类',
    ...{
      for (final row in _rows)
        if ('${row['category']}'.isNotEmpty) '${row['category']}',
    },
  ];

  List<Map<String, dynamic>> get _shown {
    final needle = widget.query.toLowerCase();
    if (needle.isNotEmpty) {
      return _rows.where((row) => resourceRowMatches(row, needle)).toList();
    }
    return _category == '全部分类'
        ? _rows
        : _rows.where((row) => '${row['category']}' == _category).toList();
  }

  Future<void> _edit(Map<String, dynamic> row) async {
    final model = AppScope.of(context);
    model.showModal(
      ResourceMetaForm(
        row: row,
        onReset: () async {
          final host = model.host;
          if (host == null) return;
          await host.resetResourceMeta('${row['metaKey']}');
          await _load(force: true);
        },
        onSubmit: (update) async {
          final host = model.host;
          if (host == null) return;
          final rows = await host.saveResourceMeta(update);
          if (!mounted) return;
          setState(() {
            _rows = rows
                .cast<Map<String, dynamic>>()
                .where((item) => '${item['matchSource']}' == 'Booth')
                .toList();
            _status = 'Booth 分类下 ${_rows.length} 个资源';
          });
        },
      ),
    );
  }

  void _preview(Map<String, dynamic> row) {
    final host = AppScope.of(context).host;
    if (host == null) return;

    var target = row;
    final children = ((row['children'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    if (children.isNotEmpty) {
      target = children.firstWhere(
        (child) => '${child['kind']}' == 'unitypackage',
        orElse: () => children.first,
      );
    }
    if ('${target['kind']}' != 'unitypackage') return;

    final key = '${target['key']}';
    final open = widget.onOpenPackage;
    if (open != null) {
      open({
        'resourceKey': key,
        'resourceName': '${target['name']}',
        'entry': null,
      });
      return;
    }

    final tokens = NeuTheme.of(context);
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => NeuTheme(
          tokens: tokens,
          child: PreviewPage(
            host: host,
            resourceKey: key,
            resourceName: '${target['name']}',
          ),
        ),
      ),
    );
  }

  Widget _shelfCard(Map<String, dynamic> row) {
    final t = NeuTheme.of(context);
    final key = '${row['key']}';
    final kids = ((row['children'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    final open = _open.contains(key);
    final container = kids.isNotEmpty;

    final card = NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 13),
      child: Row(
        children: [
          NeuCover('${row['coverUrl']}'),
          const SizedBox(width: 13),
          Expanded(
            child: GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: () => _preview(row),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${row['name']}',
                    style: const TextStyle(
                      fontSize: 13.5,
                      fontWeight: FontWeight.w600,
                    ),
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 3),
                  Text(
                    container
                        ? '含 ${kids.length} 个 .unitypackage · ${row['boothShop']}'
                        : '${row['boothShop']} · ${row['filePath']}',
                    style: TextStyle(fontSize: 11, color: t.inkTertiary),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(width: 10),
          NeuPill('${row['category']}'.isEmpty ? '其他' : '${row['category']}'),
          const SizedBox(width: 8),
          _IconPoke(
            icon: Icons.open_in_new_rounded,
            tooltip: '打开商品页',
            enabled: '${row['sourceUrl']}'.isNotEmpty,
            onTap: () {
              final url = '${row['sourceUrl']}';
              if (url.isNotEmpty) {
                AppScope.of(context).openUrl(url);
              }
            },
          ),
          const SizedBox(width: 6),
          _IconPoke(
            icon: Icons.view_in_ar_outlined,
            tooltip: '预览 prefab 模型与组件',
            enabled: true,
            onTap: () => _preview(row),
          ),
          const SizedBox(width: 6),
          _IconPoke(
            icon: Icons.edit_outlined,
            tooltip: '编辑分类、标签、源链接与封面',
            enabled: true,
            onTap: () => _edit(row),
          ),
          if (container) ...[
            const SizedBox(width: 6),
            _IconPoke(
              icon: open
                  ? Icons.expand_less_rounded
                  : Icons.expand_more_rounded,
              tooltip: open ? '收起压缩包内容' : '展开压缩包内容',
              enabled: true,
              onTap: () => setState(() {
                if (open) {
                  _open.remove(key);
                } else {
                  _open.add(key);
                }
              }),
            ),
          ],
        ],
      ),
    );

    if (!container || !open) return card;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        card,
        Padding(
          padding: const EdgeInsets.fromLTRB(28, 8, 0, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [for (final child in kids) _shelfChild(child)],
          ),
        ),
      ],
    );
  }

  Widget _shelfChild(Map<String, dynamic> child) {
    final t = NeuTheme.of(context);
    final source = '${child['matchSource']}';
    final relative = '${child['relativePath']}';

    return Padding(
      padding: const EdgeInsets.only(bottom: 7),
      child: Row(
        children: [
          Icon(Icons.inventory_2_outlined, size: 14, color: t.inkTertiary),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${child['name']}',
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w500,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
                if (relative.contains('/'))
                  Text(
                    relative,
                    style: TextStyle(fontSize: 10, color: t.inkTertiary),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
              ],
            ),
          ),
          if ('${child['category']}'.isNotEmpty) ...[
            NeuPill('${child['category']}'),
            const SizedBox(width: 6),
          ],
          if (source.isNotEmpty)
            const Padding(
              padding: EdgeInsets.only(right: 6),
              child: NeuPill(
                'Booth',
                bg: Color(0xFFBFE3C9),
                fg: Color(0xFF237A45),
              ),
            ),
          IconButton(
            tooltip: '预览这个包',
            iconSize: 16,
            visualDensity: VisualDensity.compact,
            onPressed: () => _preview(child),
            icon: Icon(Icons.view_in_ar_outlined, color: t.inkSecondary),
          ),
          IconButton(
            tooltip: '编辑这个包',
            iconSize: 16,
            visualDensity: VisualDensity.compact,
            onPressed: () => _edit(child),
            icon: Icon(Icons.edit_outlined, color: t.inkSecondary),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return Column(
      children: [
        Row(
          children: [
            NeuDropdown(
              value: _category,
              items: _categories,
              icon: Icons.category_rounded,
              onChanged: (value) => setState(() => _category = value),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                _status,
                style: TextStyle(fontSize: 12, color: t.inkTertiary),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            NeuActionButton(
              label: '去 Booth',
              icon: Icons.shopping_bag_rounded,
              onTap: () => AppScope.of(context).jumpTo('booth'),
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '刷新',
              icon: Icons.refresh_rounded,
              onTap: () => _load(force: true),
            ),
          ],
        ),
        const SizedBox(height: 14),
        Expanded(
          child: shown.isEmpty
              ? const NeuEmptyState(
                  height: 200,
                  text:
                      'Booth 分类下还没有资源。\n到「用户软件包」点「匹配 Booth」，'
                      '按名称匹配到的包会自动转到这里。',
                )
              : ListView.separated(
                  padding: EdgeInsets.zero,
                  itemCount: shown.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final row = shown[index];
                    return _shelfCard(row);
                  },
                ),
        ),
      ],
    );
  }
}

class ResourceMetaForm extends StatefulWidget {
  const ResourceMetaForm({
    super.key,
    required this.row,
    required this.onSubmit,
    required this.onReset,
  });

  final Map<String, dynamic> row;
  final Future<void> Function(Map<String, dynamic> update) onSubmit;
  final Future<void> Function() onReset;

  @override
  State<ResourceMetaForm> createState() => _ResourceMetaFormState();
}

class _ResourceMetaFormState extends State<ResourceMetaForm> {
  late final TextEditingController _tags = TextEditingController(
    text: ((widget.row['tags'] as List?) ?? const []).join('，'),
  );
  late final TextEditingController _source = TextEditingController(
    text: '${widget.row['sourceUrl']}',
  );
  late final TextEditingController _cover = TextEditingController(
    text: '${widget.row['coverUrl']}'.startsWith('http')
        ? '${widget.row['coverUrl']}'
        : '',
  );
  late int _category = _initialCategory();

  int _initialCategory() {
    final index = resourceCategories.indexOf('${widget.row['category']}');
    return index < 0 ? resourceCategories.length - 1 : index;
  }

  @override
  void dispose() {
    _tags.dispose();
    _source.dispose();
    _cover.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final model = AppScope.of(context);
    final update = <String, dynamic>{
      'key': '${widget.row['metaKey']}',
      'category': resourceCategories[_category],
      'tags': _tags.text
          .split(RegExp(r'[,，]'))
          .map((tag) => tag.trim())
          .where((tag) => tag.isNotEmpty)
          .toList(),
      'sourceUrl': _source.text.trim(),
      'coverUrl': _cover.text.trim(),
    };

    model.closeModal();
    await widget.onSubmit(update);
  }

  Future<void> _reset() async {
    final model = AppScope.of(context);
    model.closeModal();
    await widget.onReset();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);

    return NeuModalScaffold(
      title: '编辑资源信息',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: '名称',
            child: Text(
              '${widget.row['name']}',
              style: TextStyle(
                fontSize: 13,
                color: NeuTheme.of(context).inkSecondary,
              ),
            ),
          ),
          NeuField(
            label: '分类',
            child: NeuChoiceField(
              values: resourceCategories,
              index: _category,
              icon: Icons.category_rounded,
              onChanged: (value) => setState(() => _category = value),
            ),
          ),
          NeuField(
            label: '标签',
            hint: '用逗号分隔，例如：常用，QvPen 补充',
            child: NeuTextInput(controller: _tags, hint: '常用，待整理'),
          ),
          NeuField(
            label: '源链接',
            hint: '商品页或下载页，留空表示没有',
            child: NeuTextInput(controller: _source, hint: 'https://…'),
          ),
          NeuField(
            label: '封面',
            hint: '图片地址或本地文件路径；留空时自动使用包内预览图',
            child: NeuTextInput(controller: _cover, hint: 'https://…'),
          ),
          const SizedBox(height: 6),
          Row(
            children: [
              NeuActionButton(
                label: '恢复自动分类',
                icon: Icons.restart_alt_rounded,
                onTap: _reset,
              ),
              const Spacer(),
              NeuActionButton(label: '取消', onTap: model.closeModal),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '确定',
                icon: Icons.check_rounded,
                emphasised: true,
                onTap: _submit,
              ),
            ],
          ),
        ],
      ),
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
    return Semantics(
      label: tooltip,
      button: true,
      enabled: enabled,
      child: Tooltip(
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
          child: Icon(
            icon,
            size: 16,
            color: enabled
                ? t.inkSecondary
                : t.inkTertiary.withValues(alpha: 0.4),
          ),
        ),
      ),
    );
  }
}
