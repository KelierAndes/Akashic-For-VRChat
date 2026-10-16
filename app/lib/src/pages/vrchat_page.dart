import 'dart:async';

import 'package:flutter/material.dart';

import '../browser/browser_panel.dart';
import '../shell/app_scope.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

/// VRChat 数据同步. The content list comes off the disk cache first, so opening this
/// page costs nothing; only 刷新云端 walks the API. The local blueprint map is memoised
/// in the app model because scanning every project is the expensive half.
class VRChatSyncPage extends StatefulWidget {
  const VRChatSyncPage({super.key});

  static const views = ['全部内容', '世界', '虚拟形象', '已关联本地工程'];
  static const states = ['全部状态', '公开', '私有', '内部'];
  static const codes = {'公开': 'public', '私有': 'private', '内部': 'internal'};
  static String labelOf(String code) =>
      codes.entries
          .firstWhere((e) => e.value == code,
              orElse: () => MapEntry(code, code))
          .key;

  /// Position of a server status inside [states] without the 全部状态 entry.
  static int indexOfCode(String code) {
    for (var i = 1; i < states.length; i++) {
      if (codes[states[i]] == code) return i - 1;
    }
    return 0;
  }

  /// Some records only carry the full-size artwork, so the thumbnail falls back to it.
  static String coverOf(Map<String, dynamic> row) {
    final thumb = '${row['thumbnailImageUrl']}';
    return thumb.isNotEmpty ? thumb : '${row['imageUrl']}';
  }

  @override
  State<VRChatSyncPage> createState() => _VRChatSyncPageState();
}

class _VRChatSyncPageState extends State<VRChatSyncPage> {
  final GlobalKey<BrowserPanelState> _browser = GlobalKey<BrowserPanelState>();
  final TextEditingController _search = TextEditingController();

  List<Map<String, dynamic>> _rows = [];
  Map<String, dynamic> _local = {};
  Map<String, dynamic>? _self;
  String _view = VRChatSyncPage.views.first;
  String _state = VRChatSyncPage.states.first;
  String _status = '';
  String _savedAt = '';
  String _url = 'https://vrchat.com/home';
  bool _browserOpen = false;
  bool _signedIn = false;
  bool _busy = false;

  /// Set by a jump from a project card: the list narrows to these blueprint ids until
  /// the chip is cleared.
  Set<String> _focus = {};

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
    final jump = model.takeVrchatJump();
    if (jump.isNotEmpty) _focus = jump.toSet();

    final host = model.host;
    if (host == null) return;

    try {
      final cache = await host.vrchatContentCache();
      _rows =
          ((cache['items'] as List?) ?? const []).cast<Map<String, dynamic>>();
      _savedAt = _stamp(cache['savedAt']);
      _signedIn = model.state?.vrchat.signedIn ?? false;

      // The list is the reason this page exists, so it goes on screen before
      // anything that has to ask the network.
      if (mounted) setState(() => _status = _describe());

      if (!_signedIn) return;

      // A dead session answers 401, which the client already folds into null.
      _self = await host.vrchatSelf();

      // Cached rows already answer the question the map is for, so the scan only
      // runs when there is something to match against.
      if (_rows.isNotEmpty) _local = await model.vrchatLocalMap();

      if (!mounted) return;
      setState(() => _status = _describe());

      // Nothing on disk yet: the first read has to come from the API.
      if (_rows.isEmpty) await _refresh();
    } catch (error) {
      // A blank page with no explanation is worse than a red line of text.
      if (mounted) setState(() => _status = '读取失败：$error');
    }
  }

  /// The sidecar writes a full ISO stamp; the subtitle only has room for the minute.
  static String _stamp(Object? raw) {
    final parsed = DateTime.tryParse('$raw');
    if (parsed == null) return '';
    final local = parsed.toLocal();
    String two(int v) => v.toString().padLeft(2, '0');
    return '${two(local.month)}/${two(local.day)} ${two(local.hour)}:${two(local.minute)}';
  }

  String _describe() {
    final who = _self?['displayName'] ?? '未登录';
    final matched = _rows
        .where((row) => _local['${row['id']}'] != null)
        .length;
    return '$who · ${_rows.length} 项 · 本地关联 $matched · '
        '${_savedAt.isEmpty ? '无缓存' : '缓存于 $_savedAt'}';
  }

  List<Map<String, dynamic>> get _shown {
    final query = _search.text.trim().toLowerCase();
    final wantedCode = VRChatSyncPage.codes[_state];
    return _rows.where((row) {
      if (_focus.isNotEmpty && !_focus.contains('${row['id']}')) return false;
      if (_view == '世界' && '${row['kind']}' != 'World') return false;
      if (_view == '虚拟形象' && '${row['kind']}' != 'Avatar') return false;
      if (_view == '已关联本地工程' && _local['${row['id']}'] == null) return false;
      if (wantedCode != null && '${row['releaseStatus']}' != wantedCode) {
        return false;
      }
      if (query.isNotEmpty &&
          !('${row['name']}'.toLowerCase().contains(query) ||
              '${row['id']}'.toLowerCase().contains(query))) {
        return false;
      }
      return true;
    }).toList()
      ..sort((a, b) =>
          '${b['updated_at']}'.compareTo('${a['updated_at']}'));
  }

  Future<void> _refresh() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    setState(() {
      _busy = true;
      _status = '正在读取云端内容…';
    });

    final content = await host.vrchatContent();
    if (content.isEmpty) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = _signedIn
              ? '云端没有返回内容，登录态可能已失效。'
              : '还没有登录，无法读取云端。';
        });
      }
      return;
    }

    _rows = content.cast<Map<String, dynamic>>();
    await host.vrchatSaveContentCache(_rows);
    _local = await model.refreshVrchatLocalMap();
    _savedAt = _stamp(DateTime.now().toIso8601String());

    if (!mounted) return;
    setState(() {
      _busy = false;
      _status = _describe();
    });
  }

  Future<void> _nextFrame() async {
    final completer = Completer<void>();
    WidgetsBinding.instance.addPostFrameCallback((_) => completer.complete());
    await completer.future;
  }

  Future<void> _signIn() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    await BrowserSession.seed(await host.sessionCookies('vrchat'));
    setState(() {
      _browserOpen = true;
      _url = 'https://vrchat.com/home';
    });
    await _nextFrame();
    await _browser.currentState?.navigate(_url);
    if (mounted) {
      setState(() =>
          _status = '请在内嵌页面登录 VRChat，然后点「保存登录态」。');
    }
  }

  Future<void> _saveLogin() async {
    final model = AppScope.of(context);
    final host = model.host;
    if (host == null) return;

    final cookies = await BrowserSession.harvest([
      'https://vrchat.com',
      'https://api.vrchat.cloud',
    ]);
    if (cookies.isEmpty) {
      setState(() => _status = '浏览器里还没有 Cookie，请先登录。');
      return;
    }

    await host.importSessionCookies('vrchat', cookies);
    await model.refreshState();
    if (!mounted) return;
    setState(() {
      _signedIn = true;
      _browserOpen = false;
      _status = '已保存 ${cookies.length} 个 Cookie 到本地加密库。';
    });
    await _refresh();
  }

  Future<void> _applyEdit(Map<String, dynamic> row, Map<String, dynamic> edit) async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    setState(() {
      _busy = true;
      _status = '正在提交 ${row['name']}…';
    });

    try {
      await host.vrchatUpdateContent({
        'id': row['id'],
        'kind': row['kind'],
        ...edit,
      });
      if (mounted) setState(() => _busy = false);
      await _refresh();
    } catch (error) {
      if (mounted) {
        setState(() {
          _busy = false;
          _status = '提交失败：$error';
        });
      }
    }
  }

  Future<void> _publish(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    try {
      final content = await host.vrchatPublish('${row['id']}');
      _rows = (content as List).cast<Map<String, dynamic>>();
      await host.vrchatSaveContentCache(_rows);
      if (mounted) setState(() => _status = '${row['name']} 已公开。');
    } catch (error) {
      if (mounted) setState(() => _status = '发布失败：$error');
    }
  }

  Future<void> _unpublish(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    try {
      final content = await host.vrchatUnpublish('${row['id']}');
      _rows = (content as List).cast<Map<String, dynamic>>();
      await host.vrchatSaveContentCache(_rows);
      if (mounted) setState(() => _status = '${row['name']} 已改为私有。');
    } catch (error) {
      if (mounted) setState(() => _status = '下架失败：$error');
    }
  }

  Future<void> _selectAvatar(Map<String, dynamic> row) async {
    final host = AppScope.of(context).host;
    if (host == null) return;
    try {
      _self = await host.vrchatSelectAvatar('${row['id']}', true);
      if (mounted) setState(() => _status = '已切换为 ${row['name']}。');
    } catch (error) {
      if (mounted) setState(() => _status = '切换失败：$error');
    }
  }

  void _edit(Map<String, dynamic> row) {
    AppScope.of(context).showModal(_VrChatEditForm(
      row: row,
      onSubmit: (edit) => _applyEdit(row, edit),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = _shown;

    return NeuPageBody(
      title: 'VRChat 数据同步',
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
          if (_browserOpen)
            NeuActionButton(
              label: '保存登录态',
              icon: Icons.save_alt_rounded,
              onTap: _busy ? null : _saveLogin,
            ),
          NeuActionButton(
            label: '刷新云端',
            icon: Icons.cloud_sync_rounded,
            emphasised: true,
            onTap: _busy ? null : _refresh,
          ),
        ],
      ),
      fixed: [
        Row(
          children: [
            Expanded(
              child: NeuSearchField(
                  hint: '按名称或蓝图 ID 过滤…',
                  controller: _search,
                  onChanged: (_) => setState(() {})),
            ),
            const SizedBox(width: 14),
            NeuDropdown(
              value: _view,
              items: VRChatSyncPage.views,
              icon: Icons.category_rounded,
              onChanged: (value) => setState(() => _view = value),
            ),
            const SizedBox(width: 12),
            NeuDropdown(
              value: _state,
              items: VRChatSyncPage.states,
              icon: Icons.lock_open_rounded,
              onChanged: (value) => setState(() => _state = value),
            ),
            const SizedBox(width: 14),
            Text('显示 ${shown.length} / ${_rows.length} 项',
                style: TextStyle(fontSize: 12, color: t.inkTertiary)),
            if (_focus.isNotEmpty) ...[
              const SizedBox(width: 12),
              NeuPill('工程定位 ${_focus.length} 项 · 点击清除',
                  bg: const Color(0xFFC7DCF3),
                  fg: const Color(0xFF1F5A8A),
                  tooltip: '取消从项目卡片带来的定位',
                  onTap: () => setState(() => _focus = {})),
            ],
            const Spacer(),
            if (_browserOpen)
              NeuActionButton(
                label: '隐藏页面',
                icon: Icons.keyboard_hide_rounded,
                onTap: () => setState(() => _browserOpen = false),
              ),
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
                ? NeuEmptyState(
                    expand: true,
                    text: _signedIn
                        ? '清单还是空的。\n点「刷新云端」读取一次，之后每次打开都会先用本地缓存。'
                        : '还没有登录 VRChat。\n点「登录」在内嵌页面完成登录后再点「保存登录态」。')
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: shown.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final row = shown[index];
                      final id = '${row['id']}';
                      final isWorld = '${row['kind']}' != 'Avatar';
                      final local = _local[id];
                      final projectName = local == null
                          ? ''
                          : '${(local as Map)['name']}';

                      return NeuCard(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 14, vertical: 11),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Row(
                              children: [
                                NeuCover(VRChatSyncPage.coverOf(row), width: 80, height: 54),
                                const SizedBox(width: 12),
                                Expanded(
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text('${row['name']}',
                                          style: const TextStyle(
                                              fontSize: 13.5,
                                              fontWeight: FontWeight.w600),
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis),
                                      const SizedBox(height: 3),
                                      Text(id,
                                          style: TextStyle(
                                              fontSize: 11,
                                              color: t.inkTertiary),
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis),
                                    ],
                                  ),
                                ),
                                const SizedBox(width: 10),
                                NeuPill(isWorld ? '世界' : '虚拟形象'),
                                const SizedBox(width: 8),
                                NeuPill(VRChatSyncPage.labelOf(
                                    '${row['releaseStatus']}')),
                                const SizedBox(width: 8),
                                if (isWorld &&
                                    ((row['capacity'] as num?)?.toInt() ?? 0) > 0)
                                  Padding(
                                    padding: const EdgeInsets.only(right: 8),
                                    child: NeuPill(
                                        '容量 ${row['capacity']}'),
                                  ),
                                if (!isWorld)
                                  Padding(
                                    padding: const EdgeInsets.only(right: 8),
                                    child: NeuPill(_performance(row)),
                                  ),
                                if (local != null)
                                  Padding(
                                    padding: const EdgeInsets.only(right: 8),
                                    child: NeuPill('本地：$projectName',
                                        bg: const Color(0xFFBFE3C9),
                                        fg: const Color(0xFF237A45)),
                                  ),
                              ],
                            ),
                            if ('${row['description']}'.isNotEmpty) ...[
                              const SizedBox(height: 8),
                              Text('${row['description']}',
                                  style: TextStyle(
                                      fontSize: 11.5,
                                      height: 1.5,
                                      color: t.inkSecondary),
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis),
                            ],
                            const SizedBox(height: 10),
                            Wrap(
                              spacing: 8,
                              runSpacing: 8,
                              alignment: WrapAlignment.end,
                              children: [
                                NeuActionButton(
                                  label: '编辑',
                                  icon: Icons.edit_outlined,
                                  onTap: _busy ? null : () => _edit(row),
                                ),
                                if (isWorld)
                                  NeuActionButton(
                                    label: '${row['releaseStatus']}' == 'public'
                                        ? '改为私有'
                                        : '公开发布',
                                    icon: '${row['releaseStatus']}' == 'public'
                                        ? Icons.visibility_off_rounded
                                        : Icons.public_rounded,
                                    onTap: _busy
                                        ? null
                                        : () =>
                                            '${row['releaseStatus']}' == 'public'
                                                ? _unpublish(row)
                                                : _publish(row),
                                  )
                                else
                                  NeuActionButton(
                                    label: '设为当前形象',
                                    icon: Icons.face_retouching_natural_rounded,
                                    onTap: _busy
                                        ? null
                                        : () => _selectAvatar(row),
                                  ),
                                if (local != null)
                                  NeuActionButton(
                                    label: '打开本地工程',
                                    icon: Icons.folder_open_rounded,
                                    onTap: () {
                                      final model = AppScope.of(context);
                                      model.openProject(
                                          '${(local as Map)['path']}');
                                      model.jumpTo('projects');
                                    },
                                  ),
                                NeuActionButton(
                                  label: '云端管理页',
                                  icon: Icons.open_in_new_rounded,
                                  onTap: () => AppScope.of(context).openUrl(
                                      'https://vrchat.com/home/${isWorld ? 'world' : 'avatar'}/$id'),
                                ),
                              ],
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

  static String _performance(Map<String, dynamic> row) {
    final performance = row['performance'];
    if (performance is Map) {
      for (final key in ['standalonewindows', 'android', 'ios']) {
        final value = performance[key];
        if (value is String && value.isNotEmpty) return '评级 $value';
      }
    }
    return '评级 —';
  }
}

/// Name, description and — for a world — capacity and release status. Anything the
/// form leaves out is sent as null so the sidecar keeps the server's own value.
class _VrChatEditForm extends StatefulWidget {
  const _VrChatEditForm({required this.row, required this.onSubmit});

  final Map<String, dynamic> row;
  final void Function(Map<String, dynamic> edit) onSubmit;

  @override
  State<_VrChatEditForm> createState() => _VrChatEditFormState();
}

class _VrChatEditFormState extends State<_VrChatEditForm> {
  late final TextEditingController _name =
      TextEditingController(text: '${widget.row['name']}');
  late final TextEditingController _description =
      TextEditingController(text: '${widget.row['description']}');
  late final TextEditingController _capacity = TextEditingController(
      text: ((widget.row['capacity'] as num?)?.toInt() ?? 0).toString());
  late int _status =
      VRChatSyncPage.indexOfCode('${widget.row['releaseStatus']}');

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _capacity.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isWorld = '${widget.row['kind']}' != 'Avatar';
    final statuses = VRChatSyncPage.states.skip(1).toList();

    return NeuModalScaffold(
      title: '编辑 ${isWorld ? '世界' : '虚拟形象'}',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: '名称',
            child: NeuTextInput(controller: _name, hint: '显示在 VRChat 里的名字'),
          ),
          NeuField(
            label: '描述',
            child: NeuTextInput(
                controller: _description,
                hint: '简介、操作说明…',
                maxLines: 5),
          ),
          if (isWorld) ...[
            NeuField(
              label: '人数上限',
              hint: '世界容量会按两倍的推荐人数计算',
              child: NeuTextInput(controller: _capacity, hint: '例如 16'),
            ),
            NeuField(
              label: '发布状态',
              child: NeuChoiceField(
                values: statuses,
                index: _status,
                icon: Icons.lock_open_rounded,
                onChanged: (i) => setState(() => _status = i),
              ),
            ),
          ],
          const SizedBox(height: 6),
          NeuDialogActions(
            confirmLabel: '保存',
            onCancel: AppScope.of(context).closeModal,
            onConfirm: () {
              final edit = <String, dynamic>{
                'name': _name.text.trim(),
                'description': _description.text,
                if (isWorld)
                  'releaseStatus': VRChatSyncPage.codes[statuses[_status]],
                if (isWorld)
                  'maxCapacity': int.tryParse(_capacity.text.trim()),
              };
              AppScope.of(context).closeModal();
              widget.onSubmit(edit);
            },
          ),
        ],
      ),
    );
  }
}
