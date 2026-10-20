import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/app_colors.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

class AboutPage extends StatefulWidget {
  const AboutPage({super.key});

  @override
  State<AboutPage> createState() => _AboutPageState();
}

class _AboutPageState extends State<AboutPage> {
  static const _features = [
    ('工程与包管理', '扫描本地 Unity 工程，识别 UPM / VPM 双清单，按注册表安装并递归解析依赖。'),
    ('资产导入', '直接解析 .unitypackage 的 tar.gz 归档，勾选后写入工程，并按工程记录台账可一键撤销。'),
    ('账号与资源', 'Booth 已购、Unity Asset Store 与 VRChat 内容清单集中管理，登录态加密保存在本机。'),
    ('项目模板', '自动接入 Creator Companion 的官方世界 / 形象模板，也能把调好的工程存成自己起点。'),
    ('备份与版本控制', '关键资产打包与还原，可按工程托管方式走 git 或 Unity Version Control。'),
    ('配置同步', '浏览器里点一次授权登录 GitHub，设置与登录态端到端加密写入私有 gist，换机登录即还原。'),
    ('界面与视觉', '新拟态外壳：亮暗三档、可定制图标栏，一个不透明度滑条同时驱动底板、托盘与弹窗。'),
  ];

  static const _stack = [
    '前端 · Flutter (Windows desktop, 无边框玻璃画布)',
    '业务 · .NET 8 类库 Akashic.Core，由本机侧车 Akashic.Host 以 JSON 提供',
    '登录 · WebView2 会话 Cookie，DPAPI 当前用户作用域加密落盘',
    '同步 · PBKDF2 派生密钥 + AES-256-GCM 信封，明文与密钥都不上传',
  ];

  Map<String, dynamic> _progress = const {};
  bool _checking = false;
  bool _downloading = false;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _check());
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _check({bool force = false}) async {
    final model = AppScope.of(context);
    if (model.host == null || _checking) return;

    setState(() => _checking = true);
    try {
      await model.checkUpdate(force: force);
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  Future<void> _download() async {
    final host = AppScope.of(context).host;
    if (host == null || _downloading) return;

    setState(() => _downloading = true);
    _poll?.cancel();
    _poll = Timer.periodic(const Duration(milliseconds: 900), (_) async {
      final state = await host.updateState();
      if (mounted) setState(() => _progress = state);
    });

    try {
      final state = await host.updateDownload();
      if (mounted) setState(() => _progress = state);
    } catch (error) {
      if (mounted) {
        setState(() => _progress = {'phase': 'error', 'message': '下载失败：$error'});
      }
    } finally {
      _poll?.cancel();
      _poll = null;
      if (mounted) setState(() => _downloading = false);
    }
  }

  Future<void> _apply() async {
    final host = AppScope.of(context).host;
    if (host == null) return;

    final started = await host.updateApply(pid);
    if (started) exit(0);
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    final plus = model.hostVersion.indexOf('+');
    final version = plus < 0
        ? model.hostVersion
        : '${model.hostVersion.substring(0, plus)}'
            '+${model.hostVersion.substring(plus + 1, plus + 8)}';

    return NeuPageBody(
      title: 'Akashic',
      subtitle: 'VRChat & Unity 包管理套件',
      fixed: [
        NeuCard(
          child: Row(
            children: [
              Container(
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: AppColors.rail,
                  borderRadius: BorderRadius.circular(14),
                  boxShadow: neuRaised(depth: 5, dark: t.shadowDark),
                ),
                clipBehavior: Clip.antiAlias,
                child: Image.asset('assets/brand/akashic-light.png',
                    fit: BoxFit.cover),
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('版本 ${version.isEmpty ? '未连接' : version}',
                        style: const TextStyle(
                            fontSize: 13.5, fontWeight: FontWeight.w600)),
                    const SizedBox(height: 4),
                    Text('数据服务 ${model.state == null ? '未连接' : '已连接'} · '
                        '${model.state?.projectCount ?? 0} 个本地工程',
                        style: TextStyle(
                            fontSize: 12, color: t.inkSecondary)),
                  ],
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        _updateCard(t),
        const SizedBox(height: 22),
        const NeuSectionTitle('核心特性'),
        for (final feature in _features) ...[
          NeuCard(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 15),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(feature.$1,
                    style: const TextStyle(
                        fontSize: 13.5, fontWeight: FontWeight.w600)),
                const SizedBox(height: 5),
                Text(feature.$2,
                    style: TextStyle(
                        fontSize: 12, color: t.inkSecondary, height: 1.5)),
              ],
            ),
          ),
          const SizedBox(height: 12),
        ],
        const SizedBox(height: 14),
        const NeuSectionTitle('技术栈'),
        NeuCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final line in _stack)
                Padding(
                  padding: const EdgeInsets.only(bottom: 7),
                  child: Text(line,
                      style: TextStyle(fontSize: 12.5, color: t.inkSecondary)),
                ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _updateCard(NeuTokens t) {
    final info = AppScope.of(context).updateInfo;
    final latest = '${info?['latest'] ?? ''}';
    final error = '${info?['error'] ?? ''}';
    final hasUpdate = info?['hasUpdate'] == true;
    final phase = '${_progress['phase'] ?? ''}';
    final ready = phase == 'ready';

    final assetBytes = (info?['assetBytes'] as num?)?.toInt() ?? 0;
    final received = (_progress['received'] as num?)?.toInt() ?? 0;
    final total = (_progress['total'] as num?)?.toInt() ?? assetBytes;
    final percent =
        ((_progress['percent'] as num?)?.toDouble() ?? 0).clamp(0, 100).toDouble();

    final status = switch (phase) {
      'downloading' => total > 0
          ? '已下载 ${formatBytes(received)} / ${formatBytes(total)} · ${percent.toStringAsFixed(0)}%'
          : '正在下载…',
      'verifying' => '正在校验签名…',
      'extracting' => '正在解压安装包…',
      'ready' => '${_progress['message'] ?? '已准备好'}',
      'error' => '${_progress['message'] ?? '更新失败'}',
      _ => '',
    };

    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 15),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.system_update_alt_rounded, size: 18),
              const SizedBox(width: 8),
              const Text('软件更新',
                  style:
                      TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600)),
              const SizedBox(width: 10),
              if (info != null && error.isEmpty)
                NeuPill(
                    hasUpdate
                        ? '发现新版本 $latest'
                        : '已是最新${latest.isEmpty ? '' : '（$latest）'}',
                    bg: hasUpdate
                        ? const Color(0xFFBFE3C9)
                        : Colors.black.withValues(alpha: 0.06),
                    fg: hasUpdate ? const Color(0xFF237A45) : null),
              if (info != null && '${info['route']}'.isNotEmpty) ...[
                const SizedBox(width: 8),
                NeuPill('网络：${info['route']}'),
              ],
              if (hasUpdate && assetBytes > 0) ...[
                const SizedBox(width: 8),
                NeuPill('更新包 ${formatBytes(assetBytes)}'),
              ],
              const Spacer(),
              NeuActionButton(
                label: _checking ? '检查中…' : '检查更新',
                icon: Icons.refresh_rounded,
                onTap: _checking || _downloading ? null : () => _check(force: true),
              ),
            ],
          ),
          const SizedBox(height: 10),
          if (info == null)
            Text(_checking ? '正在检查更新…' : '还没有检查更新。',
                style: TextStyle(fontSize: 12, color: t.inkSecondary))
          else if (error.isNotEmpty)
            Text(error, style: TextStyle(fontSize: 12, color: t.warn))
          else ...[
            if (hasUpdate && '${info['notes']}'.trim().isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text('${info['notes']}'.trim(),
                    maxLines: 6,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 12, height: 1.5, color: t.inkSecondary)),
              ),
            if (phase == 'downloading' || phase == 'extracting' || phase == 'verifying')
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: _progressBar(t, percent),
              ),
            if (status.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text(status,
                    style: TextStyle(
                        fontSize: 12,
                        color: phase == 'error' ? t.warn : t.inkSecondary)),
              ),
            Row(
              children: [
                if (hasUpdate && '${info['pageUrl']}'.isNotEmpty)
                  NeuActionButton(
                    label: '打开发布页',
                    icon: Icons.open_in_new_rounded,
                    onTap: () => AppScope.of(context).openUrl('${info['pageUrl']}'),
                  ),
                const SizedBox(width: 10),
                if (hasUpdate && !ready)
                  NeuActionButton(
                    label: _downloading ? '下载中…' : '下载更新',
                    icon: Icons.download_rounded,
                    emphasised: true,
                    onTap: _downloading ? null : _download,
                  ),
                if (ready)
                  NeuActionButton(
                    label: '重启并安装',
                    icon: Icons.restart_alt_rounded,
                    emphasised: true,
                    onTap: _apply,
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  Widget _progressBar(NeuTokens t, double percent) {
    final value = (percent / 100).clamp(0.0, 1.0);

    return NeuBox(
      mode: NeuMode.inset,
      radius: 6,
      depth: 3,
      color: Colors.black.withValues(alpha: 0.08),
      padding: EdgeInsets.zero,
      child: SizedBox(
        height: 8,
        child: FractionallySizedBox(
          alignment: Alignment.centerLeft,
          widthFactor: value,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: const Color(0xFF3E9E63),
              borderRadius: BorderRadius.circular(6),
            ),
          ),
        ),
      ),
    );
  }
}
