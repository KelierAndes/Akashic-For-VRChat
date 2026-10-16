import 'package:flutter/material.dart';

import '../app_nav.dart';
import '../ui/neu_kit.dart';

/// Placeholder body until each destination is ported page by page. It keeps
/// the real page chrome — title, subtitle, cards — so the layout is already
/// reviewed against the framework.
class StubPage extends StatelessWidget {
  const StubPage({super.key, required this.entry});

  final NavEntry entry;

  static const _notes = <String, String>{
    'projects': '项目卡片列表、分类与排序筛选、云端链接异步匹配',
    'resources': '存储库 / 用户软件包 / 外部包三段列表与批量操作',
    'packages': 'VPM 注册表检索、版本浏览与递归依赖安装',
    'import': '.unitypackage 解包预览与资产勾选写入',
    'booth': 'Booth 已购 / 礼物 / 免费商品清单与下载入库',
    'assetstore': 'Unity 我的资产读取、缓存扫描与入库',
    'vrchatsync': 'VRChat 内容与蓝图清单、名称描述回写',
    'templates': 'VCC 内置模板与自建模板管理',
    'settings': '仓库源、路径、备份策略与三站登录态',
    'about': '版本信息与致谢',
  };

  @override
  Widget build(BuildContext context) {
    return NeuPageBody(
      title: entry.label,
      subtitle: '此页面尚未接入 Host 侧车',
      fixed: [
        NeuCard(
          child: Row(
            children: [
              Icon(entry.icon, size: 20, color: Colors.black.withValues(alpha: 0.5)),
              const SizedBox(width: 14),
              Expanded(
                child: Text(
                  _notes[entry.id] ?? '待规划',
                  style: const TextStyle(
                      fontSize: 13, fontWeight: FontWeight.w500),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const NeuEmptyState(
          text: 'Flutter 外壳已就绪。\n接下来逐页接上 Akashic.Host 的 JSON 接口。',
        ),
      ],
    );
  }
}
