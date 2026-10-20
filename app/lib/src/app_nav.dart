import 'package:flutter/material.dart';

class NavEntry {
  const NavEntry({
    required this.id,
    required this.label,
    required this.icon,
    this.section,
  });

  final String id;
  final String label;
  final IconData icon;

  final String? section;
}

class AppNav {
  AppNav._();

  static const entries = <NavEntry>[
    NavEntry(
      id: 'projects',
      label: '项目',
      icon: Icons.account_tree_rounded,
      section: '工作区',
    ),
    NavEntry(
      id: 'restore',
      label: '恢复/同步',
      icon: Icons.history_rounded,
    ),
    NavEntry(id: 'resources', label: '资源', icon: Icons.inventory_2_rounded),
    NavEntry(
        id: 'packages', label: '包浏览器', icon: Icons.storefront_rounded),
    NavEntry(
        id: 'templates', label: '项目模板', icon: Icons.view_in_ar_rounded),
    NavEntry(
      id: 'import',
      label: '导入 .unitypackage',
      icon: Icons.publish_rounded,
      section: '工具',
    ),
    NavEntry(id: 'booth', label: 'Booth', icon: Icons.shopping_bag_rounded),
    NavEntry(
        id: 'assetstore', label: 'Asset Store 导入',
        icon: Icons.collections_rounded),
    NavEntry(
        id: 'vrchatsync',
        label: 'VRChat 数据同步',
        icon: Icons.cloud_sync_rounded),
    NavEntry(
        id: 'settings', label: '设置', icon: Icons.settings_rounded,
        section: '其他'),
    NavEntry(id: 'about', label: '关于', icon: Icons.info_outline_rounded),
  ];

  static const railCount = 9;

  static List<String> get defaultRail => [
        for (var i = 0; i < railCount; i++) 'nav:${entries[i].id}',
      ];

  static String navTile(String id) => 'nav:$id';

  static String projectTile(String path) => 'proj:$path';

  static bool isProjectTile(String raw) => raw.startsWith('proj:');

  static String tileValue(String raw) =>
      raw.startsWith('proj:') ? raw.substring(5) : raw.substring(4);

  static int settingsIndex =
      entries.indexWhere((e) => e.id == 'settings');

  static int indexOf(String id) =>
      entries.indexWhere((e) => e.id == id);
}
