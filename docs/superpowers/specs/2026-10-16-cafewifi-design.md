# CafeWiFi 設計文件

日期：2026-10-16
參考專案：[madeyexz/louisa-wifi](https://github.com/madeyexz/louisa-wifi)（macOS、Swift、MIT）

## 1. 目標

做一個 Windows 版的 louisa-wifi，功能和原版完全對等，而且從一開始就能擴充到其他咖啡廳品牌。

**要解決的問題**：路易莎每一家的 Wi-Fi 都叫 `LouisaCoffee`，但每家的密碼都不同（是門市電話）。Windows 一個 SSID 只會記住一組密碼，所以換一家店就連不上。

**成功標準**

- 走進一家連過的門市，App 會在 Wi-Fi 掃到後幾秒內自動連上，不用做任何事。
- 第一次去的門市，靠定位找最近 3 家、依序試候選密碼，自動連上。
- 自動連不上的時候，可以從系統匣選單手動選門市、選密碼或輸入密碼。
- 有連線統計視窗（熱度方格圖、門市／月份／年份統計）、開機自動啟動、通知、自動更新。
- 發布成單一 `.exe`（self-contained），使用者不用安裝任何執行環境。
- 新增品牌只需要加一份資料檔和一支抓取腳本，不需要修改 C# 程式。

**不做（v1）**

- 需要網頁登入的開放網路（captive portal，例如星巴克）。
- 企業級（802.1X）網路。
- macOS／Linux 支援。

## 2. 技術選擇

- C# / .NET 8，WinForms 系統匣 App，TFM 為 `net8.0-windows10.0.19041.0`，以便使用 WinRT 的 `Windows.Devices.Geolocation`。
- Wi-Fi：`ManagedNativeWifi`（NuGet），包裝 Windows Native Wifi API（`wlanapi.dll`）。
- 統計視窗：WebView2（`Microsoft.Web.WebView2`），用 HTML/CSS 繪製。WebView2 Runtime 是 Windows 11 內建的；沒有的話，視窗會顯示下載連結。
- 通知：Windows toast（`Microsoft.Toolkit.Uwp.Notifications`）。原本打算用 `NotifyIcon.ShowBalloonTip`，但系統匣圖示隱藏時氣球通知不會顯示，而原版在圖示隱藏時仍會通知，所以改用 toast（實作時的變更）。
- 發布方式：`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`，輸出 `CafeWiFi.exe`（實測約 75 MB），打包成 `CafeWiFi-win-x64.zip`（約 69 MB）。
- 測試：xUnit。
- 抓取腳本：Python 3（只用標準函式庫），跟原版一樣。

## 3. 多品牌資料格式

每個品牌一份 JSON，放在 `data/brands/<id>.json`，以 EmbeddedResource 的形式編進 exe。

```json
{
  "id": "louisa",
  "name": "路易莎咖啡",
  "ssids": ["LouisaCoffee"],
  "stores": [
    {
      "name": "延平門市",
      "address": "台北市 中正區 ...",
      "city": "台北市",
      "phone": "02-2361-0718",
      "lat": 25.04,
      "lng": 121.51,
      "passwords": ["23610718", "0223610718", "223610718"]
    }
  ]
}
```

- `passwords` 由品牌的抓取腳本算好，依可能性排序。App 不知道任何品牌的密碼規則。
- `lat`／`lng` 為 0 表示沒有座標：這家店不會出現在「最近門市」，但可以在「所有門市」裡找到。
- `city` 由抓取腳本決定（路易莎取地址第一段，並把「臺」改成「台」）。
- 使用者可以把自訂品牌 JSON 放在 `%APPDATA%\CafeWiFi\brands\` 裡。如果 `id` 和內建品牌相同，就用使用者的版本取代內建的。這也方便在不重新編譯的情況下測試新品牌。
- 新增品牌的步驟：寫 `scripts/brands/<id>.py`，產出 `data/brands/<id>.json`，再把它加進 refresh workflow。

**門市的唯一識別**是 `(brandId, storeName)`。在統計和學習紀錄裡，以字串 `"<brandId>/<storeName>"` 表示（稱為 StoreKey）。

## 4. 模組

```
src/CafeWiFi/
  Core/        純邏輯，不碰 Windows API，全部有單元測試
    Brand.cs, Store.cs, StoreKey.cs
    BrandCatalog.cs      載入／合併品牌、named()、nearest()、byCity()
    Geo.cs               haversine 距離
    Bssid.cs             BSSID 正規化（小寫、補零、冒號分隔）
    LearnedStore.cs      BSSID → {StoreKey, password}，讀寫 learned.json
    AttemptPlanner.cs    依「學過的 BSSID → 最近 3 家」排出要嘗試的 (store, password) 清單
    UsageStore.cs        day → StoreKey → seconds，讀寫 usage.json
    StatsModel.cs        統計彙總（期間、方格、門市／月份／年份、最長連續天數）
    VersionCompare.cs    比較版本號
    Status.cs            Idle / Connecting / Connected / Failed 和顯示文字
  Platform/    Windows API 的薄包裝，用介面隔開，方便測試
    IWifi.cs + NativeWifi.cs        掃描、目前連線、寫入設定檔、連線
    ProfileXml.cs                   產生 WPA2PSK/AES 設定檔 XML（有單元測試）
    ILocator.cs + WindowsLocator.cs Geolocator，並把最後一次定位存起來
    IConnectivity.cs + Connectivity.cs  GET http://www.msftconnecttest.com/connecttest.txt
    Startup.cs                      HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    SingleInstance.cs               具名 Mutex + 具名管道（轉送命令列參數）
    Paths.cs, Log.cs
  App/
    Joiner.cs            自動／手動連線流程（依賴 IWifi、ILocator、IConnectivity，有單元測試）
    Tracker.cs           每 60 秒記錄一次
    Updater.cs           GitHub Releases 檢查與自我更新
    Settings.cs          settings.json
    TrayController.cs    系統匣圖示與選單
    PasswordDialog.cs    「輸入密碼…」對話框
    StatsForm.cs         WebView2 視窗，和 stats.html 用 postMessage 溝通
    Program.cs
  Resources/stats.html, Resources/icons/*.ico
tests/CafeWiFi.Tests/
scripts/brands/louisa.py, scripts/test_louisa.py
data/brands/louisa.json
```

## 5. 連線流程（Joiner）

所有 Wi-Fi 操作都在同一條背景工作佇列上依序執行（`SemaphoreSlim(1)`），因為掃描和連線都會阻塞。

**觸發時機**

- `NetworkChange.NetworkAvailabilityChanged` 和 `NetworkAddressChanged`
- 每 15 秒一次的計時器
- 定位結果更新
- 使用者動作（手動）

自動觸發如果已經在排隊或正在跑，就忽略新的觸發。自己的連線成功後 15 秒內（`quietUntil`），也忽略自動觸發，因為我們自己的連線會引發網路事件。

**自動連線**（設定裡的「自動連線」開啟時）

1. Wi-Fi 介面不存在或關閉 → 結束。
2. Wi-Fi 已經連上任何網路 → 結束（不打斷現有連線）。
3. 已經能上網（例如插著網路線）→ 結束。
4. 觸發掃描，等它完成（最多 4 秒），讀取 BSS 清單，只保留 SSID 屬於任一品牌的項目。沒有 → 結束。
5. 品牌 = 訊號最強那台 BSS 的 SSID 所屬的品牌。這個 SSID 的所有 BSS 依訊號由強到弱排序。
6. 訊號最強那台 BSSID 如果在 10 分鐘內失敗過（snoozed）→ 結束。
7. 用 `AttemptPlanner` 排出嘗試清單：
   a. 這個 SSID 所有 BSSID 裡學過的 (store, password)。
   b. 有定位的話，取該品牌 5 km 內最近 3 家門市，依「各家第 1 組 → 各家第 2 組 → 各家第 3 組」的順序。
   c. 依密碼去除重複。
8. 清單是空的 → snooze，通知「請允許定位，以自動尋找附近門市」（沒有定位權限時）或「無法辨識門市，請從選單選擇」。
9. 依序嘗試。每試一組之前，都檢查 epoch 有沒有被使用者動作改變；有的話就停止。成功 → 記住、通知「已連上：<門市>」。全部失敗 → snooze，通知「自動連線失敗，請從選單選擇門市」。

**手動連線**（`Connect(store, password?)`），從選單或命令列觸發

- 先遞增 epoch，讓正在跑的嘗試停下來。
- Wi-Fi 已經連在該品牌的 SSID 上 → 回報已連上。Wi-Fi 連在別的網路，而且已經能上網 → 回報「目前已可上網，保留現有連線」。
- 掃描。找不到這個品牌的 SSID → 「附近找不到<品牌>Wi-Fi」。
- 有指定密碼就只試那一組；沒有的話，先試這家店學過的密碼，再試它的候選密碼。
- 失敗訊息：「密碼 X 無法連上<門市>」或「無法連上<門市>，請確認門市與密碼」。

**連最近門市**（`ConnectNearest()`）：清空 snooze，用手動模式跑自動流程（會回報所有結果）。

**單次連線（`IWifi.TryJoin(bss, password)`）**

1. 用 `ProfileXml.Build(ssid, password)` 產生設定檔：WPA2PSK、AES、`connectionMode=manual`、`keyMaterial` 為明文、`protected=false`。用 `SetProfile` 寫入並覆寫同名設定檔。
2. `ConnectNetworkAsync(interface, profileName = ssid, Infrastructure, bssid, timeout 20s)`。
3. 回傳成功或失敗。

**學習**：成功後，把「訊號最強的那台」和「所有訊號強於 -75 dBm 的 BSSID」都記成 `{StoreKey, password}`，寫入 `learned.json`。

**狀態檔**：每次狀態改變都寫入 `%APPDATA%\CafeWiFi\status.json`（`state`、`message`、`time`），給命令列或外部工具讀取。

## 6. 定位

- 啟動時呼叫 `Geolocator.RequestAccessAsync()`，之後用 `PositionChanged` 監聽位置（精確度設為預設，`MovementThreshold = 100 m`）。
- 每次拿到位置，就存一份到 `settings.json`（`lastLat`、`lastLng`、`lastFixTime`）。`Location` 屬性回傳最近一次的位置，不限時間長短，因為店裡通常沒網路，Windows 的定位可能拿不到最新位置。
- `Authorized` 等於 `GeolocationAccessStatus.Allowed`。
- 沒有權限時：選單會出現「開啟定位權限…」，點下去開 `ms-settings:privacy-location`。Windows 11 24H2 之後，沒有定位權限也讀不到 SSID／BSSID，所以第一次啟動時如果沒權限，會發一則通知，點通知就開設定頁。

## 7. 系統匣選單（TrayController）

選單每次打開時重建（`ContextMenuStrip.Opening`），項目和原版一一對應：

1. （有新版本時）**更新到 X.Y.Z**（粗體），然後一條分隔線
2. 狀態文字（停用）
3. 「用密碼連線」▸（有定位時才出現）：最近那家門市的名稱（停用）、它的各組密碼、「輸入密碼…」
4. 分隔線
5. 「附近門市」標題 + 最近 6 家（`門市名 · 350 m`）；沒有定位時改成「連線到最近的門市」；勾了「只顯示連過的門市」時，標題改成「連過的門市」，只列學過的門市。
6. 「所有門市」▸。只有一個品牌時是 縣市 ▸ 門市 ▸（自動嘗試 / 分隔線 / 各組密碼 / 輸入密碼…）；有多個品牌時，最外層多一層品牌。
7. 分隔線，「連線統計…」，分隔線
8. （沒有定位權限時）「開啟定位權限…」
9. 勾選項目：「自動連線」「只顯示連過的門市」「開機時啟動」，以及「不在系統匣顯示…」
10. 分隔線，「檢查更新（目前 X.Y.Z）」（沒有設定 GitHub repo 時隱藏），「結束」

- 有多個品牌時，門市名稱前面加上品牌名，例如「路易莎 延平門市」。
- 門市項目的 ToolTip 顯示地址和電話。
- 圖示有三種狀態：已連上（實心咖啡杯）、連線中、其他（空心咖啡杯），用內嵌的 `.ico`。
- 狀態顯示規則：最近一次失敗的訊息會一直保留，直到真的連上為止（跟原版的 `orFailure` 一樣）。
- 點兩下系統匣圖示會打開連線統計。
- **隱藏圖示**：先跳出確認對話框，然後設定 `hideTrayIcon=true` 並把 `NotifyIcon.Visible` 設為 false。再執行一次 exe（第二個實例）會透過管道送出 `--show`，把圖示叫回來。

## 8. 命令列（取代原版的 URL scheme 和 Raycast）

`CafeWiFi.exe [--connect <門市名稱> [--brand <id>]] [--nearest] [--stats] [--show] [--update]`

- 如果已經有實例在跑（具名 Mutex `CafeWiFi.SingleInstance`），就把參數透過具名管道 `CafeWiFi.Command` 傳給它，然後結束。沒有參數時等同 `--show`。
- `--connect` 的門市名稱可以是部分字串：剛好只有一家符合就連那家；有多家符合時，在 stdout 列出符合的門市，結束碼為 2（主程式是 WinExe，所以要先 `AttachConsole` 到父行程）。

## 9. 連線時間統計

- **Tracker**：每 60 秒檢查一次。如果 Wi-Fi 連在任一品牌的 SSID 上，就在今天（本地時間 `yyyy-MM-dd`）的對應門市加 60 秒。門市由目前 BSSID 的學習紀錄決定，查不到時記為 `<brandId>/未知門市`。累加後寫入 `usage.json`。電腦睡眠時計時器不會跑，所以闔上蓋子的時間不會被算進去。
- **StatsModel**（純 C#）：輸入 usage、期間（過去一年或某一年）、選定的門市（可為 null）以及今天的日期，輸出：
  - 標題總時數
  - 四個統計方塊：到訪天數、到訪門市（不含未知門市）、最長連續天數、平均每天時數
  - 方格資料：從星期一開始的週欄、每格的秒數和顏色等級（0／<30 分／<1 小時／<2 小時／≥2 小時）、月份標籤
  - 門市排行、最近 12 個月的月份統計、年份統計
  - 可選的年份清單
  - 某一天的明細
- **StatsForm**：WebView2 載入內嵌的 `stats.html`。JS 用 `postMessage({range, store, day})` 請求資料，C# 回傳 StatsModel 的 JSON，JS 負責繪製：
  - 標題
  - 統計方塊
  - 熱度方格（可以點格子、用方向鍵移動）
  - 方格下方顯示選到那天的明細
  - 門市／月份／年份切換（點門市長條會篩選、點年份會切換期間）
  - 右側的年份選擇器
  - 用 `prefers-color-scheme` 支援深色模式，色階沿用原版的藍色。
- 時間格式跟原版一樣：`X 小時 Y 分` / `Y 分`。

## 10. 自動更新

- GitHub repo 由 MSBuild 屬性 `GitHubRepo` 寫入 `AssemblyMetadata("GitHubRepo", ...)`。CI 會把它設成 `${{ github.repository }}`；本機編譯時是空字串，此時停用更新功能、隱藏選單項目。
- 啟動時檢查一次，之後每 6 小時檢查一次：`GET https://api.github.com/repos/<repo>/releases/latest`，找名為 `CafeWiFi-win-x64.zip` 的 asset，比較版本號。
- 發現新版本時發一則通知（同一個版本只通知一次；手動檢查時一定通知），選單出現「更新到 X」。
- 安裝流程：
  1. 下載 zip 到暫存資料夾並解壓縮，確認裡面有 `CafeWiFi.exe`。
  2. 產生一個 `update.cmd`，內容是：等目前行程結束 → 把舊的 exe 改名成 `.old` → 搬入新的 exe → 啟動新版 → 刪除 `.old`。
  3. 執行 `update.cmd` 後，App 結束。
- exe 所在的資料夾不能寫入時，改成直接打開 Releases 網頁。

## 11. 設定與資料檔

`%APPDATA%\CafeWiFi\`：

| 檔案 | 內容 |
|---|---|
| `settings.json` | `autoConnect`（預設 true）、`onlyKnown`、`hideTrayIcon`、`lastLat`、`lastLng`、`lastFixTime`、`notifiedVersion` |
| `learned.json` | `{ "<bssid>": { "store": "<StoreKey>", "password": "..." } }` |
| `usage.json` | `{ "yyyy-MM-dd": { "<StoreKey>": seconds } }` |
| `status.json` | `{ "state", "message", "time" }` |
| `brands\*.json` | 使用者自訂或覆寫的品牌資料 |

- 日誌寫到 `%LOCALAPPDATA%\CafeWiFi\CafeWiFi.log`，超過 1 MB 時輪替。
- 所有 JSON 都先寫到 `.tmp`，再用 `File.Replace`／`Move` 取代，避免寫到一半損毀。讀取失敗時當作空資料處理，並把壞掉的檔案改名成 `.bad` 保留下來。
- 開機自動啟動：在 `HKCU\...\Run` 寫入 `CafeWiFi = "<exe 路徑>" --startup`。`--startup` 啟動時不會把隱藏的圖示叫回來。選單的勾選狀態直接讀登錄機碼。

## 12. 錯誤處理

- WLAN 服務沒有啟動，或沒有無線網卡：狀態顯示「找不到 Wi-Fi 介面」，自動流程什麼都不做。
- Win32 例外（例如 `ERROR_ACCESS_DENIED`，通常是沒有定位權限）：記錄到日誌，狀態顯示「請允許定位權限以讀取 Wi-Fi 資訊」。
- 網路請求（連線檢查、更新）一律有逾時（連線檢查 3 秒、更新 API 15 秒），失敗時視為離線或檢查失敗，不會讓程式崩潰。
- 未處理例外：寫進日誌，App 繼續執行（WinForms `ThreadException`／`UnhandledException`）。

## 13. 測試

- **單元測試（xUnit）**：
  - BrandCatalog：解析、覆寫合併、nearest、byCity 的排序、named
  - Geo、Bssid、ProfileXml（含 XML 特殊字元跳脫）
  - AttemptPlanner：學過的優先、5 km 篩選、交錯順序、去重複
  - LearnedStore／UsageStore：讀寫、檔案毀損時的處理
  - StatsModel：期間邊界、週欄從星期一開始、最長連續天數、月份和年份統計
  - VersionCompare
  - 命令列解析
- **Joiner 測試**：用假的 IWifi／ILocator／IConnectivity 測試以下情況：
  - 已連線或已能上網時不動作
  - 學過的密碼優先嘗試
  - 失敗後 snooze
  - epoch 改變時中斷
  - 手動指定密碼只試那一組
  - 成功後記住附近的 BSSID
- **Python**：`scripts/test_louisa.py` 用 README 表格裡的例子驗證密碼候選的產生。
- **手動驗證（Spike，最先做）**：在真的 Windows 11 上確認：
  1. 用 API 寫入設定檔並連線時，密碼錯誤會不會跳出系統的「輸入網路安全性金鑰」視窗。
  2. 錯誤密碼大約多久會回報失敗。
  3. 一般使用者（非系統管理員）能不能寫入 all-user 設定檔；不行的話改用 per-user 設定檔。

  結果記錄在 `docs/spike-wifi.md`。

## 14. CI／發布（GitHub Actions）

- `ci.yml`：在 windows-latest 上跑 `dotnet test` 和 `dotnet publish`，也跑 Python 測試。
- `release.yml`：推送 `v*` 標籤時觸發，publish 時帶入 `-p:Version=<tag>` 和 `-p:GitHubRepo=${{ github.repository }}`，然後把 zip 上傳到 Release。
- `refresh-stores.yml`：每週一跑各品牌的抓取腳本。資料有變更時 commit、patch 版號加 1、打 tag，然後呼叫 release。
- 推送到 GitHub 或建立 repo 需要使用者決定，不在自動流程內。

## 15. 授權

原專案是 MIT 授權。`data/brands/louisa.json` 的產生方式和密碼規則改寫自原專案，在 README 和 LICENSE 註明來源。
