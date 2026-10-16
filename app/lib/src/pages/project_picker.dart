import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';

/// A project picker used by every action that installs something somewhere.
class ProjectPickerForm extends StatefulWidget {
  const ProjectPickerForm({super.key, required this.title, required this.onPick});

  final String title;
  final void Function(String projectPath) onPick;

  @override
  State<ProjectPickerForm> createState() => _ProjectPickerFormState();
}

class _ProjectPickerFormState extends State<ProjectPickerForm> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    if (model.projects.isEmpty) {
      return NeuModalScaffold(
        title: widget.title,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const NeuEmptyState(height: 140, text: '还没有登记任何 Unity 工程。'),
            const SizedBox(height: 12),
            NeuDialogActions(onCancel: model.closeModal, onConfirm: model.closeModal,
                confirmLabel: '知道了'),
          ],
        ),
      );
    }

    final index = _index.clamp(0, model.projects.length - 1);
    final chosen = model.projects[index];

    return NeuModalScaffold(
      title: widget.title,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: '目标工程',
            hint: '${chosen.unityVersion} · ${chosen.installedPackages.length} 个已安装软件包',
            child: NeuChoiceField(
              values: [for (final p in model.projects) '${p.name} · ${p.path}'],
              index: index,
              icon: Icons.account_tree_rounded,
              onChanged: (i) => setState(() => _index = i),
            ),
          ),
          const SizedBox(height: 6),
          NeuDialogActions(
            onCancel: model.closeModal,
            onConfirm: () {
              widget.onPick(chosen.path);
              model.closeModal();
            },
          ),
        ],
      ),
    );
  }
}
