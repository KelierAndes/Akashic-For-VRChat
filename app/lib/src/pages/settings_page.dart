import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../state/app_model.dart';
import '../ui/app_colors.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

/// Paths, editors, backup behaviour, the hosting tools and the three stored logins —
/// all of it read back from the sidecar after every change so the page shows what is
/// actually on disk.
class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key});

  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  /// Marks the 配置同步 card so the sidebar account card can scroll to it.
  final GlobalKey _syncKey = GlobalKey();

  /// Static on purpose: the page is thrown away and rebuilt every time it is opened, and a fresh
  /// instance must not treat an older request from the account card as its own. Seeded at 0,
  /// which is what the counter starts at, so clicking 设置 in the rail never scrolls.
  static int _revealed = 0;

  @override
  void initState() {
    super.initState();
    // The probe spawns git, git-lfs, cm and gh once each, so it runs when this page is
    // opened rather than on every start-up.
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final model = AppScope.of(context);
      await model.loadVcsTools();
      await model.loadSync();
    });
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final state = model.state;
    final t = NeuTheme.of(context);

    // The account card asks for this by bumping a counter; scrolling has to wait for the
    // card to exist, which is the frame after the page is first shown.
    if (model.syncReveal != _revealed) {
      _revealed = model.syncReveal;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        final target = _syncKey.currentContext;
        if (target == null) return;
        Scrollable.ensureVisible(target,
            alignment: 0.06,
            duration: const Duration(milliseconds: 260),
            curve: Curves.easeOut);
      });
    }

    return NeuPageBody(
      title: '设置',
      subtitle: state == null
          ? '本地数据服务未连接'
          : '共 ${state.editors.length} 个 Unity 编辑器 · 备份写入 ${state.backupDirectory}',
      fixed: [
        const NeuSectionTitle('路径'),
        _PathRow(
          label: '默认工程目录',
          hint: '新建工程与「扫描目录」都以此处为起点',
          value: state?.defaultProjectPath ?? '',
          onPick: () async {
            final picked = await FilePicker.getDirectoryPath(dialogTitle: '选择默认工程目录');
            if (picked != null) {
              await model.updateSetting((c) => c.setDefaultProjectPath(picked));
            }
          },
        ),
        const SizedBox(height: 12),
        _PathRow(
          label: '备份目录',
          hint: '工程备份压缩包的输出位置',
          value: state?.backupDirectory ?? '',
          onPick: () async {
            final picked = await FilePicker.getDirectoryPath(dialogTitle: '选择备份目录');
            if (picked != null) {
              await model.updateSetting((c) => c.setBackupPath(picked));
            }
          },
        ),
        const SizedBox(height: 12),
        _PathRow(
          label: 'Booth 下载目录',
          hint: '已购商品 zip 的落盘位置，资源页按此识别本地文件',
          value: state?.booth.directory ?? '',
          onPick: () async {
            final picked = await FilePicker.getDirectoryPath(dialogTitle: '选择 Booth 下载目录');
            if (picked != null) {
              await model.updateSetting((c) => c.setBoothDownloadDir(picked));
            }
          },
        ),
        const SizedBox(height: 12),
        _PathRow(
          label: 'Asset Store 缓存目录',
          hint: 'Unity 下载助手写入 .unitypackage 的位置',
          value: state?.assetStore.directory ?? '',
          onPick: () async {
            final picked = await FilePicker.getDirectoryPath(dialogTitle: '选择 Asset Store 缓存目录');
            if (picked != null) {
              await model.updateSetting((c) => c.setAssetStoreCacheDir(picked));
            }
          },
        ),
        const SizedBox(height: 12),
        _PathRow(
          label: 'Asset Store 下载目录',
          hint: '从我的资产直接下载时使用的目录',
          value: state?.assetStoreDownloadDirectory ?? '',
          onPick: () async {
            final picked = await FilePicker.getDirectoryPath(dialogTitle: '选择 Asset Store 下载目录');
            if (picked != null) {
              await model.updateSetting(
                  (c) => c.setAssetStoreDownloadDir(picked));
            }
          },
        ),
        const SizedBox(height: 12),
        NeuSwitchTile(
          label: '备份剔除软件包',
          hint: '只保留清单与工程设置，Packages 与 Assets 下的包目录不写入备份',
          value: state?.backupExcludesPackages ?? false,
          onChanged: (value) =>
              model.updateSetting((c) => c.setBackupExcludesPackages(value)),
        ),
        const SizedBox(height: 26),
        Row(
          // The section title carries its own bottom padding, so a centred row let
          // the taller button hang into the list below it.
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const NeuSectionTitle('Unity 编辑器'),
            const Spacer(),
            NeuActionButton(
              label: '添加编辑器',
              icon: Icons.add_rounded,
              onTap: () async {
                final picked = await FilePicker.pickFiles(
                  dialogTitle: '选择 Unity 可执行文件',
                  type: FileType.custom,
                  allowedExtensions: const ['exe'],
                );
                if (picked.isEmpty) return;
                final path = picked.single.path;
                if (path == null) return;
                await model.updateSetting((c) => c.addEditor(path));
              },
            ),
          ],
        ),
        const SizedBox(height: 10),
        if ((state?.editors ?? []).isEmpty)
          const NeuEmptyState(height: 140, text: '还没有登记任何 Unity 编辑器。')
        else
          Column(
            children: [
              for (final editor in state!.editors)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: NeuCard(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 16, vertical: 13),
                    child: Row(
                      children: [
                        NeuAvatar(editor.version.isNotEmpty
                            ? editor.version.substring(0, 1)
                            : 'U'),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text('${editor.version} · ${editor.source}',
                                  style: const TextStyle(
                                      fontSize: 13.5,
                                      fontWeight: FontWeight.w600),
                                  overflow: TextOverflow.ellipsis),
                              const SizedBox(height: 2),
                              Text(editor.path,
                                  style: TextStyle(
                                      fontSize: 11.5, color: t.inkTertiary),
                                  overflow: TextOverflow.ellipsis),
                            ],
                          ),
                        ),
                        NeuButton(
                          width: 32,
                          height: 32,
                          radius: 9,
                          depth: 4,
                          color: t.raised,
                          insetColor: t.inset,
                          border: glassBorder(alpha: 0.45),
                          onTap: () =>
                              model.updateSetting((c) => c.removeEditor(editor.path)),
                          child: Icon(Icons.delete_outline_rounded,
                              size: 17, color: t.inkSecondary),
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        const SizedBox(height: 26),
        const NeuSectionTitle('登录态'),
        _SessionRow(
          name: 'Booth',
          site: 'booth',
          signedIn: state?.booth.signedIn ?? false,
          detail: '已购 / 礼物 / 免费领取的读取与下载都依赖此会话',
        ),
        const SizedBox(height: 12),
        _SessionRow(
          name: 'Unity Asset Store',
          site: 'assetstore',
          signedIn: state?.assetStore.signedIn ?? false,
          detail: '我的资产列表与付费内容下载',
        ),
        const SizedBox(height: 12),
        _SessionRow(
          name: 'VRChat',
          site: 'vrchat',
          signedIn: state?.vrchat.signedIn ?? false,
          detail: '内容与蓝图清单、名称描述回写',
        ),
        const SizedBox(height: 26),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const NeuSectionTitle('版本控制依赖'),
            const Spacer(),
            if (model.installingTool)
              const Padding(
                padding: EdgeInsets.fromLTRB(0, 6, 12, 0),
                child: SizedBox(
                  width: 15,
                  height: 15,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            NeuActionButton(
              label: '重新检测',
              icon: Icons.refresh_rounded,
              onTap: model.loadVcsTools,
            ),
          ],
        ),
        const SizedBox(height: 10),
        _ToolRow(
          name: 'Git',
          detail: '工程历史、提交与远端同步的基础',
          tool: 'git',
          url: 'https://git-scm.com/download/win',
          installed: _flag(model.vcsTools['gitFound']),
          version: '${model.vcsTools['gitVersion'] ?? ''}',
          busy: model.installingTool,
          onInstall: () => model.installVcsTool('git'),
        ),
        const SizedBox(height: 12),
        _ToolRow(
          name: 'Git LFS',
          detail: '图片、模型、音频等大文件的二进制存储（仅 git 工程需要）',
          tool: 'lfs',
          url: 'https://git-lfs.com/',
          installed: _flag(model.vcsTools['gitLfsFound']),
          version: '${model.vcsTools['gitLfsVersion'] ?? ''}',
          busy: model.installingTool,
          onInstall: () => model.installVcsTool('lfs'),
        ),
        const SizedBox(height: 12),
        _ToolRow(
          name: 'Unity Version Control',
          detail: 'UVCS / Plastic 工作区，cm 命令走原生二进制存储',
          tool: 'uvcs',
          url: 'https://unity.com/products/version-control',
          installed: _flag(model.vcsTools['cmFound']),
          version: '${model.vcsTools['cmVersion'] ?? ''}',
          busy: model.installingTool,
          onInstall: () => model.installVcsTool('uvcs'),
        ),
        const SizedBox(height: 12),
        _ToolRow(
          name: 'GitHub CLI',
          detail: 'GitHub 仓库外部包与配置同步都经 gh 走，登录态取自系统钥匙串',
          tool: 'gh',
          url: 'https://cli.github.com/',
          installed: _flag(model.vcsTools['ghFound']),
          version: '${model.vcsTools['ghVersion'] ?? ''}',
          busy: model.installingTool,
          onInstall: () => model.installVcsTool('gh'),
        ),
        if (model.vcsTools.isNotEmpty) ...[
          const SizedBox(height: 10),
          Text(
            _missingOf(model.vcsTools).isEmpty
                ? '三项依赖都齐了，「恢复/同步」页的所有功能可用。'
                : '缺少 ${_missingOf(model.vcsTools).join('、')}：'
                    '接入版本托管、提交与推送会受影响，点上面的「一键安装」由 winget 装好${_flag(model.vcsTools['installerFound']) ? '' : '（本机没有 winget，会给出官网地址手动下载）'}。',
            style: TextStyle(
                fontSize: 12,
                height: 1.5,
                color: _missingOf(model.vcsTools).isEmpty
                    ? t.inkTertiary
                    : t.warn),
          ),
        ],
        const SizedBox(height: 26),
        const NeuSectionTitle('视觉效果'),
        _VisualCard(model),
        const SizedBox(height: 26),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const NeuSectionTitle('配置同步'),
            const Spacer(),
            if (model.syncing)
              const Padding(
                padding: EdgeInsets.fromLTRB(0, 6, 12, 0),
                child: SizedBox(
                  width: 15,
                  height: 15,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            NeuActionButton(
              label: '刷新',
              icon: Icons.refresh_rounded,
              onTap: model.syncing ? null : model.loadSync,
            ),
          ],
        ),
        const SizedBox(height: 10),
        NeuSwitchTile(
          label: '启用 gist 同步',
          hint: '关掉后仍可登录 GitHub（私有仓库外部包要用它），但不会上传、恢复，登录成功也不再自动首传。',
          value: model.sync['enabled'] != false,
          onChanged: model.setSyncEnabled,
        ),
        const SizedBox(height: 10),
        KeyedSubtree(key: _syncKey, child: _SyncCard(model)),
        const SizedBox(height: 26),
        const NeuSectionTitle('数据服务'),
        NeuCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _kv('侧车版本', model.hostVersion.isEmpty ? '未连接' : model.hostVersion),
              const SizedBox(height: 6),
              _kv('仓库源', '${state?.registrySources.length ?? 0} 个'),
              const SizedBox(height: 6),
              _kv('本地工程', '${state?.projectCount ?? 0} 个'),
              const SizedBox(height: 6),
              _kv('登录凭据', '以当前 Windows 用户加密保存在 %LOCALAPPDATA%\\Akashic\\sessions'),
            ],
          ),
        ),
        const SizedBox(height: 18),
        Text(model.status,
            style: TextStyle(fontSize: 12, color: t.inkTertiary)),
      ],
    );
  }

  Widget _kv(String key, String value) => Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 88,
            child: Text(key,
                style: const TextStyle(
                    fontSize: 12.5, fontWeight: FontWeight.w600)),
          ),
          Expanded(
            child: Text(value,
                style: const TextStyle(fontSize: 12.5),
                overflow: TextOverflow.ellipsis),
          ),
        ],
      );
}

class _PathRow extends StatelessWidget {
  const _PathRow({
    required this.label,
    required this.hint,
    required this.value,
    required this.onPick,
  });

  final String label;
  final String hint;
  final String value;
  final VoidCallback onPick;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label,
                    style: const TextStyle(
                        fontSize: 13.5, fontWeight: FontWeight.w600)),
                const SizedBox(height: 3),
                Text(value.isEmpty ? hint : value,
                    style: TextStyle(
                        fontSize: 11.5,
                        color: value.isEmpty ? t.inkTertiary : t.inkSecondary),
                    overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          const SizedBox(width: 12),
          NeuActionButton(
            label: '选择',
            icon: Icons.folder_open_rounded,
            onTap: onPick,
          ),
        ],
      ),
    );
  }
}

class _SessionRow extends StatelessWidget {
  const _SessionRow({
    required this.name,
    required this.site,
    required this.signedIn,
    required this.detail,
  });

  final String name;
  final String site;
  final bool signedIn;
  final String detail;

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(name,
                    style: const TextStyle(
                        fontSize: 13.5, fontWeight: FontWeight.w600)),
                const SizedBox(height: 3),
                Text(detail,
                    style: TextStyle(
                        fontSize: 11.5,
                        color: NeuTheme.of(context).inkTertiary),
                    overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          const SizedBox(width: 12),
          NeuPill(
            signedIn ? '已保存登录态' : '未登录',
            bg: signedIn
                ? const Color(0xFFBFE3C9)
                : Colors.black.withValues(alpha: 0.06),
            fg: signedIn ? const Color(0xFF237A45) : null,
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '忘记',
            icon: Icons.logout_rounded,
            onTap: () => model.updateSetting((c) => c.forgetSession(site)),
          ),
        ],
      ),
    );
  }
}

bool _flag(Object? value) => value as bool? ?? false;

List<String> _missingOf(Map<String, dynamic> tools) =>
    ((tools['missing'] as List?) ?? const []).cast<String>();

/// One dependency with what the probe found and the way to get it. The install button
/// only appears when the tool is missing: an already-installed git should not offer to
/// overwrite itself.
class _ToolRow extends StatelessWidget {
  const _ToolRow({
    required this.name,
    required this.detail,
    required this.tool,
    required this.url,
    required this.installed,
    required this.version,
    required this.busy,
    required this.onInstall,
  });

  final String name;
  final String detail;
  final String tool;
  final String url;
  final bool installed;
  final String version;
  final bool busy;
  final VoidCallback onInstall;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final model = AppScope.of(context);
    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        children: [
          Icon(
            installed ? Icons.verified_rounded : Icons.error_outline_rounded,
            size: 18,
            color: installed ? AppColors.greenFg : t.warn,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(name,
                    style: const TextStyle(
                        fontSize: 13.5, fontWeight: FontWeight.w600)),
                const SizedBox(height: 2),
                Text(detail,
                    style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
                    overflow: TextOverflow.ellipsis),
              ],
            ),
          ),
          const SizedBox(width: 10),
          NeuPill(
            installed
                ? (version.isEmpty ? '已安装' : '已装 $version')
                : '未检测到',
            bg: installed
                ? const Color(0xFFBFE3C9)
                : Colors.black.withValues(alpha: 0.06),
            fg: installed ? const Color(0xFF237A45) : null,
          ),
          const SizedBox(width: 10),
          if (installed)
            Opacity(
              opacity: busy ? 0.45 : 1,
              child: NeuActionButton(
                label: '官网',
                icon: Icons.open_in_new_rounded,
                onTap: busy ? null : () => model.openUrl(url),
              ),
            )
          else
            NeuActionButton(
              label: busy ? '安装中…' : '一键安装',
              icon: Icons.download_rounded,
              emphasised: true,
              onTap: busy ? null : onInstall,
            ),
        ],
      ),
    );
  }
}

/// 颜色模式与页面全局透明度. Both apply live: the slider drives the window sheet as the
/// thumb moves and the sidecar only stores the value once it settles.
class _VisualCard extends StatelessWidget {
  const _VisualCard(this.model);

  final AppModel model;

  /// The sheet stores an alpha; the page speaks the inverse, so right really is more see-through.
  double get _transparency => (1 - model.sheetOpacity).clamp(0.1, 0.6);

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return NeuCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text('颜色模式',
                  style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w600,
                      color: t.ink)),
              const SizedBox(width: 14),
              NeuSegmented(
                labels: const ['浅色', '暗色', '跟随系统'],
                index: model.themeChoice,
                onChanged: model.chooseTheme,
              ),
            ],
          ),
          const SizedBox(height: 10),
          _slider(
            context,
            '页面全局透明度',
            '${(_transparency * 100).round()}%',
            0.1,
            0.6,
            _transparency,
            (value) => model.setVisual(opacity: 1 - value),
            hint: '向右更透明、向左更实，可设范围 10%–60%。'
                '作用于窗口底板、侧栏托盘与弹出面板；越透明，桌面透出越多，文字对比度也越低。',
          ),
        ],
      ),
    );
  }

  Widget _slider(
    BuildContext context,
    String label,
    String valueText,
    double min,
    double max,
    double value,
    ValueChanged<double> onChanged, {
    String hint = '',
  }) {
    final t = NeuTheme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(label,
                  style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w600,
                      color: t.ink)),
            ),
            Text(valueText,
                style: TextStyle(fontSize: 12, color: t.inkSecondary)),
          ],
        ),
        Slider(
          min: min,
          max: max,
          value: value.clamp(min, max),
          activeColor: t.isDark ? const Color(0xFF7FCB9A) : const Color(0xFF2E7D46),
          inactiveColor: Colors.black.withValues(alpha: 0.16),
          onChanged: onChanged,
        ),
        if (hint.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(hint,
                style:
                    TextStyle(fontSize: 11.5, height: 1.5, color: t.inkTertiary)),
          ),
      ],
    );
  }
}

/// What would go up to the gist, where the last upload landed, and the two actions.
class _SyncCard extends StatelessWidget {
  const _SyncCard(this.model);

  final AppModel model;

  /// The sidecar writes a full ISO stamp; this row only has room for the minute.
  static String _when(Object? raw) {
    final parsed = DateTime.tryParse('$raw');
    if (parsed == null) return '$raw';
    final local = parsed.toLocal();
    String two(int v) => v.toString().padLeft(2, '0');
    return '${local.year}/${two(local.month)}/${two(local.day)} '
        '${two(local.hour)}:${two(local.minute)}';
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final sync = model.sync;
    final github = model.github;

    final login = '${github['login'] ?? ''}';
    final uploaded = '${sync['uploadedAt'] ?? ''}';
    final bytes = (sync['payloadBytes'] as num?)?.toInt() ?? 0;
    final included = ((sync['included'] as List?) ?? const []).cast<String>();
    final note = '${sync['note'] ?? ''}';
    final ready = sync['ready'] == true && sync['enabled'] != false;
    final ghFound = github['found'] == true;
    final log = model.syncLog;

    final account = github['found'] != true
        ? '未安装 GitHub CLI'
        : github['signedIn'] == true
            ? '$login${github['hasGistScope'] == true ? '' : '（令牌缺 gist 权限）'}'
            : '未登录，点下面「使用浏览器登录 GitHub」';

    return NeuCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '设置、三站登录 Cookie 与各站清单先在本地用 AES-256-GCM 加密，再写入一个私有 gist。'
            '密钥由你的 GitHub 账号 id 与程序内置盐值派生，换机登录同一个账号就能自动还原，'
            '不需要设置或记住任何同步密码；明文与密钥都不会上传。',
            style: TextStyle(
                fontSize: 12, height: 1.6, color: t.inkSecondary),
          ),
          const SizedBox(height: 12),
          _row('GitHub 账号', account),
          // The gist's address is deliberately never shown: the id alone is enough to open the
          // backup on the web, and the encrypted blob names its owner.
          _row('上次上传', uploaded.isEmpty
              ? '还没有上传过，首次上传会自动新建一个私有 gist'
              : '${_when(uploaded)} · ${bytes ~/ 1024} KB 密文'),
          _row('包含内容', included.isEmpty ? '—' : included.join(' · ')),
          if (note.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(note,
                style: TextStyle(fontSize: 12, height: 1.5, color: t.warn)),
          ],
          const SizedBox(height: 14),
          Row(
            children: [
              NeuActionButton(
                label: model.githubLoggingIn
                    ? '等待浏览器授权…'
                    : github['signedIn'] == true
                        ? '重新登录 GitHub'
                        : '使用浏览器登录 GitHub',
                icon: Icons.open_in_browser_rounded,
                emphasised: github['signedIn'] != true,
                onTap: model.githubLoggingIn || !ghFound ? null : model.githubLogin,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  model.githubLoggingIn
                      ? '浏览器里点一次 Authorize 就行，不用输验证码；回来会自动完成同步。'
                      : ghFound
                          ? '登录会打开你自己的浏览器，Akashic 收不到密码或令牌。'
                          : '需要先在 版本控制依赖 装好 GitHub CLI。',
                  style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              NeuActionButton(
                label: '上传到 GitHub',
                icon: Icons.cloud_upload_outlined,
                emphasised: true,
                onTap: !ready || model.syncing ? null : model.pushSync,
              ),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '从 gist 恢复',
                icon: Icons.cloud_download_outlined,
                onTap: !ready || model.syncing
                    ? null
                    : () => model.showModal(_SyncRestoreForm(model)),
              ),
              const Spacer(),
              if (log.isNotEmpty)
                NeuActionButton(
                  label: '查看输出',
                  icon: Icons.terminal_rounded,
                  onTap: () => model.showModal(NeuResultForm(log)),
                ),
            ],
          ),
          if (log.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text('${log['title'] ?? ''}',
                style: TextStyle(fontSize: 12, color: t.inkTertiary),
                overflow: TextOverflow.ellipsis),
          ],
        ],
      ),
    );
  }

  /// Values here are lists of names and paths, so they are allowed to take a second line
  /// rather than ellipsize away the thing the user is checking for.
  Widget _row(String key, String value) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 88,
              child: Text(key,
                  style: const TextStyle(
                      fontSize: 12.5, fontWeight: FontWeight.w600)),
            ),
            Expanded(
              child: Text(value,
                  style: const TextStyle(fontSize: 12.5, height: 1.45)),
            ),
          ],
        ),
      );
}

/// Restoring overwrites what is on this machine, so it asks first and says what it will touch.
/// It never names the backup: the id alone is enough to open the gist on the web, so the page only
/// says where it will look, and the sidecar finds it under the signed-in account.
class _SyncRestoreForm extends StatelessWidget {
  const _SyncRestoreForm(this.model);

  final AppModel model;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    return NeuModalScaffold(
      title: '从 gist 恢复配置',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            '会用解密后的云端内容覆盖本机的设置、三站登录态与清单缓存。'
            '当前 settings.json 先另存为 settings.json.before-sync。',
            style: TextStyle(
                fontSize: 12.5, height: 1.6, color: t.inkSecondary),
          ),
          const SizedBox(height: 8),
          Text('备份地址不在界面上显示，会在当前登录的 GitHub 账号下自动定位。',
              style: TextStyle(fontSize: 12, color: t.inkTertiary)),
          const SizedBox(height: 16),
          NeuDialogActions(
            confirmLabel: '覆盖并恢复',
            onConfirm: () {
              model.closeModal();
              model.pullSync();
            },
            onCancel: model.closeModal,
          ),
        ],
      ),
    );
  }
}
