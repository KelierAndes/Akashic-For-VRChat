import 'package:flutter/material.dart';

import 'theme.dart';

/// Dark surfaces swallow shadows, so caller-provided shadow alphas are
/// amplified in dark mode (and white ambience capped) to keep the emboss
/// readable.
Color _ampDark(Color c, NeuTokens t) {
  if (!t.isDark) return c;
  return c.withValues(alpha: (c.a * 2.6).clamp(0.0, 0.85));
}

Color _capLight(Color c, NeuTokens t) {
  if (!t.isDark) return c;
  return c.withValues(alpha: c.a.clamp(0.0, 0.16));
}

/// Convex (raised) look as in the reference: the surface itself is lighter
/// than its surroundings and casts ONE soft dark shadow toward the bottom,
/// with only a faint white ambient at the top-left. Never a white halo.
List<BoxShadow> neuRaised({
  double depth = 6,
  Color? dark,
  Color? light,
  NeuTokens? theme,
}) {
  final t = theme ?? NeuTokens.light;
  return [
    BoxShadow(
      color: _ampDark(dark ?? t.shadowDark, t),
      blurRadius: depth * 2.6,
      offset: Offset(depth * 0.3, depth * 0.95),
    ),
    BoxShadow(
      color: _capLight(light ?? t.shadowLight, t),
      blurRadius: depth * 1.6,
      offset: Offset(-depth * 0.25, -depth * 0.55),
    ),
  ];
}

/// Concave (inset) look per neumorphic rules: a UNIFORM, low-contrast inner
/// shadow all around the edge, with the top-left lip slightly darker and the
/// bottom-right lip slightly lighter. Never a chunky or uneven border.
class InsetShadowPainter extends CustomPainter {
  InsetShadowPainter({
    required this.radius,
    required this.depth,
    required this.dark,
    required this.light,
  });

  final double radius;
  final double depth;
  final Color dark;
  final Color light;

  @override
  void paint(Canvas canvas, Size size) {
    final rrect =
        RRect.fromRectAndRadius(Offset.zero & size, Radius.circular(radius));
    canvas.clipRRect(rrect);

    // A blurred ring hugging the inside of the edge — uniform by construction.
    Path innerRing() {
      return Path.combine(
        PathOperation.difference,
        Path()..addRRect(rrect),
        Path()..addRRect(rrect.deflate(depth * 1.6)),
      );
    }

    final blur = depth * 2.0;

    // Base uniform inner shadow.
    canvas.drawPath(
      innerRing(),
      Paint()
        ..color = dark
        ..maskFilter = MaskFilter.blur(BlurStyle.normal, blur),
    );
    // Directional pass: dark lip pushed toward top-left interior.
    canvas
      ..save()
      ..translate(depth * 0.5, depth * 0.5)
      ..drawPath(
        innerRing(),
        Paint()
          ..color = dark
          ..maskFilter = MaskFilter.blur(BlurStyle.normal, blur),
      )
      ..restore();
    // Light lip pushed toward bottom-right interior.
    canvas
      ..save()
      ..translate(-depth * 0.5, -depth * 0.5)
      ..drawPath(
        innerRing(),
        Paint()
          ..color = light
          ..maskFilter = MaskFilter.blur(BlurStyle.normal, blur),
      )
      ..restore();
  }

  @override
  bool shouldRepaint(covariant InsetShadowPainter old) =>
      old.radius != radius ||
      old.depth != depth ||
      old.dark != dark ||
      old.light != light;
}

enum NeuMode { flat, raised, inset }

Border glassBorder({double alpha = 0.6}) =>
    Border.all(color: Colors.white.withValues(alpha: alpha));

/// Glass edge reflection spec: bright top lip fading to a faint bottom lip.
class GlassEdgeStyle {
  const GlassEdgeStyle({this.top = 0.95, this.bottom = 0.2, this.width = 1.2});

  final double top;
  final double bottom;
  final double width;
}

/// Strokes a rounded rect with a vertical white gradient — the specular
/// highlight along a glass edge.
class GlassEdgePainter extends CustomPainter {
  GlassEdgePainter(this.radius, this.style);

  final double radius;
  final GlassEdgeStyle style;

  @override
  void paint(Canvas canvas, Size size) {
    final inset = style.width / 2;
    final rect = Rect.fromLTWH(inset, inset, size.width - style.width,
        size.height - style.width);
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = style.width
      ..shader = LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        colors: [
          Colors.white.withValues(alpha: style.top),
          Colors.white.withValues(alpha: (style.top + style.bottom) / 2),
          Colors.white.withValues(alpha: style.bottom),
        ],
      ).createShader(rect);
    canvas.drawRRect(
        RRect.fromRectAndRadius(rect, Radius.circular(radius)), paint);
  }

  @override
  bool shouldRepaint(covariant GlassEdgePainter old) =>
      old.radius != radius || old.style != style;
}

/// Wraps any rounded widget with a glass edge reflection stroke.
class GlassEdgeOverlay extends StatelessWidget {
  const GlassEdgeOverlay({
    super.key,
    required this.radius,
    required this.child,
    this.style = const GlassEdgeStyle(),
  });

  final double radius;
  final GlassEdgeStyle style;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.passthrough,
      children: [
        child,
        Positioned.fill(
          child: IgnorePointer(
            child: CustomPaint(painter: GlassEdgePainter(radius, style)),
          ),
        ),
      ],
    );
  }
}

/// A neumorphic box that switches between flat / convex / concave.
class NeuBox extends StatelessWidget {
  const NeuBox({
    super.key,
    required this.mode,
    this.child,
    this.padding,
    this.radius = 14,
    this.depth = 6,
    this.color,
    this.darkShadow,
    this.lightShadow,
    this.width,
    this.height,
    this.border,
    this.edge,
  });

  final NeuMode mode;
  final Widget? child;
  final EdgeInsetsGeometry? padding;
  final double radius;
  final double depth;
  final Color? color;
  final Color? darkShadow;
  final Color? lightShadow;
  final double? width;
  final double? height;
  final BoxBorder? border;
  final GlassEdgeStyle? edge;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    Widget box;
    final borderRadius = BorderRadius.circular(radius);

    if (mode == NeuMode.flat) {
      box = Container(
        width: width,
        height: height,
        padding: padding,
        decoration: border == null
            ? null
            : BoxDecoration(borderRadius: borderRadius, border: border),
        child: child,
      );
    } else if (mode == NeuMode.raised) {
      box = Container(
        width: width,
        height: height,
        padding: padding,
        decoration: BoxDecoration(
          color: color ?? t.raised,
          borderRadius: borderRadius,
          border: border,
          boxShadow: neuRaised(
              depth: depth,
              dark: darkShadow,
              light: lightShadow,
              theme: t),
        ),
        child: child,
      );
    } else {
      // concave
      final content = padding != null
          ? Padding(padding: padding!, child: child ?? const SizedBox.shrink())
          : (child ?? const SizedBox.shrink());
      final fixedSize = width != null || height != null;
      box = SizedBox(
        width: width,
        height: height,
        child: ClipRRect(
          borderRadius: borderRadius,
          child: Stack(
            fit: fixedSize ? StackFit.expand : StackFit.loose,
            alignment: Alignment.center,
            children: [
              Positioned.fill(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: color ?? t.inset,
                    border: border,
                  ),
                ),
              ),
              Positioned.fill(
                child: CustomPaint(
                  painter: InsetShadowPainter(
                    radius: radius,
                    depth: depth,
                    dark: _ampDark(darkShadow ?? t.shadowDark, t),
                    light: _capLight(lightShadow ?? t.shadowLight, t),
                  ),
                ),
              ),
              content,
            ],
          ),
        ),
      );
    }

    if (edge != null) {
      box = Stack(
        fit: StackFit.passthrough,
        children: [
          box,
          Positioned.fill(
            child: IgnorePointer(
              child: CustomPaint(painter: GlassEdgePainter(radius, edge!)),
            ),
          ),
        ],
      );
    }
    return box;
  }
}

/// Interactive neumorphic control: press sinks the surface (convex -> concave),
/// hover lifts it, selected stays convex.
class NeuButton extends StatefulWidget {
  const NeuButton({
    super.key,
    required this.child,
    this.onTap,
    this.selected = false,
    this.radius = 14,
    this.depth = 6,
    this.padding,
    this.color,
    this.insetColor,
    this.darkShadow,
    this.lightShadow,
    this.width,
    this.height,
    this.border,
    this.edge,
  });

  final Widget child;
  final VoidCallback? onTap;
  final bool selected;
  final double radius;
  final double depth;
  final EdgeInsetsGeometry? padding;
  final Color? color;
  final Color? insetColor;
  final Color? darkShadow;
  final Color? lightShadow;
  final double? width;
  final double? height;
  final BoxBorder? border;
  final GlassEdgeStyle? edge;

  @override
  State<NeuButton> createState() => _NeuButtonState();
}

class _NeuButtonState extends State<NeuButton> {
  bool _hover = false;
  bool _pressed = false;

  void _setPressed(bool v) {
    if (_pressed != v) setState(() => _pressed = v);
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final NeuMode mode;
    final double depth;
    final Color? color;
    if (_pressed) {
      mode = NeuMode.inset;
      depth = widget.depth * 0.9;
      color = widget.insetColor ?? t.inset;
    } else if (widget.selected) {
      mode = NeuMode.raised;
      depth = widget.depth;
      color = widget.color ?? t.raisedHi;
    } else if (_hover) {
      mode = NeuMode.raised;
      depth = widget.depth * 0.5;
      color = widget.color ?? t.raised;
    } else {
      mode = NeuMode.flat;
      depth = widget.depth;
      color = null;
    }

    return MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hover = true),
      onExit: (_) => setState(() => _hover = false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapDown: (_) => _setPressed(true),
        onTapCancel: () => _setPressed(false),
        onTapUp: (_) => _setPressed(false),
        onTap: widget.onTap,
        child: NeuBox(
          mode: mode,
          radius: widget.radius,
          depth: depth,
          color: color,
          padding: widget.padding,
          width: widget.width,
          height: widget.height,
          border: widget.border,
          edge: widget.edge,
          darkShadow: widget.darkShadow,
          lightShadow: widget.lightShadow,
          child: widget.child,
        ),
      ),
    );
  }
}

/// Frosted glass window sheet. The app canvas is fully transparent, so this
/// sheet carries its own translucent gray fill and reads as an independent
/// window: hairline highlight edge, no drop shadow. A shadow here used to paint
/// a gray halo into the transparent margin, which made the window edge look dirty.
class GlassSheet extends StatelessWidget {
  const GlassSheet({
    super.key,
    required this.child,
    this.radius = 16,
    this.opacity = 1,
  });

  final Widget child;
  final double radius;

  /// Multiplies the alpha of the sheet gradient — the 全局透明度 knob.
  final double opacity;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final borderRadius = BorderRadius.circular(radius);
    return Container(
      decoration: BoxDecoration(borderRadius: borderRadius),
      child: ClipRRect(
        borderRadius: borderRadius,
        child: DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [
                t.sheetA.withValues(alpha: t.sheetA.a * opacity),
                t.sheetB.withValues(alpha: t.sheetB.a * opacity),
                t.sheetC.withValues(alpha: t.sheetC.a * opacity),
              ],
              stops: const [0.0, 0.55, 1.0],
            ),
          ),
          child: Stack(
            fit: StackFit.expand,
            children: [
              child,
              IgnorePointer(
                child: CustomPaint(
                  painter: GlassEdgePainter(
                    radius,
                    GlassEdgeStyle(top: t.edgeTop, bottom: t.edgeBottom, width: 1.4),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A carved groove separator — concave per neumorphic rules:
/// dark hairline on top, light hairline below.
class NeuDivider extends StatelessWidget {
  const NeuDivider(
      {super.key,
      this.margin =
          const EdgeInsets.symmetric(vertical: 14, horizontal: 6)});

  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: margin,
      child: Column(
        children: [
          Container(
            height: 1,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  Colors.transparent,
                  Colors.black.withValues(alpha: 0.14),
                  Colors.black.withValues(alpha: 0.10),
                  Colors.transparent,
                ],
              ),
            ),
          ),
          Container(
            height: 1,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  Colors.transparent,
                  Colors.white.withValues(alpha: 0.55),
                  Colors.white.withValues(alpha: 0.45),
                  Colors.transparent,
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
