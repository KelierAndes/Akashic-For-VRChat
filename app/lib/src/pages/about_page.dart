import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/app_colors.dart';
import '../ui/neu_kit.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

/// Static product information. The version line is read from the sidecar, so it
/// can no longer drift away from what is actually running.
class AboutPage extends StatelessWidget {
  const AboutPage({super.key});

  static const _features = [
    ('工程与包管理', '扫描本地 Unity 工程，识别 UPM / VPM 双清单，按注册表安装并递归解析依赖。'),
    ('资产导入', '直接解析 .unitypackage 的 tar.gz 归档，勾选后写入工程，并按工程记录台账可一键撤销。'),
    ('账号与资源', 'Booth 已购、Unity Asset Store 与 VRChat 内容清单集中管理，登录态加密保存在本机。'),
    ('项目模板', '自动接入 Creator Companion 的官方世界 / 形象模板，也能把调好的工程存成自己的起点。'),
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

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    // The sidecar reports "2.0.0+<full sha>"; the commit is useful, all 40 of it is not.
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
              // The same treatment as the rail's mark: the light artwork on the
              // black base, so the two never drift apart by theme.
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
}
