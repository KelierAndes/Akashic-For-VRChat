import 'dart:async';

import 'package:flutter/material.dart';

import '../browser/browser_panel.dart';
import '../shell/app_scope.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

class BoothPage extends StatefulWidget {
  const BoothPage({super.key});

  static const views = ['全部商品', '未下载', '已下载'];

  static const mediaFilters = ['可用软件包', '全部商品', '演示·图集'];
  static const mediaKeywords = [
    'デモ動画', 'デモムービー', 'サンプル動画', '紹介動画', '宣伝動画', '予告動画',
    '演示动画', '演示视频', '视频演示', 'sample video', 'demo video',
    'demo movie', 'preview movie', 'trailer',
    'サンプル画像', 'sample画像', '画像集', 'sample image', 'sample picture',
    'イラスト', '插画', '画集', '图集', 'artbook', 'art book', 'fanbook',
    'ファンブック', '設定資料集', '写真集', 'フォト集', 'photo book', 'photobook',
    'illustration', 'コミック', '漫画', 'comic', '小説', '小说', 'novel',
    'サンプル', 'お試し', '体験版', '試聴', 'デモ', '素材集', '壁紙',
  ];

  static const mediaTokens = {
    'sample', 'samples', 'demo', 'preview', 'trailer', 'teaser', 'promo', 'pv',
  };

  static bool looksLikeMedia(Map<String, dynamic> row) {
    final text = '${row['title']} ${row['formLabel']}'.toLowerCase();
    if (mediaKeywords.any(text.contains)) return true;

    final tokens = text.split(RegExp(r'[^0-9a-z\u3040-\u30ff\u4e00-\u9fff]+'));
    return tokens.any(mediaTokens.contains);
  }

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
  String _view = BoothPage.views.first;
  String _media = BoothPage.mediaFilters.first;
  String _category = '全部分类';
  String _status = '';
  String _directory = '';
  String _url = 'https://booth.pm/';
  bool _browserOpen = false;
  bool _signedIn = false;
  bool _busy = false;

  String _lastProbe = '';

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

  List<String> get _categories => [
        '全部分类',
        '未分类',
        ...{
          for (final row in _rows)
            if ('${row['category']}'.isNotEmpty) '${row['category']}'
        },
      ];

  List<Map<String, dynamic>> get _shown => _rows.where((row) {
        final media = BoothPage.looksLikeMedia(row);
        if (_media == BoothPage.mediaFilters[0] && media) return false;
        if (_media == BoothPage.mediaFilters[2] && !media) return false;
        final category = '${row['category']}';
        if (_category == '未分类' && category.isNotEmpty) return false;
        if (_category != '全部分类' && _category != '未分类' && category != _category) {
          return false;
        }
        final has = _downloaded.contains('${row['number']}');
        if (_view == BoothPage.views[1]) return !has;
        if (_view == BoothPage.views[2]) return has;
        return true;
      }).toList();

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

  Future<Map<String, dynamic>?> _extractWith(String kind, String script) async {
    final host = AppScope.of(context).host;
    if (host == null) return null;

    for (var wait = 0;
        wait < 60 && _browser.currentState?.controller == null;
        wait++) {
      await Future<void>.delayed(const Duration(milliseconds: 250));
    }

    final controller = _browser.currentState?.controller;
    if (controller == null) {
      _lastProbe = '页面组件未就绪 · 页面 $_url';
      return null;
    }

    Map<String, dynamic>? last;
    var sawRaw = false;

    for (var attempt = 0; attempt < 60; attempt++) {
      final raw = await controller.evaluateJavascript(source: script);
      if (raw != null) {
        sawRaw = true;
        last = await host.boothParse(kind, '$raw');
        final items = ((last['items'] as List?) ?? const []).cast<Map<String, dynamic>>();
        if (items.isNotEmpty || last['loginRequired'] == true) return last;
      }
      await Future<void>.delayed(const Duration(milliseconds: 350));
    }

    final count = ((last?['items'] as List?) ?? const []).length;
    _lastProbe = '${sawRaw ? '已解析 $count 项' : '脚本无响应'} · 页面 $_url';
    return null;
  }

  Future<Map<String, dynamic>?> _readList({
    required String kind,
    required String script,
    required String label,
    required void Function(List<Map<String, dynamic>> items) onItems,
    bool navigateFirst = true,
  }) async {
    final host = AppScope.of(context).host;
    if (host == null) return null;

    if (navigateFirst) {
      await _browser.currentState?.navigate(await host.boothUrl(kind, 1));
    }
    final first = await _extractWith(kind, script);
    if (first == null || first['loginRequired'] == true) return first;

    onItems(((first['items'] as List?) ?? const []).cast<Map<String, dynamic>>());
    final lastPage = (first['lastPage'] as num?)?.toInt() ?? 1;
    if (mounted) setState(() => _status = '$label 第 1 / $lastPage 页');

    for (var next = 2; next <= lastPage; next++) {
      await _browser.currentState
          ?.navigate(await host.boothUrl(kind, next));
      final more = await _extractWith(kind, script);
      if (more == null) break;
      if (more['loginRequired'] == true) return more;

      onItems(((more['items'] as List?) ?? const []).cast<Map<String, dynamic>>());
      if (!mounted) return more;
      setState(() => _status = '$label 第 $next / $lastPage 页');
    }

    return first;
  }

  Future<List<Map<String, dynamic>>> _readFreeDownloads(String itemUrl,
      String itemScript) async {
    final host = AppScope.of(context).host;
    if (host == null) return const [];

    for (var wait = 0;
        wait < 60 && _browser.currentState?.controller == null;
        wait++) {
      await Future<void>.delayed(const Duration(milliseconds: 250));
    }

    final controller = _browser.currentState?.controller;
    if (controller == null) return const [];

    await _browser.currentState?.navigate(itemUrl);

    for (var attempt = 0; attempt < 12; attempt++) {
      final raw = await controller.evaluateJavascript(source: itemScript);
      if (raw != null) {
        final page = await host.boothParse('item', '$raw');
        final items =
            ((page['items'] as List?) ?? const []).cast<Map<String, dynamic>>();
        if (items.isNotEmpty) return items;
      }
      await Future<void>.delayed(const Duration(milliseconds: 300));
    }

    return const [];
  }

  Future<void> _read() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    final purchaseScript = _scripts?['purchase'] as String?;
    final freeScript = _scripts?['freeLibrary'] as String?;
    final itemScript = _scripts?['itemPage'] as String?;
    if (purchaseScript == null || freeScript == null || itemScript == null) {
      setState(() => _status = '还没有取到提取脚本。');
      return;
    }

    setState(() {
      _busy = true;
      _status = '正在注入登录态并打开 Booth…';
    });
    await _seed();
    await _showBrowser(await host.boothUrl('purchase', 1));

    final collected = <Map<String, dynamic>>[];
    final seen = <String>{};

    void collect(List<Map<String, dynamic>> items) {
      for (final item in items) {
        final number = '${item['number']}';
        if (number.isEmpty || !seen.add(number)) continue;
        collected.add(item);
      }
    }

    final paid = await _readList(
      kind: 'purchase',
      script: purchaseScript,
      label: '已购列表',
      onItems: collect,
      navigateFirst: false,
    );
    if (paid == null) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '页面超时，没有抓到已购列表（$_lastProbe）。';
        });
      }
      return;
    }
    if (paid['loginRequired'] == true) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '登录态已失效，请在内嵌页面重新登录。';
        });
      }
      return;
    }

    final paidCount = collected.length;

    await _readList(
      kind: 'gifts',
      script: purchaseScript,
      label: '礼物列表',
      onItems: (items) {
        for (final item in items) {
          item['isFree'] = true;
        }
        collect(items);
      },
    );
    final giftCount = collected.length - paidCount;

    final cards = <Map<String, dynamic>>[];
    final cardSeen = <String>{};

    void collectCard(Map<String, dynamic> item) {
      final number = '${item['number']}';
      if (number.isEmpty || !cardSeen.add(number)) return;
      cards.add(item);
    }

    await _readList(
      kind: 'free',
      script: freeScript,
      label: '免费领取列表',
      onItems: (items) {
        for (final item in items) {
          collectCard(item);
        }
      },
    );

    var withoutDownload = 0;
    for (var i = 0; i < cards.length; i++) {
      if (!mounted || !_busy) break;

      final card = cards[i];
      setState(() => _status =
          '正在读取免费商品 ${i + 1} / ${cards.length}：${card['title']}');

      final downloads =
          await _readFreeDownloads('${card['itemUrl']}', itemScript);
      if (downloads.isEmpty) {
        withoutDownload++;
        continue;
      }

      for (final item in downloads) {
        item['isFree'] = true;
        if ('${card['imageUrl']}'.isNotEmpty) item['imageUrl'] = card['imageUrl'];
        if ('${item['title']}'.isEmpty) item['title'] = card['title'];
        if ('${item['shop']}'.isEmpty) item['shop'] = card['shop'];
        if ('${item['shopUrl']}'.isEmpty) item['shopUrl'] = card['shopUrl'];
      }

      collect(downloads);
    }

    final freeCount = collected.length - paidCount - giftCount;

    _rows = collected
      ..sort((a, b) => '${a['title']}'
          .toLowerCase()
          .compareTo('${b['title']}'.toLowerCase()));
    _applySnapshot(await host.boothSaveLibrary(_rows));

    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = collected.isEmpty
          ? '已购列表是空的，确认一下登录的是购买过的账号。'
          : '已读取 ${collected.length} 个商品（已购 $paidCount · 礼物 $giftCount · 免费 $freeCount）'
              '${withoutDownload > 0 ? ' · $withoutDownload 个免费商品无下载文件' : ''}'
              '${_hiddenTotal > 0 ? ' · 同一商品的 $_hiddenTotal 个旧版本已折叠' : ''}';
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
      title: 'Booth',
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
            const SizedBox(width: 14),
            Text('分类',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            const SizedBox(width: 8),
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
                            if ('${row['category']}'.isNotEmpty)
                              Padding(
                                padding: const EdgeInsets.only(right: 8),
                                child: NeuPill('${row['category']}'),
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
