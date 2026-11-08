# 啟動失敗：缺少 .NET 8 ASP.NET Core / .NET Runtime（Windows x64）

## 症狀

啟動 Akashic for VRChat & Unity 後，主視窗內嵌顯示 host 的啟動錯誤：

```
You must install or update .NET to run this application.
App: C:\Users\<user>\Downloads\Akashic\Akashic\host\Akashic.Host.exe
Architecture: x64
Framework: 'Microsoft.AspNetCore.App', version '8.0.0' (x64)
.NET location: C:\Program Files\dotnet\

The following frameworks were found:
10.0.14 at [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
```

應用程式無法載入任何專案，UI 停留在錯誤對話框。

## 環境

- Windows 11 x64（build 26300）
- Akashic for VRChat & Unity（BOOTH 下載版，2026-11-08 取得）
- 修復前已安裝的 shared frameworks：
  - `Microsoft.NETCore.App` 8.0.21、10.0.14
  - `Microsoft.AspNetCore.App` 10.0.14
  - `Microsoft.WindowsDesktop.App` 10.0.14

## 原因

1. `host/Akashic.Host.runtimeconfig.json` 指定 `tfm: net8.0`，並依賴
   `Microsoft.NETCore.App 8.0.0` 與 `Microsoft.AspNetCore.App 8.0.0`。
2. .NET 的預設 roll-forward 原則**不會跨 major 版本**，因此機器上已有的
   .NET 10（10.0.14）無法滿足 net8.0 的需求。
3. 機器上完全沒有 `Microsoft.AspNetCore.App 8.x`，故第一階段報缺
   ASP.NET Core 8。
4. 補裝 ASP.NET Core 8.0.33 後出現第二階段錯誤：改報缺
   `Microsoft.NETCore.App 8.0.33`。原因是 `Microsoft.AspNetCore.App 8.0.33`
   依賴**同版本**的 base runtime，而機器上的 base runtime 仍為舊的 8.0.21
   （8.0.21 < 8.0.33，patch 層級亦不滿足）。

結論：安裝包未內含（self-contained）其所需的 .NET 8 runtime，且機器上的
.NET 8 base runtime 版本過舊，需同時具備 **ASP.NET Core 8 與 .NET Runtime 8
且版本一致（8.0.33）** 才能啟動。

## 修復步驟

以系統管理員權限執行（winget 會觸發 UAC 授權）：

```powershell
# 1) 補裝 ASP.NET Core 8 Runtime (x64)
winget install --id Microsoft.DotNet.AspNetCore.8 --exact --silent `
  --accept-package-agreements --accept-source-agreements

# 2) 將 .NET 8 base Runtime 更新至與 ASP.NET Core 相同的 8.0.33
winget install --id Microsoft.DotNet.Runtime.8 --exact --silent `
  --accept-package-agreements --accept-source-agreements

# 3) 確認兩個 framework 皆為 8.0.33
dotnet --list-runtimes

# 4) 重新啟動 Akashic
```

若不使用 winget，可自官方下載對應安裝包：
- ASP.NET Core Runtime 8.0.33 Windows x64：`aspnetcore-runtime-8.0.33-win-x64.exe`
- .NET Runtime 8.0.33 Windows x64：`dotnet-runtime-8.0.33-win-x64.exe`

## 驗證

`dotnet --list-runtimes` 應包含：

```
Microsoft.AspNetCore.App 8.0.33 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.NETCore.App 8.0.33 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

重新啟動 Akashic 後錯誤對話框消失、`Akashic.Host` 處理程序正常啟動，
UI 成功載入專案清單（實測於 2026-11-08，Windows 11 x64）。

## 對維護者的建議

- 於安裝包／說明文件明列前置需求：.NET 8 ASP.NET Core Runtime（x64），
  且 base runtime 需與之同版本；或
- 改以 self-contained 方式發佈 `Akashic.Host`，避免依賴機器上既有的
  .NET 安裝狀態；或
- 於 UI 啟動 host 前偵測所需 framework，給出可一鍵安裝的引導，而非直接
  顯示 apphost 的原始 stderr。