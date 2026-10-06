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

發佈與安裝程式(需 Inno Setup 6/7):

```powershell
.\build-installer.ps1                  # publish + 編譯安裝程式,版本取自 csproj,產出 installer\MemoTack-Setup-<版本>.exe
.\build-installer.ps1 -Version 1.1.0   # 指定版本號
.\build-installer.ps1 -SkipPublish     # 只重編安裝程式
```

正式發佈走 GitHub Actions:推送 `v*` tag 即自動建置並發佈 Release(`.github/workflows/release.yml`)。版本號集中在 `MemoTack.csproj` 的 `<Version>`,建置腳本會以 `-p:Version` 覆寫。

## 架構

程式沒有主視窗:`Program.cs` 以 `ApplicationContext` 模式啟動 `TrayApplicationContext`,它是全域樞紐——持有系統匣圖示、所有 `NoteForm` 實例(`_notes`)與已關閉便箋的資料(`_closedNotes`),並負責啟動還原、結束存檔、全域快捷鍵註冊。

資料流:任何變更(移動、縮放、打字、換色)→ `TrayApplicationContext` 的 1.5 秒防抖計時器 → `SaveAll()` 把所有便箋狀態收進 `AppState` → `NoteStorage.Save()` 原子寫入(先寫 `.tmp` 再 `File.Move` 取代)`%APPDATA%\MemoTack\notes.json`。`NoteStorage.Load()` 對損毀檔案與舊格式(純便箋陣列)都有容錯,讀寫失敗一律吞掉、不讓程式崩潰——修改存取邏輯時要維持這個原則。

其餘檔案各司其職:`NoteForm`(單張便箋:無邊框視窗、拖曳/縮放/顏色/名稱/標題列按鈕)、`SettingsForm`(設定視窗)、`StyledDialog`(對話框共用外觀基底類別,`SettingsForm`/`ReminderForm` 繼承)、`HotkeyManager`(Win32 `RegisterHotKey`)、`StartupManager`(HKCU Run 自動啟動)、`AppSettings`/`NoteData`(資料模型 POCO)、`ReminderSchedule`(提醒時間計算,純邏輯)、`ReminderForm`(設定提醒對話框)。

提醒:`TrayApplicationContext` 每 15 秒(及啟動、睡眠喚醒時)檢查所有便箋——含 `_closedNotes`,到期的會被重新開啟——呼叫 `NoteForm.StartRinging()`。響鈴狀態只存在記憶體;`ReminderAt` 要等使用者按「完成」才清除/推進,所以未回應的提醒重開程式會以「已逾時」補發。

需要留意的行為約定:

- 按 `✕` 關閉便箋是「保留內容」(資料移到 `_closedNotes`,可從系統匣還原);按 `🗑` 才是永久刪除。改動便箋生命週期時不要混淆這兩者。
- 提醒的「延後」寫在 `ReminderSnoozeUntil`,不改 `ReminderAt`——後者是重複提醒的基準時間,`Next()` 一律從它往後推。響鈴中按 `✕` 視同「完成」。`ReminderRepeat` 以整數存 JSON(未知值載入時視為不重複),新增列舉值不會讓舊版讀檔失敗。
- 有舊版設定遷移邏輯(如舊預設快捷鍵改為 `Alt+F10`),改預設值時要考慮既有使用者的 JSON。
- 便箋不出現在工具列與 Alt+Tab;Win+D 收起便箋是系統行為、無法攔截,屬已知限制。所以「叫出便箋」(單擊系統匣、`Alt+F10`、再次開啟程式)要用 `NoteForm.RaiseToTop()`——`BringToFront()` 只在同程式內有效。
- 版面不寫死尺寸:便箋最小寬度由 `UpdateMinimumSize()` 依按鈕實際寬度計算;對話框繼承 `StyledDialog` 用 AutoSize 版面、`AutoScaleMode.None`、間距經 `Dpi()` 換算。寫死像素在 150% 縮放下會切字或讓按鈕互相覆蓋。
- 標題列的按鈕與標籤「按住移動超過系統拖曳門檻才開始拖曳視窗」,不是按下就拖——否則按鈕點擊與名稱雙擊都收不到。

## 專案慣例

- PowerShell 腳本(`.ps1`)含中文註解,必須以 UTF-8 with BOM 儲存,否則 Windows PowerShell 會亂碼。
- 所有公開內容(README、UI 文字、commit 訊息、安裝程式)一律使用 MemoTack 名稱。
- 不隨意引入第三方套件;目前零依賴是刻意的。
