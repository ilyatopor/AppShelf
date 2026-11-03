$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$appFile = Join-Path $appRoot 'AppShelf.exe'
if (-not (Test-Path -LiteralPath $appFile)) { & (Join-Path $appRoot 'build.ps1') }
$hash = (Get-FileHash -LiteralPath $appFile -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = Join-Path $PSScriptRoot 'payload.sha256'
Set-Content -LiteralPath $hashFile -Value $hash -Encoding ASCII
$dist = Join-Path $appRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$output = Join-Path $dist 'AppShelf-Setup.exe'
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Core.dll',
    "/win32icon:$appRoot\assets\AppShelf.ico", "/resource:$appRoot\assets\app-icon.png,AppShelf.Logo",
    "/resource:$appFile,Payload.AppShelf", "/resource:$appRoot\README.md,Payload.Readme",
    "/resource:$appRoot\LICENSE,Payload.License", "/resource:$hashFile,Payload.Hash",
    "/out:$output", "$PSScriptRoot\Loader.cs", "$appRoot\Design.cs", "$appRoot\Catalog.cs")
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Loader build failed' }
$sumFile = Join-Path $dist 'SHA256SUMS.txt'
$existingSums = @()
if (Test-Path -LiteralPath $sumFile) { $existingSums = @(Get-Content -LiteralPath $sumFile | Where-Object { $_ -notmatch '  AppShelf-Setup\.exe$' }) }
$setupHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
@($existingSums; "$setupHash  AppShelf-Setup.exe") | Set-Content -LiteralPath $sumFile -Encoding ASCII
Write-Output "Built loader: $output"
