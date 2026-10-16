import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import '../state/app_model.dart';
import '../ui/app_colors.dart';
import '../ui/neu_dialog.dart';
import '../ui/neu_kit.dart';
import '../ui/theme.dart';

/// Getting a project back and keeping it in sync: the archives in the backup folder,
/// and the hosting tools installed on the machine.
class RestorePage extends StatefulWidget {
  const RestorePage({super.key, this.initialTab = 0});

  final int initialTab;

  static const tabs = ['备份还原', '版本托管'];

  @override
  State<RestorePage> createState() => _RestorePageState();
}

class _RestorePageState extends State<RestorePage> {
  late int _tab = widget.initialTab;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final model = AppScope.of(context);
      model.loadBackups();
      model.loadVcs();
    });
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);

    return NeuPageBody(
      title: '恢复/同步',
      subtitle: _tab == 0 ? model.backupSummary : model.vcsSummary,
      fixed: [
        NeuTabs(
          tabs: RestorePage.tabs,
          index: _tab,
          onChanged: (i) => setState(() => _tab = i),
        ),
      ],
      content: switch (_tab) {
        0 => const _RestoreTab(),
        _ => const _VcsTab(),
      },
    );
  }
}

// ---------- 1 · 备份还原 ----------

class _RestoreTab extends StatelessWidget {
  const _RestoreTab();

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(model.backupDirectory,
                  style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
                  overflow: TextOverflow.ellipsis),
            ),
            if (model.restoring)
              const Padding(
                padding: EdgeInsets.only(right: 12),
                child: SizedBox(
                  width: 15,
                  height: 15,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            NeuActionButton(
              label: '打开备份目录',
              icon: Icons.folder_open_rounded,
              onTap: () => model.openFolder(model.backupDirectory),
            ),
            const SizedBox(width: 10),
            NeuActionButton(
              label: '重新扫描',
              icon: Icons.refresh_rounded,
              onTap: model.loadBackups,
            ),
          ],
        ),
        const SizedBox(height: 14),
        if (model.restoreMessage.isNotEmpty) ...[
          Text(
            model.restoreMessage,
            style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w500,
                color: model.restoring ? t.inkSecondary : AppColors.greenFg),
          ),
          const SizedBox(height: 12),
        ],
        Expanded(
          child: model.backupsLoading
              ? const NeuEmptyState(expand: true, text: '正在读取备份清单…')
              : model.backups.isEmpty
                  ? NeuEmptyState(
                      expand: true,
                      text:
                          '这个目录下还没有备份。\n在「项目」页里对项目点「备份」，或把 Unity Creator Companion\n的备份目录指到此处（设置页可以改）。',
                    )
                  : ListView.separated(
                      padding: EdgeInsets.zero,
                      itemCount: model.backups.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 12),
                      itemBuilder: (context, i) =>
                          _BackupCard(model.backups[i]),
                    ),
        ),
      ],
    );
  }
}

class _BackupCard extends StatelessWidget {
  const _BackupCard(this.archive);

  final Map<String, dynamic> archive;

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    final fileName = archive['fileName'] as String? ?? '';
    final readable = archive['readable'] as bool? ?? false;
    final match = archive['projectMatch'] as String?;
    final matchPath = archive['projectPath'] as String?;
    final exists = archive['targetExists'] as bool? ?? false;

    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(fileName,
                    style: const TextStyle(
                        fontSize: 14, fontWeight: FontWeight.w600),
                    overflow: TextOverflow.ellipsis),
              ),
              NeuPill(archive['source'] as String? ?? '其他'),
              const SizedBox(width: 8),
              NeuPill(
                readable ? '清单可读' : '读不出清单',
                bg: readable
                    ? const Color(0xFFBFE3C9)
                    : Colors.black.withValues(alpha: 0.06),
                fg: readable ? const Color(0xFF237A45) : null,
              ),
            ],
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              NeuPill(archive['createdAtDisplay'] as String? ?? ''),
              NeuPill(archive['sizeDisplay'] as String? ?? ''),
              NeuPill(archive['contentDisplay'] as String? ?? ''),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            match == null
                ? '没有和已登记工程对上名字，还原时可以自己指定目录。'
                : exists
                    ? '对应工程：$match'
                    : '对应工程：$match（目录已经不在了，可以还原到新位置）',
            style: TextStyle(fontSize: 12, color: t.inkSecondary),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              NeuActionButton(
                label: '还原…',
                icon: Icons.restore_rounded,
                emphasised: exists != true,
                onTap: () => model.showModal(_RestoreForm(archive)),
              ),
              const SizedBox(width: 10),
              NeuActionButton(
                label: '打开所在目录',
                icon: Icons.folder_open_rounded,
                onTap: () {
                  final path = archive['filePath'] as String? ?? '';
                  if (path.isNotEmpty) {
                    model.openFolder(File(path).parent.path);
                  }
                },
              ),
              if (matchPath != null && exists) ...[
                const SizedBox(width: 10),
                NeuActionButton(
                  label: '打开工程',
                  icon: Icons.account_tree_rounded,
                  onTap: () {
                    model.openProject(matchPath);
                    model.jumpTo('projects');
                  },
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _RestoreForm extends StatefulWidget {
  const _RestoreForm(this.archive);

  final Map<String, dynamic> archive;

  @override
  State<_RestoreForm> createState() => _RestoreFormState();
}

class _RestoreFormState extends State<_RestoreForm> {
  int _target = 0;
  late final TextEditingController _directory;
  bool _overwrite = false;

  @override
  void initState() {
    super.initState();
    final model = AppScope.of(context);

    // Only a backup that names a registered project may pre-fill that project:
    // otherwise the form has to open on an empty 新目录, so no click can land on
    // somebody's real project by accident.
    final matchPath = widget.archive['projectPath'] as String?;
    final index = matchPath == null
        ? -1
        : model.projects.indexWhere((p) => p.path == matchPath);

    _target = index >= 0 ? index + 1 : 0;
    _directory = TextEditingController(text: index >= 0 ? matchPath : '');
  }

  @override
  void dispose() {
    _directory.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    final choices = <String>[
      '新目录（自己指定）',
      for (final project in model.projects)
        '${project.name} · ${project.unityVersion}',
    ];
    final index = _target.clamp(0, choices.length - 1);

    return NeuModalScaffold(
      title: '还原备份',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.archive['fileName'] as String? ?? '',
              style: const TextStyle(
                  fontSize: 13.5, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          Text(
              '${widget.archive['createdAtDisplay']} · ${widget.archive['contentDisplay']}',
              style: TextStyle(fontSize: 12, color: t.inkSecondary)),
          const SizedBox(height: 14),
          NeuField(
            label: '还原到哪里',
            child: NeuChoiceField(
              values: choices,
              index: index,
              icon: Icons.folder_rounded,
              onChanged: (i) => setState(() {
                _target = i;
                _directory.text = i > 0 && i - 1 < model.projects.length
                    ? model.projects[i - 1].path
                    : '';
              }),
            ),
          ),
          NeuField(
            label: '目标目录',
            hint: '只会写入 Assets / Packages / ProjectSettings，其他内容一律不删。',
            child: Row(
              children: [
                Expanded(
                  child: NeuTextInput(
                      controller: _directory, hint: r'E:\VRChat\我的工程'),
                ),
                const SizedBox(width: 10),
                NeuActionButton(
                  label: '选择',
                  icon: Icons.folder_open_rounded,
                  onTap: () async {
                    final picked = await FilePicker.getDirectoryPath(
                        dialogTitle: '选择还原目标目录');
                    if (picked != null) setState(() => _directory.text = picked);
                  },
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          NeuSwitchTile(
            label: '覆盖同名文件',
            hint: _overwrite
                ? '工程里已有的同名文件会被备份里的版本替换'
                : '默认只补齐缺失的文件，同名文件保持原样',
            value: _overwrite,
            onChanged: (value) => setState(() => _overwrite = value),
          ),
          const SizedBox(height: 10),
          if (_overwrite)
            Text('覆盖会改动现有工程，建议先对项目做一次新的备份。',
                style: TextStyle(fontSize: 11.5, color: t.warn)),
          const SizedBox(height: 10),
          NeuDialogActions(
            onCancel: model.closeModal,
            onConfirm: () {
              final target = _directory.text.trim();
              if (target.isEmpty) {
                model.setStatus('请先指定还原到哪个目录。');
                return;
              }

              model.closeModal();
              model.restoreBackup(
                filePath: widget.archive['filePath'] as String? ?? '',
                targetPath: target,
                overwrite: _overwrite,
              );
            },
          ),
        ],
      ),
    );
  }
}

// ---------- 2 · 版本托管 ----------

class _VcsTab extends StatelessWidget {
  const _VcsTab();

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                model.vcsTools.isEmpty
                    ? '正在检测 git 与 Unity Version Control…'
                    : '${model.vcsTools['summary'] ?? ''}',
                style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            NeuActionButton(
              label: '重新检测',
              icon: Icons.refresh_rounded,
              onTap: model.loadVcs,
            ),
          ],
        ),
        if (_missingOf(model.vcsTools).isNotEmpty) ...[
          const SizedBox(height: 12),
          Row(
            children: [
              Icon(Icons.warning_amber_rounded, size: 16, color: t.warn),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  '缺少 ${_missingOf(model.vcsTools).join('、')}，接入与提交会受影响。',
                  style: TextStyle(fontSize: 12, color: t.warn),
                ),
              ),
              NeuActionButton(
                label: '去设置安装',
                icon: Icons.settings_rounded,
                onTap: () => model.jumpTo('settings'),
              ),
            ],
          ),
        ],
        if (model.vcsLog.isNotEmpty) ...[
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: Text(
                  '上一次：${model.vcsLog['title'] ?? ''}',
                  style: TextStyle(fontSize: 12, color: t.inkSecondary),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              NeuActionButton(
                label: '查看输出',
                icon: Icons.terminal_rounded,
                onTap: () =>
                    model.showModal(NeuResultForm(model.vcsLog)),
              ),
            ],
          ),
        ],
        const SizedBox(height: 14),
        Expanded(
          child: model.vcsLoading
              ? const NeuEmptyState(expand: true, text: '正在读取每个工程的托管状态…')
              : model.vcsStates.isEmpty
                  ? const NeuEmptyState(
                      expand: true,
                      text: '没有已登记的工程可以检查。\n先在「项目」页登记工程，再回到这里接入版本托管。',
                    )
                  : ListView.separated(
                      padding: EdgeInsets.zero,
                      itemCount: model.vcsStates.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 12),
                      itemBuilder: (context, i) =>
                          _VcsCard(model.vcsStates[i], model),
                    ),
        ),
      ],
    );
  }
}

List<String> _missingOf(Map<String, dynamic> tools) =>
    ((tools['missing'] as List?) ?? const []).cast<String>();

/// A hosting operation ends with the tool's own words in a dialog: a one-line
/// status is not enough to act on when git or cm refuses something.
Future<void> runVcsAndShow(AppModel model, String action, String path,
    {String? kind, String? message, String? url, bool push = false}) async {
  await model.runVcs(action, path,
      kind: kind, message: message, url: url, push: push);
  if (model.vcsLog.isNotEmpty) model.showModal(NeuResultForm(model.vcsLog));
}

class _VcsCard extends StatelessWidget {
  const _VcsCard(this.state, this.model);

  final Map<String, dynamic> state;
  final AppModel model;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);

    final path = state['projectPath'] as String? ?? '';
    final kind = state['kind'] as String? ?? '';
    final managed = kind.isNotEmpty;
    // LFS is a git-only concern: a UVCS workspace already stores its binaries
    // natively, so offering the button there only invites a needless repo change.
    final isGit = kind == 'git' || kind == 'both';
    final busy = model.vcsBusyPath == path;

    final changed = (state['changedCount'] as num?)?.toInt() ?? -1;
    final lfs = state['lfsEnabled'] as bool? ?? false;
    final ahead = (state['aheadCount'] as num?)?.toInt() ?? -1;
    final behind = (state['behindCount'] as num?)?.toInt() ?? -1;
    final commits = ((state['commits'] as List?) ?? const [])
        .cast<Map<String, dynamic>>();
    final note = state['note'] as String? ?? '';
    final latest = commits.isEmpty ? null : commits.first;

    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(state['projectName'] as String? ?? '',
                    style: const TextStyle(
                        fontSize: 14, fontWeight: FontWeight.w600),
                    overflow: TextOverflow.ellipsis),
              ),
              NeuPill(
                state['kindDisplay'] as String? ?? '未托管',
                bg: managed ? const Color(0xFFBFE3C9) : Colors.black.withValues(alpha: 0.06),
                fg: managed ? const Color(0xFF237A45) : null,
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(path,
              style: TextStyle(fontSize: 11.5, color: t.inkTertiary),
              overflow: TextOverflow.ellipsis),
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              if ((state['branch'] as String? ?? '').isNotEmpty)
                NeuPill('分支 ${state['branch']}'),
              if ((state['provider'] as String? ?? '').isNotEmpty && managed)
                NeuPill(state['provider'] as String),
              if (lfs)
                const NeuPill('LFS',
                    bg: Color(0xFFBFE3C9), fg: Color(0xFF237A45)),
              if (changed >= 0) NeuPill('$changed 项改动'),
              if (ahead > 0) NeuPill('待推送 $ahead'),
              if (behind > 0) NeuPill('待拉取 $behind'),
              if (latest != null)
                NeuPill('${latest['shortId']} ${latest['message']}'),
            ],
          ),
          if ((state['remote'] as String? ?? '').isNotEmpty) ...[
            const SizedBox(height: 8),
            // A UVCS workspace has no git remote: that string is the repository the
            // workspace pulls from, and UVCS itself owns it.
            Text('${isGit ? '远端' : '仓库'}：${state['remote']}',
                style: TextStyle(fontSize: 12, color: t.inkSecondary),
                overflow: TextOverflow.ellipsis),
          ],
          if (note.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(note,
                style: TextStyle(fontSize: 11.5, color: t.warn),
                overflow: TextOverflow.ellipsis),
          ],
          const SizedBox(height: 12),
          busy
              ? Row(
                  children: [
                    const SizedBox(
                        width: 15,
                        height: 15,
                        child: CircularProgressIndicator(strokeWidth: 2)),
                    const SizedBox(width: 10),
                    Text('正在执行，大工程的第一次提交可能要几分钟…',
                        style: TextStyle(fontSize: 12, color: t.inkSecondary)),
                  ],
                )
              : SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  padding: const EdgeInsets.only(bottom: 2),
                  child: Row(
                    children: [
                      if (!managed)
                        NeuActionButton(
                          label: '接入版本托管',
                          icon: Icons.bolt_rounded,
                          emphasised: true,
                          onTap: () => model.showModal(_InitForm(path,
                              model.vcsTools['gitFound'] as bool? ?? false,
                              model.vcsTools['cmFound'] as bool? ?? false,
                              model.vcsTools['gitLfsFound'] as bool? ?? false)),
                        )
                      else ...[
                        NeuActionButton(
                          label: '改动与历史',
                          icon: Icons.pending_rounded,
                          onTap: () => model.refreshVcsState(path),
                        ),
                        const SizedBox(width: 10),
                        NeuActionButton(
                          label: '提交',
                          icon: Icons.upload_file_rounded,
                          onTap: () => model.showModal(_CommitForm(path)),
                        ),
                        const SizedBox(width: 10),
                        NeuActionButton(
                          label: '更新',
                          icon: Icons.download_rounded,
                          onTap: () => runVcsAndShow(model, 'update', path),
                        ),
                        const SizedBox(width: 10),
                        NeuActionButton(
                          label: '推送',
                          icon: Icons.ios_share_rounded,
                          onTap: () => runVcsAndShow(model, 'push', path),
                        ),
                        const SizedBox(width: 10),
                        if (isGit) ...[
                          NeuActionButton(
                            label: '远端',
                            icon: Icons.link_rounded,
                            onTap: () => model.showModal(_RemoteForm(path)),
                          ),
                          const SizedBox(width: 10),
                        ],
                        NeuActionButton(
                          label: '忽略规则',
                          icon: Icons.filter_alt_off_rounded,
                          onTap: () => runVcsAndShow(model, 'gitignore', path),
                        ),
                        const SizedBox(width: 10),
                        if (isGit)
                          NeuActionButton(
                            label: lfs ? '禁用 LFS' : '启用 LFS',
                            icon: lfs
                                ? Icons.link_off_rounded
                                : Icons.drive_folder_upload_rounded,
                            onTap: () =>
                                runVcsAndShow(model, lfs ? 'unlfs' : 'lfs', path),
                          ),
                      ],
                    ],
                  ),
                ),
        ],
      ),
    );
  }
}

/// Picking the hosting tool is the only thing this form does, so the two options
/// say what will actually happen to the folder.
class _InitForm extends StatefulWidget {
  const _InitForm(this.path, this.gitFound, this.cmFound, this.lfsFound);

  final String path;
  final bool gitFound;
  final bool cmFound;
  final bool lfsFound;

  @override
  State<_InitForm> createState() => _InitFormState();
}

class _InitFormState extends State<_InitForm> {
  int _kind = 0;

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    final available = <String>[
      if (widget.gitFound) 'git',
      if (widget.cmFound) 'uvcs',
    ];
    if (available.isEmpty) {
      return NeuModalScaffold(
        title: '接入版本托管',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const NeuEmptyState(
                height: 140,
                text: '本机没有检测到 git，也没有检测到 Unity Version Control。\n先安装其中之一，再回到这里。'),
            const SizedBox(height: 12),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                NeuActionButton(
                  label: '去设置安装',
                  icon: Icons.settings_rounded,
                  emphasised: true,
                  onTap: () {
                    model.closeModal();
                    model.jumpTo('settings');
                  },
                ),
              ],
            ),
          ],
        ),
      );
    }

    final kind = available[_kind.clamp(0, available.length - 1)];

    return NeuModalScaffold(
      title: '接入版本托管',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.path,
              style: TextStyle(fontSize: 12, color: t.inkSecondary),
              overflow: TextOverflow.ellipsis),
          const SizedBox(height: 12),
          NeuField(
            label: '托管方式',
            child: NeuSegmented(
              labels: [
                for (final option in available)
                  option == 'git' ? 'git' : 'Unity Version Control',
              ],
              index: _kind.clamp(0, available.length - 1),
              onChanged: (i) => setState(() => _kind = i),
            ),
          ),
          const SizedBox(height: 8),
          Text(
            kind == 'git'
                ? '会执行 git init、写入 Unity 的 .gitignore，然后 add -A 并做第一次提交。\n'
                    'Library、Temp 这些可重建目录会被 ignore 排除，不会进历史。'
                    '${widget.lfsFound ? "\n本机有 Git LFS：会同时执行 git lfs install --local 并写入 .gitattributes，图片、模型、音频走 LFS。" : "\n本机没有 Git LFS，二进制资源会直接进仓库；装好后可以点「启用 LFS」。"}'
                : '会执行 cm workspace create，把整个工程变成一个 UVCS workspace，并写入 .plasticignore。\n'
                    '仓库跟随你已登录的 Unity 版本控制账号。',
            style: TextStyle(fontSize: 12, height: 1.5, color: t.inkSecondary),
          ),
          const SizedBox(height: 14),
          NeuDialogActions(
            onCancel: model.closeModal,
            confirmLabel: '开始接入',
            onConfirm: () {
              model.closeModal();
              runVcsAndShow(model, 'init', widget.path, kind: kind);
            },
          ),
        ],
      ),
    );
  }
}

class _CommitForm extends StatefulWidget {
  const _CommitForm(this.path);

  final String path;

  @override
  State<_CommitForm> createState() => _CommitFormState();
}

class _CommitFormState extends State<_CommitForm> {
  final _message = TextEditingController();
  bool _push = false;

  @override
  void dispose() {
    _message.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return NeuModalScaffold(
      title: '提交改动',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.path,
              style: TextStyle(fontSize: 12, color: t.inkSecondary),
              overflow: TextOverflow.ellipsis),
          const SizedBox(height: 12),
          NeuField(
            label: '改动说明',
            child: NeuTextInput(
                controller: _message,
                hint: '例如 调好灯光与后处理',
                maxLines: 3),
          ),
          const SizedBox(height: 10),
          NeuSwitchTile(
            label: '提交后推送到远端',
            hint: _push ? '会连到远端，可能需要浏览器登录' : '只记录在本地仓库 / workspace',
            value: _push,
            onChanged: (value) => setState(() => _push = value),
          ),
          const SizedBox(height: 10),
          NeuDialogActions(
            onCancel: model.closeModal,
            confirmLabel: '提交',
            onConfirm: () {
              final message = _message.text.trim();
              if (message.isEmpty) {
                model.setStatus('请先写一句改动说明。');
                return;
              }

              model.closeModal();
              runVcsAndShow(model, 'submit', widget.path,
                  message: message, push: _push);
            },
          ),
        ],
      ),
    );
  }
}

class _RemoteForm extends StatefulWidget {
  const _RemoteForm(this.path);

  final String path;

  @override
  State<_RemoteForm> createState() => _RemoteFormState();
}

class _RemoteFormState extends State<_RemoteForm> {
  final _url = TextEditingController();

  @override
  void dispose() {
    _url.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);

    return NeuModalScaffold(
      title: '设置远端',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.path,
              style: TextStyle(fontSize: 12, color: t.inkSecondary),
              overflow: TextOverflow.ellipsis),
          const SizedBox(height: 12),
          NeuField(
            label: '远端地址',
            hint: '支持 https://、ssh://、git:// 或 git@主机:路径',
            child: NeuTextInput(
                controller: _url, hint: 'https://github.com/你/工程.git'),
          ),
          const SizedBox(height: 10),
          Text('git 会记住这个地址；推送与拉取都走它。私有仓库第一次会弹登录窗口。',
              style: TextStyle(fontSize: 11.5, height: 1.5, color: t.inkSecondary)),
          const SizedBox(height: 14),
          NeuDialogActions(
            onCancel: model.closeModal,
            confirmLabel: '保存',
            onConfirm: () {
              final url = _url.text.trim();
              if (url.isEmpty) {
                model.setStatus('请先填写远端地址。');
                return;
              }

              model.closeModal();
              runVcsAndShow(model, 'remote', widget.path, url: url);
            },
          ),
        ],
      ),
    );
  }
}
