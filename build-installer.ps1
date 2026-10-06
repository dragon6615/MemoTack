# MemoTack 發佈 + 安裝程式建置
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\build-installer.ps1                 # 版本號取自 MemoTack.csproj
#   powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -SkipPublish   # 只重編安裝程式
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot

# 未指定版本時以 csproj 的 <Version> 為準，避免和程式本身的版本號脫鉤
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'MemoTack.csproj') -Raw -Encoding UTF8
    $Version = "$($project.Project.PropertyGroup.Version)".Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version' (expected x.y.z)"
}
Write-Host "Version: $Version"
$publishDirectory = Join-Path $projectRoot 'publish\win-x64'
$publishedExecutable = Join-Path $publishDirectory 'MemoTack.exe'
$installerScript = Join-Path $projectRoot 'installer.iss'
$installerOutput = Join-Path $projectRoot "installer\MemoTack-Setup-$Version.exe"

# ---- 1) dotnet publish（自包含單一執行檔） ----
if (-not $SkipPublish) {
    # 先清空輸出資料夾，避免改名或移除檔案後殘留舊檔被一起打包進安裝程式
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    Push-Location $projectRoot
    try {
        & dotnet publish `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:Version=$Version `
            -o $publishDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable not found: $publishedExecutable"
}

# ---- 2) 尋找 ISCC.exe：常見路徑 + 解除安裝登錄檔 ----
$compilerCandidates = @(
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)

$uninstallRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$registeredCompilers = Get-ItemProperty $uninstallRoots -ErrorAction SilentlyContinue |
    Where-Object {
        $_.DisplayName -like 'Inno Setup*' -and
        -not [string]::IsNullOrWhiteSpace($_.InstallLocation)
    } |
    ForEach-Object {
        Join-Path $_.InstallLocation 'ISCC.exe'
    }

$innoCompiler = @($compilerCandidates + $registeredCompilers) |
    Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    } |
    Select-Object -Unique -First 1

if ($null -eq $innoCompiler) {
    throw 'ISCC.exe not found. Please install Inno Setup 6 or 7.'
}
Write-Host "Using compiler: $innoCompiler"

# ---- 3) 編譯安裝程式（把版本號傳進 .iss） ----
Push-Location $projectRoot
try {
    & $innoCompiler "/DAppVersion=$Version" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compile failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

# ---- 4) 驗證輸出 ----
if (-not (Test-Path -LiteralPath $installerOutput -PathType Leaf)) {
    throw "Compile finished but expected installer not found: $installerOutput"
}

$installerFile = Get-Item -LiteralPath $installerOutput
Write-Host ''
Write-Host 'Installer created:'
Write-Host $installerFile.FullName
Write-Host ("Size: {0:N2} MB" -f ($installerFile.Length / 1MB))
