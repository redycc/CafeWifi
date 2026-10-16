# Wi-Fi Spike 結果

日期：2026-10-16
環境：Windows 11 Home 10.0.26100（24H2）、Intel Wi-Fi 6E AX211、一般使用者（非系統管理員），當時人在路易莎門市、已連上 `LouisaCoffee`
工具：`tools/WifiSpike`（`scan`、`profile`、`join <ssid> <password>`）

## 已驗證

| 項目 | 結果 |
|---|---|
| 一般使用者能不能掃描並讀到 BSSID | 可以。`ScanNetworksAsync` 花了約 2.6 秒，`EnumerateBssNetworks` 會列出所有 BSS，含 RSSI |
| 目前連線的 SSID／BSSID | ManagedNativeWifi 沒有提供這個資訊，改用 P/Invoke `WlanQueryInterface(wlan_intf_opcode_current_connection)` 取得，可以正常運作（`WlanCurrentConnection.cs`） |
| 一般使用者能不能寫入 all-user 設定檔 | 可以。`SetProfile(AllUser, overwrite)` 回傳 true，也能 `DeleteProfile` 刪除 |
| 同一家店的 AP 數量 | 這家店有 2 台 AP 廣播 `LouisaCoffee`（`e0:63:da:6b:bc:51`、`e0:63:da:6c:bc:51`），這也說明「記住所有訊號夠強的 BSSID」是必要的 |

## 發現的問題，以及要改的地方

- `NetworkIdentifier.ToString()` 遇到非 ASCII 的 SSID 會變成亂碼（例如 `Fengyi �� iPhone`）。**要改用 `ToBytes()` 再用 UTF-8 解碼**。
- 隱藏網路的 SSID 是空字串，比對時直接忽略就好。

## 還沒驗證（需要到店裡實測）

連線失敗需要讓目前的 Wi-Fi 斷線，而且不該對別人的網路嘗試錯誤密碼，所以以下項目要等到現場再手動測：

1. 密碼錯誤時，Windows 會不會跳出「輸入網路安全性金鑰」的視窗（預期不會，因為是透過 API 連線，而且設定檔已經帶了 keyMaterial）。
2. 密碼錯誤時大約多久會回報失敗（預期 5～20 秒，程式設定的逾時是 20 秒）。

測試方法：在路易莎門市先「忘記」`LouisaCoffee`，執行 `WifiSpike join LouisaCoffee 00000000`，記錄耗時和有沒有跳出視窗，再用正確密碼執行一次 `join`。
