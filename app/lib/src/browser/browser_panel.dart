import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_inappwebview/flutter_inappwebview.dart';

import '../host/host_models.dart';

class BrowserSession {
  BrowserSession._();

  static final CookieManager _jar = CookieManager();

  static Future<void> seed(List<CookieSpec> cookies) async {
    for (final cookie in cookies) {
      if (cookie.name.isEmpty || cookie.domain.isEmpty) continue;

      await _jar.setCookie(
        url: WebUri('https://${_hostOf(cookie.domain)}/'),
        name: cookie.name,
        value: cookie.value,
        domain: cookie.domain,
        path: cookie.path,
        isSecure: true,
      );
    }
  }

  static Future<List<CookieSpec>> harvest(List<String> origins) async {
    final seen = <String>{};
    final cookies = <CookieSpec>[];

    for (final origin in origins) {
      for (final cookie in await _jar.getCookies(url: WebUri(origin))) {
        final domain = cookie.domain ?? '';
        final path = cookie.path ?? '/';
        final key = '${cookie.name}\n$domain\n$path';
        if (domain.isEmpty || !seen.add(key)) continue;

        cookies.add(CookieSpec(
            name: cookie.name, value: cookie.value, domain: domain, path: path));
      }
    }

    return cookies;
  }

  static String _hostOf(String domain) =>
      domain.startsWith('.') ? domain.substring(1) : domain;
}

class BrowserPanel extends StatefulWidget {
  const BrowserPanel({
    super.key,
    required this.initialUrl,
    this.onController,
    this.onUrlChanged,
    this.minZoom = 0.6,
  });

  final String initialUrl;
  final void Function(InAppWebViewController controller)? onController;
  final void Function(String url)? onUrlChanged;

  final double minZoom;

  @override
  State<BrowserPanel> createState() => BrowserPanelState();
}

class BrowserPanelState extends State<BrowserPanel> {
  InAppWebViewController? _controller;
  Size? _lastSize;

  Future<void> fitToWidth() async {
    final controller = _controller;
    if (controller == null) return;

    final raw = await controller.evaluateJavascript(
        source: 'JSON.stringify([Math.max(document.documentElement.scrollWidth,'
            'document.body ? document.body.scrollWidth : 0),'
            'window.innerWidth])');
    if (raw == null) return;

    List<dynamic> metrics;
    try {
      metrics = jsonDecode(raw is String ? raw : raw.toString()) as List<dynamic>;
    } catch (_) {
      return;
    }
    if (metrics.length < 2) return;

    final scroll = (metrics[0] as num).toDouble();
    final client = (metrics[1] as num).toDouble();
    if (scroll <= 0 || client <= 0 || scroll <= client + 2) return;

    final zoom = (client / scroll).clamp(widget.minZoom, 1.0);
    await controller.evaluateJavascript(
        source: 'document.documentElement.style.zoom="$zoom";');
  }

  Future<void> resetZoom() async {
    final controller = _controller;
    if (controller == null) return;
    await controller.evaluateJavascript(
        source: 'document.documentElement.style.zoom="1";');
  }

  Future<void> navigate(String url) async {
    final controller = _controller;
    if (controller == null) return;
    await controller.loadUrl(urlRequest: URLRequest(url: WebUri(url)));
  }

  InAppWebViewController? get controller => _controller;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(builder: (context, constraints) {
      final size = Size(constraints.maxWidth, constraints.maxHeight);
      if (_lastSize != null && _lastSize != size && _lastSize!.width != size.width) {
        _lastSize = size;
        WidgetsBinding.instance.addPostFrameCallback((_) => fitToWidth());
      } else {
        _lastSize = size;
      }

      return InAppWebView(
        initialUrlRequest: URLRequest(url: WebUri(widget.initialUrl)),
        initialSettings: InAppWebViewSettings(
          javaScriptEnabled: true,
          clearCache: false,
        ),
        onWebViewCreated: (controller) {
          _controller = controller;
          widget.onController?.call(controller);
        },
        onProgressChanged: (controller, progress) {
          if (progress > 0 && progress < 60) resetZoom();
        },
        onLoadStop: (controller, url) {
          widget.onUrlChanged?.call(url?.toString() ?? '');
          fitToWidth();
          Timer(const Duration(milliseconds: 1200), fitToWidth);
        },
      );
    });
  }
}
