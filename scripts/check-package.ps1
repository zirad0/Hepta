$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$stage=Join-Path $root ('build\verify-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$setup=Join-Path $root 'release\Hepta-Setup.exe'
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($setup))
$msi=Join-Path $stage 'Hepta.msi'
$s=$asm.GetManifestResourceStream('Hepta.msi');$f=[IO.File]::Create($msi)
try{$s.CopyTo($f)}finally{$s.Dispose();$f.Dispose()}
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class HeptaMsiCheck {
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint db);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)]static extern uint MsiDatabaseOpenView(uint db,string sql,out uint view);
    [DllImport("msi.dll")]static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")]static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll")]static extern uint MsiRecordReadStream(uint rec,uint field,byte[] buffer,ref uint length);
    [DllImport("msi.dll")]static extern uint MsiCloseHandle(uint handle);
    static void Check(uint code){if(code!=0)throw new IOException("MSI "+code);}
    public static void Cabinet(string path,string output){uint db=0,view=0,rec=0;
        try{Check(MsiOpenDatabase(path,IntPtr.Zero,out db));Check(MsiDatabaseOpenView(db,"SELECT \u0060Data\u0060 FROM \u0060_Streams\u0060 WHERE \u0060Name\u0060='payload.cab'",out view));Check(MsiViewExecute(view,0));Check(MsiViewFetch(view,out rec));
            using(var target=File.Create(output)){var buf=new byte[65536];while(true){uint n=(uint)buf.Length;Check(MsiRecordReadStream(rec,1,buf,ref n));if(n==0)break;target.Write(buf,0,(int)n);}}}
        finally{if(rec!=0)MsiCloseHandle(rec);if(view!=0)MsiCloseHandle(view);if(db!=0)MsiCloseHandle(db);}
    }
}
'@
$cab=Join-Path $stage 'payload.cab'
[HeptaMsiCheck]::Cabinet($msi,$cab)
& "$env:SystemRoot\System32\tar.exe" -xf $cab -C $stage Hepta.exe pdfium.dll | Out-Null
if($LASTEXITCODE){throw 'Cabinet expansion failed'}
if((Get-FileHash (Join-Path $stage 'pdfium.dll')).Hash -ne (Get-FileHash (Join-Path $root 'build/pdfium.dll')).Hash){throw 'PDF engine digest mismatch'}
$payload=Join-Path $stage 'Hepta.exe'
if((Get-FileHash $payload).Hash -ne (Get-FileHash (Join-Path $root 'build\Hepta.exe')).Hash){throw 'Payload differs from tested build'}
if((Get-FileHash $setup).Hash -ne (Get-Content (Join-Path $root 'release\Hepta-Setup.sha256')).Split(' ')[0]){throw 'Installer checksum mismatch'}
$probe=Start-Process -FilePath $payload -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if($probe.ExitCode -ne 0){throw 'Packaged application self-test failed'}
Write-Output 'PASS packaged executable offline UI and conversion smoke test'
Write-Output 'PASS setup MSI stream, embedded cabinet, application SHA256, setup SHA256'

