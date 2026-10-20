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
            ├── UnityPackageLibraryService.cs            # 本地库扫描（含 zip 中央目录）与导入台账
            ├── UnityPackageIconService.cs               # 包内 preview.png 提取
            ├── ResourceCatalogService.cs                # 资源目录、Booth 匹配与元数据缓存
            ├── PackageCategory.cs                       # 分类词表、Booth 标签映射与匹配打分
            ├── BoothHtml.cs                             # Booth 搜索卡片 / 商品页 HTML 解析
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
powershell -ExecutionPolicy Bypass -File scripts\sign-release.ps1 dist\Akashic.zip
gh release create v<版本> dist\Akashic.zip dist\Akashic.zip.sha256 dist\Akashic.zip.sig --title "Akashic for VRChat v<版本>" --notes-file <notes.md>
```

`sign-release.ps1` 用 openssl 对 zip 做 SHA256 + ECDSA P-256 签名，产出 `Akashic.zip.sha256` 与 `Akashic.zip.sig`；**三个文件都要作为 Release 资源上传**。私钥在 `E:\Akashic\backups\Akashic-UpdateSigning\update-signing-key.pem`（仓库外，绝不能提交），公钥已内置在 `UpdateService.PublicKey`。换密钥意味着老版本无法验证新包，只能靠用户手动下载覆盖。

### 自动更新

* 「关于」页打开时自动检查：匿名请求 GitHub 的 `releases/latest`，把 tag 的主版本号与侧车 informational version 比较；结果在侧车里缓存 10 分钟（失败缓存 2 分钟），手动「检查更新」会强制刷新。
* 发布侧**必须给 Release 附一个 `.zip` 资源**（就是 `stage-dist.ps1` 的 `Akashic.zip`），否则界面会提示「没有附带 zip 资源」。压缩包内层 `Akashic/` 或直接平铺都能识别，只要里面找得到 `akashic_ui.exe`。
* 下载与解压都在侧车里完成，落在 `%LOCALAPPDATA%\Akashic\updates\<版本>\`；只有确认 staged 目录里有 `akashic_ui.exe` 才标记「已就绪」。
* 「重启并安装」会生成一个 `apply.ps1`：等 UI 与侧车的 pid 都退出、再等 2 秒，把 staged 覆盖到安装目录并重新启动。所以安装目录必须可写——解压到用户目录即可，装在 `Program Files` 下会失败。
* 更新只替换程序文件，绝不碰 `%LOCALAPPDATA%` 里的设置与缓存。
* 校验：下载后先比对 Release 里的 `.sha256`，再用内置 ECDSA 公钥验 `.sig`（openssl 的 DER 签名，校验时必须指定 `DSASignatureFormat.Rfc3279DerSequence`，默认的 P1363 格式对不上）。缺任一文件直接拒绝安装——**新版本必须签名，否则更新链会停在旧版**。
* 网络按这个顺序试：**直连（含系统代理）→ 镜像站 → 本机代理端口**。镜像把原 URL 直接拼在站点后面（`https://gh-proxy.com/https://api.github.com/...`），对发布信息与 release 资源都生效（实测 gh-proxy.com 两者都通）；镜像按 `gh-proxy.com / ghfast.top / ghproxy.net / gh.llkk.cc` 依次尝试。代理只在端口监听时才试，覆盖 `7890 / 7897 / 10809 / 1080（http 与 socks5）/ 10808 / 1087 / 8889`。选中的路线在进程内记住，「关于」页显示「直连 / 镜像 xxx / 代理 xxx」。
* 有更新时的提示：启动后第一次拿到检查结果就弹一次模态（同一进程只弹一次，「查看更新」跳到关于页）；侧栏底部设置齿轮与导航「设置」同时显示绿点。检查结果放在 `AppModel.updateInfo` 里，关于页与角标共用，不再各查各的。

### 版本号

只有一个来源需要人工维护：`Directory.Build.props` 的 `<Version>`。侧车的 informational version 会带上 git sha，「关于」页显示的就是它；`app/pubspec.yaml` 的 `version:` 决定 exe 的文件版本，两者要保持一致。

---

## 实现要点与已知坑

从这一节起，代码里不再携带注释。这一节收的是**跨模块的规矩**——一处看懂、多处适用；某个具体方法为什么这么写，在 [`docs/source-reference.md`](docs/source-reference.md) 里按文件按声明归档，那是剥离前注释的完整副本。

### 界面

* 新拟态的凸凹靠双向阴影 + 内阴影描边，`app/lib/src/ui/{app_colors,theme,neumorphic}.dart` 是从参考工程逐字复制的设计原语，改样式前先确认该改的是不是参考工程；页面级配方在 `neu_kit.dart`。
* 无边框 + 真透明需要 runner 里的 DWM sheet-of-glass；`RotatedBox` 而不是 `Transform.rotate`，否则竖排文字会折行。**不要动最底层的透明画布**（窗口合成/背板），那会连带影响边缘。
* 「页面全局不透明度」滑条读数就是面板 alpha，范围 0.4–1、默认 0.7。它同时作用于 `GlassSheet`、`LeftRail._baseStrip` 与 `NeuModalScaffold` 三处，且这三处**只能有一个 alpha 来源**——`NeuTokens.sheetA/B/C` 必须是不透明色，否则令牌 alpha 会再乘一遍，拉到 1 也永远不实心。Flutter 的 `BackdropFilter` 模糊不到透明窗口后面的桌面，所以没有、也不应再有「模糊强度」控件。
* 表单不用路由弹层：在这扇每像素透明的无边框窗口上，push 出来的路由收不到指针事件。壳层在页面托盘里留了一个模态槽位（`AppModel.showModal`）。
* 移植页面时先通读旧 ViewModel，展示层的 `Where` / 关键词过滤在迁移中丢过三次。
* **字体**：`Segoe UI` 没有中文字形，必须显式列 YaHei 回退，否则中文掉进 Skia 的通用替代字形，整个界面看着又细又歪。另外几乎所有标签只设字号不设字重，所以 `textTheme` 的基线必须是 w500——那是 11–13 px 中文不发虚的位置，想要更重的地方自己会要 w600/w700。
* 需要「滚动到某张卡片」的列表用 `SingleChildScrollView` 而不是 `ListView`：懒构建的列表还没创建那张卡，滚动请求会找不到目标。
* 弹层菜单要自己量剩余空间并在下方不够时翻到上方——25 条的工程列表曾经直接冲出窗口下沿且无法滚动。
* 模态槽位复用同一个 widget 实例，所以子组件里要经 `AppScope.of(context)` 现取模型，存成字段会把列表冻结在打开那一刻的值上。

### 侧车与进程边界

* 只绑 `IPAddress.Loopback`，随机端口，每个请求带 `X-Akashic-Token`；`WatchParent` 让侧车随前端退出。`ListenLocalhost(0)` 不接受动态端口，要直接点名回环地址。
* **侧车的 stdout 是握手协议**：前端 spawn 它之后只读一行（绑定地址 + 运行令牌）。任何多余输出都会破坏这次握手，调试信息一律走别处。
* 长任务的超时单独给：解包几 GB 的备份、三万文件的首次提交、`gh` 等网络、以及等人在浏览器里点授权，都远超常规读超时。
* `dart:io` 的 GET 不允许带 body（会撞 content-length 断言），只有真要发东西的方法才写 body。
* `/shell/open` 故意做窄：链接只收 http(s)，目标只收已存在的目录。放行调用者任意路径等于给出一个以当前用户执行任意程序的后门。

### 共享 `settings.json` 的写入纪律

一个文件被七八个服务共同拥有，每个只负责其中几个字段。因此：

* **写之前必须重新 `LoadSettings()`**，只改自己那部分再整体写回。凭内存里的旧副本写回，会清掉别的服务刚改的东西——这条在 `AssetStoreService`、`BoothService`、`ConfigSyncService`、`ExternalPackageService`、`ProjectService`、`ThemeService`、`UnityPackageLibraryService` 里各有一份。
* 数值在**入口和出口都钳位**：旧版本或手改过的文件不该把面板 alpha 推出滑条能表达的范围；不钳位的 NaN 会让窗口下次启动时不可读。
* 写设置失败宁可拒绝：不要落一个会让所有服务下次读崩的文件。

### 外部 CLI 的调用约定（git / gh / cm）

* 输出编码：`git` 和 `gh` 恒为 UTF-8，`cm`（UVCS）写控制台代码页，必须保留系统默认，否则中文分支名乱码。
* **禁掉一切交互**：`GH_PAGER=cat`、`GIT_TERMINAL_PROMPT=0`。一个等按键的子进程会挂在超时上，看起来像网络故障。
* **先抽干管道再写 stdin**：给 `gh` 喂令牌前必须开始读 stdout，没人读的管道会让 `gh` 永远不退出。
* 探测结果只缓存成功的那次：被取消的、报「未登录」的不能 memoise，否则装完 `gh` 或跑完 `gh auth login` 不重启就永远不生效；同理，刚装的二进制不在本进程 PATH 上，要清掉缓存重探一次。
* 发 JSON 用 UTF-8 **字节**，不要用 `Encoding.UTF8` 写字符串——那会带 BOM，GitHub 回 "Problems parsing JSON"。
* `--jq .id` 这类投影参数不是可选的优化：不加 `gh` 会把整个 gist 原样回显，也就是把整个加密包吐进日志管道，文本采集会在中途截断，而 gist 其实已经建好了。
* 记住 jq 的备选运算符写作 `//`，所以 `--jq` 表达式里满是看起来像注释的斜杠；`Accept: */*` 这类头同理。改这些字符串时不要用正则去找注释。

### 会话 Cookie 与三站网络行为

* 会话 Cookie 只进各站点自己的私有 `HttpClient`，绝不进 DI 容器里那个共享客户端——注册表服务也用它，站点会话头绝不能跟着那些请求出去。
* 会话按每条 Cookie 的域名与路径完整保存并各归其域回写：Booth 在 `booth.pm` 与 `accounts.booth.pm` 上发同名不同值的 Cookie，Asset Store 的会话令牌在 `.unity.com`，扁平化成 name→value 会让它们互相覆盖或种错域名，表现为重启后掉线。
* **空的 Cookie 导出绝不覆盖现有会话**：空集合意味着浏览器那边啥也没交出来（profile 被重置、页面从没设过 Cookie），不是「用户登出了」。登出要显式做。
* 三站登录态以 DPAPI（CurrentUser）加密落盘。entropy 不是密钥，只是防同用户进程乱接；写盘走旁路临时文件，崩在半路不会把好端端的 blob 换成半截；解不开（换了 Windows 账户、或文件被动过）就当没有会话。
* Booth：非浏览器 UA 直接 403，所以用导出 Cookie 发的请求必须长得像产生它的那个 WebView2；会话值每次调用都在轮换，任何刷新回来的 Cookie 都要留下一次用；**会话悄悄过期时 Booth 回的是 200 + 一个登录页**，不是 401；下载链接经一串重定向发出，签名链接本身不能带 Cookie 取，所以重定向手动一跳跳（上限 5），跳到 booth.pm 之外就断掉。Rails 对匿名访客也发会话 Cookie，所以「有 Cookie」不等于「已登录」。
* Asset Store 的读取只有页面抓取一条路：它的 GraphQL 接口对复用 Cookie 的请求永远返回拒绝，别再试。缓存目录布局是 `publisher/category/name.unitypackage`，浅于这一层的文件是用户自己丢进去的，没有发布者。
* Booth 的「读取清单」是三步：**已购 → 礼物 → 免费**。免费列表页的卡片没有下载控件，必须逐个打开商品页找 `/downloadables/`；打不开或找不到下载链接的计入「无下载文件」。演示视频/图集**不在抓取时按关键词剔除**——关键词判断会误伤正常包，它们照常入库，靠界面「类型」筛选（默认「可用软件包」）隐藏。只有全部读完才用本次结果覆盖缓存（旧版本仍然折叠）。
* 打开内嵌浏览器后 `WebView2` 控制器要等它就绪：`_browser.currentState?.controller` 一开始是 null，这时候调 `evaluateJavascript` 会拿到 null，表现为「页面超时」。提取前先轮询等待控制器，和 Asset Store 补封面的写法一致。
* `WebView2.ExecuteScriptAsync` 的返回值是 JSON 再编码一层，字符串会双重包裹；页面读取器也可能返回数组（我的资产每一页一项）。
* 封面 URL 可能是协议相对的（`//cdn…/x.png`），补 scheme 再用。店铺从没给过封面的，从 `.unitypackage` 里抽一张当本地路径交出去——它本来就在盘上，不需要再套一层缓存。封面是可丢弃的：缓存文件写坏/被锁就当没有，目录满了只损失下一次抓取。
* 封面名称→URL 那份缓存**写和读必须一起核对**：`JsonSerializer.Serialize(dict.ToArray())` 落盘是 `[{"Key":…,"Value":…}]`，而读取端按 `Dictionary` 解，必然抛 `JsonException` 并被「文件坏了就当没有」的 catch 吞掉——写得越勤，读回来越是空的，症状就是每次重启都要重新搜一遍封面。直接序列化字典本体才是对象。
* 店铺搜索匹配不上的本地包名（缓存目录里的文件夹名比商店标题长，比如 `Starter Assets - ThirdPerson Updates in new CharacterController package`）永远搜不到，于是每次打开页面都要重跑一轮抓取。`AppModel.coverAttempts` 记住本会话试过的名字，页面来回切换不再重复；跨重启的负缓存故意没做——搜不到只是白跑一轮，做成永久标记会让「商店哪天又能搜到」变成需要手动清理的状态。

### GitHub 与配置同步

* 登录走 Loopback + PKCE：随机空闲端口起 `HttpListener`，回调路径 `/callback/`。`gh` 会拒绝缺少 `repo` 与 `read:org` 的令牌，所以这三个 scope 都要申请。
* OAuth App 凭据以 PBKDF2 + AES-GCM 加密后内置在 `GitHubOAuthApp.cs`，可用 `AKASHIC_GITHUB_CLIENT_ID` / `_SECRET` 覆盖。**这是混淆不是机密**：pepper 与密文同仓，源码公开后任何人都能解出 client_secret；桌面端的 OAuth App secret 本身也守不住。
* 同步密钥由 GitHub 的永久数字 `user.id` 与内置 pepper 派生（PBKDF2 310k + AES-256-GCM），不需要用户口令；同样地，源码公开后这层加密只对不知道 pepper 的人有效。派生用的输入要**在抹掉密钥之前**取，它是让还原能怪到「身份不对」而不是「文件损坏」的唯一线索。
* gist 的 id 与地址只在内部使用，不出现在界面、按钮、日志或端点响应里。还原不需要参数：侧车认得自己的记录，也能找到账号的备份。
* 清单文件名只写名字。信封里除 `payload` 之外都是明文，把 files 整个克隆过去等于把明文（Cookie 在内）和该藏它的密文并排寄到 GitHub。
* 首次推送时包里那份 `settings.json` 还是「上传前」的样子，推完要把 gist id 与时间戳补回去，否则刚还原的机器不知道自己的备份在哪。这类记账失败绝不能把一次成功的还原报成失败。
* 存的 id 指向一个已被删掉、或属于别的账号的 gist 时，新建一个比抛一个用户无法处理的 404 好。
* 随包同步的清单缓存集中在 `ConfigSyncService.CacheFiles`：Booth 已购、Asset Store 资产与封面、VRChat 内容、资源分类与匹配、unitypackage 安装台账。**新增一份列表缓存时要同时加进这张表**，否则它只会待在本机，跨设备看起来像「没同步」。`settings.json` 整份进出（工程列表、路径、模板、快捷栏、主题、透明度都在里面）。
* 拉取后会就地重载主题/透明度与快捷栏（`ThemeService.Reload` / `RailLayoutService.Reload`），前端随后刷新 state 与列表缓存，所以配色、透明度和侧栏位置不用重启就能生效；工程列表仍要重启侧车才重读。
* 自动上传由侧车的 `AutoSync` 收敛：启动（已登录 GitHub 时）、三站登录态导入、Booth / Asset Store / VRChat 清单保存、手动匹配结束、离开设置页（前端打 `/sync/touch`）都只调用 `Queue(reason)`，10 秒防抖后合并成一次推送；正在推送时再来请求会等这次结束后补一次。同步开关关掉或 gh 未登录时 `PushAsync` 直接返回失败，不会弹错。手动「上传到 GitHub」仍立即执行、不走防抖。

### VRChat 数据接口

* `releaseStatus` 服务端默认 `public`，不带 `all` 就看不见任何私有草稿。
* **PUT 会重置请求里没提交的字段**，所以编辑必须先按 id 取回完整记录再改，不能拿列表行当底。
* 列表端点报的 capacity 与记录端点不是同一尺度，而且完全不返回描述，所以每个世界要再走一遍自己的记录。
* 发布是独立端点，排在元数据写入之后。
* 封面上传返回的是 File 记录，它的 id 才是 `imageUrl` 要的东西。
* 死会话回 401，客户端已折成 null；缺行是一个答案不是失败，界面显示「内容已不存在」。
* 蓝图扫描读 `*.unity` / `*.prefab` / `*.asset`（描述符在场景和预制体里都走），**绝不进 `Packages`**——SDK 自带的测试场景里装着别人的 id。单文件上限 128 MB、单工程最多 16 个 id，超出的那是资产集散地，多出来的只会给匹配列表添噪。两个工程持有同一内容说明有一个是副本，先注册的赢，不猜。
* id→本地工程 的关联表是纯磁盘扫描，和登录态、云端可用性无关，所以 `vrchat_page._loadCached` 里它排在 `vrchatSelf` **之前**。原来排在后面，探针一超时整页关联就消失——列表照常渲染，只是每条的「本地：xxx」全没了，看起来像关联丢了而不是网络坏了。`AppModel` 因此留着上一次成功取到的表：刷新失败不该把已有的关联抹成空。
* 冷启动第一次取关联表要把 25 个工程的 Assets 全扫一遍，实测 16 秒；之后 `matchCloud` 已经把这些文件的指纹喂进扫描缓存，同一进程里再取只要 3–4 秒。侧车进程一重启这层缓存就没了，别指望它跨启动。

### 版本控制

* UVCS 状态离线读 `.plastic`，`cm status` 报错通常是环境问题不是 bug。
* LFS 只在 git 托管时出现，UVCS 工程用原生同步，不给「启用 LFS」按钮。
* 判断 LFS 开没开，读 `.gitconfig` 里有没有 `[filter "lfs"]` 段就够；替代方案是每个工程 spawn 一次 `git config` 只为列个表。
* `git-lfs` 是 git 在自己旁边找到的独立二进制，所以对 git 唯一可移植的问法是「那个子命令答不答应」。
* 新仓库默认开 LFS（机器上有 `git-lfs` 的话）：Unity 工程几乎全是二进制美术资源，不开的话第一次推几个 GB 必挂。
* 关托管时只删本应用自己写的那个 `.gitattributes`——它存在的唯一作用是指向那两条 filter，留着会让 git 去找已经不存在的 clean/smudge 命令。
* `.git` 可能是个文件（worktree、submodule 指向真目录），要读出它指哪儿。有 `.git` 但读不出分支 = 从没提交过，这时候报「已托管」是撒谎，分支留空并说明原因。
* 老版本 git 没有 `init -b`，那种情况下改成先 init 再重命名分支。
* UVCS 工作区没有 git 远端：界面上那串是工作区拉取的仓库名，归 UVCS 自己管，标签要写「仓库」不是「远端」。
* 安装依赖会走 winget，可能弹 UAC，调用方必须等它。

### 包与工程扫描

* Unity 会在某些 `manifest.json` 里留 `//` 注释，JSON 解析必须 `JsonCommentHandling.Skip`。
* Unity 自带模块跟着编辑器版本走，不在包管理的讨论范围内，跳过。
* 内嵌包的 `package.json` 才带着友好名和真实版本；`file:` 引用把文件放在 `Packages/` 之外，所以它的元数据在目标旁边而不是 PackageCache 里。
* 每个工程都要注入包解析器（`com.unity.package-manager-ui` 那类）：VCC 给它创建的每个工程都加，尽管没有任何模板声明它——少了它用户没法在 Unity 里管包。
* 版本区间里的 `x` 是通配补丁号，不是数字，按 0 解析会错。
* 官方模板是一份完整骨架，按 Creator Companion 的方式整个复制；模板自己的 `package.json` 描述的是模板不是工程，故意不带过去。没有 VCC 时，内置替身写出能让 Unity 拿到自带模块的最小 manifest，SDK 包随后再装。
* Unity 编辑器路径要多路发现：Hub 注册表（键是版本号）、VCC 列出的编辑器、以及各盘常规的 Unity 目录往下走一两层。Hub 式布局还会再嵌一层 `<root>\Editor\<version>\Editor`。拷贝走的、或 Hub 忘了上报的编辑器也得能启动。
* 资源库删条目**从不删磁盘文件**：库只是一堆指向用户自己文件夹的指针。
* 重新排序时，被筛掉、没进视图的行不会到达回调，所以未知的 URL 要保留，不能当成「用户删了它」。
* 归属标签只加不减：从 Booth / Asset Store 重新添加一个文件夹会打标签，但绝不清除已有标签；放在 Booth 缓存目录里的文件即使早于「按文件记来源」就被导入，也按目录推断成 Booth 来的。
* Git 装的外部包在 `Packages/` 下嵌一份，本地文件夹只是引用。`Assets/<repo>` 覆盖 git 托管 UPM 包常见的布局；`git@host:owner/repo.git`、`/tree/<ref>`（GitHub、Gitee）与 `/-/tree/<ref>`（GitLab）这几种形状都要认；自建 GitLab 沿用 gitlab.com 的 URL 形状，所以走同一个 kind。

### 资源目录与 Booth 匹配

* 资源页的行由 `ResourceCatalogService` 合并四路来源：库扫描（含 zip）、Booth 已购缓存、Asset Store 账户与封面缓存、`cache/resource-meta.json`。元数据按**规范化名称**（去版本号、去符号、转小写）做键，不按路径——文件挪个位置不该丢掉已经配好的封面和分类。
* zip 只读中央目录，不预解压：一次扫描就能列出内层 `.unitypackage`，同包多个包在界面上折叠成一行。`<zip>-extracted` 里的散包在列表里折回压缩包，只有点「解包」才会真的落盘并登记入库；`ExtractArchiveAsync` 逐条校验路径没逃出目标目录。
* 版本号从文件名里剥：`Name_v1.2`、`Name [1.0.4]`、`Name_Ver1.06_`、`Name_1.1fix1` 都要认；`v`/`ver`/`version` 前缀可省略，剥完再 trim 一次分隔符，否则 `MiniSimplisticPens_1.1_` 这类会整串留下。
* Booth 搜索接口是 `https://booth.pm/ja/search/<query>`（查询词放路径里，`?q=` 会 404）。结果卡片自带 `data-product-id` / `data-product-brand` / `data-product-category`、标题锚点与 `data-original` 封面；商品页只有 `og:title` / `og:image` 可靠，**分类别从 item 页取**——那个页面到处是侧栏与推荐的 browse 链接，第一条往往不是本商品的分类。
* 匹配顺序：名称里的商品 id（`items/123` 或裸的 5 位以上数字）优先按 id 取商品页；否则按名称搜索打分。前缀命中要求长度比 ≥ 0.7，否则「Magica Cloth 2」会被「Magica Cloth 2 Helper」抢走；后缀命中（`【工具】EreMorph`）与精确命中直接给高分。标题先剥掉 `【】「」()` 与 ` / `、` | `、` - ` 之后的分句再比对，否则带副标题的正品会被判远。
* 本地包名与商品标题常常语言不同（`LuraVideoPlayer_SDK3_2019` → 「ワールド用音楽再生プレイヤーセット【SDK2／SDK3】」），词面永远对不上，所以**搜索只返回一个商品时直接采信它**，不再要求分数。这条不会误伤 Asset Store 的包：那些名字在 Booth 上要么搜不到，要么返回 7–35 张卡片（`TowelCloud2`、`VRM` 这类泛词甚至 60 张）。
* **R-18 商品的搜索要带年龄确认**：Booth 的「はい」按钮做的事就是写 `adult=t` 这个 Cookie（有效期 1 天），搜索 URL 还要带 `?adult=include`。只带其中一个都会返回 0 张卡片——页面标题里明明写着「2 件あります」，列表却被年龄确认挡住。搜索请求单独拼这个 Cookie，不写进持久化的会话。
* 包名里的系列缩写可能只出现在商品标题的 `#标签` 里（`DMCustom_PCS` 对应 Penetration Contact System 的 `#PCS`）。命中多个结果时，若**只有一个**结果的 `#标签` 包含包名分词，就采信它；不要退回普通分词匹配，否则「Magica Cloth 2」会被同名的「Magica Cloth 2 Helper」抢走。
* 匹配是**手动触发**的长任务：本地包多时每条要一次网络请求，所以放在后台跑、状态端点轮询进度；每命中一条立刻落盘，任务中途被打断也不会丢已有结果。命中的分类会回写 Booth 已购缓存，Booth 页和资源页的分类筛选才能离线用。
* 匹配默认**增量**：失败也写 `attemptedAt`，下一次只处理从没试过的包（界面上标「已尝试」）；要重试历史失败项走 `force`（资源页匹配按钮旁的圆形箭头）。不加这条的话，40 个永远搜不到的 Asset Store 包每次都要把网络请求挨个发一遍。
* 「恢复自动分类」是删掉这条元数据，而不是写一份空值——删掉后下一次取行会重新走「Booth 已购缓存 → Asset Store → 标题关键词」的自动推断。手动填的分类/标签/封面只算自定义信息，不会把包标成已匹配。
* 列表一类的重复读取在 `AppModel` 里缓存：注册表包、库文件、每个工程的导入台账、资源行各一份，页面打开直接用缓存渲染；「刷新」按钮才带 `force`，其中注册表刷新还会让侧车清掉内存缓存（`/api/packages?refresh=true`）。项目详情页的缩略图也从资源行的 `coverUrl` 里取，不再单独发一次请求。
* 没有匹配到 Booth / Asset Store 的包留在「用户软件包」的「未匹配」筛选里；用户可以自己填源链接、封面、分类与标签，这些只算自定义信息，**不算匹配**，不会把它挪出未匹配列表。
* 分类走 Booth 自己的标签优先（`3D衣装`→服饰、`3D環境・ワールド`→场景…），没有标签时按标题关键词猜；关键词表里「道具/工具」排在「场景」前面，否则「ワールド用ギミック」会被归进场景。
* 项目详情页的分类筛选读 `GET /api/resources/categories`（只返回元数据里的名称→分类，不触发库扫描），前端把行名按同一套规则规范化后查表。VPM 注册表包没有资源分类，显示「—」，筛「未分类」时归到那一侧。

### 备份与还原

* **还原只写 `Assets` / `Packages` / `ProjectSettings`，其他内容一律不删**——这是产品对用户的承诺，不是实现细节。
* 备份文件名认两种：本应用的 `<name>_<yyyyMMdd_HHmmss>` 与 Unity VCC 的 `<name>-<yyyy-MM-ddTHH-mm-ss>`。名字不认识的 zip 依然可还原，只是日期和归属要从文件系统取。
* 剔除软件包是体积的大头：留下的 manifest 说明工程要哪些包，解析器能把包本身再抓回来。
* 目录条目 `Name` 为空，它们靠下面的文件自然成形。删除按最深路径优先，轮到某层时它已经空了。
* 只读或被锁的文件只挡住那一个条目，不让整次还原失败。
* 网络归档可能带路径穿越，逃出目标目录的条目直接丢。
* 只有点名了已登记工程的备份才允许预填那个工程，否则表单必须开在空白的新目录上——不能让人一点击就落到别人的真实工程上。

### 构建脚本

* **图标字体的缓存要删**：Flutter 把 tree-shaken 的图标字体留在 `build\flutter_assets`，不察觉 `Icons.*` 用量的变化，增量构建会沿用上一轮的字形表，按钮出来就是空白图标。删掉它能强制重生成，又不用付一次完整 `flutter clean`。
* **原生命令的退出码不会抛异常**，所以 C# 构建失败必须显式 `throw`——否则流程继续走到 flutter，把**上一个**侧车 stage 进产物，应用跑着旧代码却看起来是刚重建的。
* 构建前先停掉 `akashic_ui` / `Akashic.Host`：运行中的 exe 锁着自己的输出，构建会以 MSB3027 失败而不是一条清楚的消息。
* `*.WebView2` 是运行时 profile（登录态、缓存），不是产物，绝不能进分发包。
* WebView2 插件在构建时还原它的 NuGet 包，仓库自带 `nuget.exe`，干净机器无需全局安装。
* **`scripts/*.ps1` 保持纯 ASCII**：Windows PowerShell 把无 BOM 的 `.ps1` 当 ANSI 读，字符串里的非 ASCII 字符会打断解析。（要写中文注释就存成带 BOM 的 UTF-8。）

### 写盘类自检

* 只碰 `C:\temp` 下的假工程，事后恢复 `settings.json`，绝不动真实工程。改设置类控件前先把 `%LOCALAPPDATA%\Akashic\settings.json` 备份一份。

---

## 回退点与备份

* tag `pre-flutter-refactor` → 迁移前的 WinUI 版本。
* `E:\Akashic\backups\`：git bundle、迁移前的工作树 tar.gz、v1.1.0 发布 zip。
* 仓库根目录、`dist\VPMManager`、`%LOCALAPPDATA%\VPMManager` 是改名前故意保留的，不要顺手删。
* 剥离全部注释之前的状态在 `9e62e56`，需要看某段代码原本的注释时从那个提交取，不要往新代码里加回去。

