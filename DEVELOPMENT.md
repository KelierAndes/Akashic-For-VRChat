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
* `ProjectService.SaveOwnedFieldsAsync` 用一份**字段白名单**决定这次写回携带哪些配置：`ProjectService` 自己拥有的设置（Unity 编辑器列表、备份路径、剔除软件包、默认工程目录、模板、预览缓存目录、预览缓存上限）必须逐个列进去。只往 `AppSettings` 加属性、或只在 `Set…Async` 里改内存副本而不加白名单，字段会被静默丢弃——表现是界面当场生效、重启回到默认，跟着配置同步一起失效（包里那份 `settings.json` 读盘，本来就没有这一项）。

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
* 随包同步的清单缓存集中在 `ConfigSyncService.CacheFiles`：Booth 已购、Asset Store 资产与封面、VRChat 内容、资源分类与匹配、unitypackage 安装台账。**新增一份列表缓存时要同时加进这张表**，否则它只会待在本机，跨设备看起来像「没同步」。`settings.json` 整份进出（工程列表、路径、模板、快捷栏、主题、透明度、预览缓存目录与上限都在里面），因此新增设置项天然跟着同步——前提是按上面的白名单真的写进了盘。
* 不进包的只有可从盘上重算的东西：资源扫描结果（`resources`/`package_files`/`archive_files`，键是绝对路径，换台机器就是另一套）、封面图、工程与 VPM 清单、GLB 与着色器编译缓存。分类与匹配结果写在 `resource_meta` 里、按名称匹配，所以换了机器仍然接得回去。
* 拉取后除了清单缓存，还会用还原出来的 `settings.json` 重跑一次 `PreviewCachePaths.Configure`（目录 + 上限）并就地回收，配额不必等重启。
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
* **目录递归展平到 `.unitypackage` 一层**（`UnityPackageLibraryService.Catalog`）：扫描根下的每个顶层条目要么是散包本身，要么是一个容器（文件夹 / `.zip` / `.7z`）。容器只作分组行，真正入库的是它里面每个 `.unitypackage` **叶子**，所以一个商品 zip、一个散装的 `X-extracted` 文件夹、里面散落的多个包，最终都是同一批独立行。`AddFolderLeaves` 递归目录并钻进 `.zip`；`AddArchiveLeaves` 拆包列出 `.unitypackage`，再按 `!` 拼接的 `entrySpec` 递归嵌套 zip（`MaxArchiveDepth = 3`、`MaxNestedArchiveBytes = 256 MB`，超了就不再往下钻）。
* 容器与叶子的 `Key` 不同（容器是 zip 或文件夹路径，叶子是 `容器路径!/内层路径` 或磁盘真实路径），但**元数据键各自按名字算**：`GetRowsAsync` 逐行取 `MetaKey`，不再让子行继承容器行的键。旧版就是这行继承把 `SeiranKiss-M` 三个包绑成一个匹配结果——第一个命中 Booth 商品后，另外两个直接显示同一个来源。
* **匹配顺序是「整包优先、逐包兜底」**：`RunMatchAsync` 先只拿容器（`CanMatch` = 有子包的文件夹/压缩包）按**容器名**去匹配 Booth，整包命中就写容器自己的元数据；跑完重新取一次行，再展开**未命中容器的子包**（以及散包）逐个匹配。命中容器的子包不再单独试——`InheritFromContainer` 会把 `Matched`/来源/封面/分类/标签填到空字段上，并标 `Attempted`，所以增量跑不会重复请求。一个商品 zip 里放多个包时，这是唯一能命中「商品级」标题的做法：子包名常常是 `Assets` 里的零碎文件名，整包名才是 Booth 上的商品名。
* 容器自己没命中、但**非依赖子包**命中了，容器也按匹配成功处理（继承子包的 Booth 来源、封面与分类）；lilToon、Modular Avatar、VRChat SDK、VRCFury、Poiyomi、AAO 这两类依赖包在匹配阶段直接跳过，不参与容器身份判定——把作者随包附带的依赖当成商品本身，会把整包错绑到依赖作者的商品页。
* 包名搜索没有结论时，再带 `tags[]=VRChat` 搜一次：很多形象/服装商品的标题与包名对不上字面（`nijiiro` → 「【無料】2416△の「Nijiiroちゃん」」），但加上 VRChat 标签后只剩一张卡片，按「唯一结果」直接采信。标签搜索仍走同一套打分与唯一性判定，不会放宽到多结果里随便挑。
* 同一个包既在 zip 里又在解出来的文件夹里（`X.zip` + `X-extracted/`）会扫出两条，按**相对路径**去重：同一条路径上**压缩包内的条目优先**，`-extracted` 里的那份直接丢掉。以前反过来「只保留磁盘上那份」，结果容器唯一的子包成了物理文件、`ArchiveEntry` 为空，预览时把它的完整路径当成 zip 条目去取，直接报「压缩包里没有 C:\...\X.unitypackage」。扫描顺序不保证（文件夹和 zip 谁先扫到看文件系统），所以这条规则必须与顺序无关。叶子的「多大」取 `ZipArchiveEntry.Length`（解压后大小），不是外层 zip 的长度。
* **文件夹里嵌的 zip，`entryPrefix` 必须是空的**：`AddFolderLeaves` 发现子项是压缩包时，`archivePath` 已经指向那个 zip 文件了，entry 规格只该含 zip 内部的路径。早先多传了一层 `relative`（zip 自己的文件名），于是 `ExtractInnerPackage` 先在这个 zip 里找「Sapphy VRChat v2.0.2.zip」这个条目，必然找不到——Sapphy 那类「整个商品文件夹里各放一个 zip」的资产全部预览失败。zip 里再套 zip 时才用 `Prefix(entrySpec, nested)` 串联 `!` 段。
* 预览的解析顺序：先按 `ArchiveEntry` 从 zip 里剥（支持 `!` 多级），再从 `FilePath` 直接读物理包；容器（`IsContainer`）取第一个子包或 `entry` 命中的那个，子包是物理文件就直接读、不冒充 zip 条目。
* `ProductKey` 把 `X.zip` 与 `X-extracted` 折成同一个键，所以一个商品在列表里只有一行分组。`InJunk` 跳过 `__MACOSX`、`.DS_Store` 一类噪声目录。
* zip 只读中央目录，不预解压：一次扫描就能列出内层 `.unitypackage`。只有点「解包」才会真的落盘并登记入库；`ExtractArchiveAsync` 逐条校验路径没逃出目标目录。
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

### 数据层（SQLite）

* 列表数据的唯一读取源是 `%LOCALAPPDATA%\Akashic\akashic.db`（Microsoft.Data.Sqlite，WAL）。工程、已安装包、VPM 清单、资源行、资源元数据、Booth、Asset Store、VRChat、库文件扫描结果、安装台账都在里面；`settings.json` 仍然只管配置，`cache/*.json` 只在配置同步时由数据库导出。
* 表里的 `payload` 列保留完整模型 JSON，便于新旧字段兼容；索引列（名称、来源、分类、mtime）只用于排序和筛选。资源筛选可以走 `GET /api/resources?origin=&category=&q=`，由 SQL 完成。
* 第一次启动会把旧版 JSON 缓存一次性导入，并在 `meta` 表记 `legacy.imported`。同步拉取后调用 `ImportSnapshots()` 覆盖对应表；同步上传前调用 `ExportSnapshots()` 生成旧格式 JSON，保证 2.1.0 及更早版本仍能还原。
* 工程列表用文件签名（`ProjectVersion.txt`、`manifest.json`、`vpm-manifest.json` 的 mtime+长度与目录 mtime）判断是否要重扫；未变化的工程直接读数据库里的记录。库里没有的工程路径会同时从 `settings.json` 和数据库清掉。
* 库扫描结果按「文件大小 + mtime」缓存 `UnityPackageFileInfo` 与 zip 子项；第二次打开资源页不再打开每个 zip。缩略图也记录命中与未命中，避免每次重开都去拆同一个包找 `preview.png`。
* 实测（本机 75 个资源、62 个包文件、14 个压缩包）：资源页热查询约 70 ms，工程列表约 40 ms；冷启动首次仍要扫描并解缩略图，约 4–7 秒。

### Prefab 预览

> 这一条链路的**待办**与取舍写在 `docs/preview-improvement-plan.md`（输入护栏、回归基线、实时化、采样质量等分档）；本小节只记「已经怎么实现、为什么这样写」。完成一项后把编号与实测数字回写到这里。

* 预览管线：`PackageContentCache` 把 `.unitypackage` 的 tar.gz 里需要的资产（prefab/mat/shader/fbx/贴图/脚本/package.json）按 `pathname` 落盘到 `%LOCALAPPDATA%\Akashic\preview\packages\<hash>\`；`UnityMetaIndex` 扫 `.meta` 建 GUID→路径索引并解析 FBX 的 `internalIDToNameTable`；`PrefabParser` 用块头正则 + YamlDotNet 还原 GameObject/Transform/组件/嵌套 prefab；`GlbBuilder` 用 Assimp 找 FBX 子网格、SharpGLTF 输出带贴图的 GLB。
* 场景 JSON 里带层级、局部变换、网格引用、材质槽位与组件字段；Flutter 左侧树、右侧组件面板直接消费它。三列布局在 `app/lib/src/pages/preview_page.dart`。
* **材质降级缓存**：每个 `.mat` 解析出的 fallback 描述（主贴图/颜色、阴影色、法线、MatCap、自发光、描边、透明/双面、纹理 ST、关键字、管线）以「路径 + 大小 + mtime」为键存进 SQLite 表 `preview_materials`（`material-vN` 前缀），同一材质只解析一次；GLB 与 scene.json 仍按 `build-vN` 缓存。实测 Nijiiro 第二次构建 36 ms。
* 能拿到 `.shader` 时，额外扫描 `Properties { ... }` 块：按 `[MainTexture]` / `[MainColor]` / `[Normal]` 标记与 `2D/Color/Range` 类型补全主贴图、主色、法线与裁剪，不认识的槽位名也能映射；非 lilToon 的卡通 Shader 归为 `toon` 走通用卡通替身，Unlit/UI/Sprite 走基础材质，其余保持 PBR。
* 透明语义按材质模式分开处理：**opaque 必须忽略贴图 alpha**（否则脸/衣服贴图里的透明区会挖洞），mask 才按 `_Cutoff` 丢弃，blend 才输出贴图 alpha 并关闭深度写入；预制乘 alpha 只在 blend 下生效。Shader 文本里的 `clip`/`AlphaTest` 与 `Blend SrcAlpha` 只对非 Standard、非 lilToon 的自定义 Shader 作为补充线索，避免把不透明 Shader 的 `Blend One Zero` 误判成透明。
* 同一个节点有多个子网格时，按 Assimp 材质的 **名称** 先匹配 prefab 材质，匹配不上再按索引；GLTF 每个 primitive 会生成一个子 Mesh，视窗按「节点材质数组 + primitive 序号」回查，避免同名/空名材质全部套到第一个。
* MatCap 依赖环境光照/视角，外部渲染容易产生错误高光，**默认不应用**（仍解析并显示在组件面板里）；需要判断烘焙效果时用顶部的「平光预览」按钮，临时关闭光照看基础贴图与颜色。
* 全局光照烘焙（Lightmap）本身不重建，动态光照下的明暗会与 Unity 内不同；平光模式是给这类资产核对贴图/材质的兜底。
* 打开预览先进入「选择要预览的内容」列表：**包内每一个文件都在列**（prefab / 模型 / 贴图 / 材质 / 着色器 / 动画 / 状态机 / 脚本 / 音频 / 视频 / 文本 / 场景），带完整相对目录、类型徽标与大小，可按名称或目录筛选；只有 prefab 与模型给「预览」（进 3D 视窗），其余给「查看」。标题副文案「N 个文件 · X 个可建模 · Y 张贴图 · Z 个材质」就是按这个分类统计的。`PackageContentCache` 的白名单已经换成**黑名单**（`.dll .so .dylib .exe .pdb` 与嵌套压缩包不解），所以列的是真·全部资产，而不是「能被建模的那几个」。
* 列表**按类型分组**：类型顺序固定（`_kindOrder`：prefab → 模型 → 材质 → 贴图 → 着色器 → 动画 → 状态机 → 脚本 → 音频 → 视频 → 文本 → 场景，未列出的类型排在最后），每组一个小标题带图标与数量；上方一排可点的类型标签（「全部 N」「贴图 3」…）做筛选，选中时只列该类型、不再显示分组标题。计数来自**全部资产**而不是当前筛选结果，否则点一个标签后其他标签的数字会跟着变。
* 容器（zip / 文件夹）行的预览入口取**第一个子包**：`_preview` 遇到带 `children` 的行会换成 `children.first`，容器本身不是可预览的资源（它的 `Key` 交给 `PreviewService` 只会解析成「压缩包里没有 …」）。之前那条「预览容器里的第一个包」的提示语是真的、实现是假的。
* 贴图与材质走右侧分栏（列表 flex 5 / 查看器 flex 6）：贴图由 `GET /preview/<token>/file` 出图，材质由 `GET /preview/<token>/material` 出一个 **HLSL 渲染的球**（`HlslSceneData.BuildMaterialPreview` 造一个 2× 缩放的内置 10204 球体，只挂这一个材质，`RenderOptions { View = "iso", Mode = "textured" }`）。这两条路由的 token 在路径里，所以能当普通图片 URL 用。
* **`.tga/.psd/.hdr/.exr/.tif/.dds` 要转 PNG 才能显示**：`TexturePreviewService.Resolve` 对 png/jpg/gif/bmp/webp 直接透传，其余用 StbImageSharp 解码后写进 `cache/preview/textures/tex-vN`（版本号是该服务自己的常量，与 `shim-vN` 不同族）。StbImageSharp 与 StbImageWriteSharp 都导出 `ColorComponents`，**必须全限定**，否则 CS0104 二义。
* **给 `Uri.replace` 传带 `?` 的 path 会把问号编成 `%3F`**，整条查询掉进路径里 → 路由不匹配 → 404，而 Flutter 的 `Image.network` 只会给一句「无法加载」。`HostClient._previewUrl` 因此把 path 与 query 分开传；路径里带空格的资产（`Kiss Like ver.8`）此前就是这样静默失败的。
* 查看器**不用 `Image.network`**，改走 `HostClient.fetchPreviewImage`：材质球要编译着色器，动辄十秒，`Image.network` 期间只能给一块黑；而且它拿不到响应体，侧车返回的真实原因（`Problem` JSON 的 `detail`）会被吞成一句笼统的「渲染不出来」。自己发请求才能显示「正在用 HLSL 渲染材质球…」并把 `detail` 原样贴出来。
* **Unity 5 时代的 `.mat` 用重复的 `data:` 键表达列表**（`m_TexEnvs`/`m_Floats`/`m_Colors` 下每个条目再写一次 `data:`），YamlDotNet 在装配 `YamlMappingNode` 时对重复键抛 `YamlException`，于是整个 block 被丢掉、`ParseMaterial` 返回 null——表现为「这个 .mat 解析不出材质」，SUIMONO 49 个材质里 35 个中招。`ReadBlocks` 现在先按原样解析，失败再把 `data:` + 缩进的 `first:` 合并成 `- first:`（序列项的列正好等于原子项的列，深层缩进不用动）重解析一次。改完 SUIMONO 从 4/49 到 **39/49**；全库 89 个包、抽 229 个材质跑一遍球渲染是 **225 成功 / 4 二进制 / 0 其他失败**，同一轮 prefab 回归是 `buildFail 0`、可渲染 52、无网格 22、无 prefab 15、428 次绘制 / 215 个回退材质。
* **另有一类 `.mat` 是二进制序列化的**（文件头不是 `%YAML`，而是带 `5.0.0f4` 与类型树的裸字节），SUIMONO 剩下 10 个就是。这类不做逆向，`MaterialAsync` 先读前 5 个字节判头，直接给「这个 .mat 是二进制序列化的，读不出里面的属性。」——报错文案要说真原因，别一律推给「着色器无法编译」。
* 视窗由侧车在 `/preview/<host-token>/` 同源提供 `viewer.html` 与本地打包的 Three.js（`src/Akashic.Host/preview/`），WebView2 只加载这一页；`LoopbackGuard` 对 `/preview` 校验路径里的 token，不接受跨域来源。
* 未知 Shader 按「语义槽位嗅探」降级：`_BaseMap`/`_MainTex`/… 依次找主贴图，`_BaseColor`/`_Color` 找颜色，`Cull Off` → 双面，`Blend`/`_Cutoff` → 透明或裁剪；名字命中 toon/lilToon/Poiyomi 等或带 ramp 槽位的走**自写的 lilToon 近似 `THREE.ShaderMaterial`**（`viewer.html` 的 `createLilToonMaterial`，不是 `MeshToonMaterial`——Unity 的 ramp 分档、双面光照与描边 Pass 它表达不了），unlit 用 `MeshBasicMaterial`，其余按 PBR 渲染。
* **贴图的 `_ST`（平铺/偏移）只在精确模式生效**：`PrefabParser` 把每个纹理槽的 `scale/offset` 解析进 `PreviewMaterial.TextureST`，D3D11 侧按 `$Globals` 里的 `<槽名>_ST` 绑定（`Values()` 里 `_ST` 结尾的分支），所以「HLSL 精确」下平铺是对的；但 GLB 侧既没写 `KHR_texture_transform`、`viewer.html` 也没读 `scene.json` 里的 ST，近似模式一律按 1:1 UV 画。**同一材质在两种模式下的平铺/偏移可能不同**，比对效果时先想到这条。要补齐就得在 `GlbBuilder` 里给 `MaterialTexture` 挂上 `KHR_texture_transform`（SharpGLTF 1.0.7 已有 `TextureTransform`），并注意 Unity 的 UV 原点在左下、GLTF 的 offset 方向要跟着顶边翻转。
* **内置 Standard 着色器**：`Services/Preview/Shader/Resources/Standard.shader` 随程序集嵌入，`ShaderEnvironment` 落到缓存目录（目录名就是 `ShaderEnvironment.Version`，别在文档里抄具体版本号）；`PreviewMaterial.BuiltInShader` 为 `Standard` / `Standard (Specular)`（GUID `…f000…`/`…e000…` 的 fileID 46/47）或 `ShaderFamily == "Standard"` 时，HLSL 直接编译这份着色器，不再落到「内置着色器，用近似材质代替」。它按 Unity Standard 的金属度工作流实现：`_Metallic`/`_Glossiness`（可被 `_MetallicGlossMap` 调制）、`_BumpMap`、`_OcclusionMap`、`_EmissionMap`、`_Cutoff`，Blend/ZWrite/Cull 走材质里的 `[_SrcBlend]` 等属性；FBX 里没有 `.mat` 的合成材质也标记为 Standard。常规 3D 预览沿用 glTF 的 `MeshStandardMaterial`，Standard 材质读 `_Metallic` 与 `_Glossiness` 换算成 metallic/roughness，其余保持原有近似。`Standard.shader` 是手写的等效实现（Unity 官方 Standard 是 Surface Shader，没有可复用的 Pass 源码），标准材质的 HLSL 渲染从此不再计入回退数。
* **内置 UI 渲染器**：`Resources/UIDefault.shader` 实现 Unity UI/Default 的语义——顶点色 × `_MainTex` × `_Color`/`_ColorTint`、`Cull Off ZWrite Off ZTest Always`、`Blend SrcAlpha OneMinusSrcAlpha`、`clip` 掉全透明像素；Pass 必须标 `LightMode="Always"` 才会被 `ShaderCompileService` 收进前向列表。结构体名要避开 shim 里已有的 `appdata_t` / `appdata_full`，否则 fxc 报重定义。
* **内置着色器 id 认不出来时按属性签名猜原形**：Unity 找不到 Shader 时会把材质引用换成内置兜底 id（本库里见过 211 / 10721 / 10723 / 45），于是 lilToon 或 Standard 材质全都显示成「内置着色器#211」。`ApplyRenderState` 现在对未知 id 先看属性：有 `_ClipRect`/`_ColorTint`/`_Stencil` → UI/Default；有 `_Glossiness`/`_Metallic`/`_Mode`/`_Parallax`/`_DetailAlbedoMap`/`_SmoothnessTextureChannel` → Standard；已经是 lilToon/toon 的不动。`builtInStandard` 的判定也从「GUID 等于内置 f000…」改成「`BuiltInShader` 以 Standard 开头」——同一个 f000… GUID 下既有 Standard 也有内置兜底 id，只按 GUID 判会把 ABT 的 lilToon 材质强行变成 Standard。`ApplyLilToon` 同理不再因为内置 GUID 提前返回，改由 `_TransparentMode`/`_1st_ShadeColor`/`_LightMinLimit` 这些 lilToon 专属字段判定。全库 93 个包重跑：可渲染 46、零回退从 39 升到 **42**，剩下 4 个是 ABT 里确实缺 lilToon 源码的 UI 材质。
* **文本与 PDF 预览**：`GET /preview/<token>/text?k&p&entry&limit` 读包内文件，按 BOM → 严格 UTF-8 → Shift-JIS(932) → Latin-1 顺序解码，返回 `{ text, encoding, size, truncated }`；前 4 KB 出现 NUL 判二进制（415），>32 MB 拒绝（413），控制字符在返回前洗掉、`
` 归一。PDF 仍走 `file` 路由，只是加了 `application/pdf` 的 Content-Type，交给 WebView2 自带的 PDF 查看器。实测全库 2847 个文本文件可读出、15 个 PDF 全部按 `application/pdf` 返回；9 个「空」是包内 0 字节的 `!flaticon.com.txt` 一类，界面显示「这个文件是空的」。
* **点开包 = 整页接管，预览走左右分栏**：`app/lib/src/pages/asset_viewer.dart` 拆出 `AssetViewer`（贴图 / 材质球 / 文本 / PDF，材质自带黑白底色）与 `PackageAssetsPanel`（只剩筛选框 + 类型标签 + 文件列表，不再自己渲染查看器）。`ResourcesPage` 用两个状态量驱动：`_panel` 非空时 `NeuPageBody` 的 `fixed` 直接给 `const []`（分页标签与页内搜索框整体让位）、`trailing` 换成「返回资源列表」，`content` 变成 `Row`——无预览时列表 `flex: 10` 左右拉满，有预览时列表 `flex: 5` + 右侧 `flex: 5` 的 `AssetViewer`。选中项由父级持有（`picked` 传下去、`onPick` 传上来），所以右侧换文件时左侧列表和筛选题不会丢状态。只有 prefab / 模型走 `onOpenModel` 弹独立预览页，`PreviewPage` 的 `initialAsset` 进去直接构建那一个资产；`_back()` 里那条「多于一个资产就先展开清单」的分支删掉了——它以前是死代码，有了 `initialAsset` 后会劫持 ←。类型标签从 `SingleChildScrollView + Row` 换成 `Wrap(spacing: 6, runSpacing: 6)`：左右分栏后列表变窄，一排 13 个标签会被截断（实测截到「着色器」就没了），Wrap 会自动排成两三行。
* **预览视窗按 `viewerUrl` 重建**：`InAppWebView` 的 `initialUrlRequest` 只在创建时生效，切换 prefab 后 URL 虽然变了、原生页面却仍是上一个模型，于是出现「新模型的层级树 + 旧模型的画面」。`_build` 先把 `_viewerUrl` 清空、再给控件加 `ValueKey(_viewerUrl)`，URL 变化即销毁旧 WebView 并新建；WebView 内的 Three.js 资源本来就用 build id 与 `cache: 'no-store'`，不会命中旧模型。
* **渲染信息不再压在画面上**：HLSL 模式下隐藏 `#summary`（只保留左下角的操作提示与右下角缩放百分比），`window.akashicHlslInfo()` 暴露绘制次数、回退数、回退明细、错误与当前模式；Flutter 每 900 ms 轮询，把结果画在右侧组件栏下半的固定「渲染信息」分块里（同时显示节点/网格/材质数），不再盖住图像。
* **资源页有页内搜索框，侧栏搜索按包浏览器的方式交接**：侧栏 `_performSearch` 在注册表包之外，把 `resourceRows()` 的叶子（容器展开后的 `kind == unitypackage`）按名称、相对路径、绝对路径与店铺名匹配，最多 8 条列在「本地 .unitypackage」分组；首次搜索触发一次库扫描，之后走 `AppModel` 内存缓存。`ResourcesPage` 顶部一个 `NeuSearchField`（`_query` 控制器），文本作为 `query` 传进五个分页；`resource_tabs.dart` 的 `resourceRowMatches(row, needle)` 递归比对 `name`/`displayName`/`relativePath`/`filePath`/`boothShop`/`category`（含容器子项）。`UserPackagesTab._shown` 与 `BoothShelfTab._shown` 在关键词非空时**直接跳过来源/分类下拉的谓词**——否则默认「未匹配」会把搜到的已匹配包又过滤掉，看起来就像搜索没生效。侧栏搜索点本地 `.unitypackage` 结果走 `AppModel.resourceQuery = row['name']` + `jumpTo('resources')`（和 `packageQuery` → 包浏览器一模一样，不再自动开面板），资源页在 `build()` 里取走、清空、postFrame `setState` 填进搜索框并切到「用户软件包」。这条交接要求 `AppShell._page('resources')` 不能写 `const ResourcesPage()`：const 实例相等时 Flutter 直接跳过子树重建，已经在资源页时就永远收不到 `resourceQuery`（一次性 `_pendingHandled` 标记也会造成同样的症状，所以改成每次 build 都取、取完即清）。
* **分组下拉遍历所有包列表**：Booth 分页与工程详情页都按「容器 / 文件夹」分组并带折叠箭头；工程详情页对 `.unitypackage` 用父目录名作为组名（去掉 `-extracted` 后缀），VPM/本地/Git/外部包按类型成组。Booth 主页的已下载商品也能展开查看压缩包内的 `.unitypackage`/UPM 包，lilToon、Modular Avatar 这类依赖会标「依赖」。Booth 分页的容器行以前只有右侧那个 3D 图标可点，现在标题区（`GestureDetector` + `HitTestBehavior.opaque`）同样打开面板；两个分页的 `_preview` 取子项时都先找第一个 `kind == unitypackage`，不再盲取 `children.first`——容器里混着 UPM 包时 `first` 会不是 unitypackage，判定直接 return，点了没反应。
* **lilToon 定向解析**：包里通常不含 lilToon 的 `.shader`，所以不能依赖 Shader 文件，改按材质字段识别与读取：
  * 识别：出现 `_TransparentMode`、`_LightMinLimit`、`_ShadowColor`/`_1st_ShadeColor` 或 Shader 名含 lilToon。
  * 主色/主贴图：`_Color`/`_BaseColor`，贴图优先 `_BaseMap`、`_MainTex`、`_BaseColorMap`。
  * 阴影：旧版 `_1st_ShadeColor`/`_2nd_ShadeColor`，新版 `_ShadowColor`/`_Shadow2ndColor`/`_Shadow3rdColor`；`_ShadowStrength`、`_LightMinLimit`、`_LightMaxLimit`、`_AsUnlit`。阴影色 alpha 为 0 表示未启用，回退到上一阶，避免把暗部渲染成纯黑。
  * 渲染状态：`_TransparentMode`（0 不透明 / 1 裁剪 / ≥2 透明）、`_Cutoff`、`_Cull`（0 双面）。
  * 扩展贴图：`_EmissionMap`/`_EmissionColor`、`_MatCapTex`/`_MatCapColor`、`_BumpMap`/`_BumpScale`、`_OutlineColor`/`_OutlineWidth`。
  * 视窗用自写的 lilToon 近似 Shader：主贴图 × 主色，按 `NdotL` 与阴影强度分三阶混合 `_ShadowColor`/`_Shadow2ndColor`，叠加自发光；`_UseOutline` 时加反向外扩的背面描边。MatCap 纹理由侧车复制到 `builds/<id>/textures/` 并保留元数据，但默认不参与着色（环境光照依赖太强）。
  * 非 lilToon 的旧材质要按 Unity 渲染状态解析：`_Mode`（0 不透明 / 1 裁剪 / 2 淡出 / 3 透明）、`_SrcBlend`/`_DstBlend`/`_ZWrite`、`_ALPHABLEND_ON` / `_ALPHAPREMULTIPLY_ON` / `_SURFACE_TYPE_TRANSPARENT` 关键字。MiniSimplisticPens 的玻璃就是 `_Mode 3` + `_DstBlend 10` + `_ZWrite 0`，只按 lilToon 字段会误判成不透明。预制乘 alpha 要同步设置材质的 `premultipliedAlpha`。
  * 一个 prefab 节点可能引用多个 FBX 子网格（Assimp `node.MeshIndices`），要全部输出成多个 primitive，并按 `mesh.MaterialIndex` 取对应材质；只取第一个会丢零件并让所有面用错材质。GLTF 里的材质再按名称映射回场景 JSON。
  * 支持 **模型 prefab**（`m_SourcePrefab` 直接指向 `.fbx`）和 Unity 内置网格（`unity_builtin_extra` 的 10202 立方体、10207 Quad、10206 Plane、10204 Sphere、10203 Cylinder 等）；UnyStylus 同时用到这两种，漏掉就会「无法解析」或 0 网格。
  * prefab 可能有多个根节点（模型 prefab、UI Canvas 分开挂），PrefabInstance 合并时不能只取 `baseRoots[0]`，否则模型分支会整段丢失；多根时建一个容器节点装全部。
  * 取景用整个场景的包围球，不要按「最大网格 / 去掉离群网格」自动裁剪：有的资产自带 75 倍放大的笔身或玻璃展示盒，自动裁剪会把相机放进模型内部；宁可初始视角远一点，让用户自己缩放。
* 常见的坑：
  * `.unitypackage` 的 tar 成员可能带 `./` 前缀，`guid/pathname` 的切分前必须先剥掉，否则整个包会解析成 0 个资产（`inari-udon` 就是这种包）。
  * 解 tar 必须用 SharpZipLib：.NET 自带的 `System.Formats.Tar.TarReader` 会在 UnyStylus 这类非标准头/大数值字段上报 `Unable to parse number`，SharpCompress 则报 `Unexpected EOF reading tar file`；SharpZipLib 的 `TarInputStream` 能完整读出 656 个条目。读 `pathname`/`asset.meta`/`asset` 时必须严格按 `entry.Size` 读取，不能 `ReadToEnd`，否则会吞掉后面的条目。**这条对两个入口都成立**：预览用的 `PackageContentCache` 早就换了 SharpZipLib，但安装用的 `UnityPackageExtractor.ParseAsync` 漏改，导致「预览正常、装到工程报 500」——加新 tar 入口时先确认用的是哪个。
  * `TarReader` 的 `entry.DataStream` 在进入下一条后会被释放，包着读的 `StreamReader` 必须 `leaveOpen: true`，否则抛 `SubReadStream` 已释放。
  * `SceneBuilder` 没有 `CreateNode`，节点用 `new NodeBuilder(name)` 建；`MeshBuilder.UsePrimitive` 的第二个参数是每个图元的顶点数，三角形传 3，传 0 会直接抛异常。
  * `PreviewPage` 是 `Navigator.push` 出来的路由，拿不到 `MaterialApp.home` 里的 `AppScope`/`NeuTheme`；必须把 `HostClient` 与主题 tokens 显式传进去并再包一层 `NeuTheme`。
  * 预览缓存带 `extract-vN`/`build-vN`/`material-vN`/`shim-vN`/`hlsl-vN`/`tex-vN` 版本号，改提取或 GLB 逻辑时要同步递增，否则旧缓存会一直命中。

* **HLSL 精确渲染（可选）**：预览页顶部「HLSL 精确」切到侧车的 D3D11 离屏渲染器。机位由视窗自己维护（`yaw/pitch/distance/target`，左键旋转、右键/Shift 左键平移、滚轮推拉、双击或「重置视角」复位），每次交互发一次 `POST /api/preview/render`；服务端按 `view:"orbit"` 用透视相机（FOV 38°，与 Three.js 一侧一致）渲一张单视图，**回原始 RGBA 而不是 JPEG**（`raw:true` → `application/octet-stream`，`Results.Bytes(frame.Pixels…)`），视窗用 `createImageBitmap` 贴进 `<canvas>`。首帧不带机位，服务端用 AABB 自动取景（`(radius*1.8+0.15)*1.205`，与 GLB 侧 `frameObject` 同一套），并把实际机位放进 `X-Akashic-Camera` 响应头，让视窗接管后续的绝对参数；`X-Akashic-Frame-Width/Height` 告诉它这次回的是什么尺寸。原始像素是刻意的取舍：JPEG q=90 编解码在 1600×1000 上要十几毫秒，而本来就要从 GPU 读回一次，多一层编码只是白烧 CPU；实测单帧 2–10 ms（首个 Pass 编译除外）。请求走单飞 + 最新优先（`hlslBusy/hlslQueued`），拖动时不会排队爆炸；**静止时不发请求**，画面停在最后一帧，GPU 占用归零。HLSL 打开时 Three.js 的 `setAnimationLoop` 直接返回，避免两套渲染同时跑。`X-Akashic-Render-Ms`/`Queue-Ms` 是这条路径的埋点，视窗右下角直接显示。
* **离屏分辨率跟着视口走，但有总像素闸门**：`hlslFrameSize()` 取视口设备像素（`devicePixelRatio` 上限 2），超过 260 万像素时按比例缩边，渲染器再把每条边夹到 128–4096。放大查看是在这张图上做 canvas 缩放，不是把小图硬拉大。
* **贴图采样与抗锯齿（B2，2026-10-29）**：解码后的贴图按 `MipLevels=0` + `BindFlags.RenderTarget|ShaderResource` + `ResourceOptionFlags.GenerateMips` 建立，上传 0 级后 `GenerateMips` 生成完整 mip 链；采样器由 `MinMagMipLinear` 升到 `Filter.Anisotropic`（`MaxAnisotropy=8`，WARP 下退回三线性），wrap/clamp 仍按采样器名含 `clamp` 判定。RT 在真实显卡上是 4× MSAA 的 BGRA8 + D24S8，画完 `ResolveSubresource` 到单采样的 `_color` 再走原来的读回；WARP 下 `_sampleCount=1`，保持旧路径。`CreateTexture2D` 那组便捷重载的第 6 个参数是 `SubresourceData[]`（初始数据）而不是 `SampleDescription`，所以带采样的目标必须走 `Texture2DDescription` 那条（`CreateTarget`）。实测（AudioHairClip、900×900、同一 build 同一机位，HEAD 版 vs 现版）：轮廓处拉普拉斯能量 134.4→93.1（正视角）、148.4→81.1（pitch 1.32 的斜视角），物体内部 64.9→50.4 / 97.3→58.5，绘制次数不变，单帧 4.1→6.5 ms。**显存是增加的**：9 张贴图带 mip 链约 201 MB（不带 mip 是 151 MB），+33% 换掉走样。计数取当前 GPU 会话里出现过的去重贴图路径（`SessionTextures()` 读 `TextureBinding.Path`，所以要在 `ResolveTexture` 里把真正用的文件路径带出来），经 `X-Akashic-Textures`（`张数|MB|解码失败数`）与 `X-Akashic-Msaa` 出去，右栏「渲染信息」多两行「抗锯齿 / 贴图」；这两行是字段渲染的，改 `akashicHlslInfo()` 的返回体才会生效，只改 `summary` 文案不会显示。
* **BC/DDS 直传没做，是量出来的结论**：改进计划里写「VRChat 素材的贴图绝大多数是 BC7/BC1 的 DDS」，实测 12 个工程的 `Assets` 目录里 4200+ 张贴图中 `.dds` 只有 1 个（Asset Store 缓存与 Booth 目录里 0 个），分布是 PNG 3681 / JPG 469 / PSD 60 / TGA 67 / EXR 96。为 0.02% 的资产引入 unsafe 指针上传路径不值，`.dds` 目前仍走 StbImage 解码失败 → 白图，并被计入「N 张无法解码」。真要补，缺的是 DDS 头解析 + 逐级 `UpdateSubresource`（`Filter.Anisotropic`、`ResourceOptionFlags.GenerateMips` 这些枚举名已核对过 Vortice 3.8.3）。同一类静默白图还有 `.exr`（StbImage 不支持）与部分 `.psd`。
* **D3D11 会话键必须带材质/着色器签名**：`_sessionKey` 原来只含 `SessionId|选项`，而材质球的 SessionId 固定是 `material`——连续查看两个材质时会复用第一颗球的 GPU 缓冲，表现为「材质球不更新」。键里加上每个材质的路径与编译后 CSO 路径后，同一个 build 内换材质/换着色器都会重建会话；模型预览的 SessionId 本身按 build 唯一，不受影响。
* **底色可切黑/白**（页面右上角 `#bgToggle`，实时与 HLSL 共用一个 `backgroundLight` 状态）：实时视图换 `scene.background` 与网格配色，并给 `<body>` 加 `.light` 让 HUD、视图标签、模式按钮换成浅色主题；切 HLSL 时把 `lightBackground` 一起发进 `/api/preview/render`，渲染器用近白清屏色（0.93/0.94/0.96），并改取 `debug-clay-light` / `debug-wireframe-light` 两个调试着色器变体——白膜在浅底上必须压暗（`lerp(0.20…, 0.62…)`），否则白球贴白底完全看不出轮廓。light 变体是独立缓存键，旧的深色二进制不受影响，不用递增 `hlsl-vN`。**材质球也有同一组底色**：`/preview/<token>/material?light=1` 把 `LightBackground` 传给 `RenderMaterialAsync`，材质查看器头部按钮切换并重新取图。
* **四视图方案已移除**（2026-10-29）：原来一次出 2×2 正交图、靠 CSS `transform` 缩放，和 GLB 侧的轨道相机是两种手势。现在两条路径同一套操作与同一套开关（贴图/白膜/网格、坐标轴、底色、重置视角都只有一份 UI）。材质球预览仍走 `view:"iso"` 的正交单视图（球不需要自由机位），所以 `Views`/`FitView`/`Orthographic` 保留，`quad` 分支删除。改机位或分辨率要连带改 `viewer.html` 里的 `hlslBasis()/hlslPan()`（平移速度 = 2·distance·tan(fov/2)/视口高度），两侧的正负号是成对的：视窗往 +target 推，画面里的人往反方向走。
* **坐标轴只在右上角，不再画进模型**：HLSL 场景不再生成 gizmo 网格（请求里 `gizmo:false`，绘制次数因此从 8 降到 5），墙角那套坐标系改由一块独立的透明 WebGL 画布渲染（`#gizmoView`，右上 108px），两种模式共用。它按**当前模式的相机**转：GLB 用 `camera.quaternion`；HLSL 把轨道机位换算回 Three.js 空间（眼点与目标点都取 X 负，因为 GLB 是 Unity 空间的镜像）再 `lookAt`，这样同一副模型在两种模式下看到的轴指向一致。白膜/网格在 GLB 侧是材质替换（共享 `clayMaterial` / `wireMaterial`，并隐藏 lilToon 描边网格），在 HLSL 侧是 `mode` 参数，语义对齐。
* 侧车渲染失败时页面会自己 `akashicSetHlsl(false)` 退回实时，但 Flutter 的 `_hlsl` 是独立状态，会一直卡在「返回实时」。`_toggleHlsl` 打开后 1.5 秒查一次 `window.akashicHlslState()`，页面说没开就跟着复位。
* 绘制次数由子网格数量决定，不是 Bug：一个 341 节点的完整形象单视图就有 290+ 次绘制（Pass 数其实只有 1–2）。别为了压低数字去裁剪 Pass——lilToon 的两遍透明（`FORWARD_BACK` + `FORWARD`）和描边 Pass 少画一个就直接错。
* Shader 查找：先按内容包内的 `.meta` GUID 解析；找不到时查 `%LOCALAPPDATA%\Akashic\settings.json` 里已登记工程的 `Packages` 与 `Library/PackageCache`，只扫描包根、`Shader`、`Shaders`、`Assets`、`Runtime` 目录里的 `*.shader.meta`。因此素材包只带 `.mat`、lilToon 来自工程包的常见情况也能拿到真实 Shader；Nijiiro 的解析时间从 11.5 s 降到 95 ms。
* `ShaderLabExtractor` 按花括号/字符串/注释感知扫描 `Properties`、`SubShader`、`Pass`，识别 `CGINCLUDE`/`HLSLINCLUDE` 与 `HLSLPROGRAM`/`CGPROGRAM`，解析 `UsePass "Shader/Pass"`（lilToon 的 `lts*.shader` 全是 UsePass 外壳），并保留 Pass 的 `Cull/ZWrite/ZTest/Blend/BlendOp/ColorMask/Offset/AlphaToMask` 表达式与 Shader 属性默认值。
* 每个 Pass 生成「UnityShim.hlsl + 文件级 include 块 + SubShader include 块 + Pass 程序」的单一 HLSL 文件；`UnityShim.hlsl` 与 Unity 内置 include 空桩作为 Core 内嵌资源释放到 `preview/hlsl/shim-vN`。Shim 提供精度类型、`appdata_*`、`UnityObjectToClipPos`/`TransformObjectToHClip`、纹理/采样器宏、`_Time`/光照/阴影兜底，以及 `ExternalEnginePerFrame : register(b0)`；材质属性由编译器自动归入 `$Globals`（反射拿到 b1 与每个变量的字节偏移）。
* 编译由随包的 `src/Akashic.Host/tools/fxc/fxc.exe` + `d3dcompiler_47.dll` 完成（`/Gec` 兼容 `sampler2D` + `tex2D`），带 `DIRECTIONAL` 与材质 `m_ValidKeywords`/`m_ShaderKeywords` 定义；`.cso` 与反射缓存在 `preview/hlsl/<hlsl-vN>/<key>/`（`key` 由 `ShaderCacheKey.For` 生成，含 Shader 路径/mtime、关键字、是否包含描边 Pass 与 shim 版本；编译失败的那份另存 `<key>-fb` 并带 `failureKind`）。`Vortice.D3DCompiler.Reflect` 读回常量缓冲、资源槽与输入签名。
* HLSL 渲染器不走 Three.js 的 `model.glb`，而是用 Assimp 直接读包内原始 `.fbx`（`Triangulate|GenerateNormals|JoinIdenticalVertices|RemoveRedundantMaterials|ValidateDataStructure`），贴图按 Assimp 材质槽的相对路径从 FBX 同目录解析，所以 Unity 里被脚本改过的导入设置不会干扰精确模式；模型 prefab 与 Unity 内置网格由 `BuiltInGeometry` 现生成。顶点固定 128 字节交错（POSITION/NORMAL/TANGENT/TEXCOORD0..3/COLOR，UV 的 V 翻转），按 VS 输入签名建 InputLayout。引擎矩阵以 `Matrix4x4.Transpose` 上传，配合 HLSL 的 `mul(M, v)` 行向量约定；相机是右手系 LookAt + D3D `[0,1]` 深度正交投影，`FrontCounterClockwise = true`。每帧按材质上传 `$Globals`、绑定贴图与采样器，按材质浮点解析 Cull/ZWrite/ZTest/Blend（D3D11 不允许把 `SrcColor` 类因子用于 alpha，需换成 Alpha 版本），透明材质排在最后。
* 白膜/网格是两条独立的调试 Shader（`GetDebugAsync`，键 `debug-clay`/`debug-wireframe`，靠 `AKASHIC_CLAY`/`AKASHIC_WIREFRAME` 宏区分）：白膜跳过描边 Pass、强制 `Cull Back + ZWrite + 不混合`，网格用 `FillMode.Wireframe` 且必须 `Cull None` 才看得到背面线框。
* 两个必须记住的坑：
  * Shader 若没有声明任何 `$Globals` 变量（白膜就是），反射拿不到 PS 常量缓冲；`BindPoint` 此时必须返回 `uint.MaxValue` 并在绑定处跳过，否则材质 CB 会占住 slot 0、把引擎自己的 `ExternalEnginePerFrame : register(b0)` 顶掉，光照向量变成 NaN、画面全黑。
  * 随包 FBX 的法线朝向和 Unity 导入后的不一定一致（Nijiiro 的头发法线整体朝内），白膜用 `abs(dot(normal, light))` 而不是 `saturate(dot(...))`，否则换个资产就黑一半。
* **三个空间、三种约定，先把它们分开**：Assimp 读出来的 FBX 网格是**右手系**；预制件 YAML 里的 Transform 是 **Unity 左手系世界**；glTF/Three.js 又是**右手系**。所以「镜像几次」要按目标空间算，不能两边照抄。
  * HLSL 侧（`HlslSceneData.Geometry`）：顶点翻 `position.X`/`normal.X`/`tangent.X`（`tangent.w` 同时取反）并反转三角形绕序 → 网格进 Unity 空间；再套预制件变换；相机基向量用 `x = cross(z, up)`、`y = cross(x, z)`（`z = eye - target`）配 `FrontCounterClockwise = false`，即左手系读法。三处齐全才对，缺任何一处要么 gizmo 对物体反、要么整幅水平翻。
  * GLB 侧（`GlbBuilder.WorldMatrix`）：**顶点保持 Assimp 原样**，改成对世界矩阵做共轭 `Mirror * W * Mirror`（`Mirror = CreateScale(-1,1,1)`）。等价于「镜像进 Unity 空间 → 套预制件变换 → 镜像回右手系」，行列式 (+1) 所以绕序不用动。
  * 实时窗的相机因此要画在 Unity 等价的**镜像机位**上（iso 视线方向 (−0.72, 0.46, 0.85)，X 取负），gizmo 的 X 轴标在 GLB 的 −X 上——因为 glTF +X = Unity −X。
* **为什么共轭这一步同时修掉了「部件飞开」**：Assimp 会把 FBX 物体自身的位移**烘进顶点**、节点 `Transform` 留成单位阵。实测 AudioHairClip：FBX 里 `HairClip2` 节点 `localT=(0,0,0)`，但它的网格 AABB 中心已经在 (0.017, −0.001, 0)，而预制件 `m_LocalPosition` 是 (0.02, −0.00099975, 0)——同一个数。旧写法 `W * D`（未镜像顶点 + 预制件变换）等于把这个偏移**算了两遍**，HairClip2 的世界 X 区间跑到 [0.015, 0.059]，和 [−0.025, 0.02] 的主体首尾相接、看着像两块拼错。共轭后镜像把这层烘焙翻到反号，与预制件值互相抵消：HairClip2 X [−0.025, 0.019]、HairClip1 [−0.025, 0.02]、Display [−0.019, 0.019] 三者重合，Z 上 HairClip2 落在 −0.002（屏幕背面）——与 HLSL 逐位一致。
* **判断手性不要靠猜，用包内自带的不对称特征**：`Display` 的 `m_Materials` 顺序是 Volume/Mic/Viseme，Assimp 的子网格顺序与之一致；Unity 里麦克风图标在 +X 端，而 Assimp 数据里对应子网格在 −X ⇒ 直接证明 Assimp X 相对 Unity 镜像。比对照截图可靠，也不需要开着 Unity。
* **坐标轴 gizmo 是自检手段，但要看清它测的是什么**：它和模型画在同一空间里，单独看无法判断「数据镜像」——数据镜像会带着它一起翻。判据必须两条并用：(1) 上面那种包内特征顺序；(2) Unity 对应视图里坐标球的方向。实时窗口右上角与 HLSL 每格左下角各有一副，模式条「坐标轴」开关控制。
* **层级矩阵链的组合顺序**：`WorldMatrix` 里 `chain` 是 root→target，而 System.Numerics 是行向量约定（`v * M`），必须先乘自身再乘父级，即 `for (i = last; i >= 0; i--) matrix *= LocalMatrix(chain[i])`。写成 `foreach (node in chain)` 得到的是 `L_root * … * L_target`（列向量顺序），根节点一有旋转，子节点的 local position 就会落到错误的轴上——AudioHairClip 的 `HairClip2` 位移 (0.02, −0.001, 0) 在根节点 RotX=90° 下本应变成世界的 (0.02, 0, −0.001)，错序后变成 (0.02, −0.001, 0)，屏幕上看着就是往反方向偏。**`GlbBuilder` 和 `HlslSceneBuilder` 是两份同名实现，改一处必须同步另一处**，改完要递增 `build-vN` 否则旧 GLB 一直命中缓存。
* **子网格识别不能靠「画全部」兜底**：`m_Mesh` 引用的是 FBX 内部的 **fileID**，而 fileID→名字 的映射存在工程的 `Library` 里、**不在 `.fbx.meta`**（`internalIDToNameTable` 通常是 `[]`）。所以 `ResolveSubAsset` 经常返回空，`PreviewMesh.SubMesh` 为空是常态。此时若兜底成「返回 FBX 全部网格」，一个多节点 prefab（AudioHairClip 的 `HairClip1`/`HairClip2`/`Display`）的每个节点都会把全部网格各画一遍 → 部件重影、看着像位置错乱（每格 12 次绘制而不是正确的 5 次）。正确做法：先按 `SubMesh`、再按**节点自身名字**匹配（归一化：只留字母数字、小写、去掉结尾 `mesh`，于是 Unity 的 `Hair Clip 1` 能对上 FBX 的 `HairClip1Mesh`）；只有「整个 FBX 就一个网格」或「这个 prefab 只有一个带网格节点」时才允许画全部。两条判据缺一不可——一刀切禁兜底会让 6 个包变成空白（可渲染数 35→29），补上节点数判据后回到 34。
* **矩阵约定要按库的实际行为来，别凭直觉补转置**：System.Numerics 是行向量（`v * M`），glTF 线上格式是列主序。但 **SharpGLTF 的 `NodeBuilder.LocalMatrix` 已经自己做这个转换**——再手动 `Matrix4x4.Transpose` 会把平移从第四列挪到第四行，序列化后平移直接变成 `(0,0,0)`，子物体全部叠回原点。HLSL 侧则**必须**转置（`BuildEngine` 每处都转），因为那里是手写常量缓冲。判断依据永远是读出来的 GLB 矩阵，不是规范条文。
* Unity 场景视图 Front 的相机其实在 **+Z** 侧朝 −Z 看（由「X 指左 + Y 指上」反推），不是常见的 −Z；Iso/Persp 是 X 左下、Z 右下。别按 OpenGL 习惯假设 Front 在 −Z。
* **验证这类问题的正确工具**：`C:\temp` 下一个只引用 `AssimpNet` 的小 console 把 FBX 的节点树 + 每个子网格的 local/world AABB 打出来，再配一个解析 `model.glb` JSON chunk 的 node 脚本做同样的事。两边都是数字，一眼就能看出「偏了两倍」还是「镜像反号」，比反复截图猜快得多——截图只能证明「看着像」，证明不了 0.02 这种量级的差异。
* **`int`/`bool` uniform 必须按整数写进 `$Globals`**：`BuildGlobals` 原先不分类型一律 `BitConverter.TryWriteBytes(float)`，于是着色器里声明成 `int` 的属性读到的是浮点位模式（1.0 → 1065353216）。AudioHairClip 的两个贴花就是这么消失的：`Viseme.shader` 里 `_Viseme` 是 `int`，巨大值让 `step(1..15, viseme)` 全为真，链式 lerp 最后落到 `_Color15 = (0,0,0,0)`，`col.a = min(col.a, 0)` → 绿色端盖整个透明掉；`ToggleMask.shader` 的 `int _Toggle` 同理，`mask *= 1-_Toggle` 变成巨大负数，麦克风图标也废了。修法：反射时取 `variable.VariableType.Description.Type`（**`ShaderVariableDescription` 本身没有 `Type` 字段，必须绕 `VariableType`**），存进 `ShaderUniform.Type`，打包时命中 `Int`/`Bool` 就写 `(int)MathF.Round(value)`。只有非零整数属性会改变结果，而那些本来就是坏的，所以是纯修复。反射 JSON 变了要递增 `hlsl-vN`。
* **`.mat` 里存的属性不等于着色器声明的属性**：Unity 只认 Properties 块里声明的，`m_Colors`/`m_Floats` 里那些残留项（从 Standard 模板继承来的 `_Color=白`、`_Cutoff`、`_Metallic`…）一律忽略。我们全量读进 `PreviewMaterial.Colors`，`BaseColor` 又默认白色不透明，于是 GLB 把这些自定义材质当成不透明白色面片——AudioHairClip 的 `Display_Volume`/`Display_Mic`/`Display_Viseme` 三个共面 quad 糊成一块白膜盖住屏幕。判据：`ApplyShaderProperties` 读到了 `.shader` 源码、Properties 里既没有主色（`_Color`/`_BaseColor`/`_MainColor`/`[MainColor]`）也没有可用的主贴图 → 这个材质的可见输出是**算出来的**，标 `PreviewMaterial.Procedural`。
* **Procedural 材质在实时视图画成半透明占位**（用户选定）：`GlbBuilder` 对 `Procedural && 无主贴图 && AlphaMode == "blend"` 输出 0.25 alpha 的 BLEND 材质，只做占位、不猜颜色。**必须限定在 blend**：opaque 的自定义程序化材质（实测 Yodare `teller.shader` 的 `TellerA`）画穿比画白更糟，保持实心。精确渲染（HLSL）不受影响，它跑的是真着色器。改 `PreviewMaterial` 的字段要递增 `material-vN`（SQLite 里旧行没有新字段），改 GLB 生成要递增 `build-vN`。
* **回退必须报出「谁」和「为什么」**：只给一个 `X-Akashic-Fallbacks` 数字，用户看到的是「自定义着色器没生效」却无从判断是缺文件、编译失败还是内置着色器。`HlslPreviewService` 现在为每个回退材质生成 `材质名 · 原因`（编译失败会把 fxc 的首段错误带上），走 `X-Akashic-Fallback-Detail`（URL 编码，响应头只能放 ASCII）交给视窗，底部状态条显示前两条、悬停看完整清单。原因是三类：**内置着色器**（Unity 自带，包内根本没有这个文件）、**找不到着色器文件**（GUID 解析不到）、**编译失败**。
* **内置着色器要认出来，别当成「找不到文件」**：`m_Shader` 的 GUID 是 `0000000000000000f000000000000000` / `…e000000000000000` 时，文件当然找不到，但这是 Unity 自带的。按 fileID 映射名字（46 = Standard、10752/10753 = Sprites、10755 = UI/Default），并分两档近似：Standard/legacy lit 走原来的光照近似，Sprites/UI/Unlit 走 `fallback-unlit`（`AKASHIC_UNLIT` 宏，只做 `tex2D * _Color` + 裁剪，不参与光照）——不然 UI 图集在 HLSL 里会被打光，看着和 Unity 完全不同。fileID 表不认识的一律显示成 `内置着色器#<id>`，会出现在回退清单里，方便按实测补表。
* **模型 prefab 的 FBX 内嵌材质要按名字回落到包内同名 `.mat`**：`UnyStylus.prefab` 这类纯模型 prefab 的 YAML 里没有 `m_Materials`，材质在 FBX 里，于是 `HlslSceneData` 只能现造一个「没有着色器」的合成材质——包里明明有 `UnyStylus.mat`/`UnyStylusColor.mat` 也用不上，颜色、贴图、着色器全丢。现在合成之前先按名字（大小写无关、只留字母数字）在包内找 `*.mat`，解析出带 `Shader` 的那个就用它；这正是 Unity 导入器的默认行为。另外这里原本还有一处 `material = material ?? generated`，当 prefab 材质存在但没着色器时会把生成的那个丢掉，等于永远修不好。
* 材质编译失败或 URP 着色器降级为内置 `FallbackLit.hlsl`（主贴图/颜色/自发光/法线/裁剪），并计入 `X-Akashic-Fallbacks` 供视窗提示；lilToon 的透明/裁剪/描边由材质选中的 `lts*` 变体与 `_TransparentMode`、`_UseOutline`/`_OutlineWidth`、`_SrcBlend` 等真实状态决定。Shader 常量里的 `UNITY_FOG_LERP_COLOR` 必须写成 `lerp(fogColor, col, fogCoord)`，否则 fogFactor=1 时整模型会变黑。
* 包内自带着色器的三类降级，理由要分开写清楚，否则排查时看不出差别：
  * `.shadergraph` 是 Shader Graph 产物，没有 ShaderLab 源码可解析。
  * `#pragma surface` 的 Surface Shader，Pass 由 Unity 生成，拿不到可复用的 HLSL。
  * 真的没有 Pass。
  * 缺 Unity 内建函数时补进 `UnityShim.hlsl` 并递增 `ShaderEnvironment.Version`。注意 **`EncodeFloatRG` 的返回类型各包不一致**：Yodare 的 `teller.shader` 写的是 `float4(EncodeFloatRG(pos.z), 3/255.0, 4/255.0)`，说明它要的是 `float2`（fxc 会拒绝 `float4(float, float)`，报 X3014）。用构造参数个数反推签名，别照抄 Unity 的 `float` 版本。
* 「这个内容里没有可渲染的网格」绝大多数是真没有：SUIMONO 的 `fx_*.prefab` 只有 ParticleSystem（`!u!198/199`），nadenadeEx 的 prefab 只有 MonoBehaviour（`!u!114`）。判定方法是数 prefab 里有没有 `!u!33`/`!u!137`，别当成解析漏网格去改提取器。
* Unity YAML 里的 `1e+30` 一类哨兵值（Atlas 的地形缩放、ABT 的碰撞体）解析成 float 会溢出成 `+∞`，写 `scene.json` 时 `JsonSerializer` 直接抛异常、整个构建 500。`PrefabParser.ParseFloat` 必须用 `float.IsFinite` 拒掉非有限值。同理 Unity 材质可以是 HDR 颜色，glTF 的 `baseColorFactor` 只接受 [0,1]，`GlbBuilder` 写入前要夹。
* 预览缓存根目录在 `PreviewCachePaths`：默认 `<侧车目录>/cache/preview`（跟着软件走，不再吃 C 盘），设置页可改；目录不可写时回退 `%LOCALAPPDATA%\Akashic\preview`。四处用到缓存的地方（`PackageContentCache`、`PreviewService`、`HlslPreviewService`、`MapPreviewRoutes`）一律取 `PreviewCachePaths.Root`，不要再各自拼路径。
* **版本号必须回收，不能只递增**：`VersionedUnder(root, section, version)` 在创建 `<section>/<version>` 的同时删掉同族的旧版本目录。**按「族」判断，不按整名**——族是版本号里最后一个 `-v` 之前的部分，所以 `hlsl` 容器下的 `hlsl-vN` 与 `shim-vM` 互不误伤；如果按整名比较，`shim-vN` 会在每次升 `hlsl-vN` 时被连带删掉，内置 include 桩全部要重编。这条是踩过才知道的。
* **预览缓存有总量上限，且上限可设**（`AppSettings.previewCacheQuotaBytes`，默认 8 GB，界面给 2/4/8/16/32 GB，`PreviewCachePaths` 再钳到 2–128 GB——**UI 档位与常量上限是两回事**：32 GB 是给人选的天花板，128 GB 只是给「从 gist 同步回来的历史值」留的容身空间，免得一次同步就把配置判成非法值打回默认）：侧车启动时 `Program.cs` 从 `settings.json` 读出目录与上限一起交给 `PreviewCachePaths.Configure`，改目录不再把上限打回默认。每次解包后调 `EnforceQuota()`，按 `<section>/<version>/<leaf>` 粒度、以目录写入时间从旧到新删除，直到落到配额以内；参与回收的只有 `packages/builds/archives/textures` 四段，`hlsl` 的编译缓存不在其中（重编一次要几十秒，比省几百 MB 贵）。配额只在**超过**上限时才动手，所以正常用不到；在设置页调小上限会立刻按新配额回收一次，界面上的「当前占用」跟着变。
* 为什么这两条是硬要求：`extract-vN` 把包内**全部**资产落盘（为了列全资产树），SUIMONO 一个包就几十 MB；旧版本目录不清会一直堆（实测 `packages/` 堆到 11 GB / 87 个目录），直接把 E: 盘写满，材质球接口返回 422「磁盘空间不足」——看着像代码坏了，其实是没地方写。
* **不可信 shader 源码有三道闸门**（素材包的 `.shader` 是第三方作者写的文本，等于别人决定的文件会被打开读入）：
  1. **体积**：单个 `.shader` 超过 2 MB 直接判「着色器源码过大」走 fallback（`ShaderLabExtractor.MaxShaderBytes`）；拼好的单个 Pass 源超过 4 MB 只跳过那个 Pass，不整体失败。上限不是安全阈值而是「正常 shader 没有这么大」的判据——lilToon 全家最大的一支不到 300 KB。
  2. **include 沙箱**：`ShaderIncludeResolver` 在喂 fxc 之前自己把 `#include "x"` 就地展开，搜索顺序是 shader 目录 → 包内容根 → shim 根 → UnityStubs，**含 `..` 段或绝对路径一律拒绝**（不打开文件，只记一条 `rejected` 并让这个 Pass 失败）；解析出来的全路径还必须落在允许根内。于是 fxc 只看到一个自包含文件，`/I` 只留 shim 根，`contentRoot` 与「shader 目录的父目录」这两个宽根已经去掉。代价是少数靠 `../` 跨目录取 include 的包会退化到 fallback——它们本来就在 Unity 工程内布局，素材包里极少见。
  3. **超时分级**：单 Pass 60 秒、同一 shader 累计 180 秒后整条放弃，写进 `-fb` 的 meta。原来固定 90 秒 × 全局串行，一个病态 shader 能把所有预览拖住。
* **编译不再全局串行**：`KeyedSemaphore` 按 shader 路径分键（同一 shader 串行、不同 shader 并行），全局上限 `min(4, CPU/2)`。先把内存缓存换成 `ConcurrentDictionary`，否则并行下会撞字典。**前提是 A1 已经收紧**：没有源码上限和 include 沙箱时，4 个死循环 shader 能同时吃满 4 个核。
* **失败原因分类型而不是只有一句话**：`CompiledShader.FailureKind` 取 `size`/`include`/`timeout`/`compile`/`unsupported`，随 fallback 一起写进 `meta.json`，渲染时按类型计数进响应头 `X-Akashic-Reject-<kind>`，与既有的 `X-Akashic-Fallbacks`/`-Detail` 并列。排查「为什么这颗球用了近似材质」不用翻日志。
* **预览链路的回归基线**：`tests/Akashic.Tests`（xUnit）覆盖 `ShaderLabExtractor`（含体积闸门、URP/Surface 判定、Outline 识别）、`ShaderIncludeResolver`（嵌套展开、越界/绝对路径拒绝、缺失保留、循环与深度上限）、关键字白名单与缓存键稳定性、`PreviewCachePaths` 的族回收与配额钳制。`scripts/test-all.ps1` 把 `dotnet test` 与 `flutter test` 串成一条命令，发版前必须全绿。这批用例第一天就抓到一个真 bug：`PropertyRegex` 的 `[^)]+` 吞不下 `Range(0,1)` 这种带括号的类型，`_Glossiness` 之类的属性一直没被解析出来，属性默认值因此缺失。
* **渲染层也有回归了**（A2 第 3 步，2026-10-29）：`GoldenRenderTests` 走 WARP（`D3D11PreviewRenderer.Create(softwareOnly: true)`，与硬件路径同一套代码，只是 `_sampleCount=1` + 三线性），对两个 fixture 出图——`fixtures/shaders/golden.shader`（unlit + `_MainTex`，故意不含 `_Time`，否则每次像素都不同）配 `fixtures/textures/golden-checker.png`（四色 64×64），以及内置 Standard 球。断言分两层：结构层（非背景像素占比 15%–95%、四个色块各 ≥100 像素、`Fallbacks==0`、`Textures==1`）与像素层（原始 RGBA 的 SHA256 对 `docs/preview-golden.json`）。改渲染器后确认差异用 `$env:AKASHIC_GOLDEN_UPDATE=1` 重跑一次重新生成；连续两次跑已验证哈希稳定（约 0.3 s/次）。测试工程引用 `Akashic.Host` 时必须显式把 `preview/hlsl/*.hlsl` 与 `tools/fxc/*` 拷进输出目录：`FxcTool.Find()` 里那条 `..\..\..\..\src\...` 回退路径的深度算错了，从任何 bin 目录都拼不到，别指望它兜底。
* **全库指标回归脚本** `scripts/preview-regression.ps1`（A2 第 5 步，2026-10-29）：起一个 `--port 0` 的侧车，按固定包清单跑 list→build→render 与材质球，汇总可渲染数、零回退数、绘制次数、回退材质数、失败类型、构建/渲染墙钟与贴图显存峰值，写 `docs/preview-regression-baseline.json`。默认模式是**只准变好**：`renderable`/`zeroFallback` 不得下降，`buildFail`/`fallbackMats`/`reject/<kind>` 不得上升。渲染 422「没有可渲染的网格」单独记成 `notPreviewable`——SUIMONO 的 3 个 fx 与 ABT 的 Collider 只有粒子/碰撞体，属于预期内，不该混进失败里。首轮基线（5 包 11 prefab）：可渲染 7、零回退 2、绘制 86、回退材质 19、材质球 24/24、构建失败 0、预期内不可预览 4、峰值显存 201 MB；ABT 有 2 张贴图解不出来（`textures=9|20|2`），就是 `.dds`/`.exr` 那一类。`scripts/test-all.ps1 -Regression` 把它接进发版流程（要真实素材库，所以默认不跑）。
  * 清单现在含 **`Starlight of Irua`**（2026-10-30，6 包 14 prefab：可渲染 10、零回退 5、绘制 101、回退材质 19、材质球 30/30、构建失败 0、预期内不可预览 4、峰值显存 467 MB）。加它是因为原来 5 个包**全都在系统 ANSI 代码页里编得出来**，正好看不见 Assimp 那个路径 bug——这个包整棵树在 `Assets\이루아_IruaStore\` 下，是 `AssimpPaths` 的守门包。基线里记了包清单，`-Packages` 传子集时只报数不对比（以前子集会假报「metrics got worse」）。
* 写这个脚本踩到的四个 PowerShell 5.1 坑，动这类脚本前先看一眼：① `ConvertFrom-Json` 不把 JSON 数组摊平，整份数组会变成一个对象（属性取出来全是 `System.Object[]`），要接 `| ForEach-Object { $_ }`；② `Invoke-WebRequest -Body '字符串'` 按 ASCII 编码，中文资源键直接变 `?`，服务端回「资源不在目录里」，看着像产品 bug——必须 `-Body ([Text.Encoding]::UTF8.GetBytes($json))`；③ 无 BOM 的 UTF-8 脚本按 GBK 读，中文全角括号 `（` 的尾字节会吞掉后面的引号导致整篇解析失败，所以脚本输出统一用 ASCII；④ 侧车的 stdout 是握手通道，管道一旦没人持续排空，4 KB 写满就把宿主进程卡在写日志上（表现是脚本静默挂死）——用 `Start-Process -RedirectStandardOutput 文件` 最省事；也别想着「先同步 `ReadLine()` 再 `BeginOutputReadLine()`」，同一个流不允许混用两种读法。
* 侧车日志：`Program.cs` 里 `ClearProviders()` 是必须的（stdout 要留给握手 JSON），但必须紧跟着 `AddProvider(SidecarFileLoggerProvider)` 落到 `%LOCALAPPDATA%\Akashic\logs\host.log`（2 MB 滚动一份备份），否则线上问题完全没有现场。异常由 `UseExceptionHandler` 统一转成带 `detail` 的 Problem JSON——不加它，未捕获异常在 Production 下是**空 body 的 500**，界面只能显示一句笼统失败。
* **WebGL 上下文与页面可见性**（C1，2026-10-29）：`viewer.html` 给主画布和坐标轴画布都注册 `webglcontextlost`（**必须 `preventDefault()`**，否则上下文永不恢复、画面永久空白到整页重载为止）与 `webglcontextrestored`（恢复后按当前模式重画，HLSL 侧补发一帧）；`setAnimationLoop` 在 `document.hidden || glLost` 时直接返回，切去别的页面就不再占 GPU，回到可见且开着 HLSL 时补一帧。Flutter 侧 `_refreshRenderInfo` 在 `_pickerVisible` 时直接返回——「选择要预览的内容」是覆盖整个内容区的，视图被挡住时每 900 ms 一次 `evaluateJavascript` 纯属白烧。**「模型级 dispose」这条其实不存在**：换内容走的是 `_build` → `_viewer = null` + 新 `viewerUrl`，WebView 控件整个重建，旧文档的 GL 资源由浏览器回收，不需要在 JS 里手工 dispose。
* **GPU 贴图缓存现在有上限**：`D3D11PreviewRenderer._textureCache` 原来只增不减，一个形象 ~201 MB，连看几个包就一直堆在显存里。现在记 `_textureTotalBytes` 与每张的最后使用时间，每次重建 GPU 会话之后按 LRU 淘汰「当前会话不再引用」的 SRV（默认 512 MB，`Create(softwareOnly, textureCap)` 可注入；白/法线/黑三个默认贴图永不淘汰，因为它们就是 `_whiteView` 那几个对象）。缓存总量走 `X-Akashic-Texture-Cache`（MB），回归脚本记成 `textureCacheMbMax`。实测 5 包 7 次会话累计 427 MB 还没触顶，所以不会淘汰——这是刻意的：近期看过的模型切回去仍然秒开。
* **服务端的场景缓存同样收到 2 个会话**：`HlslPreviewService._scenes` 的 value 是整个 prefab 的顶点/索引字节数组（一个 341 节点形象几十 MB 常驻内存），原来按 build id 只增不减，`_artifacts` 同理。现在 `_recent` 维护最近 2 个 sessionId，超出就整批移除（含 `|gizmo` 变体）；代价是切回很久之前看过的模型要重解析一次 FBX（几百毫秒）。
* 这两条的验证：`TextureCacheTests` 用 1 字节上限连渲两个会话（带贴图 → 不带贴图），断言第二次渲染后缓存归零、而当前会话引用的贴图即使在 1 字节上限下也保留。真机侧在发布版里 prefab → HLSL → 切换内容 → 换 FBX → HLSL，贴图数从 `9|201 MB` 变成 `7|159 MB`，两条路径都正常出图。
* **侧车内存的结论：不是托管泄漏**（#201，2026-10-29）。`GET /api/preview/diagnostics[?collect=1]`（回环 + token 保护）一次报出 `GC.GetTotalMemory`、`GCMemoryInfo.FragmentedBytes/TotalCommittedBytes`、三代回收次数、进程 private/working，以及各缓存计数（贴图 MB、`_scenes`、`_artifacts`、GPU Pass、已编译 shader、`PreviewService._parsers`）；带 `collect=1` 时先做两次 full GC 再报，用来区分「可回收的提交量」与真保留。实测连看 9 个包：托管堆在强制 GC 后始终 **7–21 MB**，`scenes`/`artifacts` 恒为 2，`parsers` 全程 0（这条路径根本不走 `PreviewService._parsers`，之前的怀疑不成立；Assimp 的 `AssimpContext` 本来就是 `using var`）。私有提交量 521 → 1662 MB 的大头是贴图与显卡驱动侧，且会随缓存回收回落。所以「每换一个模型涨 100 MB」是 .NET 大对象堆等不到压缩，不是泄漏。**别拿工作集当证据**：同一轮实测里它在 432–1055 MB 之间来回跳。
* 贴图上限据此从 1 GB 收到 **512 MB**。实测：Lime 一个包的贴图就 ~726 MB，渲染时缓存被顶到 1169 MB，下一个模型（Celestialspell）一来就压回 443 MB。也就是说这个上限管的是**跨模型的累积**，不是单次峰值——`TrimTextures` 在会话建好之后才跑，淘汰条件又是「当前会话不再引用」，所以单个模型自己的贴图不可能被自己挤掉。改成「建会话前先淘汰」能把峰值也压住，但那样同一个包内换 prefab 会白白重解一遍贴图，不值。试过给 `_gpuPasses` 加同款回收（换模型时丢掉不再引用的着色器对象），实测既没降低内存、又让每次换模型多付一遍创建开销，已撤回。
* **Assimp 的版本要按导出的 C 函数问，别去二进制里找版本号**（C5，2026-10-29）。`AssimpNet 4.1.0` 里那句「绑的是 assimp 5.0 时代 API」是错的，方向还反了：随包的 `runtimes/win-x64/native/assimp.dll`（6,173,696 字节，SHA-1 `f90cc12dad06d45d02cc3c22ca203977ae0c4d96`，文件时间 2018-10-29）实测 `aiGetVersionMajor/Minor/Revision` = **4.1.0**，横幅是 `(c) 2008-2017, assimp team` + `assimp.sourceforge.net`（sourceforge 域名是 GitHub 迁移之前的）。二进制里只有 `assimp v%d.%d.%d` 这个格式串，没有写死的版本号，`aiGetVersionString` 在 4.x 根本不导出——所以之前那次 `EntryPointNotFoundException` 不是加载失败，恰恰证明它是 4.x。要记版本就 P/Invoke 那三个导出（`CallingConvention.Cdecl`），这也是唯一能拿到确切数字的办法。
* **预览丢网格的真凶是 AssimpNet 把路径按 ANSI 编组，不是 FBX 格式**：`AssimpContext.ImportFile(string)` 走 ANSI 转换，超出系统 ANSI 代码页（本机 CP936）的字符全变 `?`，于是回 `Error importing file: Unable to open file "…\이루아\…"` 里那种带 `?` 的路径。实测抽 120 个 FBX（库内共 1019 个）：**114 成功 / 6 失败，6 个全部是韩文与 `♥` 目录**；同一份字节复制到 ASCII 路径立刻 12 个网格正常导入。纯中文路径（ABT 的 `Assets\汐音汉化\…`）不受影响，因为 GBK 本来就编得出汉字——这也是为什么全库回归脚本的 `draws` 前后一样、只有韩文/符号包变好。修法是 `AssimpPaths.Openable(path)`：纯 ASCII 原样返回 → `GetShortPathNameW` 拿 8.3 短名（逐段转换，兄弟文件解析仍然指着同一目录）→ 拿不到短名才复制到 `%TEMP%\akashic-models/<sha1[0..24]><ext>`。两条 `ImportFile` 调用点（`GlbBuilder.LoadScene`、`HlslSceneData.LoadScene`）都过它，缓存键与贴图路径继续用原始路径，不受影响。
  * 兜底那一步会丢 FBX 的同目录相对引用，但可接受：进 Assimp 的只有 `.fbx/.obj/.gltf/.glb`（`PackageContentCache.ModelExtensions`），全都是自包含格式，而贴图从来不由 Assimp 解析——两条路径都是按 Unity `.mat` 的路径去找贴图。
  * 发布版实测（`Starlight of Irua`，整包都在 `Assets\이루아_IruaStore\…` 下）：改前 3 个 prefab 全部「没有可渲染的网格」，`renderable=0 / draws=0`；改后 3 个全可渲染、`draws=15`、回退 0。生产走的是短名分支（跑完 `%TEMP%\akashic-models` 根本没被创建）；复制到临时目录那条分支单独验证过——出来的路径纯 ASCII、字节数一致、Assimp 能读出网格。`AssimpPathTests` 三个用例把两条分支与真实导入都钉住。
  * **换依赖这件事据此判定为不做**：计划里「新 FBX 变体解析失败率会随素材更新上涨」在这批素材里没有依据（失败 6/120 全是路径问题，格式侧 0 失败）。重新引入的触发条件是**纯 ASCII 路径下的 Assimp 解析失败开始成规模出现**，那时再评估 `Assimp.Net.VR`（assimp 5.x 封装）。
* C5 另两条核对过，记在这里免得再查一遍：`.cso`/反射那份缓存（`hlsl-vN`）在 `ShaderCompileService` 构造里就走 `PreviewCachePaths.VersionedUnder`（不是裸 `Directory.CreateDirectory`），`VersionedUnderReclaimsOnlyTheSameFamily` 是它的用例。**漏的是 `shim-vN`**：`ShaderEnvironment.Ensure` 用的是裸 `Directory.CreateDirectory`，实测 Release 缓存里 `shim-v6` 与 `shim-v7` 并存、Debug 里 `shim-v3/v4/v5` 三代同堂。改成也走 `VersionedUnder` 之后，族规则保证它只删 `shim-*`、`hlsl-*` 一个不碰，两侧都只剩当前版本，出图不受影响（`AudioHairClip` 仍 `draws=5`、材质球 4/4），用例是 `ShimSetupReclaimsItsOwnFamilyAndSparesTheCompileCache`。仓库根那个 `NVIDIA Corporation` 目录是某次 native 调用顺手建的空目录，`.gitignore` 已覆盖，目录当前为空。

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

