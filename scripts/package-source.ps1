$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$release=Join-Path $root 'release'
New-Item -Type Directory -Force $release | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$folders=@('src','assets','tests','installer','scripts','docs','.github')
$files=@('Hepta.sln','Hepta.csproj','Hepta.proj','Build.cmd','README.md','README_EN.md','CHANGELOG.md','CONTRIBUTING.md','TEST_REPORT.md','SUPPORTED_FORMATS.md','THIRD_PARTY_LICENSES.md','RELEASE_NOTES.md','.gitattributes','.gitignore')
$list=@(foreach($name in $files){Get-Item -LiteralPath (Join-Path $root $name)}) + @(foreach($name in $folders){Get-ChildItem -LiteralPath (Join-Path $root $name) -File -Recurse -Force})
$archive=Join-Path $release 'Hepta-Source-1.0.0.zip'
$staging=$archive+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
try{
    $z=[IO.Compression.ZipFile]::Open($staging,[IO.Compression.ZipArchiveMode]::Create)
    try{foreach($f in $list){$relative=$f.FullName.Substring($root.Length+1).Replace('\','/');[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z,$f.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal)}}finally{$z.Dispose()}
    $z=[IO.Compression.ZipFile]::OpenRead($staging)
    try{if($z.Entries.Count -ne $list.Count){throw 'Incomplete source archive'}}finally{$z.Dispose()}
    [IO.File]::Move($staging,$archive,$true)
    Copy-Item (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $release 'RELEASE_NOTES.md') -Force
    Get-Item $archive | Select-Object Name,Length
}finally{if(Test-Path $staging){Remove-Item -LiteralPath $staging}}

