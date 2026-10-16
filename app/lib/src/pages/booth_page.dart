import 'dart:async';

import 'package:flutter/material.dart';

import '../browser/browser_panel.dart';
import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

/// Booth purchases. The sidecar owns parsing, de-duplication and the archive cache;
/// this page only drives the embedded browser with the extractor scripts the sidecar
/// hands out and feeds their JSON back.
class BoothPage extends StatefulWidget {
  const BoothPage({super.key});

  static const kinds = ['已购商品', '礼物领取', '免费商品'];
  static const views = ['全部商品', '未下载', '已下载'];

  /// Index 0 keeps demo videos, illustration packs and comics out of sight — the free
  /// and gift lists are full of them and none of it installs into Unity. Titles and
  /// file labels are matched by keyword, best effort; anything caught can still be
  /// reviewed under 演示·图集.
  static const mediaFilters = ['可用软件包', '全部商品', '演示·图集'];
  static const mediaKeywords = [
    'デモ動画', 'デモムービー', 'サンプル動画', '紹介動画', '宣伝動画', '予告動画',
    '演示动画', '演示视频', '视频演示', 'sample video', 'demo video',
    'demo movie', 'preview movie', 'trailer',
    'サンプル画像', 'sample画像', '画像集', 'sample image', 'sample picture',
    'イラスト', '插画', '画集', '图集', 'artbook', 'art book', 'fanbook',
    'ファンブック', '設定資料集', '写真集', 'フォト集', 'photo book', 'photobook',
    'illustration', 'コミック', '漫画', 'comic', '小説', '小说', 'novel',
  ];

  static bool looksLikeMedia(Map<String, dynamic> row) {
    final text = '${row['title']} ${row['formLabel']}'.toLowerCase();
    return mediaKeywords.any(text.contains);
  }

  static String kindOf(String label) => switch (label) {
        '礼物领取' => 'gifts',
        '免费商品' => 'free',
        _ => 'purchase',
      };

  @override
  State<BoothPage> createState() => BoothPageState();
}

class BoothPageState extends State<BoothPage> {
  final GlobalKey<BrowserPanelState> _browser = GlobalKey<BrowserPanelState>();

  List<Map<String, dynamic>> _rows = [];
  final Set<String> _downloaded = {};
  final Set<String> _shelved = {};
  final Map<String, int> _hidden = {};
  int _hiddenTotal = 0;
  final Set<String> _selected = {};
  Map<String, dynamic>? _scripts;
  String _kind = BoothPage.kinds.first;
  String _view = BoothPage.views.first;
  String _media = BoothPage.mediaFilters.first;
  String _status = '';
  String _directory = '';
  String _url = 'https://booth.pm/';
  bool _browserOpen = false;
  bool _signedIn = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _loadCached());
  }

  Future<void> _loadCached() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    try {
      _applySnapshot(await host.boothLibrary());
      _scripts = await host.boothScripts();
      _directory = '${_scripts?['downloadDirectory']}';
      _signedIn = model.state?.booth.signedIn ?? false;

      if (!mounted) return;
      setState(() => _status = _summary);
    } catch (error) {
      if (mounted) setState(() => _status = '读取失败：$error');
    }
  }

  /// The sidecar folds the superseded versions of one product away and reports what
  /// each survivor stands in for, so the page only has to show the counts.
  void _applySnapshot(Map<String, dynamic> snapshot) {
    _rows = ((snapshot['items'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    _downloaded
      ..clear()
      ..addAll(((snapshot['downloaded'] as List?) ?? const [])
          .cast<String>());
    _shelved
      ..clear()
      ..addAll(((snapshot['shelved'] as List?) ?? const []).cast<String>());
    _hidden.clear();
    (snapshot['hiddenVersions'] as Map?)?.forEach(
        (key, value) => _hidden['$key'] = (value as num).toInt());
    _hiddenTotal = (snapshot['hiddenTotal'] as num?)?.toInt() ?? 0;
  }

  String get _summary => _rows.isEmpty
      ? (_signedIn ? '已登录 · 还没有读取清单' : '未登录')
      : '${_rows.length} 项 · ${_downloaded.length} 项已下载'
          '${_hiddenTotal > 0 ? ' · 同一商品的 $_hiddenTotal 个旧版本已折叠' : ''}'
          ' · $_directory';

  List<Map<String, dynamic>> get _shown => _rows.where((row) {
        final media = BoothPage.looksLikeMedia(row);
        if (_media == BoothPage.mediaFilters[0] && media) return false;
        if (_media == BoothPage.mediaFilters[2] && !media) return false;
        final has = _downloaded.contains('${row['number']}');
        if (_view == BoothPage.views[1]) return !has;
        if (_view == BoothPage.views[2]) return has;
        return true;
      }).toList();

  /// The library keeps one row per downloadable, so a re-read merges by number
  /// instead of dropping the other kinds that were already collected.
  void _absorb(List<Map<String, dynamic>> incoming) {
    final byNumber = {for (final row in _rows) '${row['number']}': row};
    for (final row in incoming) {
      byNumber['${row['number']}'] = row;
    }
    _rows = byNumber.values.toList()
      ..sort((a, b) => '${a['title']}'
          .toLowerCase()
          .compareTo('${b['title']}'.toLowerCase()));
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

  Future<void> _signIn() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    // Reuse whatever is on the vault before asking the user to log in again.
    await _seed();
    await _showBrowser('https://account.booth.pm/');
    setState(() => _status = '请在内嵌页面中完成登录，然后点「保存登录态」。');
  }

  Future<void> _seed() async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    await BrowserSession.seed(await host.sessionCookies('booth'));
  }

  Future<void> _saveLogin() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    final cookies = await BrowserSession.harvest(
        ['https://booth.pm', 'https://accounts.booth.pm']);
    if (cookies.isEmpty) {
      setState(() => _status = '浏览器里还没有 Cookie，请先在页面中登录。');
      return;
    }

    await host.importSessionCookies('booth', cookies);
    await model.refreshState();
    if (!mounted) return;
    setState(() {
      _signedIn = true;
      _status = '已保存 ${cookies.length} 个 Cookie 到本地加密库。';
    });
  }

  Future<Map<String, dynamic>?> _extract(String script) async {
    final host = AppScope.of(context).host;
    final controller = _browser.currentState?.controller;
    if (host == null || controller == null) return null;

    for (var attempt = 0; attempt < 40; attempt++) {
      final raw = await controller.evaluateJavascript(source: script);
      if (raw != null) {
        final page = await host.boothParse(BoothPage.kindOf(_kind), '$raw');
        final items = ((page['items'] as List?) ?? const []).cast<Map<String, dynamic>>();
        if (items.isNotEmpty || page['loginRequired'] == true) return page;
      }
      await Future<void>.delayed(const Duration(milliseconds: 350));
    }
    return null;
  }

  /// Walks every page of the selected list. The extractor reports the highest
  /// pagination link it can see, which is how the loop knows where to stop.
  Future<void> _read() async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    final script = _scripts?[BoothPage.kindOf(_kind) == 'free'
        ? 'freeLibrary'
        : 'purchase'] as String?;
    if (script == null) {
      setState(() => _status = '还没有取到提取脚本。');
      return;
    }

    setState(() {
      _busy = true;
      _status = '正在注入登录态并打开 Booth…';
    });
    await _seed();

    final first = await host.boothUrl(BoothPage.kindOf(_kind), 1);
    await _showBrowser(first);

    final page = await _extract(script);
    if (page == null) {
      if (mounted) setState(() { _busy = false; _status = '页面超时，没有抓到列表。'; });
      return;
    }
    if (page['loginRequired'] == true) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '登录态已失效，请在内嵌页面重新登录。';
        });
      }
      return;
    }

    _absorb(((page['items'] as List?) ?? const []).cast<Map<String, dynamic>>());
    final lastPage = (page['lastPage'] as num?)?.toInt() ?? 1;
    if (mounted) {
      setState(() => _status = '第 1 / $lastPage 页 · 已抓到 ${_rows.length} 项');
    }

    for (var next = 2; next <= lastPage; next++) {
      await _browser.currentState
          ?.navigate(await host.boothUrl(BoothPage.kindOf(_kind), next));
      final more = await _extract(script);
      if (more == null) break;
      _absorb(((more['items'] as List?) ?? const []).cast<Map<String, dynamic>>());
      if (!mounted) return;
      setState(() => _status = '第 $next / $lastPage 页 · 已抓到 ${_rows.length} 项');
    }

    _applySnapshot(await host.boothSaveLibrary(_rows));

    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = _summary;
    });
  }

  Future<void> _download(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    final number = '${row['number']}';
    final title = '${row['title']}'.isEmpty ? '商品 $number' : '${row['title']}';

    setState(() {
      _busy = true;
      _status = '正在下载 $title…';
    });

    try {
      final zip = await host.boothDownload(number);
      _downloaded.add(number);

      // A Booth archive usually wraps the real deliverable, so the .unitypackage
      // files inside it — not the zip — are what gets registered.
      final contents = await host.boothInspect(number);
      final packages = ((contents['unityPackageFiles'] as List?) ?? const [])
          .cast<String>();
      for (final package in packages) {
        await host.addPackageFile(package, origin: 'Booth');
      }

      if (packages.isNotEmpty) _shelved.add(number);
      if (!mounted) return;
      setState(() {
        _busy = false;
        _status = packages.isEmpty
            ? '$title 已下载到 $zip（这个压缩包里没有 .unitypackage）'
            : '$title 已下载并入库 ${packages.length} 个 .unitypackage';
      });
    } catch (error) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '$title 下载失败：$error';
        });
      }
    }
  }

  Future<void> _downloadSelected() async {
    final picked = _shown
        .where((row) => _selected.contains('${row['number']}'))
        .toList();
    for (final row in picked) {
      await _download(row);
    }
    _selected.clear();
    if (mounted) setState(() {});
  }

  bool _onShelf(Map<String, dynamic> row) =>
      _shelved.contains('${row['number']}');

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return NeuPageBody(
      title: 'Booth 已购',
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
            label: '读取清单',
            icon: Icons.cloud_download_rounded,
            emphasised: true,
            onTap: _busy ? null : _read,
          ),
          NeuActionButton(
            label: _browserOpen ? '隐藏页面' : '显示页面',
            icon: _browserOpen
                ? Icons.keyboard_hide_rounded
                : Icons.language_rounded,
            onTap: () => setState(() {
              _browserOpen = !_browserOpen;
              if (!_browserOpen) _url = 'https://booth.pm/';
            }),
          ),
        ],
      ),
      fixed: [
        Row(
          children: [
            Text('读取',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const SizedBox(width: 8),
            NeuDropdown(
              value: _kind,
              items: BoothPage.kinds,
              icon: Icons.category_rounded,
              onChanged: (value) => setState(() => _kind = value),
            ),
            const SizedBox(width: 14),
            Text('显示',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const SizedBox(width: 8),
            NeuDropdown(
              value: _view,
              items: BoothPage.views,
              icon: Icons.download_rounded,
              onChanged: (value) => setState(() => _view = value),
            ),
            const SizedBox(width: 14),
            Text('类型',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const SizedBox(width: 8),
            Tooltip(
              message: '可用软件包会剔除演示动画、图集、漫画等无法装入 Unity 的内容',
              child: NeuDropdown(
                value: _media,
                items: BoothPage.mediaFilters,
                icon: Icons.movie_filter_rounded,
                onChanged: (value) => setState(() {
                  _media = value;
                  _selected.clear();
                }),
              ),
            ),
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
        const SizedBox(height: 12),
        Row(
          children: [
            NeuCheckbox(
              value: shown.isNotEmpty &&
                  shown.every((r) => _selected.contains('${r['number']}')),
              onTap: () => setState(() {
                final all =
                    shown.every((r) => _selected.contains('${r['number']}'));
                _selected.clear();
                if (!all) {
                  _selected.addAll([for (final r in shown) '${r['number']}']);
                }
              }),
            ),
            const SizedBox(width: 9),
            Text('显示 ${shown.length} / ${_rows.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
          ],
        ),
      ],
      content: Column(
        children: [
          if (_browserOpen) ...[
            SizedBox(
              height: 380,
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
                    if (url.isNotEmpty) _url = url;
                  },
                ),
              ),
            ),
            const SizedBox(height: 12),
          ],
          Expanded(
            child: shown.isEmpty
                ? const NeuEmptyState(
                    expand: true,
                    text: '清单还是空的。\n先「登录」保存登录态，再点「读取清单」，'
                        '页面会逐页抓取已购、礼物与免费商品。')
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final row = shown[index];
                      final number = '${row['number']}';
                      final has = _downloaded.contains(number);
                      final title =
                          '${row['title']}'.isEmpty ? '商品 $number' : '${row['title']}';
                      final checked = _selected.contains(number);

                      return NeuCard(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 14, vertical: 11),
                        child: Row(
                          children: [
                            NeuCheckbox(
                              value: checked,
                              onTap: () => setState(() => checked
                                  ? _selected.remove(number)
                                  : _selected.add(number)),
                            ),
                            const SizedBox(width: 12),
                            NeuCover('${row['imageUrl']}'),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(title,
                                      style: const TextStyle(
                                          fontSize: 13.5,
                                          fontWeight: FontWeight.w600),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis),
                                  const SizedBox(height: 3),
                                  Text(
                                      '${row['shop']} · ${'${row['formLabel']}'.isEmpty ? '默认版本' : row['formLabel']}',
                                      style: TextStyle(
                                          fontSize: 11, color: t.inkTertiary),
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis),
                                ],
                              ),
                            ),
                            const SizedBox(width: 10),
                            if (row['isFree'] == true)
                              const Padding(
                                padding: EdgeInsets.only(right: 8),
                                child: NeuPill('免费领取'),
                              ),
                            if (BoothPage.looksLikeMedia(row))
                              const Padding(
                                padding: EdgeInsets.only(right: 8),
                                child: NeuPill('演示·图集',
                                    bg: Color(0xFFEBD9B4),
                                    fg: Color(0xFF7A5514)),
                              ),
                            if (_hidden[number] != null)
                              Padding(
                                padding: const EdgeInsets.only(right: 8),
                                child: NeuPill(
                                    '已折叠 ${_hidden[number]} 个旧版本'),
                              ),
                            if (_onShelf(row))
                              const Padding(
                                padding: EdgeInsets.only(right: 8),
                                child: NeuPill('已入库',
                                    bg: Color(0xFFBFE3C9),
                                    fg: Color(0xFF237A45)),
                              ),
                            NeuPill(
                              has ? '已下载' : '未下载',
                              bg: has
                                  ? const Color(0xFFBFE3C9)
                                  : Colors.black.withValues(alpha: 0.06),
                              fg: has ? const Color(0xFF237A45) : null,
                            ),
                            const SizedBox(width: 8),
                            _Poke(
                                icon: Icons.open_in_new_rounded,
                                tooltip: '在浏览器打开商品页',
                                onTap: () => _showBrowser('${row['itemUrl']}')),
                            const SizedBox(width: 6),
                            NeuActionButton(
                              label: has ? '重新下载' : '下载并入库',
                              icon: Icons.file_download_outlined,
                              onTap: _busy ? null : () => _download(row),
                            ),
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

class _Poke extends StatelessWidget {
  const _Poke({required this.icon, required this.tooltip, required this.onTap});

  final IconData icon;
  final String tooltip;
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
        onTap: onTap,
        child: Icon(icon, size: 16, color: t.inkSecondary),
      ),
    );
  }
}
