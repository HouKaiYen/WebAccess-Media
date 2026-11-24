# WebAccess Media

在 Windows 本機監控 WebAccess 告警，並透過網頁將告警資訊顯示在 BrightSign 或其他內網播放器。

## Windows 可攜版

1. 取得 `WebAccess-Media-win-x64.zip`。GitHub Actions 有執行記錄時，可從 Windows CI 的 Artifacts 下載同名套件。
2. 完整解壓縮至固定資料夾，例如 `C:\WebAccess-Media`。套件已包含 .NET Runtime。
3. 雙擊 `Start-WebAccess-Media.cmd`，保持視窗開啟。
4. 開啟 [日常管理](http://127.0.0.1:3008/settings)，可切換至工程設定或顯示頁。
5. 在程式視窗按 `Ctrl+C` 停止監控。

## 工程設定

使用執行程式的 Windows 電腦開啟 `/engineering`。左側是測點頁面，點選右上方「WebAccess 連線」開啟 `/engineering/connection`。

| 欄位 | 填寫方式 |
|---|---|
| WebAccess IP 位址 | 只填設備 IP，例如 `192.168.1.100`。程式會組成 `http://192.168.1.100/WaWebService/Json`，再附加 `GetAlarmSummary`。 |
| 專案名稱 | 填入 WebAccess 上的專案名稱。 |
| 帳號及密碼 | 使用可查詢告警摘要的帳號。HTTP 連線請於受信任內網使用。 |

先按「測試連線」。看到成功訊息後，重新輸入密碼並按「儲存並開始監控」。程式儲存前會再驗證告警摘要，成功後自動套用。已儲存的連線資料相同時，密碼可留空以沿用原值。

舊版已儲存的完整網址會繼續用於監控。下次修改時，請確認 IP 並重新儲存，套用固定路徑。

### 測點頁面

可先在左側批次上傳 PNG 或 JPEG 至共用圖庫。按「新增測點頁面」，輸入測點 Name，或讀取 WebAccess 測點後用關鍵字選取。選用共用底圖或上傳獨立圖片，再點擊圖面、拖曳標記或用百分比欄位定位。每個測點有自己的位置，同一張共用底圖可供多個測點使用。按「儲存設定」套用。

工程圖庫會列出底圖的名稱與縮圖，點選縮圖可套用到底圖預覽。工程圖片與日常輪播媒體各自上傳及保存。圖示可使用 SOS 圓牌、警告三角、色彩圓點、緊急押鈕或上傳 PNG／JPEG 自訂圖示。閃爍預設開啟。全頁只顯示圖片預設關閉，開啟後告警時以全畫面呈現底圖並隱藏告警文字及圖示。

### 日常管理與輪播

在主機開啟 [日常管理](http://127.0.0.1:3008/settings)。上傳 PNG、JPEG、GIF 或 MP4 後會加入待機清單。用上下箭頭調整順序；移除項目、編輯秒數、影片靜音與跑馬燈會先保留在畫面中。按「預覽待機畫面」可查看目前草稿，按右上方「儲存變更」套用至播放器。告警會中斷待機內容，解除後從清單開頭重新播放。

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
2. **觸發**：先在工程設定新增測試測點，再觸發告警。確認顯示頁出現測點名稱、描述、平面圖及標記。只觸發未新增的測點時，畫面維持待機輪播。
3. **解除**：解除所有已新增測點的告警，確認畫面回到待機。未新增測點仍有告警時，也應恢復待機。
4. **復原**：在測試環境模擬 WebAccess 暫時無法連線，確認畫面保留最後已知告警與更新時間。恢復後確認畫面同步最新狀態。
5. **重啟**：在同一 Windows 帳號下停止並重新執行程式，確認能自動重新連線。

從舊版更新時，若測點只設定在 `appsettings.json` 的 `FloorPlans`，請到工程設定新增測點並上傳平面圖，讓該測點能觸發顯示頁。

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

支援工程連線、WebAccess 測點選取、共用平面圖與拖曳定位、告警圖示、待機輪播及跑馬燈。同時有多個已新增測點告警時，依 API 清單順序顯示第一個。PDF 輪播、待機共用媒體庫、系統匣、開機自啟與 MSI 安裝屬後續版本。

API 呼叫與告警摘要契約參考 [BrightSign WebAccess Alarm Display](https://github.com/HouKaiYen/brightsign-webaccess-alarm-display)。
