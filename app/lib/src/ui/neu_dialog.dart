import 'package:flutter/material.dart';

import '../shell/app_scope.dart';
import 'app_colors.dart';
import 'neu_kit.dart';
import 'neumorphic.dart';
import 'theme.dart';

/// Panel chrome for a form shown in the shell's own modal layer.
///
/// Forms are NOT routes: a route pushed over this frameless, per-pixel transparent
/// window never receives pointer events, so every button in it is dead. The shell
/// keeps a modal slot inside the page tray instead.
class NeuModalScaffold extends StatelessWidget {
  const NeuModalScaffold({
    super.key,
    required this.title,
    required this.child,
  });

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    // Popups are glass too: without this the 全局不透明度 slider faded the window and
    // the rail but left every dialog — 新建项目 included — solid.
    final opacity = AppScope.of(context).sheetOpacity;
    return NeuBox(
      mode: NeuMode.raised,
      radius: 14,
      depth: 9,
      color: t.raisedHi.withValues(alpha: opacity),
      edge: GlassEdgeStyle(top: t.edgeTop * opacity, bottom: t.edgeBottom * opacity, width: 1.4),
      darkShadow: const Color(0x40000000),
      lightShadow: const Color(0x40FFFFFF),
      padding: const EdgeInsets.fromLTRB(24, 20, 24, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title,
              style: TextStyle(
                  fontSize: 20, fontWeight: FontWeight.w600, color: t.ink)),
          const SizedBox(height: 16),
          Flexible(
            child: SingleChildScrollView(
              padding: const EdgeInsets.only(bottom: 4),
              child: DefaultTextStyle(
                style: TextStyle(color: t.ink, fontSize: 13),
                child: child,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Label above any control, so forms read the same everywhere.
class NeuField extends StatelessWidget {
  const NeuField({super.key, required this.label, required this.child, this.hint});

  final String label;
  final Widget child;
  final String? hint;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w500,
                  color: t.inkSecondary)),
          const SizedBox(height: 6),
          child,
          if (hint != null)
            Padding(
              padding: const EdgeInsets.only(top: 5),
              child: Text(hint!,
                  style: TextStyle(fontSize: 11, color: t.inkTertiary)),
            ),
        ],
      ),
    );
  }
}

/// Inset text entry — the same carved look as the search field.
class NeuTextInput extends StatelessWidget {
  const NeuTextInput({
    super.key,
    required this.controller,
    this.hint = '',
    this.maxLines = 1,
  });

  final TextEditingController controller;
  final String hint;
  final int maxLines;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuBox(
      mode: NeuMode.inset,
      radius: 9,
      depth: 5,
      color: Colors.black.withValues(alpha: 0.07),
      border: Border.all(color: Colors.black.withValues(alpha: 0.14)),
      darkShadow: const Color(0x38000000),
      lightShadow: const Color(0x55FFFFFF),
      padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 11),
      child: TextField(
        controller: controller,
        maxLines: maxLines,
        style: TextStyle(fontSize: 13, color: t.ink),
        decoration: InputDecoration(
          isDense: true,
          border: InputBorder.none,
          filled: false,
          hintText: hint,
          hintStyle: TextStyle(color: t.inkTertiary, fontSize: 13),
        ),
      ),
    );
  }
}

/// A selector that unfolds in place, so a long option list never has to be
/// positioned against a layer the modal does not own.
class NeuChoiceField extends StatefulWidget {
  const NeuChoiceField({
    super.key,
    required this.values,
    required this.index,
    required this.onChanged,
    this.icon,
  });

  final List<String> values;
  final int index;
  final ValueChanged<int> onChanged;
  final IconData? icon;

  @override
  State<NeuChoiceField> createState() => _NeuChoiceFieldState();
}

class _NeuChoiceFieldState extends State<NeuChoiceField> {
  bool _open = false;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final shown = widget.values.isEmpty ? const ['（无可用项）'] : widget.values;
    final safeIndex = widget.index.clamp(0, shown.length - 1);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => setState(() => _open = !_open),
          child: NeuBox(
            mode: _open ? NeuMode.inset : NeuMode.raised,
            radius: 9,
            depth: 4,
            color: _open ? t.inset : t.raised,
            border: glassBorder(alpha: 0.45),
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
            child: Row(
              children: [
                if (widget.icon != null) ...[
                  Icon(widget.icon, size: 16, color: t.inkSecondary),
                  const SizedBox(width: 9),
                ],
                Expanded(
                  child: Text(shown[safeIndex],
                      style: const TextStyle(
                          fontSize: 13, fontWeight: FontWeight.w500),
                      overflow: TextOverflow.ellipsis),
                ),
                Icon(
                    _open
                        ? Icons.expand_less_rounded
                        : Icons.expand_more_rounded,
                    size: 18,
                    color: t.inkSecondary),
              ],
            ),
          ),
        ),
        if (_open)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: NeuBox(
              mode: NeuMode.raised,
              radius: 10,
              depth: 6,
              color: t.raisedHi,
              edge: GlassEdgeStyle(
                  top: t.edgeTop, bottom: t.edgeBottom, width: 1.3),
              darkShadow: const Color(0x33000000),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (var i = 0; i < shown.length; i++)
                    GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onTap: () {
                        widget.onChanged(i);
                        setState(() => _open = false);
                      },
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 16, vertical: 11),
                        child: Row(
                          children: [
                            SizedBox(
                              width: 18,
                              child: i == safeIndex
                                  ? Icon(Icons.check_rounded,
                                      size: 15, color: t.ink)
                                  : null,
                            ),
                            const SizedBox(width: 6),
                            Expanded(
                              child: Text(shown[i],
                                  style: TextStyle(
                                    fontSize: 13,
                                    fontWeight: i == safeIndex
                                        ? FontWeight.w600
                                        : FontWeight.w400,
                                    color: i == safeIndex
                                        ? t.ink
                                        : t.inkSecondary,
                                  )),
                            ),
                          ],
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

/// A one-field prompt shown through the shell's modal slot. Routes are unusable in
/// this window, so even a text entry has to live in the widget tree.
class NeuPromptForm extends StatefulWidget {
  const NeuPromptForm({
    super.key,
    required this.title,
    required this.label,
    required this.onSubmit,
    this.hint = '',
    this.helper,
    this.initial = '',
  });

  final String title;
  final String label;
  final String hint;
  final String? helper;
  final String initial;
  final ValueChanged<String> onSubmit;

  @override
  State<NeuPromptForm> createState() => _NeuPromptFormState();
}

class _NeuPromptFormState extends State<NeuPromptForm> {
  late final TextEditingController _text =
      TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);

    return NeuModalScaffold(
      title: widget.title,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NeuField(
            label: widget.label,
            hint: widget.helper,
            child: NeuTextInput(controller: _text, hint: widget.hint),
          ),
          const SizedBox(height: 6),
          NeuDialogActions(
            onCancel: model.closeModal,
            onConfirm: () {
              final value = _text.text.trim();
              model.closeModal();
              if (value.isNotEmpty) widget.onSubmit(value);
            },
          ),
        ],
      ),
    );
  }
}

class NeuDialogActions extends StatelessWidget {
  const NeuDialogActions({
    super.key,
    required this.onConfirm,
    required this.onCancel,
    this.confirmLabel = '确定',
  });

  final VoidCallback onConfirm;
  final VoidCallback onCancel;
  final String confirmLabel;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.end,
      children: [
        NeuActionButton(label: '取消', onTap: onCancel),
        const SizedBox(width: 10),
        NeuActionButton(
          label: confirmLabel,
          icon: Icons.check_rounded,
          emphasised: true,
          onTap: onConfirm,
        ),
      ],
    );
  }
}

/// The outcome of one sidecar operation, in a dialog the user opens and closes on purpose.
///
/// Keeping this text on the page made the list rows underneath stop answering taps, so every
/// caller that gets a long tool transcript — git, cm, the config sync — hands it here instead.
class NeuResultForm extends StatelessWidget {
  const NeuResultForm(this.log, {super.key});

  final Map<String, dynamic> log;

  @override
  Widget build(BuildContext context) {
    final model = AppScope.of(context);
    final t = NeuTheme.of(context);
    final ok = log['success'] as bool? ?? false;
    final output = log['output'] as String? ?? '';

    return NeuModalScaffold(
      title: log['title'] as String? ?? '执行结果',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(
                ok ? Icons.check_circle_rounded : Icons.error_rounded,
                size: 16,
                color: ok ? AppColors.greenFg : t.warn,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(log['projectPath'] as String? ?? '',
                    style: TextStyle(fontSize: 12, color: t.inkSecondary),
                    overflow: TextOverflow.ellipsis),
              ),
            ],
          ),
          const SizedBox(height: 12),
          // The modal scaffold hands its child an unbounded height, so the cap has
          // to come from a box rather than an Expanded.
          ConstrainedBox(
            constraints: const BoxConstraints(maxHeight: 340),
            child: SingleChildScrollView(
              child: SelectableText(
                output.isEmpty ? '（没有输出）' : output,
                style: TextStyle(
                    fontSize: 12,
                    height: 1.5,
                    fontFamily: 'Consolas',
                    fontFamilyFallback: const ['monospace'],
                    color: t.inkSecondary),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              NeuActionButton(
                label: '关闭',
                icon: Icons.close_rounded,
                onTap: model.closeModal,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
