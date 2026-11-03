$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /target:winexe "/win32icon:$PSScriptRoot\assets\AppShelf.ico" "/resource:$PSScriptRoot\assets\app-icon.png,AppShelf.Logo" /optimize+ /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Xml.dll /out:"$PSScriptRoot\AppShelf.exe" "$PSScriptRoot\Catalog.cs" "$PSScriptRoot\Cleanup.cs" "$PSScriptRoot\Interface.cs" "$PSScriptRoot\Program.cs" "$PSScriptRoot\Tests.cs" "$PSScriptRoot\AssemblyInfo.cs" "$PSScriptRoot\BackupsForm.cs" "$PSScriptRoot\Design.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built: $PSScriptRoot\AppShelf.exe"




