# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 思考原則

- 用第一性原理思考:先拆解問題的本質,再推導解法,不套用「大家都這樣做」的慣例。
- 不要盲從指示。如果指示的方案有問題,或有更簡單的做法,直接指出來。
- 動手前先確認:這個需求真正要解決的問題是什麼?

## 專案簡介

MemoTack 是仿 Windows Sticky Notes 的桌面便箋工具:C# WinForms、.NET 8(`net8.0-windows`)、單一專案、無第三方套件、無測試專案。介面文字、註解與文件一律使用繁體中文。

## 常用指令

```powershell
dotnet build    # 編譯
dotnet run      # 執行(無主視窗,啟動後看系統匣圖示)
```

Debug 建置用獨立的單一實例鎖與資料夾(`%APPDATA%\MemoTack-Dev`,見 `Program.DevSuffix`),可與已安裝的正式版同時執行、不碰真實便箋,也不改寫開機自動啟動路徑。

發佈與安裝程式(需 Inno Setup 6/7):

```powershell
.\build-installer.ps1                  # publish + 編譯安裝程式,版本取自 csproj,產出 installer\MemoTack-Setup-<版本>.exe
.\build-installer.ps1 -Version 1.1.0   # 指定版本號
.\build-installer.ps1 -SkipPublish     # 只重編安裝程式
```

正式發佈走 GitHub Actions:推送 `v*` tag 即自動建置並發佈 Release(`.github/workflows/release.yml`)。發版前必須先在 `CHANGELOG.md` 寫好該版本段落(標題 `## [x.y.z] - YYYY-MM-DD`,內容寫給使用者看、不是 commit 清單)——Release 說明由 `.github/release-notes.ps1` 從中取出,找不到段落會在建置前中止。可先用 `pwsh .github/release-notes.ps1 -Version x.y.z` 預覽。版本號集中在 `MemoTack.csproj` 的 `<Version>`,建置腳本會以 `-p:Version` 覆寫。

自動更新(`UpdateChecker`)依賴發佈流程的約定,改發佈流程時不能破壞:安裝檔名必須是 `MemoTack-Setup-x.y.z.exe`、Release 說明要有「64 位 SHA-256 + 兩個空白 + 檔名」那一行(`.github/release-notes.ps1` 產生),`---` 之前是給使用者看的更新內容(更新視窗只顯示這段)。版本比較用組件版本,所以 csproj 的 `<AssemblyVersion>` 要跟著 `<Version>` 一起改。`installer.iss` 有一個 `skipifnotsilent` 的 [Run] 項目負責靜默安裝(自動更新)後重新開啟程式。

## 架構

程式沒有主視窗:`Program.cs` 以 `ApplicationContext` 模式啟動 `TrayApplicationContext`,它是全域樞紐——持有系統匣圖示、所有 `NoteForm` 實例(`_notes`)與已關閉便箋的資料(`_closedNotes`),並負責啟動還原、結束存檔、全域快捷鍵註冊。

資料流:任何變更(移動、縮放、打字、換色)→ `TrayApplicationContext` 的 1.5 秒防抖計時器 → `SaveAll()` 把所有便箋狀態收進 `AppState` → `NoteStorage.Save()` 原子寫入(先寫 `.tmp` 再 `File.Move` 取代)`%APPDATA%\MemoTack\notes.json`。`NoteStorage.Load()` 對損毀檔案與舊格式(純便箋陣列)都有容錯,讀寫失敗一律吞掉、不讓程式崩潰——修改存取邏輯時要維持這個原則。

其餘檔案各司其職:`NoteForm`(單張便箋:無邊框視窗、拖曳/縮放/顏色/名稱/標題列按鈕)、`SettingsForm`(設定視窗)、`StyledDialog`(對話框共用外觀基底類別,`SettingsForm`/`ReminderForm` 繼承)、`HotkeyManager`(Win32 `RegisterHotKey`)、`StartupManager`(HKCU Run 自動啟動)、`AppSettings`/`NoteData`(資料模型 POCO)、`ReminderSchedule`(提醒時間計算,純邏輯)、`ReminderForm`(設定提醒對話框)、`AboutForm`(關於視窗)、`Theme`(便箋色盤 `NoteColor`、配色、圖示字型字碼——調外觀改這裡,不要在各處寫死顏色)、`MenuStyle`(右鍵/系統匣/色盤選單的共用外觀)。

內容編輯器:`MarkdownTextBox`(繼承 RichTextBox)即時套用 markdown 樣式,`MarkdownSyntax` 負責逐行解析(純邏輯,可單獨測試)。存檔的 `Content` 是標準 markdown 純文字;編輯器畫面上待辦與清單換成 ☐/☑/•(`MarkdownSyntax.ToEditor`/`ToStorage` 互轉),所以載入/存檔一律走 `MarkdownTextBox.Markdown`,不要直接讀寫 `Text`。語法符號用 RichEdit 隱藏文字(CFE_HIDDEN)藏起來,只有游標所在段落顯示。

提醒:`TrayApplicationContext` 每 15 秒(及啟動、睡眠喚醒時)檢查所有便箋——含 `_closedNotes`,到期的會被重新開啟——呼叫 `NoteForm.StartRinging()`。響鈴狀態只存在記憶體;`ReminderAt` 要等使用者按「完成」才清除/推進,所以未回應的提醒重開程式會以「已逾時」補發。

需要留意的行為約定:

- 按 `✕` 關閉便箋是「保留內容」(資料移到 `_closedNotes`,可從系統匣還原);按 `🗑` 才是永久刪除。改動便箋生命週期時不要混淆這兩者。
- 提醒的「延後」寫在 `ReminderSnoozeUntil`,不改 `ReminderAt`——後者是重複提醒的基準時間,`Next()` 一律從它往後推。響鈴中按 `✕` 視同「完成」。`ReminderRepeat` 以整數存 JSON(未知值載入時視為不重複),新增列舉值不會讓舊版讀檔失敗。重複規則 = `RepeatRule`(種類 + `ReminderWeekDays` 星期遮罩 + `ReminderMonthDay`);「平日」在 UI 上是每週的預設組合,存檔一律存成 Weekly + 週一至週五遮罩,舊版的 `Weekdays`(2)由 `NoteStorage.MigrateRepeat` 載入時轉換。每月的日子以 `ReminderMonthDay` 為準、不從上一次推算,31 日遇小月落在月底後下個月仍回到 31 日。
- 有舊版設定遷移邏輯(如更早的預設快捷鍵 `Ctrl+Alt+S`/`Alt+F12` 改為目前預設),改預設值時要考慮既有使用者的 JSON。目前預設 `Win+Alt+Q`(叫出/隱藏)、`Win+Alt+Z`(還原已關閉);前一代預設 `Alt+F10`/`Alt+F11` 刻意不遷移,因為分不出是沿用預設還是使用者自選。
- 便箋不出現在工具列與 Alt+Tab;Win+D 收起便箋是系統行為、無法攔截,屬已知限制。所以「叫出便箋」(單擊系統匣、全域快捷鍵、再次開啟程式)要用 `NoteForm.RaiseToTop()`——`BringToFront()` 只在同程式內有效。
- 版面不寫死尺寸:便箋最小寬度由 `UpdateMinimumSize()` 依按鈕實際寬度計算;對話框繼承 `StyledDialog` 用 AutoSize 版面、`AutoScaleMode.None`、間距經 `Dpi()` 換算。寫死像素在 150% 縮放下會切字或讓按鈕互相覆蓋。
- `MarkdownTextBox` 的約定:以 `\n` 分隔的「段落」為單位處理——RichTextBox 的 `GetLineFromCharIndex` 等是自動換行後的顯示行,不能拿來對應 markdown 行。輸入法組字中不得改選取或格式(會中斷注音/倉頡組字)。套用格式要包在 TOM `Undo(tomSuspend/tomResume)` 裡,否則 Ctrl+Z 會先復原格式。程式直接設定 `Text` 時整篇重新套用,打字時只處理游標附近段落。不可在 `TextChanged` 裡同步再改文字(RichEdit 還在處理那次變更,只會換掉一部分),要 `BeginInvoke` 排到之後。拖曳或 Shift 選取中不重新套用格式(會改動選取範圍、打斷選取)。行首 `- `/`- [ ]` 轉成 •/☐ 有兩道:打字當下(`ConvertTypedPrefix`)與游標離開該行時補轉(`ConvertLeftoverPrefixes`),確保畫面與重新載入後(`ToEditor`)一致。
- 便箋外觀:標題列按鈕是自繪的 `TitleButton`,用系統圖示字型(Segoe Fluent Icons/MDL2,見 `Theme.Icon*`),不要再用 emoji(顏色與筆畫粗細不一)。按鈕只在便箋使用中(前景、滑鼠移入、響鈴、色盤或改名中)淡入,其餘淡出但位置保留。陰影靠 DWM(`DwmExtendFrameIntoClientArea`),縮放邊的色帶由 `OnPaintBackground` 補畫。深色便箋(`NoteColor.Dark`)的文字、hover、對話框色帶都要走 `NoteColor` 的屬性,不能假設底色是淺色。顏色資料:`ColorIndex`(預設色,只能往尾端加)+ `CustomHue`/`CustomDark`(自訂色,只存色相,明度彩度由 `Theme.FromHue` 以 OKLCH 換算);深色自訂色會把 `ColorIndex` 設成炭黑,讓舊版讀檔時退回深色。
- 標題列的按鈕與標籤「按住移動超過系統拖曳門檻才開始拖曳視窗」,不是按下就拖——否則按鈕點擊與名稱雙擊都收不到。

## 專案慣例

- PowerShell 腳本(`.ps1`)含中文註解,必須以 UTF-8 with BOM 儲存,否則 Windows PowerShell 會亂碼。
- 所有公開內容(README、UI 文字、commit 訊息、安裝程式)一律使用 MemoTack 名稱。
- 不隨意引入第三方套件;目前零依賴是刻意的。
