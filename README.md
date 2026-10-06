# MemoTack 桌面便箋

仿 Windows Sticky Notes 的 C# WinForms（.NET 8）桌面便箋小工具。

## 功能

- 無邊框便箋視窗，像便利貼黏在桌面上；不出現在工具列與 Alt+Tab
- 拖曳標題列移動；拖曳邊緣縮放（6px 感應區），右下角有縮放提示點陣（感應區較大，較好抓）
- 內容為填滿視窗的多行文字框，自動換行，輸入即編輯
- 常駐系統匣；右鍵選單：新增便箋／顯示/隱藏所有便箋／已關閉的便箋／即將到來的提醒／設定／結束
- 便箋標題列（左到右）：`🎨` 變更顏色（跳出黃／綠／粉紅／藍色盤）、便箋名稱、提醒時間、
  `🗑` 永久刪除（有確認）、`＋` 新增便箋、`⏰` 設定提醒、
  `✕` 關閉此便箋（**保留內容**，可從系統匣「已關閉的便箋」再開啟）
- **便箋名稱**：在 `🎨` 右邊雙擊即可命名（Enter 確定、Esc 取消）；系統匣選單與提醒通知
  會優先顯示名稱，沒有名稱時顯示內容第一行。標題列空間不夠時，提醒時間優先、名稱以「…」截斷
- 標題列按鈕上按住移動也能拖曳便箋（便箋很窄、沒有空白處可拖時也能移動）；
  最小寬度依按鈕實際大小自動計算，任何字級與螢幕縮放下按鈕都不會被擠掉
- **提醒**：每張便箋可設一個提醒（可重複：每天／平日／每週），標題列中間顯示時間（`↻` 表示重複）
  - 時間到：便箋跳到最上層（不搶鍵盤焦點）、標題列閃爍、提示音、系統匣氣泡；
    便箋頂端出現提醒列，可按「延後 10 分」或「完成」（重複提醒會排到下一次）
  - 已關閉的便箋時間到會自動重新開啟；響鈴中按 `✕` 視同「完成」
  - 程式沒在執行或電腦睡眠時錯過的提醒，下次啟動／喚醒時補發並標示「已逾時」；
    重複提醒錯過多次只補一次。電腦睡眠中不會被喚醒來響鈴
- 單擊系統匣圖示 = 把所有便箋叫到最前面（隱藏的顯示、被其他視窗蓋住的浮上來；沒有開啟中的便箋時新增一張）
- `Ctrl+滾輪` 調整該便箋字型大小（8–32pt，會一併保存）
- 系統匣選單「設定...」：外觀（標題列字型/大小，標題列高度自動隨字級調整；
  內容預設字型/大小，修改時套用到所有便箋；是否置頂，預設不置頂）、全域快捷鍵、登入自動啟動
- 注意：Win+D「顯示桌面」會把便箋一併收起（Win11 由系統底層處理、無法攔截），
  用 `Alt+F10` 或系統匣即可叫回；建議以「置頂＋快捷鍵隱藏」取代 Win+D 操作
- 全域快捷鍵（可改、可留空停用；`Win+S` 等被系統占用的組合無法註冊，會跳氣泡提示；
  `F12` 被 Windows 保留給除錯器、不可使用）：
  - `Alt+F10` 叫出／隱藏便箋：正在用其他程式時把便箋叫到最前面，正在操作便箋時隱藏
  - `Alt+F11` 還原所有已關閉的便箋
- 便箋內快捷鍵：`Ctrl+N` 新增、`Ctrl+W` 關閉（保留內容）、`Ctrl+S` 立即存檔；
  刪除便箋請用標題列的 `🗑` 按鈕（有確認）
- Windows 11 自動套用原生圓角視窗
- 設定「登入 Windows 時自動啟動」：寫入 HKCU Run 登錄值（免系統管理員權限），
  執行檔搬家後下次啟動會自動修正路徑；單一實例保護，不會重複開啟
  （已在執行時再次開啟 MemoTack，會把便箋叫到前景）

## 資料保存

- 便箋的名稱、內容、位置、大小、顏色、字級、提醒與全域設定會存成 JSON：
  `%APPDATA%\MemoTack\notes.json`
- **即時儲存**：移動、縮放、打字、換色後靜止 1.5 秒自動寫檔（防抖），
  程式被強制關閉也不會遺失；結束程式與 Windows 關機/登出時也會存檔
- 下次啟動自動還原；按 `✕` 關閉的便箋資料仍保留，按 `🗑` 刪除的才會消失
- 存檔損毀或無法讀取時，原檔會改名備份為 `notes.json.corrupt-<時間>` 並以氣泡提示，
  不會被新資料覆蓋；連備份都失敗時，該次執行暫停寫檔以保護原檔

## 建置與執行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
git clone https://github.com/dragon6615/MemoTack.git
cd MemoTack
dotnet build          # 編譯
dotnet run            # 執行（啟動後看系統匣圖示）
```

## 發佈與安裝程式

一鍵建置（需要安裝 [Inno Setup](https://jrsoftware.org/isinfo.php)）：

```powershell
.\build-installer.ps1                  # 版本號取自 MemoTack.csproj
.\build-installer.ps1 -Version 1.1.0   # 指定版本號
.\build-installer.ps1 -SkipPublish     # 只重編安裝程式（跳過 publish）
# 產出：installer\MemoTack-Setup-<版本>.exe
```

（若執行原則擋下腳本，改用 `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1`）

流程：`dotnet publish` 做出自包含單一執行檔（使用者機器**不需**安裝 .NET），
再用 Inno Setup 編譯 `installer.iss` 成安裝程式。

安裝程式特性：

- 預設個人安裝（`%LocalAppData%\Programs\MemoTack`，免系統管理員），也可選擇裝給所有使用者
- 開始選單捷徑＋可選桌面捷徑；安裝完可直接啟動
- 偵測程式執行中會提示先關閉（AppMutex）
- 解除安裝時自動清除「自動啟動」登錄值；便箋資料（`%APPDATA%\MemoTack`）刻意保留，
  重灌後便箋自動回來（要連資料一起刪，見 installer.iss 底部註解）

### 自動發佈（GitHub Actions）

推送版本 tag 即自動建置並發佈 Release（含安裝檔）。Release 說明取自 [`CHANGELOG.md`](CHANGELOG.md)
對應版本的段落，所以要**先寫好更新紀錄再推 tag**；找不到該版本段落時發佈會在建置前中止。

```powershell
pwsh .github/release-notes.ps1 -Version 1.1.0   # 預覽 Release 說明
git tag v1.1.0
git push origin v1.1.0
```

只發佈不做安裝程式：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# 輸出在 bin\Release\net8.0-windows\win-x64\publish\MemoTack.exe，單檔即可帶走
```

## 程式結構

| 檔案 | 職責 |
|---|---|
| `Program.cs` | 進入點，以 `ApplicationContext` 啟動（無主視窗） |
| `TrayApplicationContext.cs` | 系統匣圖示與選單、便箋生命週期管理、啟動還原/結束存檔 |
| `NoteForm.cs` | 單張便箋視窗（拖曳、縮放、顏色、按鈕、提醒響鈴） |
| `ReminderForm.cs` | 設定提醒對話框（時間、快捷時間、重複、清除） |
| `ReminderSchedule.cs` | 提醒時間計算（到期判斷、重複推算、顯示文字） |
| `SettingsForm.cs` | 設定視窗（字型/大小/置頂/快捷鍵/自動啟動） |
| `StyledDialog.cs` | 對話框共用外觀（色帶、按鈕列、字型、DPI 換算；版面依內容自動調整大小） |
| `HotkeyManager.cs` | 全域快捷鍵（RegisterHotKey） |
| `StartupManager.cs` | 登入自動啟動（HKCU Run 登錄值） |
| `AppSettings.cs` | 全域設定與存檔狀態模型 |
| `NoteData.cs` | 便箋資料模型（POCO） |
| `NoteStorage.cs` | JSON 讀寫（System.Text.Json，原子寫入＋損毀容錯＋舊格式相容） |

無第三方套件依賴。
