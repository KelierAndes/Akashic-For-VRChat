# 开发文档

面向想读源码、改界面或自己出包的人。用户视角的功能与安装说明在 [README.md](README.md)。

---

## 架构总览

三层，前端只画界面，一切读写文件与发网络请求的逻辑都在 C# 侧：

```text
app/  (Flutter, akashic_ui.exe)   ── 绘制、交互、WebView2 登录面板
        │  HTTP 127.0.0.1:<随机端口>，带 X-Akashic-Token
        ▼
src/Akashic.Host/  (侧车)          ── ASP.NET Core minimal API，按域拆端点，随父进程退出
        │  直接引用
        ▼
src/Akashic.Core/  (.NET 8 类库)   ── 工程扫描、清单解析、解包、抓取、加密、版本控制
```

WinUI 3 前端已于 v2.0.0 整体下线，仓库里不再有 `src/Akashic`；需要对照旧行为时 checkout tag `pre-flutter-refactor`。

### 目录结构

```text
.                                  # 仓库根目录（磁盘位置沿用旧名 VPMManager）
├── Directory.Build.props              # 统一 TFM、平台与 <Version>
├── Akashic.sln                        # 解决方案（Core + Host，均 Any CPU）
├── scripts/build-app.ps1              # 出包：先建侧车，再建 Flutter 并注入产物目录
├── scripts/stage-dist.ps1             # 把产物搬到 dist/Akashic 并重新打包 zip
├── tools/nuget.exe                    # 随仓库携带，供 WebView2 插件还原依赖
│
├── app/                               # 前端 (Flutter / Windows 桌面，包名 akashic_ui)
│   ├── assets/brand/                  # 应用图标（只留亮色版本，侧栏与关于页共用）
│   ├── lib/main.dart                  # 主题、字体回退（Segoe UI + 微软雅黑）与应用根
│   ├── lib/src/
│   │   ├── app_nav.dart               # 导航条目与顺序的唯一来源
│   │   ├── state/app_model.dart       # 全局状态：工程、会话、同步、模态槽位、视觉参数
│   │   ├── host/host_client.dart      # 侧车 HTTP 客户端
│   │   ├── host/host_models.dart      # 侧车 JSON 的 Dart 模型
│   │   ├── shell/                     # 无边框外壳：标题栏、图标栏、导航面板、页面托盘
│   │   ├── ui/                        # 新拟态控件库：NeuBox / NeuButton / GlassSheet / 弹窗表单
│   │   ├── browser/browser_panel.dart # 应用内浏览器（登录 Booth / Asset Store）
│   │   └── pages/                     # 工程、详情、包浏览器、资源、导入、模板、
│   │                                  #   Booth、Asset Store、VRChat、恢复/同步、设置、关于
│   └── windows/runner/                # Win32 外壳：无边框窗口、DWM 边框、图标资源
│
└── src/
    ├── Akashic.Host/
    │   ├── Program.cs                 # 监听回环随机端口，WatchParent，注册 Core 的 DI
    │   ├── LoopbackGuard.cs           # X-Akashic-Token 校验
    │   ├── EventHub.cs                # 长任务进度推送
    │   └── Api/                       # 按域拆分：工程、包、资源、账号、会话、模板、
    │                                  #   备份/版本控制、GitHub 同步、启动状态
    └── Akashic.Core/
        ├── Models/                    # 工程、包、清单、三站内容、模板、备份、
        │                              #   版本控制、同步信封、设置等数据模型
        └── Services/
            ├── ProjectService.cs                        # 工程扫描、清单解析、新建与目录检测
            ├── VpmRegistryService.cs / PackageInstaller.cs  # 多源注册表、依赖解析与安装
            ├── UnityPackageExtractor.cs                 # .unitypackage 流式解包
            ├── UnityPackageLibraryService.cs            # 本地库扫描与导入台账
            ├── UnityPackageIconService.cs               # 包内 preview.png 提取
            ├── BoothService.cs / AssetStoreService.cs   # 两站会话复用、清单抓取与下载
            ├── VRChatSyncService.cs                     # 云端内容读取与编辑回写
            ├── VRChatBlueprintScanService.cs            # 本地工程蓝图 ID 扫描
            ├── ExternalPackageService.cs                # 本地目录与 GitHub / GitLab 归档包
            ├── BuiltinTemplates.cs                      # 官方 VCC 模板发现与版本区间选择
            ├── DpapiSessionVault.cs                     # DPAPI 加密的登录会话存储
            ├── VersionControlService.cs                 # git / UVCS 状态与操作
            ├── ConfigSyncService.cs                     # 配置打包、派生密钥与 AES-GCM 信封
            ├── GitHubCli.cs / GitHubLoopbackAuth.cs     # gh CLI 封装与 Loopback+PKCE 登录
            ├── GitHubOAuthApp.cs                        # 内置加密的 OAuth App 凭据
            └── RailLayoutService.cs / ThemeService.cs   # 图标栏布局与视觉参数
```

---

## 构建

### 环境要求
* Windows 10 (1809 / 17763) 及以上，x64
* Flutter SDK（Windows 桌面支持）与随附的 Dart
* Visual Studio 2022 的「使用 C++ 的桌面开发」工作负载
* .NET 8.0 SDK
* NuGet 命令行——`tools/nuget.exe` 已随仓库携带，`build-app.ps1` 会把 `tools/` 加进 `PATH`

### 出包

```powershell
cd <repo>
powershell -ExecutionPolicy Bypass -File scripts\build-app.ps1
.\app\build\windows\x64\runner\Release\akashic_ui.exe
```

* 只改前端时加 `-SkipHost` 跳过 C# 构建。
* 脚本会先删掉 tree-shaken 的 `MaterialIcons-Regular.otf` 再构建：Flutter 的增量构建沿用旧字形表，按钮图标会变空白。**改过 `Icons.*` 之后不要用裸 `flutter build`。**
* 后端可单独编译：`dotnet build Akashic.sln -c Release`。
* 检查：`flutter analyze`、`flutter test`（`app/` 下），C# 侧以 `dotnet build` 为唯一 oracle。

### 打包与发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-app.ps1
powershell -ExecutionPolicy Bypass -File scripts\stage-dist.ps1   # → dist\Akashic\ 与 dist\Akashic.zip
```

`stage-dist.ps1` 会把整个 Release 目录搬到 `dist/Akashic`，**排除 `*.WebView2`**（那是运行时的 WebView2 用户数据目录，含登录态与缓存，不是构建产物），再按原来的 `Akashic/` 顶层结构压成 `dist/Akashic.zip`。`dist/` 在 `.gitignore` 里。

发布流程：

```powershell
git tag -a v<版本> -m "Akashic for VRChat v<版本>"; git push origin v<版本>
gh release create v<版本> dist/Akashic.zip --title "Akashic for VRChat v<版本>" --notes-file <notes.md>
```

### 版本号

只有一个来源需要人工维护：`Directory.Build.props` 的 `<Version>`。侧车的 informational version 会带上 git sha，「关于」页显示的就是它；`app/pubspec.yaml` 的 `version:` 决定 exe 的文件版本，两者要保持一致。

---

## 实现要点与已知坑

**界面**
* 新拟态的凸凹靠双向阴影 + 内阴影描边，`app/lib/src/ui/{app_colors,theme,neumorphic}.dart` 是从参考工程逐字复制的设计原语，改样式前先确认该改的是不是参考工程；页面级配方在 `neu_kit.dart`。
* 无边框 + 真透明需要 runner 里的 DWM sheet-of-glass；`RotatedBox` 而不是 `Transform.rotate`，否则竖排文字会折行。**不要动最底层的透明画布**（窗口合成/背板），那会连带影响边缘。
* 「页面全局不透明度」滑条读数就是面板 alpha，范围 0.4–1、默认 0.7。它同时作用于 `GlassSheet`、`LeftRail._baseStrip` 与 `NeuModalScaffold` 三处，且这三处**只能有一个 alpha 来源**——`NeuTokens.sheetA/B/C` 必须是不透明色，否则令牌 alpha 会再乘一遍，拉到 1 也永远不实心。Flutter 的 `BackdropFilter` 模糊不到透明窗口后面的桌面，所以没有、也不应再有「模糊强度」控件。
* 表单不用路由弹层：在这扇每像素透明的无边框窗口上，push 出来的路由收不到指针事件。壳层在页面托盘里留了一个模态槽位（`AppModel.showModal`）。
* 移植页面时先通读旧 ViewModel，展示层的 `Where` / 关键词过滤在迁移中丢过三次。

**侧车与安全边界**
* 只绑 `IPAddress.Loopback`，随机端口，每个请求带 `X-Akashic-Token`；`WatchParent` 让侧车随前端退出。
* 会话 Cookie 只进各站点自己的私有 `HttpClient`，绝不进共享的 DI `HttpClient`。
* 会话按每条 Cookie 的域名与路径完整保存并各归其域回写：Booth 在 `booth.pm` 与 `accounts.booth.pm` 上发同名不同值的 Cookie，Asset Store 的会话令牌在 `.unity.com`，扁平化成 name→value 会让它们互相覆盖或种错域名，表现为重启后掉线。
* 三站登录态以 DPAPI（CurrentUser）加密落盘。Booth 会发一次只读响应头的请求向站点确认会话是否仍有效，问不到结果时保留「已恢复」措辞而不误判为登出。

**GitHub 与配置同步**
* 登录走 Loopback + PKCE：随机空闲端口起 `HttpListener`，回调路径 `/callback/`。`gh` 会拒绝缺少 `repo` 与 `read:org` 的令牌，所以这三个 scope 都要申请。
* OAuth App 凭据以 PBKDF2 + AES-GCM 加密后内置在 `GitHubOAuthApp.cs`，可用 `AKASHIC_GITHUB_CLIENT_ID` / `_SECRET` 覆盖。**这是混淆不是机密**：pepper 与密文同仓，源码公开后任何人都能解出 client_secret；桌面端的 OAuth App secret 本身也守不住。
* 同步密钥由 GitHub 的永久数字 `user.id` 与内置 pepper 派生（PBKDF2 310k + AES-256-GCM），不需要用户口令；同样地，源码公开后这层加密只对不知道 pepper 的人有效。
* gist 的 id 与地址只在内部使用，不出现在界面、按钮、日志或端点响应里。
* Asset Store 的读取只有页面抓取一条路：它的 GraphQL 接口对复用 Cookie 的请求永远返回拒绝，别再试。

**版本控制**
* UVCS 状态离线读 `.plastic`，`cm status` 报错通常是环境问题不是 bug。
* LFS 只在 git 托管时出现，UVCS 工程用原生同步，不给「启用 LFS」按钮。

**写盘类自检**
* 只碰 `C:\temp` 下的假工程，事后恢复 `settings.json`，绝不动真实工程。改设置类控件前先把 `%LOCALAPPDATA%\Akashic\settings.json` 备份一份。

---

## 回退点与备份

* tag `pre-flutter-refactor` → 迁移前的 WinUI 版本。
* `E:\Akashic\backups\`：git bundle、迁移前的工作树 tar.gz、v1.1.0 发布 zip。
* 仓库根目录、`dist\VPMManager`、`%LOCALAPPDATA%\VPMManager` 是改名前故意保留的，不要顺手删。
