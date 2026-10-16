# Akashic for VRChat & Unity

面向 VRChat VPM 的 Unity 包管理桌面软件。后端是 **C# (.NET 8)** 业务引擎，前端是 **Flutter (Windows 桌面)**，界面按 **新拟态 (Neumorphism)** 语言绘制：凸起的托盘、凹陷的列表、玻璃边缘高光，整窗可透明。

---

## 🌟 核心功能

### 1. Unity 项目全生命周期管理
* **扫描与跟踪**：自动或手动添加本地 Unity 项目，监测项目最后修改时间。
* **目录级检测登记**：指定一个根目录即可批量识别其中的 Unity 工程并登记，磁盘上消失的工程会在读取时自动清理。
* **新建工程**：按默认工程路径与项目模板创建，模板决定起点、Unity 版本决定可用模板，创建后可直接装入 VRChat SDK。
* **Unity 版本识别**：自动解析 `ProjectSettings/ProjectVersion.txt`，精准识别项目使用的 Unity Editor 版本（如 `2022.3.22f1`、`2019.4.31f1`）。
* **清单探测与包状态检测**：同时支持 UPM (`Packages/manifest.json`) 和 VPM (`Packages/vpm-manifest.json`) 双清单解析，实时归类识别 Unity 官方包、VPM 社区包、Git 引用及 Local 本地包。
* **分类、云端与排序筛选**：按世界 / 虚拟形象 / VRChat 工程 / 非 VRChat 工程分类，按是否已上传到云端过滤，支持名称与最近打开时间排序。
* **异步云端匹配**：列表先渲染再后台逐个比对蓝图 ID，卡片随匹配结果就地更新，不在启动时阻塞界面。

### 2. VPM 包管理与自动依赖解析
* **多源仓库对接**：默认支持 VRChat 官方精选源（`https://packages.vrchat.com/curated`），可在设置中动态扩展第三方社区源。
* **搜索与版本浏览**：支持按包 ID、显示名称、关键字、描述快速检索，支持多版本（历史版本及最新版本）浏览与详情查看。
* **自动下载与缓存**：自动从注册表下载 `.zip` 封包至本地缓存目录，避免重复下载。
* **递归依赖解析**：依据 SemVer 语义化版本规则，安装包时自动解析并递归拉取其所有前置依赖项（如 SDK Base、AV3 Emulator 等）。
* **一键安装与卸载**：自动解压到目标项目的 `Packages/<PackageName>` 文件夹，并同步更新 `vpm-manifest.json` 与 `manifest.json`。
* **复选框批量操作**：包表按多条件过滤后勾选，批量安装 / 卸载走同一条操作条。

### 3. UnityPackage 深度解包与选择性安装
* **基于 tar.gz 的流式解包引擎**：无需依赖 Unity Editor 运行环境，直接解析 `.unitypackage` 二进制归档。
* **GUID 与路径精确还原**：`pathname` 条目在包内的真实编码为 `资产路径\n标志位`，解析时仅取首行；据此还原真实目录层级，并为目录写入 `folderAsset` 版 `.meta`。
* **可视化资产清单与选择性安装**：以清单呈现所有待安装资产（附带文件名、文件大小、GUID 与 .meta 状态），支持**全选/取消全选/按需勾选**并一键安全写入指定项目的 `Assets/` 目录。
* **写入安全边界**：安装前对包内路径做归一化与越界校验，任何指向项目外部的路径都会中止安装。

### 4. UnityPackage 本地资产库与导入台账
* **库目录扫描**：在设置中添加一个或多个本地目录，递归收集其中的 `.unitypackage` 作为「可选包」，与 VPM 包、Unity 官方包一同列在项目详情的统一包表中。
* **手动添加**：导入页与项目页均可把单个 `.unitypackage` 文件加入包列表。
* **文件名解析**：自动从文件名剥离尾部版本号（如 `Magica Cloth 2 v2.3.1.unitypackage` → 名称 `Magica Cloth 2` / 版本 `2.3.1`）。
* **导入台账与一键撤销**：按项目记录导入过的包及其写入的全部相对路径，包表中据此显示「已安装版本」，卸载时精确删除这些文件与文件夹。

### 5. Booth 已购 / 免费商品导入与 Asset Store 对接
* **应用内登录**：Booth、Asset Store 直接在应用内的浏览器面板里登录，会话由系统 WebView2 承载，不跳外部浏览器。
* **Booth 三分区读取**：已购（`/library`）、礼物（`/library/gifts`）、免费领取（`/library/free_downloads`）各自翻页读取后合并，免费商品逐个访问商品页收集真实下载项。
* **同商品多版本折叠**：文件名标有版本号的按版本取最新，否则按 Booth 编号（上传顺序）取最新，折叠数量在列表中注记。
* **内容类型筛选**：默认"可用软件包"视图按关键词剔除演示视频、图集、漫画等无法入 Unity 的内容，可随时切换查看全部。
* **免费商品直连下载**：通过商品链接即可加入免费商品，免费文件无需登录下载；付费内容自动复用已登录会话。
* **Asset Store 导入页**：登录读取"我的资产"、下载即自动登记入库；本机缓存目录扫描与下载目录共同构成"已获取"视图。
* **封面磁盘缓存**：三站封面落盘复用，重启不再重复拉取；Booth 商品封面取自库卡片缩略图，Asset Store 优先按名称从商店检索，检索不到时从 `.unitypackage` 里流式提取 `preview.png` 兜底。

### 6. VRChat 数据同步与本地工程双向跳转
* **我的内容读取**：通过站点自身的 JSON 接口拉取账号下的世界与虚拟形象，含名称、描述、可见性、容量与封面，支持在界面内直接编辑并回写。
* **本地缓存**：内容清单与封面落盘缓存，未登录或网络异常时仍可浏览上一次的结果，并标注缓存时间。
* **蓝图 ID 扫描**：解析工程 `Assets/` 下的场景、预制体与资产，识别 `avtr_…` / `wrld_…` 蓝图标识（覆盖 SDK 直接写入与预制体覆盖两种写法）。
* **双向跳转**：与云端清单求交集后，项目卡片可跳到 VRChat 云端管理页，内容行可跳回本地工程并高亮定位；交集判定避免把别人的 ID 误标成本工程。

### 7. 项目模板
* **官方模板自动接入**：直接读取 VRChat Creator Companion 自带的 `VRCTemplates`（世界 / 虚拟形象 / Base，2022 与 2019 两代），按官方骨架原样复制，并按模板声明的 `~3.10.x` / `~3.4.x` 区间安装对应 SDK 版本。
* **自建模板**：把调好的工程存为模板，仅保留 `ProjectSettings` 与 `Packages` 清单，软件包交由 Resolver 重新拉取；未安装 Creator Companion 时回退到内置骨架。

### 8. 备份与版本控制
* **关键资产打包**：把 `Assets` 与 `ProjectSettings` 等关键目录压缩为独立备份文件，并可从备份还原。
* **可选剔除软件包**：备份时可只保留清单而不打包 `Packages/` 下的软件包内容，得到更轻量的备份。
* **git / Unity Version Control**：识别工程托管方式，git 工程走 `git` + LFS，UVCS 工程离线读 `.plastic` 报状态，两者各自给出提交 / 同步入口。
* **依赖自检**：缺少 `git` / `cm` / `gh` 时在设置页给出提示与一键安装入口。

### 9. GitHub 账号与配置同步
* **浏览器一键登录**：Loopback + PKCE 的 OAuth 流程——应用开一个随机端口的本地回调，用户在浏览器里点一次「Authorize」即完成，全程不接触密码或令牌。OAuth App 凭据以派生密钥加密内置在程序里，用户侧不出现任何配置项。
* **端到端加密的配置备份**：设置、三站登录会话、清单与台账打包写入一个私有 gist。密钥由 GitHub 账号的永久数字 id 与内置盐派生，换机登录同一账号即可自动还原，不需要记住任何同步口令；明文与密钥都不上传。
* **可选关闭**：登录 GitHub 与配置同步彼此独立，关掉同步仍能拉取私有外部仓库包。
* **凭据边界**：gist 标识与地址只在内部使用，不出现在界面、按钮或日志里；会话 Cookie 只用于各站点各自的私有 HTTP 客户端，不进入共享请求管道。

### 10. 界面与视觉
* **新拟态外壳**：左侧图标栏 + 可折叠导航面板 + 凹陷页面托盘，凸凹面靠双向阴影与内阴影描边区分。
* **亮暗主题**：浅色 / 暗色 / 跟随系统三档，墨色、玻璃高光与阴影强度各自成套。
* **全局透明度**：一个滑条同时驱动窗口底板、侧栏托盘与弹出面板（10 %–60 % 可调）。
* **可定制图标栏**：页面与置顶工程按 `nav:` / `proj:` 顺序存进设置，跨重启保留。

---

## 🏗️ 架构设计

前端只负责绘制与交互，一切读写文件的逻辑都在 C# 侧；两者通过本机回环 HTTP 通信。

```text
E:\Akashic\VPMManager\              # 项目根目录（磁盘位置沿用旧名）
├── Directory.Build.props              # 统一平台与 Windows SDK 编译参数
├── Akashic.sln                        # 解决方案入口（Core + Host）
├── scripts/build-app.ps1              # 一键出包：先建侧车，再建 Flutter 并注入
│
├── app/                               # 前端 (Flutter / Windows 桌面，包名 akashic_ui)
│   ├── assets/brand/                  # 应用图标（亮色版本，侧栏与窗口共用）
│   ├── lib/main.dart                  # 主题、字体回退（Segoe UI + 微软雅黑）与应用根
│   ├── lib/src/
│   │   ├── app_nav.dart               # 导航条目与顺序的唯一来源
│   │   ├── state/app_model.dart       # 全局状态：工程、会话、同步、模态槽位
│   │   ├── host/host_client.dart      # 侧车 HTTP 客户端（回环 + 令牌）
│   │   ├── host/host_models.dart      # 侧车 JSON 的 Dart 模型
│   │   ├── shell/                     # 无边框窗口外壳：标题栏、图标栏、导航面板、页面托盘
│   │   ├── ui/                        # 新拟态控件库：NeuBox / NeuButton / GlassSheet / 弹窗表单
│   │   ├── browser/browser_panel.dart # 应用内浏览器（登录 Booth / Asset Store）
│   │   └── pages/                     # 工程、详情、包浏览器、资源、导入、模板、
│   │                                  #   Booth、Asset Store、VRChat、恢复/同步、设置、关于
│   └── windows/runner/                # Win32 外壳：无边框窗口、DWM 边框、图标资源
│
├── src/Akashic.Host/                  # 侧车：托管 Core 并暴露 JSON 接口（随前端启动，仅回环）
│   ├── Program.cs                     # 监听 127.0.0.1 随机端口，随父进程退出
│   ├── LoopbackGuard.cs               # X-Akashic-Token 校验
│   ├── EventHub.cs                    # 长任务进度推送
│   └── Api/                           # 按域拆分的端点：工程、包、资源、账号、会话、
│                                      #   模板、备份/版本控制、GitHub 同步、启动状态
│
└── src/Akashic.Core/                  # 后端核心业务引擎 (.NET 8)
    ├── Models/                        # 工程、包、清单、Booth / Asset Store / VRChat 内容、
    │                                  #   模板、备份、版本控制、同步信封、设置等数据模型
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
        ├── ConfigSyncService.cs                     # 配置打包、PBKDF2 派生密钥与 AES-GCM 信封
        ├── GitHubCli.cs / GitHubLoopbackAuth.cs     # gh CLI 封装与 Loopback+PKCE 登录
        ├── GitHubOAuthApp.cs                        # 内置加密的 OAuth App 凭据
        ├── RailLayoutService.cs / ThemeService.cs   # 图标栏布局与视觉参数
        └── ...                                      # 各服务的 I*.cs 接口
```

---

## 🚀 编译与运行

### 方式一：从 Release 直接运行（推荐普通用户）
构建产物自包含全部运行时，无需安装 .NET、Visual C++ 运行时或 Flutter SDK：

1. 打开 [Releases](https://github.com/KelierAndes/Akashic-For-VRChat/releases/latest) 页面，下载最新版本的压缩包。
2. 完整解压到任意目录（保留同级的 `host/` 与 `data/`），双击 `akashic_ui.exe`。

### 方式二：从源码构建

#### 环境要求
* Windows 10 (1809 / 17763 及以上) 或 Windows 11，x64
* Flutter SDK（Windows 桌面支持）与随附的 Dart
* Visual Studio 2022 的「使用 C++ 的桌面开发」工作负载
* .NET 8.0 SDK
* NuGet 命令行——`tools/nuget.exe` 已随仓库携带，构建脚本会把它加到 `PATH`，供 WebView2 插件还原依赖

#### 一键出包
```powershell
# 1. 切换至项目根目录
cd E:\Akashic\VPMManager

# 2. 构建侧车 + Flutter，并把侧车注入产物目录
powershell -ExecutionPolicy Bypass -File scripts\build-app.ps1

# 3. 运行
.\app\build\windows\x64\runner\Release\akashic_ui.exe
```

只改了前端时可加 `-SkipHost` 跳过 C# 构建。脚本会先删掉 tree-shaken 的图标字体再构建——增量构建沿用旧字形表会让按钮图标变空白。

后端可单独编译：

```powershell
dotnet build Akashic.sln -c Release
```

---

## 📝 更新日志

### v1.2.0
* **前端整体重写为 Flutter**：新拟态界面（凸起托盘、凹陷列表、玻璃边缘高光），无边框窗口、可定制图标栏与亮暗主题；WinUI 3 前端下线，业务逻辑全部留在 C# 侧，由新增的 `Akashic.Host` 本机回环服务对前端提供 JSON 接口。
* **GitHub 浏览器一键登录**：Loopback + PKCE OAuth，点一次授权即完成；OAuth App 凭据加密内置，用户侧零配置。
* **端到端加密的配置同步**：设置、登录态与各类清单打包写入私有 gist，密钥由 GitHub 账号 id 派生，换机登录即还原；可选择只登录不同步。
* **备份与还原页**：关键资产打包与还原，支持备份时剔除软件包；工程按 git / Unity Version Control 分别给出版本控制入口。
* **封面磁盘缓存**：Booth / Asset Store / VRChat 封面落盘复用，重启不再重复请求；Asset Store 空封面回退到从 `.unitypackage` 提取。
* **视觉效果设置**：颜色模式三档 + 全局透明度滑条，同时作用于窗口底板、侧栏托盘与弹出面板。
* **中文字体与可读性**：补微软雅黑回退、提高正文基线字重与墨色对比。

### v1.1.0
* **VRChat 数据同步页**：读取账号下的世界与虚拟形象，界面内编辑回写，内容清单与封面本地缓存。
* **本地工程 ↔ 云端双向跳转**：扫描工程 `Assets/` 的蓝图 ID 与云端清单求交集，项目卡片与内容行互相定位。
* **项目模板页**：自动接入 Creator Companion 官方模板（2022 / 2019 两代世界与形象模板），支持把现有工程存为模板。
* **新建项目与目录检测**：按默认工程路径新建工程并按所选 Unity 版本匹配可用模板，支持一个目录批量检测登记。
* **项目页筛选与排序**：按分类、是否 VRChat 工程、是否已上传云端过滤，支持名称与最近打开时间排序；云端状态改为启动后后台逐个匹配，不再阻塞列表。
* **登录态加密持久化**：Booth / Asset Store / VRChat 会话以 DPAPI 加密保存，浏览器配置被重置后仍能恢复登录；修复 Cookie 域名丢失导致的部分站点重启后掉线。
* **重启后自动恢复登录态**：打开页面即载入保存的会话，Booth 额外用一次轻量请求确认会话是否仍然有效并直接标为已登录；只有确认失败才提示重新登录，网络问不到时保留"已恢复"而不误报未登录。
* **备份可选剔除软件包**：生成更轻量的备份。

### v1.0.0
* 首个公开发布版本：Unity 项目管理、VPM 多源与依赖解析、`.unitypackage` 流式解包与导入台账、Booth 三分区导入、Asset Store 导入、外部仓库包与列表封面。
