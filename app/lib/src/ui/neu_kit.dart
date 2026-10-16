import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';

import 'app_colors.dart';
import 'neumorphic.dart';
import 'theme.dart';

/// Hairline used by the reference sheet for glass-tinted containers.
Border glassEdgeBorderFallback() =>
    Border.all(color: Colors.white.withValues(alpha: 0.45));

/// Page body shared by every Akashic page.
///
/// With `content` set, the title and the `fixed` rows stay put and that one region
/// takes the rest of the tray: lists then reach the bottom edge instead of leaving
/// dead space under a fixed-height box, and an embedded browser parked in the fixed
/// area no longer shares a viewport with the scrolling list.
class NeuPageBody extends StatelessWidget {
  const NeuPageBody({
    super.key,
    required this.title,
    required this.subtitle,
    this.fixed = const [],
    this.trailing,
    this.content,
  });

  final String title;
  final String subtitle;
  final List<Widget> fixed;
  final Widget? trailing;
  final Widget? content;

  Widget _header(BuildContext context, {bool top = false}) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(28, top ? 14 : 4, 28, 0),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title,
                    style: TextStyle(
                        fontSize: 30,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -0.5,
                        color: t.ink)),
                const SizedBox(height: 4),
                Text(subtitle,
                    style: TextStyle(fontSize: 13, color: t.inkSecondary)),
              ],
            ),
          ),
          ?trailing,
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final body = content;

    if (body == null) {
      // A plain scroll view rather than a ListView: the settings page asks the shell to
      // scroll to one of these cards, and a lazily built list has not created that card
      // yet, so the request would find nothing to scroll to.
      return SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(28, 4, 28, 28),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _header(context),
            const SizedBox(height: 26),
            ...fixed,
          ],
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _header(context, top: true),
        if (fixed.isNotEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(28, 22, 28, 12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: fixed,
            ),
          ),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(28, 0, 28, 20),
            child: body,
          ),
        ),
      ],
    );
  }
}

/// Convex content card — the default container for rows, tiles and groups.
class NeuCard extends StatelessWidget {
  const NeuCard({
    super.key,
    required this.child,
    this.padding,
    this.selected = false,
  });

  final Widget child;
  final EdgeInsetsGeometry? padding;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuBox(
      mode: NeuMode.raised,
      radius: 11,
      depth: 5,
      color: selected ? t.raisedHi : t.raised,
      border: glassBorder(alpha: 0.4),
      darkShadow: const Color(0x1E000000),
      lightShadow: const Color(0x33FFFFFF),
      padding: padding ?? const EdgeInsets.all(16),
      child: child,
    );
  }
}

/// Pressable card, used for project tiles and every list row that opens a
/// detail view.
class NeuTileButton extends StatelessWidget {
  const NeuTileButton({
    super.key,
    required this.child,
    this.onTap,
    this.selected = false,
    this.padding,
  });

  final Widget child;
  final VoidCallback? onTap;
  final bool selected;
  final EdgeInsetsGeometry? padding;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuButton(
      selected: selected,
      radius: 11,
      depth: 5,
      color: t.raised,
      insetColor: t.inset,
      border: glassBorder(alpha: 0.4),
      darkShadow: const Color(0x1E000000),
      lightShadow: const Color(0x33FFFFFF),
      padding: padding ?? const EdgeInsets.all(16),
      onTap: onTap,
      child: child,
    );
  }
}

class NeuAvatar extends StatelessWidget {
  const NeuAvatar(this.initials, {super.key, this.size = 34, this.color});

  final String initials;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: color ?? const Color(0xFF3A3C3A),
        shape: BoxShape.circle,
        boxShadow: neuRaised(depth: 3, dark: const Color(0x24000000)),
      ),
      child: Text(
        initials,
        style: TextStyle(
            color: Colors.white,
            fontSize: size * 0.38,
            fontWeight: FontWeight.w600),
      ),
    );
  }
}

/// Small status chip: 已安装 / 有更新 / Booth / 未登录.
class NeuPill extends StatelessWidget {
  const NeuPill(this.text,
      {super.key, this.bg, this.fg, this.onTap, this.tooltip});

  final String text;
  final Color? bg;
  final Color? fg;
  final VoidCallback? onTap;
  final String? tooltip;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final pill = Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: bg ?? Colors.black.withValues(alpha: 0.06),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.white.withValues(alpha: 0.35)),
      ),
      child: Text(text,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
              fontSize: 11.5,
              fontWeight: FontWeight.w600,
              color: fg ?? t.inkSecondary)),
    );

    final tap = onTap;
    if (tap == null) return pill;
    return Tooltip(
      message: tooltip ?? text,
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        child: GestureDetector(
            behavior: HitTestBehavior.opaque, onTap: tap, child: pill),
      ),
    );
  }
}

class NeuSectionTitle extends StatelessWidget {
  const NeuSectionTitle(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Text(text,
          style: TextStyle(
              fontSize: 20, fontWeight: FontWeight.w600, color: t.ink)),
    );
  }
}

/// Underline tabs — the resource page's three-section switch and any
/// in-page view toggle.
class NeuTabs extends StatelessWidget {
  const NeuTabs({
    super.key,
    required this.tabs,
    required this.index,
    required this.onChanged,
  });

  final List<String> tabs;
  final int index;
  final ValueChanged<int> onChanged;

  static double _measure(String text, TextStyle style) {
    final painter = TextPainter(
      text: TextSpan(text: text, style: style),
      textDirection: TextDirection.ltr,
    )..layout();
    return painter.width;
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return SizedBox(
      height: 44,
      child: Row(
        children: [
          for (var i = 0; i < tabs.length; i++)
            MouseRegion(
              cursor: SystemMouseCursors.click,
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTap: () => onChanged(i),
                child: Padding(
                  padding: const EdgeInsets.only(right: 26, bottom: 10),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        tabs[i],
                        style: TextStyle(
                          fontSize: 13.5,
                          fontWeight: i == index
                              ? FontWeight.w600
                              : FontWeight.w400,
                          color:
                              i == index ? t.ink : t.inkTertiary,
                        ),
                      ),
                      const SizedBox(height: 7),
                      // Embossed underline: raised bar with a light top lip
                      // and a cast shadow. Measured, so CJK labels work.
                      AnimatedContainer(
                        duration: const Duration(milliseconds: 150),
                        height: 3,
                        width: i == index
                            ? _measure(tabs[i],
                                const TextStyle(fontSize: 13.5))
                            : 0,
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(2),
                          gradient: LinearGradient(
                            begin: Alignment.topCenter,
                            end: Alignment.bottomCenter,
                            colors: [
                              Colors.white.withValues(alpha: 0.5),
                              t.ink.withValues(alpha: 0.72),
                            ],
                            stops: const [0.0, 0.35],
                          ),
                          boxShadow: [
                            BoxShadow(
                              color: t.inkTertiary,
                              blurRadius: 3,
                              offset: const Offset(0, 2),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// The only inset (concave) surface in the kit — search boxes and text input.
class NeuSearchField extends StatelessWidget {
  const NeuSearchField({
    super.key,
    required this.hint,
    this.controller,
    this.onChanged,
    this.width,
  });

  final String hint;
  final TextEditingController? controller;
  final ValueChanged<String>? onChanged;
  final double? width;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final field = NeuBox(
      mode: NeuMode.inset,
      radius: 9,
      depth: 5,
      height: 48,
      color: Colors.black.withValues(alpha: 0.07),
      border: Border.all(color: Colors.black.withValues(alpha: 0.14)),
      darkShadow: const Color(0x38000000),
      lightShadow: const Color(0x55FFFFFF),
      padding: const EdgeInsets.symmetric(horizontal: 15),
      child: Row(
        children: [
          Icon(Icons.search_rounded, size: 20, color: t.inkTertiary),
          const SizedBox(width: 10),
          Expanded(
            child: TextField(
              controller: controller,
              onChanged: onChanged,
              style: TextStyle(fontSize: 13.5, color: t.ink),
              decoration: InputDecoration(
                isDense: true,
                border: InputBorder.none,
                filled: false,
                hintText: hint,
                hintStyle: TextStyle(color: t.inkTertiary, fontSize: 13.5),
              ),
            ),
          ),
        ],
      ),
    );
    return width == null ? field : SizedBox(width: width, child: field);
  }
}

/// Bracket mark used at the corners of the empty state.
class NeuCornerMark extends StatelessWidget {
  const NeuCornerMark({super.key, required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 10,
      height: 10,
      child: CustomPaint(painter: _CornerPainter(color)),
    );
  }
}

class _CornerPainter extends CustomPainter {
  const _CornerPainter(this.color);

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 1.4
      ..style = PaintingStyle.stroke;
    canvas.drawLine(const Offset(0, 0), Offset(size.width, 0), paint);
    canvas.drawLine(const Offset(0, 0), Offset(0, size.height), paint);
  }

  @override
  bool shouldRepaint(covariant _CornerPainter oldDelegate) =>
      oldDelegate.color != color;
}

class NeuEmptyState extends StatelessWidget {
  const NeuEmptyState(
      {super.key, required this.text, this.height = 320, this.expand = false});

  final String text;
  final double height;

  /// Fill whatever region the page gave instead of standing at a fixed height —
  /// used inside `NeuPageBody(content:)`, where a short box would leave dead space.
  final bool expand;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuBox(
      mode: NeuMode.raised,
      radius: 11,
      depth: 6,
      color: t.raisedHi,
      border: glassBorder(alpha: 0.45),
      darkShadow: const Color(0x26000000),
      lightShadow: const Color(0x3AFFFFFF),
      height: expand ? null : height,
      child: Stack(
        children: [
          Positioned(
              top: 14, left: 14, child: NeuCornerMark(color: t.inkTertiary)),
          Positioned(
            bottom: 14,
            left: 14,
            child: Transform.rotate(
                angle: 1.5708,
                child: NeuCornerMark(color: t.inkTertiary)),
          ),
          Center(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 48),
              child: Text(
                text,
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 13.5,
                  height: 1.6,
                  color: t.inkTertiary,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Overlay dropdown — filters, version pickers, per-row menus.
class NeuDropdown extends StatefulWidget {
  const NeuDropdown({
    super.key,
    required this.value,
    required this.items,
    required this.onChanged,
    this.icon,
  });

  final String value;
  final List<String> items;
  final ValueChanged<String> onChanged;
  final IconData? icon;

  @override
  State<NeuDropdown> createState() => _NeuDropdownState();
}

class _NeuDropdownState extends State<NeuDropdown> {
  final LayerLink _link = LayerLink();
  OverlayEntry? _entry;

  @override
  void dispose() {
    _entry?.remove();
    super.dispose();
  }

  void _close() {
    _entry?.remove();
    _entry = null;
  }

  void _toggle() {
    if (_entry != null) {
      _close();
      return;
    }
    final t = NeuTheme.of(context);

    // A 25-entry project list used to run off the bottom of the window with nothing to
    // scroll it, so the menu measures the room it has and flips above the trigger when
    // the space below is too tight.
    final box = context.findRenderObject() as RenderBox?;
    final screen = MediaQuery.sizeOf(context);
    var flipUp = false;
    var maxHeight = 360.0;
    if (box != null) {
      final origin = box.localToGlobal(Offset.zero);
      final below = screen.height - (origin.dy + box.size.height) - 20;
      final above = origin.dy - 20;
      if (below < 220 && above > below) {
        flipUp = true;
        maxHeight = math.min(360.0, above);
      } else {
        maxHeight = math.min(360.0, math.max(160.0, below));
      }
    }

    _entry = OverlayEntry(
      builder: (context) => Stack(
        children: [
          Positioned.fill(
            child: GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: _close,
              child: const ColoredBox(color: Color(0x01000000)),
            ),
          ),
          CompositedTransformFollower(
            link: _link,
            targetAnchor:
                flipUp ? Alignment.topRight : Alignment.bottomRight,
            followerAnchor:
                flipUp ? Alignment.bottomRight : Alignment.topRight,
            offset: Offset(0, flipUp ? -8 : 8),
            child: Material(
              type: MaterialType.transparency,
              child: NeuBox(
                mode: NeuMode.raised,
                radius: 10,
                depth: 6,
                color: t.raisedHi,
                edge: GlassEdgeStyle(
                    top: t.edgeTop, bottom: t.edgeBottom, width: 1.3),
                darkShadow: const Color(0x33000000),
                child: ConstrainedBox(
                  constraints: BoxConstraints(maxHeight: maxHeight),
                  child: SizedBox(
                    width: 220,
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          for (final item in widget.items)
                            MouseRegion(
                              cursor: SystemMouseCursors.click,
                              child: GestureDetector(
                                behavior: HitTestBehavior.opaque,
                                onTap: () {
                                  widget.onChanged(item);
                                  _close();
                                },
                                child: Padding(
                                  padding: const EdgeInsets.symmetric(
                                      horizontal: 20, vertical: 11),
                                  child: Text(
                                    item,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      fontSize: 13,
                                      fontWeight: item == widget.value
                                          ? FontWeight.w600
                                          : FontWeight.w400,
                                      color: item == widget.value
                                          ? t.ink
                                          : t.inkSecondary,
                                    ),
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
    Overlay.of(context).insert(_entry!);
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return CompositedTransformTarget(
      link: _link,
      child: NeuButton(
        radius: 9,
        depth: 4,
        color: t.raised,
        insetColor: t.inset,
        border: glassBorder(alpha: 0.45),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
        onTap: _toggle,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.icon != null) ...[
              Icon(widget.icon, size: 16, color: t.inkSecondary),
              const SizedBox(width: 8),
            ],
            Text(widget.value,
                style: const TextStyle(
                    fontSize: 13, fontWeight: FontWeight.w500)),
            const SizedBox(width: 6),
            Icon(Icons.expand_more_rounded,
                size: 18, color: t.inkSecondary),
          ],
        ),
      ),
    );
  }
}

/// Primary command button (安装 / 下载 / 新建项目).
class NeuActionButton extends StatelessWidget {
  const NeuActionButton({
    super.key,
    required this.label,
    this.onTap,
    this.icon,
    this.emphasised = false,
  });

  final String label;
  final VoidCallback? onTap;
  final IconData? icon;
  final bool emphasised;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuButton(
      selected: true,
      radius: 9,
      depth: 4,
      color: t.raised,
      insetColor: t.raised,
      border: glassBorder(alpha: emphasised ? 0.55 : 0.45),
      darkShadow: const Color(0x1E000000),
      lightShadow: const Color(0x33FFFFFF),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      onTap: onTap,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 15, color: t.inkSecondary),
            const SizedBox(width: 8),
          ],
          Text(label,
              style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w600,
                  color: t.ink)),
        ],
      ),
    );
  }
}

class NeuSwitchTile extends StatelessWidget {
  const NeuSwitchTile({
    super.key,
    required this.label,
    required this.hint,
    required this.value,
    required this.onChanged,
  });

  final String label;
  final String hint;
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return NeuCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label,
                    style: const TextStyle(
                        fontSize: 13.5, fontWeight: FontWeight.w600)),
                const SizedBox(height: 2),
                Text(hint,
                    style:
                        TextStyle(fontSize: 11.5, color: t.inkTertiary)),
              ],
            ),
          ),
          Switch(
            value: value,
            onChanged: onChanged,
            activeThumbColor: Colors.white,
            activeTrackColor: AppColors.greenFg,
            inactiveTrackColor: Colors.black.withValues(alpha: 0.12),
            inactiveThumbColor: const Color(0xFFEFEFEA),
          ),
        ],
      ),
    );
  }
}

/// Segmented control: light / dark / system, filter modes, view density.
class NeuSegmented extends StatelessWidget {
  const NeuSegmented({
    super.key,
    required this.labels,
    required this.index,
    required this.onChanged,
  });

  final List<String> labels;
  final int index;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 0; i < labels.length; i++)
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: NeuButton(
              selected: i == index,
              radius: 9,
              depth: 3.5,
              color: t.glassFillStrong,
              insetColor: t.inset,
              edge: GlassEdgeStyle(
                  top: t.edgeTop, bottom: t.edgeBottom),
              padding:
                  const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
              onTap: () => onChanged(i),
              child: Text(labels[i],
                  style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w500,
                      color: t.ink)),
            ),
          ),
      ],
    );
  }
}

/// Cover thumbnail used by the Booth, Asset Store and VRChat lists.
///
/// Fetched by hand instead of with `Image.network(headers:)`: Flutter *adds* that
/// header, so the request leaves with two User-Agent values, and VRChat's image CDN
/// answers that with 403.
///
/// Two levels of cache, because these lists are re-read every time a page opens: the
/// bytes stay in [fetched] for the run, and on disk under
/// `<LOCALAPPDATA>\Akashic\cache\covers` across runs. Booth's CDN and VRChat's both
/// rate-limit or refuse a cold reload of thirty covers.
class NeuCover extends StatefulWidget {
  const NeuCover(this.url, {super.key, this.width = 64, this.height = 48});

  final String url;
  final double width;
  final double height;

  static const userAgent =
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
      '(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0';

  static final Map<String, Uint8List> fetched = {};

  @override
  State<NeuCover> createState() => _NeuCoverState();
}

class _NeuCoverState extends State<NeuCover> {
  static final HttpClient _client = HttpClient()..userAgent = null;

  /// `%LOCALAPPDATA%\Akashic\cache\covers`, resolved once. [_noCache] is the negative
  /// answer, so a machine with no writable folder does not retry it per thumbnail.
  static String? _directory;
  static const String _noCache = '';

  /// Covers are thumbnails; anything bigger than this is not worth keeping on disk.
  static const int _maxBytes = 4 << 20;

  Uint8List? _image;

  @override
  void initState() {
    super.initState();
    _resolve();
  }

  @override
  void didUpdateWidget(covariant NeuCover oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.url == widget.url) return;
    _image = null;
    _resolve();
  }

  Future<void> _resolve() async {
    // The stores hand back protocol-relative covers ("//cdn…/x.png").
    var url = widget.url;
    if (url.isEmpty) return;

    if (!url.startsWith('//') && !url.startsWith('http')) {
      // A cover the storefront never had is cut out of the package itself and handed over
      // as a local path. It is already on disk, so it needs no cache of its own.
      try {
        final bytes = await File(url).readAsBytes();
        if (bytes.isNotEmpty && mounted) setState(() => _image = bytes);
      } catch (_) {
        // The extraction folder may have been cleaned; the placeholder glyph answers.
      }
      return;
    }

    if (url.startsWith('//')) url = 'https:$url';

    final memory = NeuCover.fetched[url];
    if (memory != null) {
      setState(() => _image = memory);
      return;
    }

    final file = await _fileFor(url);
    if (file != null) {
      try {
        final stored = await file.readAsBytes();
        if (stored.isNotEmpty) {
          NeuCover.fetched[url] = stored;
          if (mounted) setState(() => _image = stored);
          return;
        }
      } catch (_) {
        // A half-written or locked cache file is treated as no cache at all.
      }
    }

    try {
      final request = await _client.getUrl(Uri.parse(url));
      request.headers.set(HttpHeaders.userAgentHeader, NeuCover.userAgent);
      request.headers.set(HttpHeaders.acceptHeader,
          'image/avif,image/webp,image/*,*/*;q=0.8');
      final response =
          await request.close().timeout(const Duration(seconds: 20));
      if (response.statusCode != HttpStatus.ok) {
        throw HttpException('$url -> ${response.statusCode}');
      }

      final type = response.headers.contentType?.primaryType;
      final builder = BytesBuilder(copy: false);
      await for (final chunk in response) {
        builder.add(chunk);
      }
      final bytes = builder.takeBytes();
      if (bytes.length > _maxBytes ||
          (type != null && type.isNotEmpty && type != 'image')) {
        throw HttpException('$url -> not a cacheable image ($type)');
      }

      NeuCover.fetched[url] = bytes;
      if (file != null) {
        try {
          await file.create(recursive: true);
          await file.writeAsBytes(bytes, flush: true);
        } catch (_) {
          // Cover art is disposable: a full or read-only cache folder only costs the next fetch.
        }
      }

      if (mounted) setState(() => _image = bytes);
    } catch (_) {
      // A dead cover keeps the placeholder glyph, as it always has.
    }
  }

  /// `null` when this machine has no writable cache directory, which leaves the old
  /// fetch-every-launch behaviour as the fallback rather than failing the thumbnail.
  static Future<File?> _fileFor(String url) async {
    final directory = await _cacheDirectory();
    if (directory == null) return null;
    return File('$directory\\${_digest(url)}.${_extension(url)}');
  }

  static Future<String?> _cacheDirectory() async {
    final found = _directory;
    if (found != null) return found == _noCache ? null : found;

    // The sidecar keeps its data under the same folder, so the covers land beside the
    // list caches and the sync bundle picks nothing extra up.
    final local = Platform.environment['LOCALAPPDATA'];
    if (local == null || local.isEmpty) {
      _directory = _noCache;
      return null;
    }

    final path = '$local\\Akashic\\cache\\covers';
    try {
      await Directory(path).create(recursive: true);
      _directory = path;
      return path;
    } catch (_) {
      _directory = _noCache;
      return null;
    }
  }

  /// FNV-1a over the URL. Only ever a cache file name, so it needs no more than that —
  /// and on the Dart VM the multiply already wraps at 64 bits, which is what FNV wants.
  /// The top bit is dropped because Dart prints a negative int with a leading minus.
  static String _digest(String value) {
    var hash = 0xcbf29ce484222325;
    const prime = 0x100000001b3;
    for (final unit in value.codeUnits) {
      hash ^= unit;
      hash = hash * prime;
    }
    return (hash & 0x7FFFFFFFFFFFFFFF).toRadixString(16).padLeft(16, '0');
  }

  static String _extension(String url) {
    final name = Uri.parse(url).pathSegments.isEmpty ? '' : Uri.parse(url).pathSegments.last;
    final dot = name.lastIndexOf('.');
    if (dot <= 0 || dot == name.length - 1) return 'img';
    final suffix = name.substring(dot + 1).toLowerCase();
    const allowed = {'jpg', 'jpeg', 'png', 'webp', 'gif', 'avif', 'bmp'};
    return allowed.contains(suffix) ? suffix : 'img';
  }

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    final image = _image;

    return Container(
      width: widget.width,
      height: widget.height,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(6),
        color: Colors.black.withValues(alpha: 0.06),
        border: Border.all(color: Colors.white.withValues(alpha: 0.4)),
      ),
      clipBehavior: Clip.antiAlias,
      child: image == null
          ? Icon(Icons.image_outlined, size: 18, color: t.inkTertiary)
          : Image.memory(image, fit: BoxFit.cover, gaplessPlayback: true),
    );
  }
}

/// Round check mark used by the batch bars and the import checklist.
class NeuCheckbox extends StatelessWidget {
  const NeuCheckbox({super.key, required this.value, required this.onTap, this.size = 22});

  final bool value;
  final VoidCallback onTap;
  final double size;

  @override
  Widget build(BuildContext context) {
    return MouseRegion(
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Container(
          width: size,
          height: size,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: value ? AppColors.greenBg : Colors.black.withValues(alpha: 0.05),
            shape: BoxShape.circle,
            border: Border.all(color: Colors.white.withValues(alpha: 0.4)),
            boxShadow: neuRaised(depth: 2.5, dark: const Color(0x1E000000)),
          ),
          child: value
              ? Icon(Icons.check_rounded, size: 14, color: AppColors.greenFg)
              : null,
        ),
      ),
    );
  }
}

/// One column of the flex table used by 资源 / 包浏览器 / Booth 列表.
class NeuCell extends StatelessWidget {
  const NeuCell(this.text, {super.key, this.head = false, this.flex = 3});

  final String text;
  final bool head;
  final int flex;

  @override
  Widget build(BuildContext context) {
    final t = NeuTheme.of(context);
    return Expanded(
      flex: flex,
      child: Text(
        text,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(
          fontSize: head ? 11.5 : 13,
          fontWeight: head ? FontWeight.w600 : FontWeight.w500,
          color: head ? t.inkTertiary : t.ink,
        ),
      ),
    );
  }
}
