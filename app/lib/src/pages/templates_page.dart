import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';

/// Template list plus the two forms that create or capture one. The new-project
/// form picks the Unity version first, because the sidecar already knows which
/// templates that version can actually load.
class TemplatesPage extends StatefulWidget {
  const TemplatesPage({super.key});

  @override
  State<TemplatesPage> createState() => _TemplatesPageState();
}

class _TemplatesPageState extends State<TemplatesPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
        (_) => AppScope.of(context).loadTemplates());
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return NeuPageBody(
      title: '项目模板',
      subtitle: model.templates.isEmpty && model.templatesLoading
          ? '正在读取模板…'
          : model.templateSummary,
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          NeuActionButton(
            label: '保存为模板',
            icon: Icons.save_outlined,
            onTap: () => model.showModal(const SaveTemplateForm()),
          ),
          const SizedBox(width: 10),
          NeuActionButton(
            label: '新建项目',
            icon: Icons.add_rounded,
            emphasised: true,
            onTap: () async {
              await model.loadTemplates();
              model.showModal(const NewProjectForm());
            },
          ),
        ],
      ),
      fixed: [
        if (model.templates.isEmpty && !model.templatesLoading)
          const NeuEmptyState(
            text: '没有可用模板。\n官方模板随 VRChat Creator Companion 安装，也可以把现有工程保存为模板。',
          )
        else
          Column(
            children: [
              for (final template in model.templates)
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: NeuCard(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 18, vertical: 16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(template.name,
                                  style: const TextStyle(
                                      fontSize: 14,
                                      fontWeight: FontWeight.w600),
                                  overflow: TextOverflow.ellipsis),
                            ),
                            NeuPill(template.kindLabel),
                            const SizedBox(width: 8),
                            NeuPill(
                              template.isAvailable ? '可用' : '缺少目录',
                              bg: template.isAvailable
                                  ? const Color(0xFFBFE3C9)
                                  : Colors.black.withValues(alpha: 0.06),
                              fg: template.isAvailable
                                  ? const Color(0xFF237A45)
                                  : null,
                            ),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Wrap(
                          spacing: 8,
                          runSpacing: 6,
                          children: [
                            NeuPill(template.unityVersion),
                            NeuPill(template.kind),
                            NeuPill(template.sourceDisplay),
                            NeuPill(template.createdAtDisplay),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Text(template.notesDisplay,
                            style: TextStyle(
                                fontSize: 12, color: t.inkSecondary),
                            overflow: TextOverflow.ellipsis),
                        const SizedBox(height: 12),
                        Row(
                          children: [
                            if (template.directory.isNotEmpty)
                              NeuActionButton(
                                label: '打开目录',
                                icon: Icons.folder_open_rounded,
                                onTap: () =>
                                    model.openFolder(template.directory),
                              ),
                            if (template.canDelete) ...[
                              const SizedBox(width: 10),
                              NeuActionButton(
                                label: '删除',
                                icon: Icons.delete_outline_rounded,
                                onTap: () => model.deleteTemplate(template),
                              ),
                            ],
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
      ],
    );
  }
}

class NewProjectForm extends StatefulWidget {
  const NewProjectForm({super.key});

  @override
  State<NewProjectForm> createState() => _NewProjectFormState();
}

class _NewProjectFormState extends State<NewProjectForm> {
  final _name = TextEditingController(text: 'New Project');
  final _parent = TextEditingController();
  int _version = 0;
  int _template = 0;
  bool _seeded = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_seeded) return;

    final creation = AppScope.of(context).creation;
    if (creation == null || creation.versions.isEmpty) return;

    _seeded = true;
    _version = creation.defaultIndex.clamp(0, creation.versions.length - 1);
    _parent.text = creation.defaultProjectPath;
  }

  @override
  void dispose() {
    _name.dispose();
    _parent.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final creation = model.creation;

    if (creation == null || creation.versions.isEmpty) {
      return const NeuModalScaffold(
        title: '新建项目',
        child: NeuEmptyState(
            height: 140, text: '没有检测到可用的 Unity 版本，请先在设置页添加编辑器。'),
      );
    }

    final versions = creation.versions;
    final safeVersion = _version.clamp(0, versions.length - 1);
    final available = versions[safeVersion].templates;

    return NeuModalScaffold(
      title: '新建项目',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: 'Unity 版本',
            hint: available.isEmpty
                ? '${versions[safeVersion].version} 没有可用的模板，将创建空白项目。'
                : '模板按 Unity 主版本匹配，${versions[safeVersion].version} 可用 ${available.length} 个',
            child: NeuChoiceField(
              values: [for (final v in versions) v.version],
              index: safeVersion,
              icon: Icons.straighten_rounded,
              onChanged: (i) => setState(() {
                _version = i;
                _template = 0;
              }),
            ),
          ),
          NeuField(
            label: '模板',
            child: NeuChoiceField(
              values: [
                '空白项目（不使用模板）',
                ...available.map((t) => '${t.name} · ${t.kind}'),
              ],
              index: _template,
              icon: Icons.dashboard_customize_outlined,
              onChanged: (i) => setState(() => _template = i),
            ),
          ),
          NeuField(
            label: '项目名称',
            child: NeuTextInput(controller: _name, hint: '将创建的文件夹名'),
          ),
          NeuField(
            label: '父目录',
            child: Row(
              children: [
                Expanded(
                  child: NeuTextInput(controller: _parent, hint: '项目落盘位置'),
                ),
                const SizedBox(width: 10),
                NeuActionButton(
                  label: '选择',
                  icon: Icons.folder_open_rounded,
                  onTap: () async {
                    final picked = await FilePicker.getDirectoryPath(
                        dialogTitle: '选择父目录');
                    if (picked != null) setState(() => _parent.text = picked);
                  },
                ),
              ],
            ),
          ),
          const SizedBox(height: 6),
          NeuDialogActions(
            onCancel: model.closeModal,
            onConfirm: () {
              if (_name.text.trim().isEmpty) {
                model.setStatus('请先填写项目名称。');
                return;
              }

              final chosen =
                  _template > 0 && _template - 1 < available.length
                      ? available[_template - 1]
                      : null;
              model.createProject(
                name: _name.text.trim(),
                parent: _parent.text.trim(),
                unityVersion: versions[safeVersion].version,
                templateDirectory: chosen?.directory,
              );
              model.closeModal();
            },
          ),
        ],
      ),
    );
  }
}

class SaveTemplateForm extends StatefulWidget {
  const SaveTemplateForm({super.key});

  @override
  State<SaveTemplateForm> createState() => _SaveTemplateFormState();
}

class _SaveTemplateFormState extends State<SaveTemplateForm> {
  final _name = TextEditingController();
  final _notes = TextEditingController();
  int _project = 0;

  @override
  void dispose() {
    _name.dispose();
    _notes.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    if (model.projects.isEmpty) {
      return const NeuModalScaffold(
        title: '把工程保存为模板',
        child: NeuEmptyState(height: 140, text: '还没有登记任何工程。'),
      );
    }

    final index = _project.clamp(0, model.projects.length - 1);

    return NeuModalScaffold(
      title: '把工程保存为模板',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: '来源工程',
            child: NeuChoiceField(
              values: [
                for (final project in model.projects)
                  '${project.name} · ${project.unityVersion}'
              ],
              index: index,
              icon: Icons.account_tree_rounded,
              onChanged: (i) => setState(() => _project = i),
            ),
          ),
          NeuField(
            label: '模板名称',
            child: NeuTextInput(controller: _name, hint: '例如 我的世界起手式'),
          ),
          NeuField(
            label: '说明',
            child: NeuTextInput(
                controller: _notes, hint: '可选，列表里会显示这一行', maxLines: 3),
          ),
          const SizedBox(height: 6),
          NeuDialogActions(
            onCancel: model.closeModal,
            onConfirm: () {
              if (_name.text.trim().isEmpty) {
                model.setStatus('请先填写模板名称。');
                return;
              }

              model.saveTemplate(model.projects[index].path,
                  _name.text.trim(), _notes.text.trim());
              model.closeModal();
            },
          ),
        ],
      ),
    );
  }
}
