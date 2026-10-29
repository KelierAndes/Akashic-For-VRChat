import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

import '../host/host_client.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';
import 'asset_viewer.dart';

class PreviewPage extends StatefulWidget {
  const PreviewPage({
    super.key,
    required this.host,
    required this.resourceKey,
    required this.resourceName,
    this.entry,
    this.initialAsset,
  });

  final HostClient host;
  final String resourceKey;
  final String resourceName;
  final String? entry;
  final Map<String, dynamic>? initialAsset;

  @override
  State<PreviewPage> createState() => _PreviewPageState();
}

class _PreviewPageState extends State<PreviewPage> {
  final List<Map<String, dynamic>> _assets = [];
  final TextEditingController _search = TextEditingController();
  String _kindFilter = '';
  Map<String, dynamic>? _picked;
  Map<String, dynamic>? _scene;
  Map<String, dynamic>? _selected;
  final Set<String> _collapsed = {};
  InAppWebViewController? _web;
  String _viewerUrl = '';
  String _error = '';
  bool _loading = true;
  bool _building = false;
  bool _pickerVisible = false;
  bool _flat = false;
  bool _hlsl = false;
  Map<String, dynamic>? _viewer;
  Map<String, dynamic>? _renderInfo;
  Timer? _renderPoll;
  String _sessionId = '';
  List<Map<String, dynamic>> _variants = const [];
  bool _variantsAvailable = false;
  Set<String> _materialKeywords = {};
  final Set<String> _manualKeywords = {};
  String _colorSpace = 'gamma';
  int _variantLimit = 2;
  List<String> _engineKeywordsOff = const [];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadAssets());
  }

  @override
  void dispose() {
    _renderPoll?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _loadAssets() async {
    final host = widget.host;

    setState(() {
      _loading = true;
      _error = '';
    });

    try {
      final rows = await host.previewList(
        widget.resourceKey,
        entry: widget.entry,
      );
      final assets = rows
          .cast<Map<String, dynamic>>()
          .map((row) => Map<String, dynamic>.from(row))
          .toList();

      assets.sort((a, b) {
        final ka = '${a['kind']}' == 'prefab' ? 0 : 1;
        final kb = '${b['kind']}' == 'prefab' ? 0 : 1;
        if (ka != kb) return ka - kb;
        final folder = '${a['folder']}'.toLowerCase().compareTo(
          '${b['folder']}'.toLowerCase(),
        );
        if (folder != 0) return folder;
        return '${a['name']}'.toLowerCase().compareTo(
          '${b['name']}'.toLowerCase(),
        );
      });

      if (!mounted) return;
      final initial = widget.initialAsset;
      final target = initial == null
          ? null
          : assets.cast<Map<String, dynamic>?>().firstWhere(
              (asset) => '${asset?['path']}' == '${initial['path']}',
              orElse: () => null,
            );

      setState(() {
        _assets
          ..clear()
          ..addAll(assets);
        _loading = false;
        _kindFilter = '';
        _pickerVisible = target == null && assets.length != 1;
      });

      if (target != null) {
        await _build(target);
      } else if (assets.length == 1) {
        await _build(assets.first);
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = '$error';
      });
    }
  }

  Future<void> _build(Map<String, dynamic> asset) async {
    final host = widget.host;
    final path = '${asset['path']}';

    _renderPoll?.cancel();
    _renderPoll = null;
    setState(() {
      _building = true;
      _error = '';
      _viewer = null;
      _viewerUrl = '';
      _picked = asset;
      _pickerVisible = false;
      _selected = null;
      _flat = false;
      _hlsl = false;
      _renderInfo = null;
      _sessionId = '';
      _variants = const [];
      _variantsAvailable = false;
      _materialKeywords = {};
      _manualKeywords.clear();
      _colorSpace = 'gamma';
      _collapsed.clear();
    });

    try {
      final result = await host.previewBuild(
        widget.resourceKey,
        path,
        entry: widget.entry,
      );
      if (!mounted) return;

      setState(() {
        _scene = Map<String, dynamic>.from(result['scene'] as Map);
        _viewerUrl = host.absolute('${result['viewerUrl']}');
        _sessionId = '${result['sessionId']}';
        _building = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _building = false;
        _error = '$error';
      });
    }
  }

  void _openAsset(Map<String, dynamic> asset) {
    final kind = '${asset['kind']}';
    if (kind == 'prefab' || kind == 'model') {
      setState(() => _viewer = null);
      _build(asset);
      return;
    }

    setState(() {
      _viewer = asset;
      _pickerVisible = true;
    });
  }

  void _closeViewer() {
    setState(() {
      _viewer = null;
    });
  }

  void _back() {
    if (_pickerVisible && _viewer != null) {
      _closeViewer();
      return;
    }

    if (_pickerVisible) {
      Navigator.of(context).pop();
      return;
    }

    Navigator.of(context).pop();
  }

  void _select(Map<String, dynamic> node) {
    setState(() => _selected = node);
    final id = '${node['id']}'.replaceAll("'", "\\'");
    _web?.evaluateJavascript(source: "window.akashicSelect('$id')");
  }

  void _focus(Map<String, dynamic> node) {
    _select(node);
    final id = '${node['id']}'.replaceAll("'", "\\'");
    _web?.evaluateJavascript(source: "window.akashicFocus('$id')");
  }

  Map<String, dynamic>? _findNode(String id) {
    Map<String, dynamic>? walk(List nodes) {
      for (final entry in nodes) {
        final node = Map<String, dynamic>.from(entry as Map);
        if ('${node['id']}' == id) return node;
        final found = walk(((node['children'] as List?) ?? const []));
        if (found != null) return found;
      }
      return null;
    }

    return walk(((_scene?['nodes'] as List?) ?? const []));
  }

  Future<void> _toggleHlsl() async {
    final next = !_hlsl;
    setState(() {
      _hlsl = next;
      if (next) _renderInfo = null;
    });

    await _web?.evaluateJavascript(
      source: 'window.akashicSetHlsl && window.akashicSetHlsl($next)',
    );

    if (!next) {
      _renderPoll?.cancel();
      _renderPoll = null;
      return;
    }

    await Future<void>.delayed(const Duration(milliseconds: 1500));
    final state = await _web?.evaluateJavascript(
      source: 'window.akashicHlslState ? JSON.stringify(window.akashicHlslState()) : ""',
    );

    if (!mounted) return;
    if ('$state'.contains('"enabled":false')) {
      setState(() => _hlsl = false);
      return;
    }

    await _refreshRenderInfo();
    _startRenderPoll();
    await _loadVariants();
  }

  Future<void> _loadVariants() async {
    if (_sessionId.isEmpty) return;

    try {
      final data = await widget.host.previewVariants(_sessionId);
      if (!mounted) return;

      final options = ((data['options'] as List?) ?? const [])
          .map((entry) => Map<String, dynamic>.from((entry as Map).cast<String, dynamic>()))
          .toList();
      final engine = Map<String, dynamic>.from(
          ((data['engine'] as Map?) ?? const {}).cast<String, dynamic>());

      setState(() {
        _variants = options;
        _variantsAvailable = data['available'] == true;
        _variantLimit = (data['maxManual'] as num?)?.toInt() ?? 2;
        _materialKeywords = ((data['enabled'] as List?) ?? const [])
            .cast<String>()
            .toSet();
        _engineKeywordsOff =
            ((engine['alwaysOff'] as List?) ?? const []).cast<String>();
      });
    } catch (_) {}
  }

  Future<void> _applyVariants() async {
    final list = _manualKeywords.take(_variantLimit).toList();
    final encoded = list.map((keyword) => "'${keyword.replaceAll("'", '')}'").join(',');
    await _web?.evaluateJavascript(
      source: 'window.akashicSetVariants && '
          'window.akashicSetVariants([$encoded], "$_colorSpace")',
    );
    await _refreshRenderInfo();
  }

  void _toggleVariant(String keyword) {
    setState(() {
      if (_materialKeywords.contains(keyword)) return;
      if (_manualKeywords.contains(keyword)) {
        _manualKeywords.remove(keyword);
      } else if (_manualKeywords.length < _variantLimit) {
        _manualKeywords.add(keyword);
      } else {
        return;
      }
    });
    _applyVariants();
  }

  void _setColorSpace(String space) {
    if (_colorSpace == space) return;
    setState(() => _colorSpace = space);
    _applyVariants();
  }

  void _resetVariants() {
    setState(() {
      _manualKeywords.clear();
      _colorSpace = 'gamma';
    });
    _applyVariants();
  }

  void _startRenderPoll() {
    _renderPoll?.cancel();
    _renderPoll = Timer.periodic(const Duration(milliseconds: 900), (_) {
      _refreshRenderInfo();
    });
  }

  Future<void> _refreshRenderInfo() async {
    if (_pickerVisible) return;
    final raw = await _web?.evaluateJavascript(
      source: 'window.akashicHlslInfo ? JSON.stringify(window.akashicHlslInfo()) : ""',
    );
    if (!mounted || raw == null || '$raw'.isEmpty || '$raw' == 'null') return;

    try {
      final decoded = jsonDecode('$raw');
      if (decoded is! Map) return;
      final info = Map<String, dynamic>.from(decoded);
      setState(() => _renderInfo = info);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return Scaffold(
      backgroundColor: t.raisedHi,
      body: SafeArea(
        child: Column(
          children: [
            _header(t),
            Expanded(
              child: _pickerVisible
                  ? _picker(t)
                  : Row(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        SizedBox(width: 260, child: _hierarchy(t)),
                        Expanded(child: _viewport(t)),
                        SizedBox(width: 300, child: _inspector(t)),
                      ],
                    ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _header(NeuTokens t) {
    final nodes = ((_scene?['nodes'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();

    return Container(
      height: 56,
      padding: const EdgeInsets.symmetric(horizontal: 16),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.035),
        border: Border(
          bottom: BorderSide(color: Colors.black.withValues(alpha: 0.06)),
        ),
      ),
      child: Row(
        children: [
          NeuButton(
            width: 34,
            height: 34,
            radius: 10,
            depth: 4,
            onTap: _back,
            child: Icon(
              Icons.arrow_back_rounded,
              size: 18,
              color: t.inkSecondary,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _pickerVisible
                      ? '选择要预览的内容'
                      : '${_scene?['name'] ?? widget.resourceName}',
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
                if (_pickerVisible)
                  Text(
                    '${_assets.length} 个文件 · '
                    '${_assets.where((row) => '${row['kind']}' == 'prefab' || '${row['kind']}' == 'model').length} 个可建模 · '
                    '${_assets.where((row) => '${row['kind']}' == 'texture').length} 张贴图 · '
                    '${_assets.where((row) => '${row['kind']}' == 'material').length} 个材质',
                    style: TextStyle(fontSize: 11, color: t.inkTertiary),
                  )
                else if ('${_picked?['folder']}'.isNotEmpty)
                  Text(
                    '${_picked?['folder']} · ${_picked?['kind'] == 'model' ? '模型' : 'Prefab'}',
                    style: TextStyle(fontSize: 11, color: t.inkTertiary),
                    overflow: TextOverflow.ellipsis,
                  )
                else if (nodes.isNotEmpty)
                  Text(
                    '${nodes.length} 个顶层节点 · ${_scene?['meshCount'] ?? 0} 网格 · ${_scene?['materialCount'] ?? 0} 材质',
                    style: TextStyle(fontSize: 11, color: t.inkTertiary),
                  ),
              ],
            ),
          ),
          if (!_pickerVisible && _assets.length > 1) ...[
            NeuActionButton(
              label: '切换内容',
              icon: Icons.list_alt_rounded,
              onTap: _building
                  ? null
                  : () => setState(() => _pickerVisible = true),
            ),
            const SizedBox(width: 10),
          ],
          if (!_pickerVisible) ...[
            NeuActionButton(
              label: _hlsl ? '返回近似' : 'HLSL 精确',
              icon: _hlsl
                  ? Icons.threed_rotation_rounded
                  : Icons.precision_manufacturing_rounded,
              onTap: _scene == null ? null : _toggleHlsl,
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: _flat ? '恢复光照' : '平光预览',
              icon: _flat ? Icons.light_mode_rounded : Icons.flash_off_rounded,
              onTap: _scene == null || _hlsl
                  ? null
                  : () {
                      setState(() => _flat = !_flat);
                      _web?.evaluateJavascript(
                        source:
                            'window.akashicSetFlat && window.akashicSetFlat($_flat)',
                      );
                    },
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '重置视角',
              icon: Icons.center_focus_strong_rounded,
              onTap: _scene == null
                  ? null
                  : () => _web?.evaluateJavascript(
                      source: 'window.akashicResetView && window.akashicResetView()',
                    ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _picker(NeuTokens t) {
    final query = _search.text.trim().toLowerCase();
    final assets = _assets.where((row) {
      if (query.isEmpty) return true;
      return '${row['name']}'.toLowerCase().contains(query) ||
          '${row['folder']}'.toLowerCase().contains(query) ||
          '${row['path']}'.toLowerCase().contains(query);
    }).toList();

    return Padding(
      padding: const EdgeInsets.fromLTRB(24, 18, 24, 20),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(flex: 5, child: _assetList(assets, t)),
          if (_viewer != null) ...[
            const SizedBox(width: 18),
            Expanded(flex: 6, child: _viewerPane(_viewer!, t)),
          ],
        ],
      ),
    );
  }

  Widget _assetList(List<Map<String, dynamic>> assets, NeuTokens t) {
    final counts = <String, int>{};
    for (final asset in _assets) {
      final kind = '${asset['kind']}';
      counts[kind] = (counts[kind] ?? 0) + 1;
    }

    final kinds = <String>[
      ...assetKindOrder.where((kind) => (counts[kind] ?? 0) > 0),
      ...counts.keys.where((kind) => !assetKindOrder.contains(kind)),
    ];
    final visible = _kindFilter.isEmpty
        ? assets
        : assets.where((asset) => '${asset['kind']}' == _kindFilter).toList();
    final entries = _assetEntries(visible, kinds);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: NeuTextInput(
                controller: _search,
                hint: '按名称或目录筛选包内文件',
                onChanged: (_) => setState(() {}),
              ),
            ),
            const SizedBox(width: 12),
            NeuActionButton(
              label: '重新读取',
              icon: Icons.refresh_rounded,
              onTap: _loading ? null : _loadAssets,
            ),
          ],
        ),
        if (kinds.length > 1) ...[
          const SizedBox(height: 10),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                _kindChip('全部', _assets.length, t),
                for (final kind in kinds)
                  Padding(
                    padding: const EdgeInsets.only(left: 6),
                    child: _kindChip(
                      assetKindLabel(kind),
                      counts[kind] ?? 0,
                      t,
                      kind: kind,
                    ),
                  ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 14),
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
              : _assets.isEmpty
              ? Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        _error.isEmpty ? '这个包里没有解析出任何文件。' : _error,
                        textAlign: TextAlign.center,
                        style: TextStyle(fontSize: 12, color: t.inkTertiary),
                      ),
                      const SizedBox(height: 14),
                      NeuActionButton(
                        label: '重试',
                        icon: Icons.refresh_rounded,
                        onTap: _loadAssets,
                      ),
                    ],
                  ),
                )
              : visible.isEmpty
              ? Center(
                  child: Text(
                    '没有匹配的内容。',
                    style: TextStyle(fontSize: 12, color: t.inkTertiary),
                  ),
                )
              : ListView.builder(
                  padding: const EdgeInsets.only(bottom: 12),
                  itemCount: entries.length,
                  itemBuilder: (context, index) {
                    final entry = entries[index];
                    if (entry is Map<String, dynamic>) {
                      return Padding(
                        padding: const EdgeInsets.only(bottom: 9),
                        child: _assetTile(entry, t),
                      );
                    }
                    return _kindHeader(
                      '$entry',
                      counts['$entry'] ?? 0,
                      t,
                      index == 0,
                    );
                  },
                ),
        ),
      ],
    );
  }

  Widget _kindChip(String label, int count, NeuTokens t, {String kind = ''}) {
    final on = _kindFilter == kind;
    return NeuPill(
      '$label $count',
      bg: on ? const Color(0xFF2F3238) : null,
      fg: on ? Colors.white : null,
      onTap: () => setState(() => _kindFilter = kind),
    );
  }

  List<Object> _assetEntries(
    List<Map<String, dynamic>> assets,
    List<String> kinds,
  ) {
    if (_kindFilter.isNotEmpty) return List<Object>.from(assets);

    final entries = <Object>[];
    for (final kind in kinds) {
      final group = assets
          .where((asset) => '${asset['kind']}' == kind)
          .toList();
      if (group.isEmpty) continue;
      entries.add(kind);
      entries.addAll(group);
    }
    return entries;
  }

  Widget _kindHeader(String kind, int count, NeuTokens t, bool first) {
    return Padding(
      padding: EdgeInsets.only(top: first ? 0 : 10, bottom: 7, left: 2),
      child: Row(
        children: [
          Icon(assetKindIcon(kind), size: 13, color: t.inkTertiary),
          const SizedBox(width: 6),
          Text(
            assetKindLabel(kind),
            style: TextStyle(
              fontSize: 11,
              fontWeight: FontWeight.w600,
              color: t.inkTertiary,
            ),
          ),
          const SizedBox(width: 6),
          Text(
            '$count',
            style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          ),
        ],
      ),
    );
  }

  Widget _viewerPane(Map<String, dynamic> asset, NeuTokens t) {
    return NeuCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '查看文件',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                    color: t.inkSecondary,
                  ),
                ),
              ),
              NeuActionButton(
                label: '关闭',
                icon: Icons.close_rounded,
                onTap: _closeViewer,
              ),
            ],
          ),
          const SizedBox(height: 10),
          Expanded(
            child: AssetViewer(
              key: ValueKey('${asset['path']}'),
              host: widget.host,
              resourceKey: widget.resourceKey,
              asset: asset,
              entry: widget.entry,
            ),
          ),
        ],
      ),
    );
  }

  Widget _assetTile(Map<String, dynamic> asset, NeuTokens t) {
    final kind = '${asset['kind']}';
    final previewable = kind == 'prefab' || kind == 'model';
    final current = _picked?['path'] == asset['path'];
    final opened = _viewer?['path'] == asset['path'];
    final folder = '${asset['folder']}';

    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.045),
              borderRadius: BorderRadius.circular(9),
            ),
            child: Icon(assetKindIcon(kind), size: 19, color: t.inkSecondary),
          ),
          const SizedBox(width: 13),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        '${asset['name']}',
                        style: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    if (current) ...[
                      const SizedBox(width: 8),
                      const NeuPill(
                        '当前',
                        bg: Color(0xFFBFE3C9),
                        fg: Color(0xFF237A45),
                      ),
                    ],
                    if (opened && !current) ...[
                      const SizedBox(width: 8),
                      const NeuPill('已打开'),
                    ],
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  folder.isEmpty ? '包根目录' : folder,
                  style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          NeuPill(assetKindLabel(kind)),
          const SizedBox(width: 8),
          Text(
            formatAssetBytes((asset['size'] as num?)?.toInt() ?? 0),
            style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          ),
          const SizedBox(width: 12),
          NeuActionButton(
            label: previewable ? '预览' : '查看',
            icon: previewable
                ? Icons.play_arrow_rounded
                : Icons.visibility_outlined,
            onTap: _building ? null : () => _openAsset(asset),
          ),
        ],
      ),
    );
  }

  Widget _hierarchy(NeuTokens t) {
    final nodes = ((_scene?['nodes'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();

    return Container(
      decoration: BoxDecoration(
        border: Border(
          right: BorderSide(color: Colors.black.withValues(alpha: 0.06)),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 14, 16, 8),
            child: Text(
              '层级',
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: t.inkSecondary,
              ),
            ),
          ),
          Expanded(
            child: _loading || _building
                ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
                : nodes.isEmpty
                ? Center(
                    child: Text(
                      _error.isEmpty ? '这个包里没有 prefab。' : _error,
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 12, color: t.inkTertiary),
                    ),
                  )
                : Builder(
                    builder: (context) {
                      final rows = _flatRows(nodes);
                      return ListView.builder(
                        padding: const EdgeInsets.only(bottom: 16),
                        itemCount: rows.length,
                        itemBuilder: (context, index) =>
                            _nodeRow(rows[index].$1, rows[index].$2, t),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }

  Widget _nodeRow(Map<String, dynamic> node, int depth, NeuTokens t) {
    final id = '${node['id']}';
    final children = ((node['children'] as List?) ?? const []);
    final selected = _selected?['id'] == node['id'];
    final mesh = node['mesh'] as Map?;
    final active = node['active'] != false;

    return InkWell(
      onTap: () => _select(node),
      onDoubleTap: () => _focus(node),
      child: Container(
        padding: EdgeInsets.fromLTRB(8 + depth * 14.0, 5, 10, 5),
        color: selected ? Colors.black.withValues(alpha: 0.06) : null,
        child: Row(
          children: [
            SizedBox(
              width: 18,
              child: children.isEmpty
                  ? const SizedBox.shrink()
                  : GestureDetector(
                      onTap: () => setState(() {
                        if (!_collapsed.remove(id)) _collapsed.add(id);
                      }),
                      child: Icon(
                        _collapsed.contains(id)
                            ? Icons.chevron_right_rounded
                            : Icons.expand_more_rounded,
                        size: 15,
                        color: t.inkTertiary,
                      ),
                    ),
            ),
            Icon(
              mesh != null
                  ? Icons.view_in_ar_rounded
                  : Icons.crop_square_rounded,
              size: 13,
              color: active
                  ? (mesh != null ? const Color(0xFF4F86D6) : t.inkTertiary)
                  : t.inkTertiary,
            ),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                '${node['name']}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 12,
                  color: active ? t.ink : t.inkTertiary,
                  fontWeight: selected ? FontWeight.w600 : FontWeight.w400,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  List<(Map<String, dynamic>, int)> _flatRows(
    List<Map<String, dynamic>> nodes,
  ) {
    final rows = <(Map<String, dynamic>, int)>[];

    void walk(List<Map<String, dynamic>> list, int depth) {
      for (final node in list) {
        rows.add((node, depth));
        final children = ((node['children'] as List?) ?? const [])
            .cast<Map<String, dynamic>>();
        if (children.isNotEmpty && !_collapsed.contains('${node['id']}')) {
          walk(children, depth + 1);
        }
      }
    }

    walk(nodes, 0);
    return rows;
  }

  Widget _viewport(NeuTokens t) {
    if (_error.isNotEmpty && _scene == null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('预览失败', style: TextStyle(fontSize: 14, color: t.warn)),
              const SizedBox(height: 8),
              Text(
                _error,
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 12, color: t.inkSecondary),
              ),
              const SizedBox(height: 16),
              NeuActionButton(
                label: '重试',
                icon: Icons.refresh_rounded,
                onTap: _loadAssets,
              ),
            ],
          ),
        ),
      );
    }

    return Stack(
      children: [
        if (_viewerUrl.isNotEmpty)
          InAppWebView(
            key: ValueKey(_viewerUrl),
            initialUrlRequest: URLRequest(url: WebUri(_viewerUrl)),
            initialSettings: InAppWebViewSettings(
              javaScriptEnabled: true,
              clearCache: false,
              supportZoom: false,
              disableHorizontalScroll: true,
              disableVerticalScroll: true,
            ),
            onWebViewCreated: (controller) {
              _web = controller;
              controller.addJavaScriptHandler(
                handlerName: 'akashicPick',
                callback: (args) {
                  final id = args.isNotEmpty ? '${args.first}' : '';
                  final node = _findNode(id);
                  if (node != null && mounted) setState(() => _selected = node);
                  return null;
                },
              );
              _startRenderPoll();
            },
            onLoadStop: (controller, url) {
              if (_flat) {
                controller.evaluateJavascript(
                  source:
                      'window.akashicSetFlat && window.akashicSetFlat(true)',
                );
              }
            },
          ),
        if (_loading || _building)
          ColoredBox(
            color: t.raisedHi,
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const CircularProgressIndicator(strokeWidth: 2),
                  const SizedBox(height: 14),
                  Text(
                    _building ? '正在解析 prefab 并生成模型…' : '正在读取包内容…',
                    style: TextStyle(fontSize: 12, color: t.inkSecondary),
                  ),
                ],
              ),
            ),
          ),
        if (!_loading && !_building && _error.isNotEmpty && _scene != null)
          Positioned(
            left: 12,
            right: 12,
            bottom: 12,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
              decoration: BoxDecoration(
                color: t.warn.withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                _error,
                style: TextStyle(fontSize: 11, color: t.warn),
              ),
            ),
          ),
      ],
    );
  }

  Widget _inspector(NeuTokens t) {
    final node = _selected;
    final components = ((node?['components'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    final materials = ((node?['materials'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    final mesh = node?['mesh'] as Map?;

    return Container(
      decoration: BoxDecoration(
        border: Border(
          left: BorderSide(color: Colors.black.withValues(alpha: 0.06)),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 14, 16, 8),
            child: Text(
              '组件',
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: t.inkSecondary,
              ),
            ),
          ),
          Expanded(
            flex: 5,
            child: node == null
                ? Center(
                    child: Text(
                      '选择左侧节点查看组件与变换',
                      style: TextStyle(fontSize: 12, color: t.inkTertiary),
                    ),
                  )
                : ListView(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 20),
                    children: [
                      Text(
                        '${node['name']}',
                        style: const TextStyle(
                          fontSize: 13.5,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        'ID ${node['id']}',
                        style: TextStyle(fontSize: 10, color: t.inkTertiary),
                      ),
                      const SizedBox(height: 12),
                      _section(t, 'Transform'),
                      _vector(t, '位置', node['position']),
                      _vector(t, '旋转', node['rotation']),
                      _vector(t, '缩放', node['scale']),
                      if (mesh != null) ...[
                        const SizedBox(height: 10),
                        _section(t, '网格'),
                        _keyValue(t, '模型', '${mesh['assetPath']}'),
                        _keyValue(t, '子网格', '${mesh['subMesh']}'),
                        if (mesh['skinned'] == true) _keyValue(t, '蒙皮', '是'),
                      ],
                      if (materials.isNotEmpty) ...[
                        const SizedBox(height: 10),
                        _section(t, '材质'),
                        for (final material in materials)
                          _material(t, material),
                      ],
                      if (components.isNotEmpty) ...[
                        const SizedBox(height: 10),
                        _section(t, '挂载组件'),
                        for (final component in components)
                          _component(t, component),
                      ],
                    ],
                  ),
          ),
          Container(height: 1, color: Colors.black.withValues(alpha: 0.06)),
          Expanded(flex: 3, child: _renderPanel(t)),
        ],
      ),
    );
  }

  Widget _renderPanel(NeuTokens t) {
    final info = _renderInfo;
    final nodes = ((_scene?['nodes'] as List?) ?? const []).length;
    final meshes = '${_scene?['meshCount'] ?? 0}';
    final materials = '${_scene?['materialCount'] ?? 0}';
    final mode = switch ('${info?['mode'] ?? ''}') {
      'clay' => '白膜',
      'wireframe' => '网格',
      'textured' => '贴图',
      _ => '—',
    };
    final detail = '${info?['detail'] ?? ''}'.trim();
    final error = '${info?['error'] ?? ''}'.trim();
    final fallbacks = '${info?['fallbacks'] ?? ''}';

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 18),
      children: [
        Row(
          children: [
            Icon(Icons.speed_rounded, size: 14, color: t.inkSecondary),
            const SizedBox(width: 6),
            Text(
              '渲染信息',
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: t.inkSecondary,
              ),
            ),
            const Spacer(),
            NeuPill(_hlsl ? 'HLSL' : '实时 3D'),
          ],
        ),
        const SizedBox(height: 9),
        _infoRow(t, '场景', '$nodes 节点 · $meshes 网格 · $materials 材质'),
        if (_scene == null) ...[
          const SizedBox(height: 10),
          Text(
            '打开一个 prefab 后，这里会显示离屏渲染的统计数据。',
            style: TextStyle(fontSize: 11, color: t.inkTertiary),
          ),
        ] else if (!_hlsl) ...[
          const SizedBox(height: 4),
          _infoRow(t, '渲染', 'Three.js 近似（PBR / 卡通）'),
          _infoRow(t, '真实着色器', '打开右上角「HLSL 精确」开启'),
        ] else if (info == null) ...[
          const SizedBox(height: 10),
          Row(
            children: [
              const SizedBox(
                width: 13,
                height: 13,
                child: CircularProgressIndicator(strokeWidth: 1.6),
              ),
              const SizedBox(width: 8),
              Text(
                '正在等待渲染结果…',
                style: TextStyle(fontSize: 11, color: t.inkTertiary),
              ),
            ],
          ),
        ] else ...[
          _infoRow(t, '模式', mode),
          _infoRow(t, '绘制', '${info['draws']} 次'),
          _infoRow(t, '抗锯齿', _aaLabel('${info['msaa'] ?? ''}')),
          _infoRow(t, '贴图', _textureLabel('${info['textures'] ?? ''}')),
          _infoRow(
            t,
            '材质',
            fallbacks == '0' ? '全部使用真实着色器' : '$fallbacks 个回退材质',
          ),
          if (error.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              error,
              style: TextStyle(fontSize: 11, height: 1.45, color: t.warn),
            ),
          ] else if (detail.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text('回退明细', style: TextStyle(fontSize: 11, color: t.inkSecondary)),
            const SizedBox(height: 3),
            SelectableText(
              detail.replaceAll(' | ', '\n'),
              style: TextStyle(
                fontSize: 10.5,
                height: 1.45,
                color: t.inkTertiary,
              ),
            ),
          ],
          if (_variantsAvailable) _variantSection(t),
        ],
      ],
    );
  }

  Widget _variantSection(NeuTokens t) {
    final declared = _variants
        .map((entry) => '${entry['keyword']}')
        .where((keyword) => keyword.isNotEmpty);
    final blocked = declared.where(
        (keyword) => _engineKeywordsOff.contains(keyword)).toList();
    final full = _manualKeywords.length >= _variantLimit;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 10),
        Row(
          children: [
            Icon(Icons.tune_rounded, size: 14, color: t.inkSecondary),
            const SizedBox(width: 6),
            Text(
              '着色器变体',
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: t.inkSecondary,
              ),
            ),
            const Spacer(),
            if (_manualKeywords.isNotEmpty || _colorSpace != 'gamma')
              NeuPill('恢复材质原状', onTap: _resetVariants),
          ],
        ),
        const SizedBox(height: 7),
        if (_variants.isNotEmpty) ...[
          Text(
            '材质已启用',
            style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          ),
          const SizedBox(height: 4),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              for (final entry in _variants)
                if (_materialKeywords.contains('${entry['keyword']}'))
                  _variantPill(t, entry, enabled: true, selected: true, onTap: null),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            '可手动启用（最多 $_variantLimit 个，作用于声明它的所有材质）',
            style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          ),
          const SizedBox(height: 4),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              for (final entry in _variants)
                if (!_materialKeywords.contains('${entry['keyword']}'))
                  _variantPill(
                    t,
                    entry,
                    enabled: !full || _manualKeywords.contains('${entry['keyword']}'),
                    selected: _manualKeywords.contains('${entry['keyword']}'),
                    onTap: () => _toggleVariant('${entry['keyword']}'),
                  ),
            ],
          ),
        ],
        const SizedBox(height: 10),
        Row(
          children: [
            Text(
              '色彩空间',
              style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
            ),
            const SizedBox(width: 8),
            NeuPill(
              'Gamma',
              bg: _colorSpace == 'gamma' ? t.ink : null,
              fg: _colorSpace == 'gamma' ? t.raisedHi : t.inkSecondary,
              onTap: () => _setColorSpace('gamma'),
              tooltip: '按 Unity 的 gamma 空间项目出图（默认）',
            ),
            const SizedBox(width: 6),
            NeuPill(
              '线性',
              bg: _colorSpace == 'linear' ? t.ink : null,
              fg: _colorSpace == 'linear' ? t.raisedHi : t.inkSecondary,
              onTap: () => _setColorSpace('linear'),
              tooltip: '不定义 UNITY_COLORSPACE_GAMMA，只有写着色器分支时才变化',
            ),
          ],
        ),
        if (blocked.isNotEmpty) ...[
          const SizedBox(height: 8),
          Text(
            '引擎侧恒为关闭：${blocked.join('、')}。这些档在预览里没有实现，勾选不会改变画面。',
            style: TextStyle(fontSize: 10.5, height: 1.45, color: t.warn),
          ),
        ],
      ],
    );
  }

  Widget _variantPill(
    NeuTokens t,
    Map<String, dynamic> entry, {
    required bool enabled,
    required bool selected,
    required VoidCallback? onTap,
  }) {
    final keyword = '${entry['keyword']}';
    final shaders = ((entry['shaders'] as List?) ?? const []).cast<String>();
    return NeuPill(
      keyword,
      bg: selected ? t.ink : enabled ? null : t.inset,
      fg: selected ? t.raisedHi : enabled ? t.inkSecondary : t.inkTertiary,
      onTap: enabled ? onTap : null,
      tooltip: '$keyword\n${shaders.join('、')}',
    );
  }

  static String _aaLabel(String msaa) {
    final samples = int.tryParse(msaa) ?? 0;
    return samples > 1 ? '$samples× MSAA + mip 贴图' : '软件渲染，未启用 MSAA';
  }

  static String _textureLabel(String raw) {
    final parts = raw.split('|');
    if (parts.length != 3) return '—';
    final failures = int.tryParse(parts[2]) ?? 0;
    final text = '${parts[0]} 张 · 约 ${parts[1]} MB';
    return failures > 0 ? '$text · $failures 张无法解码' : text;
  }

  Widget _infoRow(NeuTokens t, String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 30,
          child: Text(
            label,
            style: TextStyle(fontSize: 11, color: t.inkTertiary),
          ),
        ),
        Expanded(
          child: Text(
            value,
            style: TextStyle(fontSize: 11, height: 1.4, color: t.ink),
          ),
        ),
      ],
    ),
  );

  Widget _section(NeuTokens t, String title) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Text(
      title,
      style: TextStyle(
        fontSize: 11.5,
        fontWeight: FontWeight.w600,
        color: t.inkSecondary,
      ),
    ),
  );

  Widget _vector(NeuTokens t, String label, dynamic value) {
    final list = value is List ? value : const [];
    final text = list
        .map((item) {
          final number = item is num ? item : num.tryParse('$item') ?? 0;
          return number.toStringAsFixed(3);
        })
        .join(', ');

    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        children: [
          SizedBox(
            width: 42,
            child: Text(
              label,
              style: TextStyle(fontSize: 11, color: t.inkTertiary),
            ),
          ),
          Expanded(
            child: Text(text, style: TextStyle(fontSize: 11, color: t.ink)),
          ),
        ],
      ),
    );
  }

  Widget _keyValue(NeuTokens t, String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 4),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: TextStyle(fontSize: 10.5, color: t.inkTertiary)),
        SelectableText(value, style: TextStyle(fontSize: 10.5, color: t.ink)),
      ],
    ),
  );

  Widget _material(NeuTokens t, Map<String, dynamic> material) {
    final textures = (material['textures'] as Map?) ?? const {};

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(9),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.035),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '${material['name']}',
            style: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.w600),
          ),
          Text(
            '${material['shaderName'] ?? material['shader'] ?? '未知 Shader'}',
            style: TextStyle(fontSize: 10, color: t.inkTertiary),
          ),
          if ('${material['kind']}' == 'liltoon' ||
              '${material['shaderFamily']}' == 'lilToon') ...[
            Text(
              'lilToon 近似 · 阴影 ${_num(material['shadowStrength'])} · '
              '${material['doubleSided'] == true ? '双面' : '单面'}',
              style: TextStyle(fontSize: 10, color: t.inkSecondary),
            ),
            if (material['useOutline'] == true)
              Text(
                '描边 ${_num(material['outlineWidth'])}',
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
            if (material['useMatCap'] == true)
              Text(
                'MatCap 已启用',
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
            if (material['useEmission'] == true)
              Text(
                '自发光已启用',
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
          ] else if ('${material['kind']}' != 'lit')
            Text(
              '外部渲染：${material['kind']}',
              style: TextStyle(fontSize: 10, color: t.inkSecondary),
            ),
          if (textures.isNotEmpty) ...[
            const SizedBox(height: 4),
            for (final entry in textures.entries.take(6))
              Text(
                '${entry.key} → ${entry.value}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
          ],
        ],
      ),
    );
  }

  Widget _component(NeuTokens t, Map<String, dynamic> component) {
    final properties = (component['properties'] as Map?) ?? const {};
    final enabled = component['enabled'] != false;

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(9),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.035),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '${component['type']}',
                  style: const TextStyle(
                    fontSize: 11.5,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(
                enabled ? '启用' : '禁用',
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
            ],
          ),
          if ('${component['script']}'.isNotEmpty &&
              '${component['script']}' != 'null')
            Text(
              '${component['script']}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
            ),
          if (properties.isNotEmpty) ...[
            const SizedBox(height: 4),
            for (final entry in properties.entries.take(12))
              Padding(
                padding: const EdgeInsets.only(top: 2),
                child: Text(
                  '${entry.key}: ${_value(entry.value)}',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 9.5, color: t.inkSecondary),
                ),
              ),
            if (properties.length > 12)
              Text(
                '… 另有 ${properties.length - 12} 个字段',
                style: TextStyle(fontSize: 9.5, color: t.inkTertiary),
              ),
          ],
        ],
      ),
    );
  }

  String _num(dynamic value) {
    final number = value is num ? value : num.tryParse('$value');
    if (number == null) return '—';
    return number.toStringAsFixed(2);
  }

  String _value(dynamic value) {
    if (value == null) return 'null';
    if (value is String) return value;
    if (value is num || value is bool) return '$value';
    try {
      final text = jsonEncode(value);
      return text.length > 160 ? '${text.substring(0, 160)}…' : text;
    } catch (_) {
      return '$value';
    }
  }
}
