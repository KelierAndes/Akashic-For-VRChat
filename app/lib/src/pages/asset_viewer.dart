import 'dart:math' as math;

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

import '../host/host_client.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';

const assetKindOrder = [
  'prefab',
  'model',
  'material',
  'texture',
  'shader',
  'animation',
  'controller',
  'script',
  'scene',
  'text',
  'audio',
  'video',
  'binary',
  'asset',
  'other',
];

String assetKindLabel(String kind) => switch (kind) {
  'prefab' => 'Prefab',
  'model' => '模型',
  'texture' => '贴图',
  'material' => '材质',
  'shader' => '着色器',
  'animation' => '动画',
  'controller' => '动画状态机',
  'script' => '脚本',
  'scene' => '场景',
  'asset' => '资产',
  'text' => '文本',
  'audio' => '音频',
  'video' => '视频',
  'binary' => '二进制',
  'other' => '其他',
  _ => '文件',
};

IconData assetKindIcon(String kind) => switch (kind) {
  'prefab' => Icons.account_tree_outlined,
  'model' => Icons.view_in_ar_outlined,
  'texture' => Icons.image_outlined,
  'material' => Icons.bubble_chart_outlined,
  'shader' => Icons.code_rounded,
  'animation' => Icons.animation_rounded,
  'controller' => Icons.pets_outlined,
  'script' => Icons.data_object_rounded,
  'audio' => Icons.audiotrack_outlined,
  'video' => Icons.movie_outlined,
  'text' => Icons.description_outlined,
  'scene' => Icons.landscape_outlined,
  'pdf' => Icons.picture_as_pdf_outlined,
  _ => Icons.insert_drive_file_outlined,
};

String assetExtension(String path) {
  final name = path.replaceAll('\\', '/').split('/').last;
  final dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.substring(dot + 1).toLowerCase();
}

bool isPdfAsset(Map<String, dynamic> asset) =>
    assetExtension('${asset['path']}') == 'pdf';

bool isMediaAsset(Map<String, dynamic> asset) {
  final kind = '${asset['kind']}';
  return kind == 'audio' || kind == 'video';
}

bool isImageAsset(Map<String, dynamic> asset) {
  final kind = '${asset['kind']}';
  return kind == 'texture' || kind == 'material';
}

String formatAssetBytes(int bytes) {
  if (bytes >= 1048576) return '${(bytes / 1048576).toStringAsFixed(1)} MB';
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
  if (bytes > 0) return '$bytes B';
  return '—';
}

class AssetViewer extends StatefulWidget {
  const AssetViewer({
    super.key,
    required this.host,
    required this.resourceKey,
    required this.asset,
    this.entry,
    this.darkStage = true,
  });

  final HostClient host;
  final String resourceKey;
  final Map<String, dynamic> asset;
  final String? entry;
  final bool darkStage;

  @override
  State<AssetViewer> createState() => _AssetViewerState();
}

class _AssetViewerState extends State<AssetViewer> {
  MemoryImage? _image;
  String? _text;
  String _encoding = '';
  bool _truncated = false;
  String _error = '';
  bool _loading = false;
  bool _light = false;
  int _token = 0;

  double _yaw = 0.7854;
  double _pitch = 0.6155;
  double _distance = 0;
  bool _orbitPending = false;

  bool get _isMedia => isMediaAsset(widget.asset);

  bool get _isMaterialBall => '${widget.asset['kind']}' == 'material';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _load());
  }

  @override
  void didUpdateWidget(AssetViewer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.asset['path'] != widget.asset['path'] ||
        oldWidget.resourceKey != widget.resourceKey) {
      _yaw = 0.7854;
      _pitch = 0.6155;
      _distance = 0;
      _load();
    }
  }

  bool get _isPdf => isPdfAsset(widget.asset);

  bool get _isImage => isImageAsset(widget.asset);

  void _orbitBy(double dx, double dy) {
    if (!_isMaterialBall) return;
    setState(() {
      _yaw -= dx * 0.0085;
      _pitch = (_pitch - dy * 0.0085).clamp(-1.5, 1.5);
    });
    _scheduleOrbitFetch();
  }

  void _dollyBy(double deltaY) {
    if (!_isMaterialBall) return;
    setState(() {
      final current = _distance <= 0 ? 2.35 : _distance;
      _distance = (current * math.exp(deltaY * 0.0008)).clamp(0.6, 12.0);
    });
    _scheduleOrbitFetch();
  }

  void _resetOrbit() {
    setState(() {
      _yaw = 0.7854;
      _pitch = 0.6155;
      _distance = 0;
    });
    _scheduleOrbitFetch();
  }

  void _scheduleOrbitFetch() {
    if (_orbitPending) return;
    _orbitPending = true;
    Future.delayed(const Duration(milliseconds: 40), () {
      _orbitPending = false;
      if (mounted) _orbitRefresh();
    });
  }

  Future<void> _orbitRefresh() async {
    final token = ++_token;
    final url = widget.host.previewMaterialUrl(
      widget.resourceKey,
      '${widget.asset['path']}',
      entry: widget.entry,
      light: _light,
      yaw: _yaw,
      pitch: _pitch,
      distance: _distance,
    );

    final image = await widget.host.fetchPreviewImage(url);
    if (!mounted || token != _token) return;

    setState(() {
      _error = image.message;
      final bytes = image.bytes;
      if (bytes != null) _image = MemoryImage(bytes);
    });
  }

  Widget _stage(Widget child) {
    if (!_isMaterialBall) return child;

    return GestureDetector(
      onPanUpdate: (details) => _orbitBy(details.delta.dx, details.delta.dy),
      onDoubleTap: _resetOrbit,
      child: Listener(
        onPointerSignal: (event) {
          if (event is PointerScrollEvent) _dollyBy(event.scrollDelta.dy);
        },
        child: child,
      ),
    );
  }

  Future<void> _load() async {
    final token = ++_token;
    final path = '${widget.asset['path']}';
    final kind = '${widget.asset['kind']}';

    if (_isPdf || _isMedia) {
      setState(() {
        _loading = false;
        _error = '';
        _image = null;
        _text = null;
      });
      return;
    }

    final wantsImage = _isImage;
    setState(() {
      _loading = true;
      _error = '';
      _image = null;
      _text = null;
    });

    if (wantsImage) {
      final url = kind == 'material'
          ? widget.host.previewMaterialUrl(
              widget.resourceKey,
              path,
              entry: widget.entry,
              light: _light,
              yaw: _yaw,
              pitch: _pitch,
              distance: _distance,
            )
          : widget.host.previewFileUrl(
              widget.resourceKey,
              path,
              entry: widget.entry,
            );

      final image = await widget.host.fetchPreviewImage(url);
      if (!mounted || token != _token) return;

      setState(() {
        _loading = false;
        _error = image.message;
        final bytes = image.bytes;
        if (bytes != null) _image = MemoryImage(bytes);
      });
      return;
    }

    final text = await widget.host.fetchPreviewText(
      widget.host.previewTextUrl(widget.resourceKey, path, entry: widget.entry),
    );
    if (!mounted || token != _token) return;

    setState(() {
      _loading = false;
      _error = text.message;
      _text = text.text;
      _encoding = text.encoding;
      _truncated = text.truncated;
    });
  }

  void _toggleBackground() {
    setState(() => _light = !_light);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final kind = '${widget.asset['kind']}';

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Icon(
              _isPdf ? assetKindIcon('pdf') : assetKindIcon(kind),
              size: 15,
              color: t.inkSecondary,
            ),
            const SizedBox(width: 7),
            Expanded(
              child: Text(
                '${widget.asset['name']}',
                style: const TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w600,
                ),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            if (_isImage)
              NeuActionButton(
                label: _light ? '底色：浅色' : '底色：深色',
                icon: Icons.contrast_rounded,
                onTap: _toggleBackground,
              ),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          '${widget.asset['folder']} · ${formatAssetBytes((widget.asset['size'] as num?)?.toInt() ?? 0)}'
          '${_text == null ? '' : ' · $_encoding${_truncated ? '（已截断）' : ''}'}',
          style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        const SizedBox(height: 10),
        Expanded(child: _body(t)),
      ],
    );
  }

  Widget _body(NeuTokens t) {
    if (_loading) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(
              width: 20,
              height: 20,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
            const SizedBox(height: 12),
            Text(
              _isImage
                  ? ('${widget.asset['kind']}' == 'material'
                        ? '正在用 HLSL 渲染材质球…'
                        : '正在读取贴图…')
                  : '正在读取文本…',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
            ),
          ],
        ),
      );
    }

    if (_isPdf) {
      return ClipRRect(
        borderRadius: BorderRadius.circular(10),
        child: Container(
          color: const Color(0xFF20242A),
          child: InAppWebView(
            initialUrlRequest: URLRequest(
              url: WebUri(
                widget.host.previewFileUrl(
                  widget.resourceKey,
                  '${widget.asset['path']}',
                  entry: widget.entry,
                ),
              ),
            ),
            initialSettings: InAppWebViewSettings(
              javaScriptEnabled: false,
              clearCache: false,
              supportZoom: false,
            ),
          ),
        ),
      );
    }

    if (_isMedia) {
      return Center(
        child: Text(
          '音频与视频没有内建播放器，可以在资源管理器里打开。',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 12, color: t.inkTertiary),
        ),
      );
    }

    if (_image != null) {
      return _stage(
        Container(
          decoration: BoxDecoration(
            color: _light ? const Color(0xFFEDF0F4) : const Color(0xFF1B1E23),
            borderRadius: BorderRadius.circular(12),
          ),
          clipBehavior: Clip.antiAlias,
          padding: const EdgeInsets.all(14),
          child: Center(
            child: _isMaterialBall
                ? Image(
                    image: _image!,
                    fit: BoxFit.contain,
                    gaplessPlayback: true,
                  )
                : Image(image: _image!, fit: BoxFit.contain),
          ),
        ),
      );
    }

    if (_text != null) {
      if (_text!.trim().isEmpty) {
        return Center(
          child: Text(
            '这个文件是空的（0 字节）。',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12, color: t.inkTertiary),
          ),
        );
      }

      return Container(
        decoration: BoxDecoration(
          color: Colors.black.withValues(alpha: 0.045),
          borderRadius: BorderRadius.circular(10),
        ),
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 14),
        child: SingleChildScrollView(
          child: SelectableText(
            _text!,
            style: TextStyle(
              fontFamily: 'Consolas',
              fontFamilyFallback: const ['Courier New', 'monospace'],
              fontSize: 11.5,
              height: 1.45,
              color: t.ink,
            ),
          ),
        ),
      );
    }

    return Center(
      child: Text(
        _error.isEmpty ? '没有拿到可显示的内容。' : _error,
        textAlign: TextAlign.center,
        style: TextStyle(fontSize: 12, color: t.inkTertiary),
      ),
    );
  }
}

class PackageAssetsPanel extends StatefulWidget {
  const PackageAssetsPanel({
    super.key,
    required this.host,
    required this.resourceKey,
    required this.resourceName,
    this.entry,
    required this.onOpenModel,
    required this.onPick,
    this.picked,
    this.onClose,
  });

  final HostClient host;
  final String resourceKey;
  final String resourceName;
  final String? entry;
  final void Function(Map<String, dynamic> asset) onOpenModel;
  final void Function(Map<String, dynamic> asset) onPick;
  final Map<String, dynamic>? picked;
  final VoidCallback? onClose;

  @override
  State<PackageAssetsPanel> createState() => _PackageAssetsPanelState();
}

class _PackageAssetsPanelState extends State<PackageAssetsPanel> {
  final List<Map<String, dynamic>> _assets = [];
  final TextEditingController _search = TextEditingController();
  String _kindFilter = '';
  String _error = '';
  bool _loading = true;

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
    setState(() {
      _loading = true;
      _error = '';
    });

    try {
      final rows = await widget.host.previewList(
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
      setState(() {
        _assets
          ..clear()
          ..addAll(assets);
        _loading = false;
        _kindFilter = '';
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = '$error';
      });
    }
  }

  void _open(Map<String, dynamic> asset) {
    final kind = '${asset['kind']}';
    if (kind == 'prefab' || kind == 'model') {
      widget.onOpenModel(asset);
      return;
    }
    widget.onPick(asset);
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final query = _search.text.trim().toLowerCase();

    final filtered = _assets.where((row) {
      if (query.isEmpty) return true;
      return '${row['name']}'.toLowerCase().contains(query) ||
          '${row['folder']}'.toLowerCase().contains(query) ||
          '${row['path']}'.toLowerCase().contains(query);
    }).toList();

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
        ? filtered
        : filtered.where((asset) => '${asset['kind']}' == _kindFilter).toList();

    final entries = <Object>[];
    if (_kindFilter.isEmpty) {
      for (final kind in kinds) {
        final group = visible
            .where((asset) => '${asset['kind']}' == kind)
            .toList();
        if (group.isEmpty) continue;
        entries.add(kind);
        entries.addAll(group);
      }
    } else {
      entries.addAll(visible);
    }

    return NeuCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  widget.resourceName,
                  style: const TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w600,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SizedBox(width: 8),
              NeuActionButton(
                label: '重新读取',
                icon: Icons.refresh_rounded,
                onTap: _loading ? null : _load,
              ),
              if (widget.onClose != null) ...[
                const SizedBox(width: 8),
                NeuActionButton(
                  label: '关闭',
                  icon: Icons.close_rounded,
                  onTap: widget.onClose,
                ),
              ],
            ],
          ),
          const SizedBox(height: 10),
          NeuTextInput(
            controller: _search,
            hint: '按名称或目录筛选包内文件',
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 10),
          if (kinds.length > 1)
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                _chip('全部 ${_assets.length}', ''),
                for (final kind in kinds)
                  _chip('${assetKindLabel(kind)} ${counts[kind] ?? 0}', kind),
              ],
            ),
          const SizedBox(height: 10),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
                : _assets.isEmpty
                ? Center(
                    child: Text(
                      _error.isEmpty ? '这个包里没有解析出任何文件。' : _error,
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 12, color: t.inkTertiary),
                    ),
                  )
                : entries.isEmpty
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
                          padding: const EdgeInsets.only(bottom: 8),
                          child: _tile(entry),
                        );
                      }
                      return _header('$entry', counts['$entry'] ?? 0);
                    },
                  ),
          ),
        ],
      ),
    );
  }

  Widget _chip(String label, String kind) {
    final on = _kindFilter == kind;
    return NeuPill(
      label,
      bg: on ? const Color(0xFF2F3238) : null,
      fg: on ? Colors.white : null,
      onTap: () => setState(() => _kindFilter = kind),
    );
  }

  Widget _header(String kind, int count) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 6, left: 2),
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

  Widget _tile(Map<String, dynamic> asset) {
    final t = NeuTheme.of(context);
    final kind = '${asset['kind']}';
    final model = kind == 'prefab' || kind == 'model';
    final opened = widget.picked?['path'] == asset['path'];
    final folder = '${asset['folder']}';

    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      child: Row(
        children: [
          Icon(assetKindIcon(kind), size: 16, color: t.inkSecondary),
          const SizedBox(width: 10),
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
                          fontSize: 12.5,
                          fontWeight: FontWeight.w600,
                        ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    if (opened) ...[
                      const SizedBox(width: 7),
                      const NeuPill(
                        '当前',
                        bg: Color(0xFFBFE3C9),
                        fg: Color(0xFF237A45),
                      ),
                    ],
                  ],
                ),
                const SizedBox(height: 2),
                Text(
                  folder.isEmpty ? '包根目录' : folder,
                  style: TextStyle(fontSize: 10, color: t.inkTertiary),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(
            formatAssetBytes((asset['size'] as num?)?.toInt() ?? 0),
            style: TextStyle(fontSize: 10.5, color: t.inkTertiary),
          ),
          const SizedBox(width: 8),
          NeuActionButton(
            label: model ? '预览' : '查看',
            icon: model ? Icons.play_arrow_rounded : Icons.visibility_outlined,
            onTap: () => _open(asset),
          ),
        ],
      ),
    );
  }
}
