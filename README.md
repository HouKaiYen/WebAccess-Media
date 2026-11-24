# WebAccess Media

在 Windows 本機監控 WebAccess 告警，並透過網頁將告警資訊顯示在 BrightSign 或其他內網播放器。

## Windows 可攜版

1. 取得 `WebAccess-Media-win-x64.zip`。GitHub Actions 有執行記錄時，可從 Windows CI 的 Artifacts 下載同名套件。
2. 完整解壓縮至固定資料夾，例如 `C:\WebAccess-Media`。套件已包含 .NET Runtime。
3. 雙擊 `Start-WebAccess-Media.cmd`，保持視窗開啟。
4. 開啟 [工程設定](http://127.0.0.1:3008/engineering) 或 [顯示頁](http://127.0.0.1:3008/display)。
5. 在程式視窗按 `Ctrl+C` 停止監控。

## 工程設定

使用執行程式的 Windows 電腦開啟 `/engineering`。

| 欄位 | 填寫方式 |
|---|---|
| WebAccess IP 位址 | 只填設備 IP，例如 `192.168.1.100`。程式會組成 `http://192.168.1.100/WaWebService/Json`，再附加 `GetAlarmSummary`。 |
| 專案名稱 | 填入 WebAccess 上的專案名稱。 |
| 帳號及密碼 | 使用可查詢告警摘要的帳號。HTTP 連線請於受信任內網使用。 |

先按「測試連線」。看到成功訊息後，重新輸入密碼並按「儲存並開始監控」。程式儲存前會再驗證告警摘要，成功後自動套用。已儲存的連線資料相同時，密碼可留空以沿用原值。

舊版已儲存的完整網址會繼續用於監控。下次修改時，請確認 IP 並重新儲存，套用固定路徑。

### 測點頁面

從工程設定開啟「測點頁面」。輸入 WebAccess 測點的 Name，上傳 PNG 或 JPEG，並填入水平與垂直位置。座標以圖片左上角為原點，從 0 到 100%。儲存後，測點告警會顯示平面圖和紅色標記。

### 日常管理與輪播

在主機開啟 [日常管理](http://127.0.0.1:3008/settings)。上傳 PNG、JPEG、GIF 或 MP4 後會加入待機清單。用上下箭頭調整順序，編輯圖片秒數、影片靜音或跑馬燈後按「儲存變更」。可另開「預覽待機」檢查結果。告警會中斷待機內容，解除後從清單開頭重新播放。

## 連接埠與 BrightSign

若 `3008` 已被占用，可在解壓縮資料夾以 PowerShell 指定其他連接埠。

```powershell
.\WebAccess.Media.exe --urls http://127.0.0.1:8080
```

要讓內網播放器連線，可將服務綁定到所有網路介面。

```powershell
.\WebAccess.Media.exe --urls http://0.0.0.0:3008
```

BrightSign 開啟 `http://<Windows 主機 IP>:3008/display`。若需新增防火牆規則，請由管理員只放行受信任的測試網段。工程設定頁仍須在服務主機以 `127.0.0.1` 或 `localhost` 開啟。

## 現場驗收

1. **連線**：開啟 `/api/display`，確認 `connectionStatus` 為 `connected`。此時若 `alarm` 為 `null`，表示 WebAccess 回傳完整的空告警清單。
2. **觸發**：對測試測點觸發告警，確認顯示頁出現測點名稱及描述。有設定該測點的平面圖網址時，畫面也會顯示圖片。
3. **解除**：解除測試告警，確認最後一筆解除後回到待機。
4. **復原**：在測試環境模擬 WebAccess 暫時無法連線，確認畫面保留最後已知告警與更新時間。恢復後確認畫面同步最新狀態。
5. **重啟**：在同一 Windows 帳號下停止並重新執行程式，確認能自動重新連線。

既有部署也可使用執行檔同一資料夾的 `appsettings.json` 提供備用平面圖。測點頁面上傳的平面圖優先使用。

```json
{
  "FloorPlans": {
    "AlarmTagA": "https://media.example.com/floor-a.png"
  }
}
```

## 更新與備份

預設設定目錄為 `%LOCALAPPDATA%\WebAccess-Media`。Windows 以目前使用者的 DPAPI 保護加密金鑰。備份時請保留完整目錄，還原後使用原電腦及原 Windows 帳號啟動。換電腦或換帳號時，請重新輸入連線資料。更新前先停止程式，再替換已解壓縮的程式檔案。若另外建立了 `appsettings.json`，更新前也請備份。

## 開發與驗證

開發需要 .NET 10 SDK。使用 Windows 執行測試可同時驗證 DPAPI。

```powershell
dotnet test -c Release
dotnet publish src/WebAccess.Media -c Release -r win-x64 --self-contained -o artifacts/win-x64
```

BDD 規格位於 `tests/WebAccess.Media.Bdd`，使用繁體中文 Gherkin、Reqnroll 及 NUnit。Windows CI 執行測試並上傳自包含套件。測試使用 WebAccess 回應替身，現場告警與播放器相容性依「現場驗收」步驟確認。

## 目前版本範圍

支援工程連線、測點平面圖與座標、待機輪播、跑馬燈及告警顯示。多筆告警採 API 清單順序。測點清單匯入、拖曳定位、PDF 輪播、共用媒體庫、系統匣、開機自啟與 MSI 安裝屬後續版本。

API 呼叫與告警摘要契約參考 [BrightSign WebAccess Alarm Display](https://github.com/HouKaiYen/brightsign-webaccess-alarm-display)。
