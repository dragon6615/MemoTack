# 從 CHANGELOG.md 取出指定版本的段落，作為 GitHub Release 說明
# 用法：
#   pwsh .github/release-notes.ps1 -Version 1.1.0                      # 預覽／檢查（輸出到畫面）
#   pwsh .github/release-notes.ps1 -Version 1.1.0 -Installer <安裝檔> -OutFile notes.md
#       發佈流程使用：在說明最後附上安裝說明（SmartScreen 提示）與安裝檔的 SHA-256
# 找不到該版本或段落是空的會丟出錯誤，讓發佈流程在建置前就中止
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Installer,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

$changelog = Join-Path $PSScriptRoot '..\CHANGELOG.md'
$lines = Get-Content -LiteralPath $changelog -Encoding UTF8

# 段落標題固定為「## [x.y.z] - YYYY-MM-DD」
$header = '^## \[' + [regex]::Escape($Version) + '\]'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $header) { $start = $i; break }
}
if ($start -lt 0) {
    throw "CHANGELOG.md 找不到 [$Version] 的段落，請先補上更新說明再推送 tag"
}

# 段落到下一個版本標題為止
$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $end = $i; break }
}

# 注意：PowerShell 的範圍運算子會反向取值（5..4 = 5, 4），段落為空時要先擋下
$body = if ($end -gt $start + 1) { ($lines[($start + 1)..($end - 1)] -join "`n").Trim() } else { '' }
if (-not $body) {
    throw "CHANGELOG.md 的 [$Version] 段落是空的"
}

# 每一版都附上的安裝說明與檢查碼：放在腳本裡而不是 CHANGELOG，才不會有版本漏寫
if ($Installer) {
    $name = Split-Path -Leaf $Installer
    $hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash
    $body += @"


---

### 📥 安裝說明
- 下載 ``$name`` 執行即可；升級時直接執行新版安裝檔，便箋資料會保留
- 若出現「Windows 已保護您的電腦」：點「**其他資訊**」→「**仍要執行**」。
  MemoTack 是開源專案，安裝檔沒有付費的程式碼簽章，新版本剛發佈時還沒累積信譽，
  Microsoft Defender SmartScreen 就會顯示這個警告。安裝檔由 GitHub Actions 從公開原始碼自動建置

### 🔒 檔案檢查碼（SHA-256）
``````
$hash  $name
``````
可在 PowerShell 執行 ``(Get-FileHash .\$name).Hash`` 比對，確認檔案沒有被竄改。
"@
}

if ($OutFile) {
    Set-Content -LiteralPath $OutFile -Value $body -Encoding UTF8
    Write-Host "Release notes for $Version written to $OutFile"
}
else {
    $body
}
