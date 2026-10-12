# Akashic for VRChat & Unity

面向 VRChat VPM 的现代化 Unity 包管理桌面软件，基于 **C# (.NET 8)** 后端与 **WinUI 3 (Windows App SDK)** 前端构建，采用微软 **Fluent Studio 设计语言**。

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

### 3. UnityPackage 深度解包与选择性安装
* **基于 tar.gz 的流式解包引擎**：无需依赖 Unity Editor 运行环境，直接解析 `.unitypackage` 二进制归档。
* **GUID 与路径精确还原**：`pathname` 条目在包内的真实编码为 `资产路径\n标志位`，解析时仅取首行；据此还原真实目录层级，并为目录写入 `folderAsset` 版 `.meta`。
* **可视化资产清单与选择性安装**：在 Fluent 界面中以清单呈现所有待安装资产（附带文件名、文件大小、GUID 与 .meta 状态），支持**全选/取消全选/按需勾选**并一键安全写入指定项目的 `Assets/` 目录。
* **写入安全边界**：安装前对包内路径做归一化与越界校验，任何指向项目外部的路径都会中止安装。

### 4. UnityPackage 本地资产库与导入台账
* **库目录扫描**：在设置中添加一个或多个本地目录，递归收集其中的 `.unitypackage` 作为「可选包」，与 VPM 包、Unity 官方包一同列在项目详情的统一包表中。
* **手动添加**：导入页与项目页均可把单个 `.unitypackage` 文件加入包列表。
* **文件名解析**：自动从文件名剥离尾部版本号（如 `Magica Cloth 2 v2.3.1.unitypackage` → 名称 `Magica Cloth 2` / 版本 `2.3.1`）。
* **导入台账与一键撤销**：按项目记录导入过的包及其写入的全部相对路径，包表中据此显示「已安装版本」，卸载时精确删除这些文件与文件夹。

### 5. Booth 已购 / 免费商品导入与 Asset Store 对接
* **Booth 三分区读取**：已购（`/library`）、礼物（`/library/gifts`）、免费领取（`/library/free_downloads`）各自翻页读取后合并，免费商品逐个访问商品页收集真实下载项。
* **同商品多版本折叠**：文件名标有版本号的按版本取最新，否则按 Booth 编号（上传顺序）取最新，折叠数量在列表中注记。
* **内容类型筛选**：默认"可用软件包"视图按关键词剔除演示视频、图集、漫画等无法入 Unity 的内容，可随时切换查看全部。
* **免费商品直连下载**：通过商品链接即可加入免费商品，免费文件无需登录下载；付费内容自动复用 WebView2 会话。
* **Asset Store 导入页**：登录读取"我的资产"、下载即自动登记入库；本机缓存目录扫描与下载目录共同构成"已获取"视图。
* **列表封面**：Booth 商品封面取自库卡片缩略图，Asset Store 封面取自我的资产行或商店搜索匹配；本地 .unitypackage 流式解包提取 `preview.png`（磁盘缓存），未入库缓存包按名称从商店搜索补齐。

### 6. VRChat 数据同步与本地工程双向跳转
* **我的内容读取**：通过站点自身的 JSON 接口拉取账号下的世界与虚拟形象，含名称、描述、可见性、容量与封面，支持在界面内直接编辑并回写。
* **本地缓存**：内容清单与封面落盘缓存，未登录或网络异常时仍可浏览上一次的结果，并标注缓存时间。
* **蓝图 ID 扫描**：解析工程 `Assets/` 下的场景、预制体与资产，识别 `avtr_…` / `wrld_…` 蓝图标识（覆盖 SDK 直接写入与预制体覆盖两种写法）。
* **双向跳转**：与云端清单求交集后，项目卡片可跳到 VRChat 云端管理页，内容行可跳回本地工程并高亮定位；交集判定避免把别人的 ID 误标成本工程。

### 7. 项目模板
* **官方模板自动接入**：直接读取 VRChat Creator Companion 自带的 `VRCTemplates`（世界 / 虚拟形象 / Base，2022 与 2019 两代），按官方骨架原样复制，并按模板声明的 `~3.10.x` / `~3.4.x` 区间安装对应 SDK 版本。
* **自建模板**：把调好的工程存为模板，仅保留 `ProjectSettings` 与 `Packages` 清单，软件包交由 Resolver 重新拉取；未安装 Creator Companion 时回退到内置骨架。

### 8. 登录态与数据安全
* **会话加密持久化**：Booth / Asset Store / VRChat 三站登录会话以 DPAPI（当前 Windows 用户）加密保存在本地，WebView2 配置目录被重置或清理后仍能恢复登录，无需重新登录。
* **按域还原 Cookie**：会话保留每条 Cookie 的域名与路径，回写时各归其域，避免跨站 Cookie 混用导致的登录态失效。
* **凭据边界**：会话 Cookie 只用于各站点各自的私有 HTTP 客户端，不进入共享请求管道；加密文件仅同一 Windows 账户可解密。

### 9. 备份
* **关键资产打包**：把 `Assets` 与 `ProjectSettings` 等关键目录压缩为独立备份文件。
* **可选剔除软件包**：备份时可只保留清单而不打包 `Packages/` 下的软件包内容，得到更轻量的备份。

---

## 🏗️ 架构设计

```text
E:\Akashic\VPMManager\              # 项目根目录（磁盘位置沿用旧名）
├── Directory.Build.props              # 统一平台与 Windows SDK 编译参数
├── Akashic.sln                        # 解决方案入口
├── src/
│   ├── Akashic.Core/                  # 后端核心业务引擎 (.NET 8.0)
│   │   ├── Models/
│   │   │   ├── UnityProject.cs        # Unity 项目、已安装包与云端蓝图 ID 模型
│   │   │   ├── ProjectTemplate.cs     # 项目模板（官方 VCC 模板与自建快照）
│   │   │   ├── UnityEditorEntry.cs    # 本机 Unity 编辑器条目
│   │   │   ├── BoothPurchase.cs       # Booth 已购 / 礼物 / 免费商品模型
│   │   │   ├── AssetStoreAsset.cs     # Asset Store 资产与缓存模型
│   │   │   ├── VRChatContent.cs       # 云端世界 / 虚拟形象内容与清单缓存
│   │   │   ├── ExternalPackage.cs     # 本地目录与外部仓库包
│   │   │   ├── UnifiedPackageItem.cs  # UPM / VPM / UnityPackage 统一包表行
│   │   │   ├── VpmPackage.cs          # VPM 注册表源与包元数据模型
│   │   │   ├── VccRepository.cs       # Creator Companion 存储库
│   │   │   ├── VpmManifest.cs         # UPM/VPM 双清单序列化模型
│   │   │   ├── UnityPackageEntry.cs   # .unitypackage 资产解包模型
│   │   │   ├── UnityPackageLibrary.cs # 本地 .unitypackage 文件与导入台账模型
│   │   │   ├── PackageOrigin.cs       # 包来源（本地 / Booth / Asset Store）
│   │   │   └── AppSettings.cs         # 用户设置模型
│   │   └── Services/
│   │       ├── IProjectService.cs & ProjectService.cs
│   │       ├── IVpmRegistryService.cs & VpmRegistryService.cs
│   │       ├── IPackageInstaller.cs & PackageInstaller.cs
│   │       ├── IUnityPackageExtractor.cs & UnityPackageExtractor.cs
│   │       ├── IUnityPackageLibraryService.cs & UnityPackageLibraryService.cs
│   │       ├── IUnityPackageIconService.cs & UnityPackageIconService.cs
│   │       ├── IBoothService.cs & BoothService.cs                  # Booth 会话复用与已购下载
│   │       ├── IAssetStoreService.cs & AssetStoreService.cs        # Asset Store 资产与缓存扫描
│   │       ├── IExternalPackageService.cs & ExternalPackageService.cs
│   │       ├── IVRChatSyncService.cs & VRChatSyncService.cs        # VRChat 内容读取与编辑
│   │       ├── IVRChatBlueprintScanService.cs & VRChatBlueprintScanService.cs
│   │       ├── BuiltinTemplates.cs                                 # 官方 VCC 模板发现与版本区间选择
│   │       ├── ISessionVault.cs & DpapiSessionVault.cs             # DPAPI 加密的登录会话存储
│   │       └── ThemeService.cs                                     # 亮暗主题切换
│   │
│   └── Akashic/                       # 前端界面 (WinUI 3 / Windows App SDK)
│       ├── app.manifest               # DPI 感知与 OS 兼容声明
│       ├── App.xaml / App.xaml.cs     # 依赖注入容器 (DI) 与应用入口
│       ├── MainWindow.xaml / .cs      # Fluent NavigationView 导航、Mica 材质与页面路由
│       ├── ViewModels/                # CommunityToolkit.Mvvm 双向数据驱动
│       └── Views/                     # Fluent Studio 标准页面
│           ├── ProjectsPage.xaml      # 项目卡片列表、筛选排序、新建与目录检测
│           ├── ProjectDetailPage.xaml # 项目已安装包管理与 VPM 包一键安装
│           ├── PackageBrowserPage.xaml# 仓库全部可用包检索与详情面板
│           ├── ResourcesPage.xaml     # 存储库 / 用户软件包 / 外部包 / 已获取资源
│           ├── ImportPackagePage.xaml # .unitypackage 解包预览与勾选注入
│           ├── BoothPage.xaml         # Booth 登录、三分区读取、下载与解析入库
│           ├── AssetStorePage.xaml    # Asset Store 登录读取我的资产与下载
│           ├── VRChatSyncPage.xaml    # 云端内容读取、编辑与跳转本地工程
│           ├── ProjectTemplatesPage.xaml # 官方与自建项目模板管理
│           ├── SettingsPage.xaml      # 仓库源、Unity 编辑器、路径与备份配置
│           ├── WebViewCookieSeed.cs   # 把加密会话按原域名回写 WebView2
│           └── AboutPage.xaml         # 软件介绍与技术栈
```

---

## 🚀 编译与运行

### 方式一：从 Release 直接运行（推荐普通用户）
无需安装 .NET 或 Windows App SDK，构建产物已自包含全部运行时：

1. 打开 [Releases](https://github.com/KelierAndes/Akashic-For-VRChat/releases/latest) 页面，下载最新版本的 `Akashic.zip`。
2. 解压到任意目录，双击运行 `Akashic.exe` 即可。

### 方式二：从源码构建

#### 环境要求
* Windows 10 (1809 及以上) 或 Windows 11
* .NET 8.0 SDK
* Windows App SDK 1.5

#### 命令行编译与启动
在终端中执行以下命令：

```powershell
# 1. 切换至项目根目录
cd E:\Akashic\VPMManager

# 2. 编译项目 (x64)
dotnet build Akashic.sln -p:Platform=x64

# 3. 运行应用程序
.\src\Akashic\bin\x64\Debug\net8.0-windows10.0.26100.0\Akashic.exe
```

也可以直接在 Visual Studio 2022 / 2026 中打开 `Akashic.sln` 并按 F5 启动调试。

---

## 📝 更新日志

### v1.1.0
* **VRChat 数据同步页**：读取账号下的世界与虚拟形象，界面内编辑回写，内容清单与封面本地缓存。
* **本地工程 ↔ 云端双向跳转**：扫描工程 `Assets/` 的蓝图 ID 与云端清单求交集，项目卡片与内容行互相定位。
* **项目模板页**：自动接入 Creator Companion 官方模板（2022 / 2019 两代世界与形象模板），支持把现有工程存为模板。
* **新建项目与目录检测**：按默认工程路径新建工程并按所选 Unity 版本匹配可用模板，支持一个目录批量检测登记。
* **项目页筛选与排序**：按分类、是否 VRChat 工程、是否已上传云端过滤，支持名称与最近打开时间排序；云端状态改为启动后后台逐个匹配，不再阻塞列表。
* **登录态加密持久化**：Booth / Asset Store / VRChat 会话以 DPAPI 加密保存，WebView2 配置被重置后仍能恢复登录；修复 Cookie 域名丢失导致的部分站点重启后掉线。
* **备份可选剔除软件包**：生成更轻量的备份。
* **亮暗主题切换**与 Booth / Asset Store 列表封面磁盘缓存等既有能力继续完善。

### v1.0.0
* 首个公开发布版本：Unity 项目管理、VPM 多源与依赖解析、`.unitypackage` 流式解包与导入台账、Booth 三分区导入、Asset Store 导入、外部仓库包与列表封面。
