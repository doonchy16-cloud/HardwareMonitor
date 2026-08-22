[CmdletBinding()]
param(
    [string]$Version = "1.0.0.0",
    [string]$Publisher = "CN=The Spark",
    [string]$ReleaseTag = "v1.0.0",
    [string]$OutputDirectory = "artifacts/package",
    [string]$AppInstallerUri = "https://github.com/doonchy16-cloud/HardwareMonitor/releases/latest/download/HardwareMonitor.appinstaller",
    [string]$PackageUri = "",
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must use four-part MSIX notation, for example 1.0.0.0."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot $OutputDirectory
$stageRoot = Join-Path $outputRoot "stage"
$publishRoot = Join-Path $outputRoot "publish"
$packageName = "HardwareMonitor-$($Version.Substring(0, $Version.LastIndexOf('.')))-x64.msix"
$packagePath = Join-Path $outputRoot $packageName
$appInstallerPath = Join-Path $outputRoot "HardwareMonitor.appinstaller"

if ([string]::IsNullOrWhiteSpace($PackageUri)) {
    $PackageUri = "https://github.com/doonchy16-cloud/HardwareMonitor/releases/download/$ReleaseTag/$packageName"
}

Remove-Item $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outputRoot, $stageRoot, $publishRoot | Out-Null

Write-Host "Publishing Hardware Monitor $Version (win-x64, self-contained)..."
& dotnet publish (Join-Path $repoRoot "src/HardwareMonitor.App/HardwareMonitor.App.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item (Join-Path $publishRoot "*") $stageRoot -Recurse -Force
$stageAssets = Join-Path $stageRoot "Assets"
New-Item -ItemType Directory -Path $stageAssets -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot "Assets/Square44x44Logo.png") $stageAssets -Force
Copy-Item (Join-Path $PSScriptRoot "Assets/Square150x150Logo.png") $stageAssets -Force
Copy-Item (Join-Path $PSScriptRoot "Assets/StoreLogo.png") $stageAssets -Force

$manifestTemplate = Get-Content (Join-Path $PSScriptRoot "AppxManifest.template.xml") -Raw
$manifest = $manifestTemplate.Replace("__VERSION__", $Version).Replace("__PUBLISHER__", $Publisher)
[System.IO.File]::WriteAllText((Join-Path $stageRoot "AppxManifest.xml"), $manifest, [System.Text.UTF8Encoding]::new($false))

$kitsBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin"
$makeAppx = Get-ChildItem $kitsBin -Filter MakeAppx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\MakeAppx\.exe$' } |
    Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $makeAppx) { throw "MakeAppx.exe was not found. Install the Windows SDK." }

Write-Host "Packing MSIX with $($makeAppx.FullName)..."
& $makeAppx.FullName pack /o /d $stageRoot /p $packagePath
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $resolvedCertificate = (Resolve-Path $CertificatePath).Path
    $signTool = Join-Path $makeAppx.Directory.FullName "SignTool.exe"
    if (-not (Test-Path $signTool)) { throw "SignTool.exe was not found beside MakeAppx.exe." }
    $signArgs = @("sign", "/fd", "SHA256", "/a", "/f", $resolvedCertificate)
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArgs += @("/p", $CertificatePassword)
    }
    $signArgs += $packagePath
    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }
}

$appInstallerTemplate = Get-Content (Join-Path $PSScriptRoot "HardwareMonitor.appinstaller.template") -Raw
$appInstaller = $appInstallerTemplate.Replace("__VERSION__", $Version).Replace("__PUBLISHER__", $Publisher).Replace("__APPINSTALLER_URI__", $AppInstallerUri).Replace("__PACKAGE_URI__", $PackageUri)
[System.IO.File]::WriteAllText($appInstallerPath, $appInstaller, [System.Text.UTF8Encoding]::new($false))

$packageHash = (Get-FileHash $packagePath -Algorithm SHA256).Hash
Write-Host "PACKAGE=$packagePath"
Write-Host "APPINSTALLER=$appInstallerPath"
Write-Host "PACKAGE_SHA256=$packageHash"
Write-Host "SIGNED=$(-not [string]::IsNullOrWhiteSpace($CertificatePath))"
