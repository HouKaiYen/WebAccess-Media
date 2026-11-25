# WebAccess Media

在 Windows 本機監控 WebAccess 告警，並透過網頁將告警資訊顯示在 BrightSign 或其他內網播放器。

## Windows 可攜版

1. 取得 `WebAccess-Media-win-x64.zip`。GitHub Actions 有執行記錄時，可從 Windows CI 的 Artifacts 下載同名套件。
2. 完整解壓縮至固定資料夾，例如 `C:\WebAccess-Media`。套件已包含 .NET Runtime。
3. 雙擊 `Start-WebAccess-Media.cmd`，保持視窗開啟。
4. 開啟 [日常管理](http://127.0.0.1:3008/settings)，可切換至工程設定或顯示頁。
5. 在程式視窗按 `Ctrl+C` 停止監控。

## Windows 通知區

使用 `Start-WebAccess-Media.cmd` 啟動後，Windows 通知區會顯示 WebAccess Media 圖示。若圖示收在溢位選單，請點右下角的「^」。滑鼠停留或按右鍵可查看服務與 WebAccess 連線狀態，雙擊可開啟日常管理。選擇「結束 WebAccess Media」會停止該圖示對應的服務並收起圖示，該服務的播放與監控也會停止。通知區圖示需要已登入 Windows 的桌面工作階段。

## 工程設定

### 開機自動啟動

1. 把完整 Windows ZIP 解壓到固定的本機資料夾，從桌面執行 `Start-WebAccess-Media.cmd`。若使用其他 Port 或開放 BrightSign 連線，先以實際監聽參數啟動。
2. 在工程設定左側點「開機自動啟動」，勾選開關並按「儲存設定」。
3. 首次建立排程時，核准 Windows 管理員提示，接著在 Windows 對話框輸入目前帳號的**登入密碼**。排程以這個帳號執行，沿用設定資料、DPAPI 及目前的監聽位址。程式會把密碼交給 Windows 工作排程器。
4. 等待頁面顯示設定結果。工作排程器程式庫會出現「WebAccess Media - 開機監控」與「WebAccess Media - 登入通知區」。重開機後可先由另一台內網裝置驗證服務，再登入檢查通知區圖示。

關閉開關並儲存會停用這個套件的排程，目前執行中的監控會繼續。更換 Port 後，請用新 Port 啟動，再保持開關勾選並儲存。程式會更新同一組排程，並停用這個帳號與套件路徑留下的舊版排程。搬移套件前請先停用原位置的排程。

### 連線設定

使用執行程式的 Windows 電腦開啟 `/engineering`。左側是測點頁面，點選右上方「WebAccess 連線」開啟 `/engineering/connection`。

| 欄位 | 填寫方式 |
|---|---|
| WebAccess IP 位址 | 只填設備 IP，例如 `192.168.1.100`。程式會組成 `http://192.168.1.100/WaWebService/Json`，再附加 `GetAlarmSummary`。 |
| 專案名稱 | 填入 WebAccess 上的專案名稱。 |
| 帳號及密碼 | 使用可查詢告警摘要的帳號。HTTP 連線請於受信任內網使用。 |

先按「測試連線」。看到成功訊息後，重新輸入密碼並按右上方「儲存設定」。程式儲存前會再驗證告警摘要，成功後自動套用。已儲存的連線資料相同時，密碼可留空以沿用原值。

舊版已儲存的完整網址會繼續用於監控。下次修改時，請確認 IP 並重新儲存，套用固定路徑。

### 測點頁面

可先在左側批次上傳 PNG 或 JPEG 至共用圖庫。按「新增測點頁面」後，可手動輸入測點 Name，或按「從 WebAccess 選取測點」，依 Name 或描述搜尋。搜尋結果可用滑鼠或方向鍵選取，按 Enter 確認。選用共用底圖或上傳獨立圖片，再點擊圖面、拖曳標記或用百分比欄位定位。每個測點有自己的位置，同一張共用底圖可供多個測點使用。按「儲存設定」套用。

工程頁在桌面版可獨立捲動測點清單與設定欄，中央畫布留在原位。共用圖庫選檔後會自動上傳，並列出底圖名稱及縮圖。可點選縮圖預覽，或刪除未被測點使用的圖片。工程圖片與日常輪播媒體各自上傳及保存。圖示預設為 SOS 圓牌，也可選擇警告三角、色彩圓點、緊急押鈕或上傳 PNG／JPEG 自訂圖示。閃爍預設開啟。全頁只顯示圖片預設關閉，開啟後告警時以全畫面呈現底圖並隱藏告警文字及圖示。

警報文字顯示在底圖上方，預設使用 WebAccess 回傳的描述。可依測點輸入自訂文字、設定文字大小與圖示大小。清空自訂文字並儲存即可恢復 API 描述。有顯示警報圖示時，底圖會有紅色圓角外框。在工程頁按「模擬警報預覽」即可用目前草稿確認告警顯示。管理頁與播放器使用套件內附的 Noto Sans TC，可離線載入。

每個測點都可開啟「播放警報提示音」、選擇音效、設定音量並試聽。選擇「自訂音效」可上傳 10 MB 以內的 MP3 或 WAV。提示音預設關閉，也可搭配全頁只顯示平面圖。顯示頁開始顯示新的警報時會播放一次。若瀏覽器要求先操作頁面，請在顯示頁按「播放警報提示音」。

### 日常管理與輪播

跑馬燈可獨立開關。停用時會保留公告文字與設定。「全文與背景樣式」可設定未個別格式化的文字及跑馬燈背景。全文初始設定為白色、32 像素。在公告文字框反白後，下方工具只套用到選取範圍。按「清除格式」會移除選取範圍的個別格式，讓文字重新跟隨全文預設。

在主機開啟 [日常管理](http://127.0.0.1:3008/settings)。可按「從現有媒體選取」或上傳 PNG、JPEG、GIF、MP4。加入媒體後，畫面上的草稿會保留。用上下箭頭調整順序。移除項目、編輯秒數、影片靜音與跑馬燈會先保留在畫面中。按「預覽待機畫面」可查看目前草稿，按右上方「儲存設定」套用至播放器。告警會中斷待機內容，解除後從清單開頭重新播放。

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

預設媒體位置在 `%LOCALAPPDATA%\WebAccess-Media`。工程底圖和告警圖示存於 `engineering-media`，日常輪播媒體存於 `media`。上傳時程式會複製檔案到對應目錄，播放器透過 WebAccess Media 的 HTTP 網址讀取。從播放清單移除的媒體仍可從「現有媒體」重新加入。若確定要刪除本機副本，請先從清單移除並儲存，再到「現有媒體」選擇「刪除檔案」。工程共用圖庫只會刪除未被使用的檔案。刪除測點頁面時，底圖仍留在圖庫。

預設設定目錄為 `%LOCALAPPDATA%\WebAccess-Media`。Windows 以目前使用者的 DPAPI 保護加密金鑰。備份時請保留完整目錄，還原後使用原電腦及原 Windows 帳號啟動。換電腦或換帳號時，請重新輸入連線資料。更新前先停止程式，再替換已解壓縮的程式檔案。若另外建立了 `appsettings.json`，更新前也請備份。

## 開發與驗證

開發需要 .NET 10 SDK。使用 Windows 執行測試可同時驗證 DPAPI。

```powershell
dotnet test -c Release
dotnet publish src/WebAccess.Media -c Release -r win-x64 --self-contained -o artifacts/win-x64
dotnet publish src/WebAccess.Media.Tray -c Release -r win-x64 --self-contained -o artifacts/win-x64/tray
```

BDD 規格位於 `tests/WebAccess.Media.Bdd`，使用繁體中文 Gherkin、Reqnroll 及 NUnit。Windows CI 執行測試並上傳自包含套件。測試使用 WebAccess 回應替身，現場告警與播放器相容性依「現場驗收」步驟確認。

## 目前版本範圍

支援工程連線、WebAccess 測點選取、共用平面圖與拖曳定位、告警圖示、待機輪播及跑馬燈。同時有多個已新增測點告警時，依 API 清單順序顯示第一個。PDF 輪播、待機共用媒體庫、系統匣、開機自啟與 MSI 安裝屬後續版本。

API 呼叫與告警摘要契約參考 [BrightSign WebAccess Alarm Display](https://github.com/HouKaiYen/brightsign-webaccess-alarm-display)。
