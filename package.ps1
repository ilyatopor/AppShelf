param([switch]$Installer)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$files = @('AppShelf.exe', 'README.md', 'LICENSE') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $dist 'AppShelf-0.2.0-beta-portable.zip') -Force
if ($Installer) {
    $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if ($command) { $compiler = $command.Source }
        else { throw 'Install Inno Setup 6 from https://jrsoftware.org/isdl.php, then run package.ps1 -Installer.' }
    }
    & $compiler "$PSScriptRoot\installer\setup.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
}
Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Extension -in @('.exe', '.zip') } | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Packages: $dist"
