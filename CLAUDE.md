# CafeWiFi

Windows 系統匣 App：走進咖啡廳就自動連上 Wi-Fi。同一品牌每家店的 SSID 都一樣（例如 `LouisaCoffee`），但每家店的密碼不同（通常是門市電話）。App 會認出你在哪家店，換上那家店的密碼。目前內建路易莎（512 家門市），設計上可以擴充到其他品牌。

- GitHub：https://github.com/redycc/CafeWifi（`origin`，分支 `main`）
- 使用者文件：`README.md`（繁體中文，目前移除了，使用者之後會自己重寫；舊版可用 `git show 912d9e8:README.md` 取回）
- Wi-Fi API 實測結果：`docs/spike-wifi.md`

## 溝通與文字

- 跟使用者一律用**繁體中文（台灣用語）**。
- App 的介面文字、通知、README 都用繁體中文；程式碼、註解、commit 訊息用英文。

## 技術與結構

C# / .NET 8 WinForms，TFM `net8.0-windows10.0.19041.0`（為了 WinRT 定位 API）。套件：ManagedNativeWifi、Microsoft.Web.WebView2、Microsoft.Toolkit.Uwp.Notifications。

```
src/CafeWiFi/
  Core/       純邏輯，不碰 Windows API，全部有單元測試
              Models（Store/Brand/StoreKey/Geo/Bssid）、BrandCatalog、AttemptPlanner、
              Persistence（JsonFile/LearnedStore/UsageStore）、StatsModel、Status、CommandLine（含 VersionCompare）
  Platform/   Windows API 薄包裝：IWifi/NativeWifiService、WlanCurrentConnection（P/Invoke 取目前 BSSID）、
              ProfileXml、Connectivity（NCSI）、WindowsLocator、Notifier（toast）、Startup（HKCU Run）、
              SingleInstance（Mutex + 具名管道）、Paths、Log
  App/        Joiner（連線流程）、Tracker、Updater、Settings、TrayController、PasswordDialog、StatsForm、CafeApp（組裝）
  Resources/  stats.html（統計頁，WebView2）、icons/*.ico（由 tools/make-icons.ps1 產生）
tests/CafeWiFi.Tests/   xUnit，Joiner 用 FakeWifi/FakeLocator/FakeNet 測
data/brands/<id>.json   品牌資料，以 EmbeddedResource 編進 exe
scripts/brands/<id>.py  抓取門市並算好候選密碼；scripts/test_louisa.py 是它的測試
tools/WifiSpike/        手動測 Wi-Fi API 的 console 程式（不在 sln 裡）
```

## 指令

Git Bash 的 PATH 沒有 dotnet，要先加：

```bash
export PATH="/c/Program Files/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet test                              # 單元測試
dotnet run --project src/CafeWiFi        # 執行 App
python scripts/test_louisa.py            # 抓取腳本的測試
python scripts/brands/louisa.py          # 重新抓路易莎門市 → data/brands/louisa.json
```

PowerShell：`./build.ps1 [-Version 1.2.3 -Repo redycc/CafeWifi]`，會先跑測試再發布成單一 exe（約 75 MB），輸出 `build/CafeWiFi-win-x64.zip`。

- App 資料：`%APPDATA%\CafeWiFi\`（`learned.json`、`usage.json`、`settings.json`、`status.json`、`brands\`）
- 日誌：`%LOCALAPPDATA%\CafeWiFi\CafeWiFi.log`

## 核心規則（改動前先看）

- **品牌 = 資料**：App 不懂任何品牌的密碼規則，只照 JSON 裡 `passwords` 的順序嘗試。新增品牌時加 `scripts/brands/<id>.py` 和 `data/brands/<id>.json`，C# 不用改。`lat/lng = 0` 表示沒有座標。
- **StoreKey** 字串格式是 `"<brandId>/<storeName>"`；查不到門市時用 `"<brandId>/未知門市"`。學習紀錄和統計都用這個 key。
- **自動連線流程**（`Joiner.RunAuto`）：
  - Wi-Fi 已連線或已能上網時不動作。
  - 先試學過的 BSSID，再試 5 km 內最近 3 家門市的候選密碼，交錯嘗試、依密碼去重複。
  - 全部失敗時，把這個 SSID 所有 BSSID snooze 10 分鐘。
  - 自己連線成功後 15 秒內忽略觸發。
- **使用者動作優先**：`Interrupt()` 要**先**遞增 epoch **再** Cancel，因為 Cancel 會同步執行被中斷那個迴圈的 continuation。被中斷但其實連上的連線要 Disconnect。
- 連線設定檔以 SSID 命名、每次覆寫，`connectionMode=manual`，由 App 決定什麼時候連。
- `NetworkIdentifier.ToString()` 會把非 ASCII 的 SSID 變亂碼，一律用 `ToBytes()` 再用 UTF-8 解碼。
- `JsonFile.Save` 依路徑加鎖、flush 到磁碟、保留 `.bak`；讀到壞檔時改名成 `.bad` 並改用 `.bak`。不要繞過它直接寫檔。
- WinForms 物件只能在 UI 執行緒碰；背景事件用 `CafeApp` 裡的 `ui.Post`。

## 發布

- 推 `v*` 標籤 → `.github/workflows/release.yml` 編譯並建立 GitHub Release（附 `CafeWiFi-win-x64.zip`）。
- 自動更新只在 CI 編的版本裡啟用（MSBuild 屬性 `GitHubRepo` 由 CI 帶入）。
- `refresh-stores.yml` 每週一重抓各品牌資料，有變更就自動發 patch 版。

## 授權

MIT。`LICENSE` 第 4～5 行的原作者版權聲明是授權要求，**不可刪除**。README 和其他地方不再另外註明。

## 尚未驗證

- **錯誤密碼時的行為**（Windows 會不會跳出系統的密碼視窗、多久會失敗）：需要在店裡用 `tools/WifiSpike join LouisaCoffee 00000000` 實測，結果補進 `docs/spike-wifi.md`。
- **自動更新的完整流程**：還沒有 Release 可以測。
- **注意**：這台電腦的 C: 曾經滿到 0 bytes；發布前先確認剩餘空間（約需 300 MB）。
