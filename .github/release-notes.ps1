# 從 CHANGELOG.md 取出指定版本的段落，作為 GitHub Release 說明
# 用法：
#   pwsh .github/release-notes.ps1 -Version 1.1.0                      # 預覽（輸出到畫面）
#   pwsh .github/release-notes.ps1 -Version 1.1.0 -OutFile notes.md    # 發佈流程使用
# 找不到該版本或段落是空的會丟出錯誤，讓發佈流程在建置前就中止
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
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

if ($OutFile) {
    Set-Content -LiteralPath $OutFile -Value $body -Encoding UTF8
    Write-Host "Release notes for $Version written to $OutFile"
}
else {
    $body
}
