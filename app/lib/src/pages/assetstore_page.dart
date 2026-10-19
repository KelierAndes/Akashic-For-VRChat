import 'dart:async';

import 'package:flutter/material.dart';

import '../browser/browser_panel.dart';
import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

class AssetStorePage extends StatefulWidget {
  const AssetStorePage({super.key});

  static const views = ['全部资产', '未入库', '已入库', '仅本地缓存', '仅我的资产'];

  @override
  State<AssetStorePage> createState() => _AssetStorePageState();
}

const _nextPageScript = r"""(() => {
  const b = document.querySelector('button[label="下一页"], button[aria-label="下一页"]');
  if (b && !b.disabled) { b.click(); return 'clicked'; }
  return 'none';
})()""";

class _AssetStorePageState extends State<AssetStorePage> {
  final GlobalKey<BrowserPanelState> _browser = GlobalKey<BrowserPanelState>();
  final TextEditingController _search = TextEditingController();

  List<Map<String, dynamic>> _rows = [];
  final Set<String> _selected = {};
  Map<String, dynamic>? _scripts;
  String _view = AssetStorePage.views.first;
  String _status = '';
  String _cacheDirectory = '';
  String _url = 'https://assetstore.unity.com/account/assets';
  bool _browserOpen = false;
  bool _signedIn = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadCached());
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _loadCached() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    try {
      _scripts = await host.assetStoreScripts();
      _cacheDirectory = '${_scripts?['cacheDirectory']}';
      _url = '${_scripts?['myAssetsUrl']}';
      final saved = await host.assetStoreAccountCache();
      final accountRows =
          ((saved['items'] as List?) ?? const []).cast<Map<String, dynamic>>();

      final merged = await host.assetStoreMerge(accountRows);
      _rows = (merged as List).cast<Map<String, dynamic>>();

      final library = await host.libraryFiles();
      final onShelf = {for (final file in library) '${file['filePath']}'};
      for (final row in _rows) {
        row['isInLibrary'] = onShelf.contains('${row['localPath']}');
      }
      _signedIn = model.state?.assetStore.signedIn ?? false;

      if (!mounted) return;
      setState(() => _status = '${_rows.length} 项 · $_downloadedCount 项本地已有 · $_cacheDirectory');
      unawaited(_refreshCovers());
    } catch (error) {
      if (mounted) setState(() => _status = '读取失败：$error');
    }
  }

  int get _downloadedCount =>
      _rows.where((row) => row['localPath'] != null).length;

  List<Map<String, dynamic>> get _shown {
    final query = _search.text.trim().toLowerCase();
    return _rows.where((row) {
      if (query.isNotEmpty &&
          !('${row['name']}'.toLowerCase().contains(query) ||
              '${row['publisher']}'.toLowerCase().contains(query))) {
        return false;
      }
      final onDisk = row['localPath'] != null;
      return switch (_view) {
        '未入库' => row['isInLibrary'] != true,
        '已入库' => row['isInLibrary'] == true,
        '仅本地缓存' => onDisk,
        '仅我的资产' => row['isFromAccount'] == true,
        _ => true,
      };
    }).toList();
  }

  Future<void> _nextFrame() async {
    final completer = Completer<void>();
    WidgetsBinding.instance.addPostFrameCallback((_) => completer.complete());
    await completer.future;
  }

  Future<void> _showBrowser(String url) async {
    setState(() {
      _browserOpen = true;
      _url = url;
    });
    await _nextFrame();
    await _browser.currentState?.navigate(url);
  }

  Future<void> _seed() async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    await BrowserSession.seed(await host.sessionCookies('assetstore'));
  }

  Future<void> _signIn() async {
    await _seed();
    await _showBrowser(_url);
    if (mounted) setState(() => _status = '请在内嵌页面登录 Unity 账号，然后点「保存登录态」。');
  }

  Future<void> _saveLogin() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    final cookies = await BrowserSession.harvest([
      'https://assetstore.unity.com',
      'https://id.unity.com',
      'https://unity.com',
    ]);
    if (cookies.isEmpty) {
      setState(() => _status = '浏览器里还没有 Cookie，请先登录。');
      return;
    }

    await host.importSessionCookies('assetstore', cookies);
    await model.refreshState();
    if (!mounted) return;
    setState(() {
      _signedIn = true;
      _status = '已保存 ${cookies.length} 个 Cookie 到本地加密库。';
    });
  }

  Future<List<Map<String, dynamic>>?> _extract(String script) async {
    final host = AppScope.of(context).host;
    final controller = _browser.currentState?.controller;
    if (host == null || controller == null) return null;

    for (var attempt = 0; attempt < 40; attempt++) {
      final ready = await controller.evaluateJavascript(
          source:
              r"""document.querySelectorAll('[data-test^="asset-item-"]').length""");
      if (ready != null && '$ready' != '0') {
        final raw = await controller.evaluateJavascript(source: script);
        if (raw == null) break;
        final parsed = await host.assetStoreParse('asset', '$raw');
        final items = ((parsed as Map)['items'] as List?) ?? const [];
        if (items.isNotEmpty) return items.cast<Map<String, dynamic>>();
        if (parsed['loginRequired'] == true) return const [];
      }
      await Future<void>.delayed(const Duration(milliseconds: 350));
    }
    return null;
  }

  Future<void> _read() async {
    final host = AppScope.of(context).host;
    final script = _scripts?['myAssets'] as String?;
    if (host == null || script == null) return;

    setState(() {
      _busy = true;
      _status = '正在注入登录态并打开我的资产…';
    });
    await _seed();
    await _showBrowser('${_scripts?['myAssetsUrl']}');

    final byName = <String, Map<String, dynamic>>{};
    var stalled = 0;
    for (var page = 0; page < 24; page++) {
      final items = await _extract(script);
      if (items == null) {
        if (mounted) {
          setState(() {
            _busy = false;
            _status = '页面超时没有抓到资产行，可能需要重新登录。';
          });
        }
        return;
      }
      if (items.isEmpty) {
        if (mounted) {
          setState(() {
            _busy = false;
            _status = '登录态已失效，请在内嵌页面重新登录。';
          });
        }
        return;
      }

      final before = byName.length;
      for (final item in items) {
        byName['${item['name']}'] = item;
      }
      if (!mounted) return;
      setState(() => _status = '第 ${page + 1} 次抓取 · 已有 ${byName.length} 项');

      if (byName.length == before) {
        if (++stalled >= 3) break;
        await Future<void>.delayed(const Duration(milliseconds: 900));
        continue;
      }
      stalled = 0;

      final advanced =
          await _browser.currentState?.controller?.evaluateJavascript(
              source: _nextPageScript);
      if ('$advanced' != 'clicked') break;
      await Future<void>.delayed(const Duration(milliseconds: 900));
    }

    final merged = await host.assetStoreMerge(byName.values.toList());
    await host.assetStoreSaveAccountCache(byName.values.toList());
    _rows = (merged as List).cast<Map<String, dynamic>>();

    final library = await host.libraryFiles();
    final onShelf = {for (final file in library) '${file['filePath']}'};
    for (final row in _rows) {
      row['isInLibrary'] = onShelf.contains('${row['localPath']}');
    }

    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = '${_rows.length} 项 · $_downloadedCount 项本地已有 · $_cacheDirectory';
    });
    unawaited(_refreshCovers());
  }

  Future<void> _refreshCovers() async {
    await _fillMissingCovers();
    await _fillLocalThumbnails();
  }

  Future<void> _fillLocalThumbnails() async {
    final host = AppScope.of(context).host;
    if (host == null || _busy) return;

    var resolved = 0;
    for (final row in _rows) {
      if ('${row['imageUrl']}'.isNotEmpty) continue;

      final path = '${row['localPath']}';
      if (path.isEmpty) continue;

      final thumb = await host.assetStoreThumbnail(path);
      if (thumb == null || thumb.isEmpty || !mounted) continue;

      row['imageUrl'] = thumb;
      resolved++;
    }

    if (resolved > 0 && mounted) setState(() {});
  }

  Future<void> _download(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    final name = '${row['name']}';

    setState(() {
      _busy = true;
      _status = '正在下载 $name…';
    });

    try {
      final path =
          await host.assetStoreDownload(name, row['productId'] as String?);
      await host.addPackageFile(path, origin: 'AssetStore');
      row['localPath'] = path;
      row['isInLibrary'] = true;
      if (!mounted) return;
      setState(() {
        _busy = false;
        _status = '$name 已下载到 $path 并入库';
      });
    } catch (error) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '$name 下载失败：$error';
        });
      }
    }
  }

  Future<void> _register(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null || row['localPath'] == null) return;
    await host.addPackageFile('${row['localPath']}', origin: 'AssetStore');
    setState(() {
      row['isInLibrary'] = true;
      _status = '${row['name']} 已加入软件包库。';
    });
  }

  bool _fillingCovers = false;

  static String _key(String raw) =>
      raw.replaceAll(RegExp(r'[^A-Za-z0-9]'), '').toLowerCase();

  Future<void> _fillMissingCovers() async {
    final host = AppScope.of(context).host;
    final script = _scripts?['searchCover'] as String?;
    if (host == null || script == null || _fillingCovers || _busy) return;

    final pending = <String>[];
    for (final row in _rows) {
      final name = '${row['name']}';
      if (name.isEmpty || '${row['imageUrl']}'.isNotEmpty) continue;
      if (!pending.contains(name)) pending.add(name);
      if (pending.length == 20) break;
    }
    if (pending.isEmpty) return;

    for (var wait = 0; wait < 40 && _browser.currentState?.controller == null; wait++) {
      await Future<void>.delayed(const Duration(milliseconds: 250));
    }
    if (_browser.currentState?.controller == null) return;

    _fillingCovers = true;
    var resolved = 0;
    try {
      for (var i = 0; i < pending.length; i++) {
        if (!mounted || _busy) break;
        final name = pending[i];
        setState(() => _status =
            '正在从 Asset Store 获取封面 ${i + 1} / ${pending.length}：$name');

        var url = await host.assetStoreCover(name) ?? '';
        if (url.isEmpty) {
          url = await _readCover(name, script) ?? '';
          if (url.isNotEmpty) await host.cacheAssetStoreCover(name, url);
        }
        if (url.isEmpty) continue;

        resolved++;
        final key = _key(name);
        for (final row in _rows) {
          if ('${row['imageUrl']}'.isEmpty && _key('${row['name']}') == key) {
            row['imageUrl'] = url;
          }
        }
        if (!mounted) break;
        setState(() {});
      }
    } finally {
      _fillingCovers = false;
      if (mounted) {
        setState(() => _status = resolved > 0
            ? '已为 $resolved 个缓存资源补齐封面 · $_cacheDirectory'
            : '${_rows.length} 项 · $_downloadedCount 项本地已有 · $_cacheDirectory');
      }
    }
  }

  Future<String?> _readCover(String name, String script) async {
    final host = AppScope.of(context).host;
    final panel = _browser.currentState;
    final controller = panel?.controller;
    if (host == null || panel == null || controller == null) return null;

    await panel.navigate(await host.assetStoreSearchUrl(name));

    for (var attempt = 0; attempt < 24; attempt++) {
      final cards = await controller.evaluateJavascript(source:
          r"""document.querySelectorAll('article[data-test^="search-results-product-card-"]').length""");
      if (cards != null && '$cards' != '0') {
        final raw = await controller.evaluateJavascript(source: script);
        if (raw == null) return null;
        final parsed = await host.assetStoreParse('cover', '$raw');
        final items = ((parsed as List?) ?? const [])
            .cast<Map<String, dynamic>>();
        final key = _key(name);
        for (final card in items) {
          if (_key('${card['name']}') == key) return '${card['image']}';
        }
        for (final card in items) {
          final ck = _key('${card['name']}');
          if (ck.length > 3 && (ck.contains(key) || key.contains(ck))) {
            return '${card['image']}';
          }
        }
        return null;
      }
      await Future<void>.delayed(const Duration(milliseconds: 350));
    }
    return null;
  }

  Future<void> _downloadSelected() async {
    final picked =
        _shown.where((row) => _selected.contains('${row['name']}')).toList();
    for (final row in picked) {
      await _download(row);
    }
    _selected.clear();
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return NeuPageBody(
      title: 'Asset Store 导入',
      subtitle: _status,
      trailing: Wrap(
        spacing: 10,
        runSpacing: 8,
        alignment: WrapAlignment.end,
        children: [
          NeuPill(_signedIn ? '已登录' : '未登录',
              bg: _signedIn
                  ? const Color(0xFFBFE3C9)
                  : Colors.black.withValues(alpha: 0.06),
              fg: _signedIn ? const Color(0xFF237A45) : null),
          NeuActionButton(
            label: '登录',
            icon: Icons.login_rounded,
            onTap: _busy ? null : _signIn,
          ),
          NeuActionButton(
            label: '保存登录态',
            icon: Icons.save_alt_rounded,
            onTap: _busy ? null : _saveLogin,
          ),
          NeuActionButton(
            label: '读取我的资产',
            icon: Icons.cloud_download_rounded,
            emphasised: true,
            onTap: _busy ? null : _read,
          ),
          NeuActionButton(
            label: _browserOpen ? '隐藏页面' : '显示页面',
            icon: _browserOpen
                ? Icons.keyboard_hide_rounded
                : Icons.language_rounded,
            onTap: () => setState(() => _browserOpen = !_browserOpen),
          ),
        ],
      ),
      fixed: [
        Row(
          children: [
            Expanded(
              child: NeuSearchField(
                  hint: '按名称或发行方过滤…',
                  controller: _search,
                  onChanged: (_) => setState(() {})),
            ),
            const SizedBox(width: 14),
            NeuDropdown(
              value: _view,
              items: AssetStorePage.views,
              icon: Icons.filter_alt_rounded,
              onChanged: (value) => setState(() => _view = value),
            ),
            const SizedBox(width: 14),
            NeuCheckbox(
              value: shown.isNotEmpty &&
                  shown.every((r) => _selected.contains('${r['name']}')),
              onTap: () => setState(() {
                final all = shown.every((r) => _selected.contains('${r['name']}'));
                _selected.clear();
                if (!all) {
                  _selected.addAll([for (final r in shown) '${r['name']}']);
                }
              }),
            ),
            const SizedBox(width: 9),
            Text('显示 ${shown.length} / ${_rows.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const Spacer(),
            if (_selected.isNotEmpty)
              NeuActionButton(
                label: '下载所选（${_selected.length}）',
                icon: Icons.file_download_outlined,
                emphasised: true,
                onTap: _busy ? null : _downloadSelected,
              ),
          ],
        ),
      ],
      content: Stack(
        children: [
          Positioned.fill(
            child: Padding(
              padding: EdgeInsets.only(bottom: _browserOpen ? 392 : 0),
              child: shown.isEmpty
                ? NeuEmptyState(
                    expand: true,
                    text: '还没有资产清单。\n「登录」→「保存登录态」→「读取我的资产」，'
                        '读取结果会与缓存目录 $_cacheDirectory 合并。')
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final row = shown[index];
                      final name = '${row['name']}';
                      final checked = _selected.contains(name);
                      final onDisk = row['localPath'] != null;
                      final inLibrary = row['isInLibrary'] == true;

                      return NeuCard(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 14, vertical: 11),
                        child: Row(
                          children: [
                            NeuCheckbox(
                              value: checked,
                              onTap: () => setState(() =>
                                  checked
                                      ? _selected.remove(name)
                                      : _selected.add(name)),
                            ),
                            const SizedBox(width: 12),
                            NeuCover('${row['imageUrl']}'),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(name,
                                      style: const TextStyle(
                                          fontSize: 13.5,
                                          fontWeight: FontWeight.w600),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis),
                                  const SizedBox(height: 3),
                                  Text(
                                      '${row['publisher']} · '
                                      '${onDisk ? row['localPath'] : _cacheDirectory}',
                                      style: TextStyle(
                                          fontSize: 11, color: t.inkTertiary),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis),
                                ],
                              ),
                            ),
                            const SizedBox(width: 10),
                            if (row['isFromAccount'] != true)
                              const Padding(
                                padding: EdgeInsets.only(right: 8),
                                child: NeuPill('仅缓存发现'),
                              ),
                            NeuPill('${row['version']}'.isEmpty
                                ? '未标注版本'
                                : '${row['version']}'),
                            const SizedBox(width: 8),
                            NeuPill(
                              inLibrary
                                  ? '已入库'
                                  : onDisk
                                      ? '已缓存'
                                      : '未下载',
                              bg: inLibrary || onDisk
                                  ? const Color(0xFFBFE3C9)
                                  : Colors.black.withValues(alpha: 0.06),
                              fg: inLibrary || onDisk
                                  ? const Color(0xFF237A45)
                                  : null,
                            ),
                            const SizedBox(width: 8),
                            if (onDisk && !inLibrary)
                              NeuActionButton(
                                label: '入库',
                                icon: Icons.library_add_outlined,
                                onTap: _busy ? null : () => _register(row),
                              )
                            else
                              NeuActionButton(
                                label: onDisk ? '重新下载' : '下载并入库',
                                icon: Icons.file_download_outlined,
                                onTap: _busy ? null : () => _download(row),
                              ),
                          ],
                        ),
                      );
                    },
                  ),
            ),
          ),
          Positioned(
            left: 0,
            right: 0,
            height: 380,
            top: _browserOpen ? 0 : -400,
            child: NeuBox(
              mode: NeuMode.inset,
              radius: 11,
              depth: 4,
              color: Colors.black.withValues(alpha: 0.05),
              border: Border.all(color: Colors.black.withValues(alpha: 0.12)),
              child: BrowserPanel(
                key: _browser,
                initialUrl: _url,
                onUrlChanged: (url) {
                  if (_browserOpen && url.isNotEmpty) _url = url;
                },
              ),
            ),
          ),
        ],
      ),
    );
  }
}
