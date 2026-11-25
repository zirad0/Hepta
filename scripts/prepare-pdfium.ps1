param([string]$Wheel)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$build=Join-Path $root 'build'
New-Item -ItemType Directory -Force $build | Out-Null
$output=Join-Path $build 'pdfium.dll'
$legacyHash='6F68C2B59DE458DA57796CCB81738E420296EDAB6345AE8D20F0C9D985CB66D0'
$wheelHash='47DCCA2A8D507B5FD24F94C3C9D48FB379430F097BC20F01BEFF6C963FFBCEDB'
function Valid($path,$expected){
    return ($path -and (Test-Path -LiteralPath $path -PathType Leaf) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $expected)
}
if(!$Wheel -and (Valid $output $legacyHash)){return}
if(!$Wheel -and (Valid $env:HEPTA_PDFIUM_DLL $legacyHash)){
    Copy-Item -LiteralPath $env:HEPTA_PDFIUM_DLL -Destination $output -Force
    if(!(Valid $output $legacyHash)){throw 'Local PDFium copy verification failed'}
    return
}
if(!$Wheel){
    $Wheel=Join-Path $build 'pdfium.whl'
    if(!(Valid $Wheel $wheelHash)){
        $meta=Invoke-RestMethod -Uri 'https://pypi.org/pypi/pypdfium2/5.13.0/json'
        $pkg=@($meta.urls | Where-Object filename -eq 'pypdfium2-5.13.0-py3-none-win_amd64.whl')
        if($pkg.Count -ne 1 -or $pkg[0].digests.sha256 -ne $wheelHash){throw 'Pinned Windows x64 PDFium wheel metadata mismatch'}
        $download=$Wheel+'.download'
        try{
            Invoke-WebRequest -Uri $pkg[0].url -OutFile $download
            if(!(Valid $download $wheelHash)){throw 'Wheel digest mismatch'}
            Move-Item -LiteralPath $download -Destination $Wheel -Force
        }finally{if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download -Force}}
    }
}
# The pinned SHA256 authenticates every byte of this specific official archive,
# including the DLL. The previously tested local DLL has its own independent pin.
if(!(Valid $Wheel $wheelHash)){throw 'Wheel digest mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stage=$output+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
try{
    $zip=[IO.Compression.ZipFile]::OpenRead($Wheel)
    try{
        $entry=$zip.GetEntry('pypdfium2_raw/pdfium.dll')
        if(!$entry -or $entry.Length -le 0){throw 'Missing pdfium.dll'}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$stage,$false)
    }finally{$zip.Dispose()}
    $digest=(Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash
    Move-Item -LiteralPath $stage -Destination $output -Force
    if(!(Valid $output $digest)){throw 'Extracted PDFium copy verification failed'}
    Write-Output ('PDFium from verified official wheel: '+$digest)
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Force}}
