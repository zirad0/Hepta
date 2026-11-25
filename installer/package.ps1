$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$build=Join-Path $root 'build'
$release=Join-Path $root 'release'
$msi=Join-Path $build 'Hepta.msi'
if(Test-Path $msi){Remove-Item -LiteralPath $msi}
Push-Location $build
try{
    @('.OPTION EXPLICIT','.Set CabinetNameTemplate=payload.cab','.Set DiskDirectory1=.','.Set Cabinet=on','.Set Compress=on','.Set CompressionType=MSZIP','.Set MaxDiskSize=0','Hepta.exe','pdfium.dll') | Set-Content files.ddf -Encoding ascii
    & makecab.exe /F files.ddf | Out-Null
    if($LASTEXITCODE){throw 'makecab failed'}
    $wi=New-Object -ComObject WindowsInstaller.Installer
    $db=$wi.OpenDatabase($msi,3)
    function Query([string]$sql){
        $v=$db.OpenView($sql)
        try{$v.Execute()}finally{$v.Close()}
    }
    function Rows($table,$columns,$rows){
        $names=($columns | ForEach-Object {([char]96)+$_+([char]96)}) -join ','
        foreach($row in $rows){
            $values=($row | ForEach-Object {if($_ -is [int]){[string]$_}else{"'"+$_.Replace("'","''")+"'"}}) -join ','
            Query "INSERT INTO $table ($names) VALUES ($values)"
        }
    }
    $tables=@{
        Property='Property CHAR(72) NOT NULL, Value CHAR(0) NOT NULL LOCALIZABLE PRIMARY KEY Property'
        Directory='Directory CHAR(72) NOT NULL, Directory_Parent CHAR(72), DefaultDir CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY Directory'
        Component='Component CHAR(72) NOT NULL, ComponentId CHAR(38), Directory_ CHAR(72) NOT NULL, Attributes SHORT NOT NULL, Condition CHAR(255), KeyPath CHAR(72) PRIMARY KEY Component'
        Feature='Feature CHAR(38) NOT NULL, Feature_Parent CHAR(38), Title CHAR(64) LOCALIZABLE, Description CHAR(255) LOCALIZABLE, Display SHORT, Level SHORT NOT NULL, Directory_ CHAR(72), Attributes SHORT NOT NULL PRIMARY KEY Feature'
        Upgrade='UpgradeCode CHAR(38) NOT NULL, VersionMin CHAR(20), VersionMax CHAR(20), Language CHAR(255), Attributes LONG NOT NULL, Remove CHAR(255), ActionProperty CHAR(72) NOT NULL PRIMARY KEY UpgradeCode, VersionMin, VersionMax, Language, Attributes'
        FeatureComponents='Feature_ CHAR(38) NOT NULL, Component_ CHAR(72) NOT NULL PRIMARY KEY Feature_, Component_'
        File='File CHAR(72) NOT NULL, Component_ CHAR(72) NOT NULL, FileName CHAR(255) NOT NULL LOCALIZABLE, FileSize LONG NOT NULL, Version CHAR(72), Language CHAR(20), Attributes SHORT, Sequence SHORT NOT NULL PRIMARY KEY File'
        Media='DiskId SHORT NOT NULL, LastSequence LONG NOT NULL, DiskPrompt CHAR(64) LOCALIZABLE, Cabinet CHAR(255), VolumeLabel CHAR(32), Source CHAR(72) PRIMARY KEY DiskId'
        Shortcut='Shortcut CHAR(72) NOT NULL, Directory_ CHAR(72) NOT NULL, Name CHAR(128) NOT NULL LOCALIZABLE, Component_ CHAR(72) NOT NULL, Target CHAR(72) NOT NULL, Arguments CHAR(255), Description CHAR(255) LOCALIZABLE, Hotkey SHORT, Icon_ CHAR(72), IconIndex SHORT, ShowCmd SHORT, WkDir CHAR(72) PRIMARY KEY Shortcut'
        RemoveRegistry='RemoveRegistry CHAR(72) NOT NULL, Root SHORT NOT NULL, Key CHAR(255) NOT NULL LOCALIZABLE, Name CHAR(255) LOCALIZABLE, Component_ CHAR(72) NOT NULL PRIMARY KEY RemoveRegistry'
        Registry='Registry CHAR(72) NOT NULL, Root SHORT NOT NULL, Key CHAR(255) NOT NULL LOCALIZABLE, Name CHAR(255) LOCALIZABLE, Value CHAR(0) LOCALIZABLE, Component_ CHAR(72) NOT NULL PRIMARY KEY Registry'
        InstallExecuteSequence='Action CHAR(72) NOT NULL, Condition CHAR(255), Sequence SHORT PRIMARY KEY Action'
    }
    $tables.AdminExecuteSequence=$tables.InstallExecuteSequence
    foreach($t in $tables.Keys){
        $def=[regex]::Replace($tables[$t], '\b([A-Za-z][A-Za-z_0-9]*)\b',{param($m) $v=$m.Groups[1].Value;if($v -cmatch '^(CHAR|SHORT|LONG|NOT|NULL|LOCALIZABLE|PRIMARY|KEY)$'){$v}else{([char]96)+$v+([char]96)}})
        Query ("CREATE TABLE "+([char]96)+$t+([char]96)+" ("+$def+")")
    }
    Rows Property @('Property','Value') @(@('ProductCode','{95C5EC2B-7BD7-4C32-AD87-761F954E1771}'),@('ProductVersion','1.0.0'),@('ProductLanguage','1033'),@('Manufacturer','Zirad'),@('ProductName','Hepta'),@('UpgradeCode','{56167F75-F21A-4F83-AD87-E2A6C7253794}'),@('ARPNOMODIFY','1'),@('ARPNOREPAIR','1'),@('INSTALLLEVEL','1'),@('SecureCustomProperties','HEPTA_OLDPRODUCTS'))
    Rows Upgrade @('UpgradeCode','VersionMin','VersionMax','Attributes','ActionProperty') (, @('{56167F75-F21A-4F83-AD87-E2A6C7253794}','0.0.0','1.0.0',256,'HEPTA_OLDPRODUCTS'))
    Rows Directory @('Directory','DefaultDir') (, @('TARGETDIR','SourceDir'))
    Rows Directory @('Directory','Directory_Parent','DefaultDir') @(@('LocalAppDataFolder','TARGETDIR','.'),@('INSTALLDIR','LocalAppDataFolder','HeptaApp'),@('ProgramMenuFolder','TARGETDIR','.'),@('DesktopFolder','TARGETDIR','.'))
    Rows Component @('Component','ComponentId','Directory_','Attributes','KeyPath') (, @('Main','{E8174158-8B09-4314-A475-4651C38D289B}','INSTALLDIR',256,'Hepta.exe'))
    Rows Component @('Component','ComponentId','Directory_','Attributes','KeyPath','Condition') (, @('Desk','{0B555600-649C-4198-B3EA-9A6E2D0B9E5A}','DesktopFolder',260,'DeskFlag','DESKTOPLINK=1'))
    Rows Registry @('Registry','Root','Key','Name','Value','Component_') @(@('DeskFlag',1,'Software\Zirad\Hepta','Desktop','#1','Desk'),@('InstallFolder',1,'Software\Zirad\Hepta','InstallDir','[INSTALLDIR]','Main'),@('RunValue',1,'Software\Microsoft\Windows\CurrentVersion\Run','Hepta','','Main'))
    Rows Feature @('Feature','Level','Attributes') (, @('Application',1,0))
    Rows FeatureComponents @('Feature_','Component_') @(@('Application','Main'),@('Application','Desk'))
    Rows File @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') (, @('Hepta.exe','Main','Hepta.exe',[int](Get-Item Hepta.exe).Length,'1.0.0.0',16384,1))
    Rows File @('File','Component_','FileName','FileSize','Attributes','Sequence') (, @('pdfium.dll','Main','pdfium.dll',[int](Get-Item pdfium.dll).Length,16384,2))
    Rows Media @('DiskId','LastSequence','Cabinet') (, @(1,2,'#payload.cab'))
    Rows Shortcut @('Shortcut','Directory_','Name','Component_','Target','WkDir') @(@('StartLink','ProgramMenuFolder','Hepta','Main','[#Hepta.exe]','INSTALLDIR'),@('DesktopLink','DesktopFolder','Hepta','Desk','[#Hepta.exe]','INSTALLDIR'))
    $sequence=@('FindRelatedProducts','ValidateProductID','CostInitialize','FileCost','CostFinalize','InstallValidate','InstallInitialize','RemoveExistingProducts','ProcessComponents','UnpublishFeatures','RemoveShortcuts','RemoveRegistryValues','RemoveFiles','RemoveFolders','CreateFolders','InstallFiles','CreateShortcuts','WriteRegistryValues','RegisterUser','RegisterProduct','PublishFeatures','PublishProduct','InstallFinalize')
    $i=0;foreach($action in $sequence){$i+=100;Rows InstallExecuteSequence @('Action','Sequence') (, @($action,$i))}
    $i=0;foreach($action in @('CostInitialize','FileCost','CostFinalize','InstallValidate','InstallInitialize','InstallAdminPackage','InstallFiles','InstallFinalize')){$i+=100;Rows AdminExecuteSequence @('Action','Sequence') (, @($action,$i))}
    $v=$db.OpenView('INSERT INTO ' + ([char]96)+'_Streams'+([char]96)+' (Name,Data) VALUES (?,?)')
    $r=$wi.CreateRecord(2);$r.StringData(1)='payload.cab';$r.SetStream(2,(Join-Path $build 'payload.cab'));$v.Execute($r);$v.Close()
    $summary=$db.SummaryInformation(20)
    $summary.Property(2)='Installation Database';$summary.Property(3)='Hepta';$summary.Property(4)='Zirad';$summary.Property(7)='x64;1033';$summary.Property(9)=[guid]::NewGuid().ToString('B').ToUpperInvariant();$summary.Property(14)=200;$summary.Property(15)=10;$summary.Persist()
    $db.Commit();foreach($item in @($summary,$v,$r)){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item)};[GC]::Collect();[GC]::WaitForPendingFinalizers();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
    $compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /resource:Hepta.msi,Hepta.msi "/win32icon:$root\assets\Hepta.ico" "/out:$release\Hepta-Setup.exe" "$root\installer\Setup.cs" "$root\src\Assembly.cs"
    if($LASTEXITCODE){throw 'Setup compilation failed'}
    (Get-FileHash "$release\Hepta-Setup.exe").Hash.ToLowerInvariant()+'  Hepta-Setup.exe' | Set-Content "$release/Hepta-Setup.sha256" -Encoding ascii
}finally{Pop-Location}

