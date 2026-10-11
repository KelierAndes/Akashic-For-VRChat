# Akashic for VRChat & Unity

面向 VRChat VPM 的现代化 Unity 包管理桌面软件，基于 **C# (.NET 8)** 后端与 **WinUI 3 (Windows App SDK)** 前端构建，采用微软 **Fluent Studio 设计语言**。

---

## 🌟 核心功能

### 1. Unity 项目全生命周期管理
* **扫描与跟踪**：自动或手动添加本地 Unity 项目，监测项目最后修改时间。
* **Unity 版本识别**：自动解析 `ProjectSettings/ProjectVersion.txt`，精准识别项目使用的 Unity Editor 版本（如 `2022.3.22f1`、`2019.4.31f1`）。
* **清单探测与包状态检测**：同时支持 UPM (`Packages/manifest.json`) 和 VPM (`Packages/vpm-manifest.json`) 双清单解析，实时归类识别 Unity 官方包、VPM 社区包、Git 引用及 Local 本地包。

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

---

## 🏗️ 架构设计

```text
E:\Akashic\VPMManager\              # 项目根目录（磁盘位置沿用旧名）
├── Directory.Build.props              # 统一平台与 Windows SDK 编译参数
├── Akashic.sln                        # 解决方案入口
├── src/
│   ├── Akashic.Core/                  # 后端核心业务引擎 (.NET 8.0)
│   │   ├── Models/
│   │   │   ├── UnityProject.cs        # Unity 项目、已安装包状态模型
│   │   │   ├── VpmPackage.cs          # VPM 注册表源与包元数据模型
│   │   │   ├── VpmManifest.cs         # UPM/VPM 双清单序列化模型
│   │   │   ├── UnityPackageEntry.cs   # .unitypackage 资产解包模型
│   │   │   ├── UnityPackageLibrary.cs # 本地 .unitypackage 文件与导入台账模型
│   │   │   └── AppSettings.cs         # 用户设置模型
│   │   └── Services/
│   │       ├── IProjectService.cs & ProjectService.cs
│   │       ├── IVpmRegistryService.cs & VpmRegistryService.cs
│   │       ├── IPackageInstaller.cs & PackageInstaller.cs
│   │       ├── IUnityPackageExtractor.cs & UnityPackageExtractor.cs
│   │       └── IUnityPackageLibraryService.cs & UnityPackageLibraryService.cs
│   │
│   └── Akashic/                       # 前端界面 (WinUI 3 / Windows App SDK)
│       ├── app.manifest               # DPI 感知与 OS 兼容声明
│       ├── App.xaml / App.xaml.cs     # 依赖注入容器 (DI) 与应用入口
│       ├── MainWindow.xaml / .cs      # Fluent NavigationView 导航与 Mica 材质
│       ├── ViewModels/                # CommunityToolkit.Mvvm 双向数据驱动
│       │   ├── ProjectsViewModel.cs
│       │   ├── ProjectDetailViewModel.cs
│       │   ├── PackageBrowserViewModel.cs
│       │   ├── ImportPackageViewModel.cs
│       │   └── SettingsViewModel.cs
│       └── Views/                     # Fluent Studio 标准页面
│           ├── ProjectsPage.xaml      # 项目卡片列表与添加/移除
│           ├── ProjectDetailPage.xaml # 项目已安装包管理与 VPM 包一键安装
│           ├── PackageBrowserPage.xaml# 仓库全部可用包检索与详情面板
│           ├── ImportPackagePage.xaml # .unitypackage 解包预览与勾选注入
│           ├── SettingsPage.xaml      # 仓库源增删与环境配置
│           └── AboutPage.xaml         # 软件介绍与技术栈
```

---

## 🚀 编译与运行

### 环境要求
* Windows 10 (1809 及以上) 或 Windows 11
* .NET 8.0 SDK
* Windows App SDK 1.5

### 命令行编译与启动
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
