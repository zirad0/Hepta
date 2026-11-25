param([switch]$Test,[switch]$Installer,[switch]$Measure)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $fw=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler=Join-Path $fw 'csc.exe'
    New-Item -ItemType Directory -Force build,assets,release | Out-Null
    & ./scripts/prepare-pdfium.ps1
    # The checked-in ICO contains seven alpha-enabled image sizes. No crop is needed.
    if(!(Test-Path 'assets/Hepta.ico' -PathType Leaf)){throw 'Missing assets/Hepta.ico'}
    $refs=@('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','Microsoft.CSharp.dll','Microsoft.VisualBasic.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $fw $_) }
    $refs+=@('System.Runtime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Runtime.WindowsRuntime.dll','System.Threading.Tasks.dll','System.IO.dll') | ForEach-Object { '/reference:'+(Join-Path $fw $_) }
    $refs+=@('Foundation','Storage','Graphics','Data','Media','UI') | ForEach-Object { '/reference:'+(Join-Path $env:windir "System32\WinMetadata\Windows.$_.winmd") }
    $flags=@('/nologo','/optimize+','/platform:x64','/utf8output','/codepage:65001','/win32icon:assets/Hepta.ico','/win32manifest:src/Hepta.manifest','/resource:assets/Hepta_Menu_V3.svg,menu.svg')
    $flags+=@( '/resource:assets/probes/webp.webp,webp.probe','/resource:assets/probes/avif.avif,avif.probe','/resource:assets/lang/tr.json,tr.json', '/resource:assets/lang/en.json,en.json', '/resource:assets/links.json,links.json', '/resource:THIRD_PARTY_LICENSES.md,licenses.md', '/resource:assets/licenses/PDFium-notices.txt,pdfium-notices.txt' )
    $sources=@(Get-ChildItem src/*.cs | ForEach-Object FullName)
    & $compiler @flags @refs /target:winexe /out:build/Hepta.exe @sources
    if($LASTEXITCODE){throw 'Application build failed'}
    if($Test){
        & $compiler @flags @refs /target:exe /main:Hepta.Tests /out:build/Hepta.Tests.exe @sources @(Get-ChildItem tests/*.cs | ForEach-Object FullName)
        if($LASTEXITCODE){throw 'Test build failed'}
        & ./build/Hepta.Tests.exe
        if($LASTEXITCODE){throw 'Tests failed'}
    }
    if($Measure){
        & $compiler @flags @refs /target:exe /main:Hepta.PerfProbe /out:build/Hepta.Perf.exe @sources tests\PerfProbe.cs
        if($LASTEXITCODE){throw 'Performance probe build failed'}
        & ./build/Hepta.Perf.exe
        if($LASTEXITCODE){throw 'Performance probe failed'}
    }
    if($Installer){& ./installer/package.ps1}
} finally {Pop-Location}

