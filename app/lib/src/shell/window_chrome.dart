import 'package:flutter/material.dart';
import 'package:window_manager/window_manager.dart';

import '../ui/app_colors.dart';
import '../ui/neumorphic.dart';
import '../ui/theme.dart';

enum WindowAction { close, minimize, maximize }

/// Strip that owns the window: traffic lights on the left, drag surface in
/// the middle, optional trailing control. Only this band is draggable, so
/// clicks below it still reach the page.
class WindowChrome extends StatelessWidget {
  const WindowChrome({
    super.key,
    this.trailing,
    this.height = 46,
  });

  final Widget? trailing;
  final double height;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return DragToMoveArea(
      child: SizedBox(
        height: height,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 18),
          child: Row(
            children: [
              const _TrafficLight(
                  color: AppColors.close, action: WindowAction.close),
              const SizedBox(width: 8),
              const _TrafficLight(
                  color: AppColors.minimize, action: WindowAction.minimize),
              const SizedBox(width: 8),
              const _TrafficLight(
                  color: AppColors.maximize, action: WindowAction.maximize),
              const Spacer(),
              if (trailing != null)
                trailing!
              else
                NeuButton(
                  width: 30,
                  height: 30,
                  radius: 8,
                  depth: 4,
                  child: Icon(Icons.picture_in_picture_alt_outlined,
                      size: 16, color: t.inkSecondary),
                  onTap: () {},
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _TrafficLight extends StatefulWidget {
  const _TrafficLight({required this.color, required this.action});

  final Color color;
  final WindowAction action;

  @override
  State<_TrafficLight> createState() => _TrafficLightState();
}

class _TrafficLightState extends State<_TrafficLight> {
  bool _hover = false;

  Future<void> _invoke() async {
    switch (widget.action) {
      case WindowAction.close:
        await windowManager.close();
      case WindowAction.minimize:
        await windowManager.minimize();
      case WindowAction.maximize:
        (await windowManager.isMaximized())
            ? await windowManager.unmaximize()
            : await windowManager.maximize();
    }
  }

  @override
  Widget build(BuildContext context) {
    return MouseRegion(
      onEnter: (_) => setState(() => _hover = true),
      onExit: (_) => setState(() => _hover = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: _invoke,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 120),
          width: 13,
          height: 13,
          decoration: BoxDecoration(
            color: widget.color,
            shape: BoxShape.circle,
            boxShadow: [
              BoxShadow(
                color: _hover
                    ? widget.color.withValues(alpha: 0.7)
                    : widget.color.withValues(alpha: 0.35),
                blurRadius: _hover ? 8 : 4,
              ),
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.18),
                blurRadius: 2,
                offset: const Offset(1, 1),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
