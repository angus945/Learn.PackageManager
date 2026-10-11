[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Utf8 = [Text.UTF8Encoding]::new($false)
$Helper = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Tools/ApplyDevelopmentBootstrap.ps1'))
$OutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.pm-v2-test-output'))
$Root = Join-Path $OutputRoot ('packageset-contracts-' + [guid]::NewGuid().ToString('N') + '-' + [char]0x6E2C + [char]0x8A66 + ' space')
$Checks = 0
function Assert([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAILED: $Name" }
    $script:Checks++; Write-Host "PASS: $Name"
}
function G([string]$At, [string[]]$Arguments) {
    $old = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $output = & git -C $At @Arguments 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if ($code -ne 0) { throw "Fixture git failed: $($Arguments -join ' ') : $($output -join ' ')" }
    return ((@($output) | ForEach-Object { [string]$_ }) -join "`n").TrimEnd([char]13, [char]10)
}
function Write-Text([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $Utf8)
}
function Hash([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { return 'missing' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-SourceExclude([string]$Source, [string]$Text) {
    $gitDirectory = G $Source @('rev-parse','--absolute-git-dir')
    Write-Text (Join-Path $gitDirectory 'info/exclude') $Text
}
function TreeHash([string]$Path) {
    $files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File -Force) {
        $rel = $file.FullName.Substring($Path.TrimEnd('\').Length + 1).Replace('\','/')
        if ($rel -ceq '.git' -or $rel -ceq '.packageset-provenance.json') { continue }
        $files.Add($rel, $file.FullName)
    }
    [string[]]$keys = @($files.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
    $record = [Text.StringBuilder]::new()
    foreach ($key in $keys) {
        if ($key.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) { continue }
        [void]$record.Append($key).Append("`n").Append((Hash $files[$key])).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Utf8.GetBytes($record.ToString())))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function InventoryHash([string]$Path) {
    $files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File -Force) {
        $rel = $file.FullName.Substring($Path.TrimEnd('\').Length + 1).Replace('\','/')
        if ($rel -ceq '.git' -or $rel -ceq '.packageset-provenance.json') { continue }
        $files.Add($rel, $file.FullName)
    }
    [string[]]$keys = @($files.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
    $record = [Text.StringBuilder]::new()
    foreach ($key in $keys) { [void]$record.Append($key).Append("`n").Append((Hash $files[$key])).Append("`n") }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Utf8.GetBytes($record.ToString())))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function InitRepo([string]$Path) {
    [IO.Directory]::CreateDirectory($Path) | Out-Null
    [void](G $Path @('init'))
    [void](G $Path @('config','user.name','PackageSet Contract Fixture'))
    [void](G $Path @('config','user.email','fixture@example.invalid'))
    [void](G $Path @('config','core.autocrlf','false'))
}
function NewFixture([string]$Name, [string]$InitialState = 'BootstrapGitlink') {
    $base = Join-Path $Root $Name
    $remote = Join-Path $base 'remote-source'; InitRepo $remote
    Write-Text (Join-Path $remote '.gitattributes') "* text eol=lf`n"
    Write-Text (Join-Path $remote 'Foo.cs') "public sealed class Foo { public const int Value = 7; }`n"
    Write-Text (Join-Path $remote 'Foo.cs.meta') "fileFormatVersion: 2`nguid: 8b88d7e96a2b4c55a551806aaf1cdef0`n"
    [void](G $remote @('add','--all')); [void](G $remote @('commit','-m','Canonical fixture'))
    $set = Join-Path $base 'PackageSet'; InitRepo $set
    [void](G $set @('-c','protocol.file.allow=always','submodule','add','--',$remote,'Sources/Module.Fixture'))
    $sourceRepository = 'https://example.invalid/crafty-racoon/Module.Fixture.git'
    [void](G $set @('config','--file','.gitmodules','submodule.Sources/Module.Fixture.url',$sourceRepository))
    [void](G (Join-Path $set 'Sources/Module.Fixture') @('remote','set-url','origin',$sourceRepository))
    Write-Text (Join-Path $set 'package-set.json') '{"schemaVersion":2,"identity":"PackageSet.Fixture"}'
    [void](G $set @('add','--all')); [void](G $set @('commit','-m','Pin canonical fixture'))
    $project = Join-Path $base 'Project'; InitRepo $project
    foreach ($directory in @('Assets','Packages','ProjectSettings')) { [IO.Directory]::CreateDirectory((Join-Path $project $directory)) | Out-Null }
    Write-Text (Join-Path $project 'ProjectSettings/ProjectVersion.txt') "m_EditorVersion: 6000.3.20f1`n"
    Write-Text (Join-Path $project 'Assets/ProjectOwned.txt') "Never migrate this product content.`n"
    $old = Join-Path $project 'Assets/Old/Module.Fixture'
    if ($InitialState -eq 'BootstrapGitlink') {
        [void](G $project @('-c','protocol.file.allow=always','submodule','add','--',$remote,'Assets/Old/Module.Fixture'))
        [void](G $project @('config','--file','.gitmodules','submodule.Assets/Old/Module.Fixture.url',$sourceRepository))
        [void](G $old @('remote','set-url','origin',$sourceRepository))
        Write-Text ($old + '.meta') "fileFormatVersion: 2`nguid: c521b206252b414ca1df3a9c15551234`nfolderAsset: yes`n"
    } elseif ($InitialState -eq 'PhysicalSnapshot') {
        [IO.Directory]::CreateDirectory($old) | Out-Null
        [IO.File]::Copy((Join-Path $remote '.gitattributes'), (Join-Path $old '.gitattributes'))
        [IO.File]::Copy((Join-Path $remote 'Foo.cs'), (Join-Path $old 'Foo.cs'))
        [IO.File]::Copy((Join-Path $remote 'Foo.cs.meta'), (Join-Path $old 'Foo.cs.meta'))
        Write-Text ($old + '.meta') "fileFormatVersion: 2`nguid: c521b206252b414ca1df3a9c15551234`nfolderAsset: yes`n"
    } elseif ($InitialState -ne 'Absent') {
        throw "Unsupported fixture state: $InitialState"
    }
    [void](G $project @('add','--all')); [void](G $project @('commit','-m','Project bootstrap fixture'))
    return [pscustomobject]@{ Base=$base; Project=$project; PackageSet=$set; Source=(Join-Path $set 'Sources/Module.Fixture'); SourceRepository=$sourceRepository; Old=$old; Projection=(Join-Path $project 'Assets/Managed/Module.Fixture'); InitialState=$InitialState }
}
function AddSecondFixtureEntry($Fixture) {
    $remote = Join-Path $Fixture.Base 'remote-source-two'; InitRepo $remote
    Write-Text (Join-Path $remote '.gitattributes') "* text eol=lf`n"
    Write-Text (Join-Path $remote 'Bar.cs') "public sealed class Bar { public const int Value = 11; }`n"
    [void](G $remote @('add','--all')); [void](G $remote @('commit','-m','Second canonical fixture'))
    [void](G $Fixture.PackageSet @('-c','protocol.file.allow=always','submodule','add','--',$remote,'Sources/Framework.Fixture'))
    $repository = 'https://example.invalid/crafty-racoon/Framework.Fixture.git'
    [void](G $Fixture.PackageSet @('config','--file','.gitmodules','submodule.Sources/Framework.Fixture.url',$repository))
    $source = Join-Path $Fixture.PackageSet 'Sources/Framework.Fixture'
    [void](G $source @('remote','set-url','origin',$repository))
    [void](G $Fixture.PackageSet @('add','--all')); [void](G $Fixture.PackageSet @('commit','-m','Pin second canonical fixture'))
    [void](G $Fixture.Project @('-c','protocol.file.allow=always','submodule','add','--',$remote,'Assets/Old/Framework.Fixture'))
    $old = Join-Path $Fixture.Project 'Assets/Old/Framework.Fixture'
    [void](G $Fixture.Project @('config','--file','.gitmodules','submodule.Assets/Old/Framework.Fixture.url',$repository))
    [void](G $old @('remote','set-url','origin',$repository))
    Write-Text ($old + '.meta') "fileFormatVersion: 2`nguid: d631239d01234d52bce05db1aa001234`nfolderAsset: yes`n"
    [void](G $Fixture.Project @('add','--all')); [void](G $Fixture.Project @('commit','-m','Add second Project bootstrap fixture'))
    $Fixture | Add-Member -NotePropertyName SecondSource -NotePropertyValue $source
    $Fixture | Add-Member -NotePropertyName SecondRepository -NotePropertyValue $repository
    $Fixture | Add-Member -NotePropertyName SecondOld -NotePropertyValue $old
    $Fixture | Add-Member -NotePropertyName SecondProjection -NotePropertyValue (Join-Path $Fixture.Project 'Assets/Managed/Framework.Fixture')
}
function ConfigurePairedMetadataFixture($Fixture, [bool]$UnignoreGeneratedProject = $false, [bool]$TrackGeneratedMetadata = $false) {
    $remote = Join-Path $Fixture.Base 'remote-source'
    $sourceIgnore = "*.meta`n"
    if ($UnignoreGeneratedProject) { $sourceIgnore += "!Generated.csproj`n" }
    Write-Text (Join-Path $remote '.gitignore') $sourceIgnore
    Write-Text (Join-Path $remote 'Generated.csproj') "<Project Sdk=`"Microsoft.NET.Sdk`" />`n"
    $metadata = "fileFormatVersion: 2`nguid: 22222222222222222222222222222222`n"
    if ($TrackGeneratedMetadata) { Write-Text (Join-Path $remote 'Generated.csproj.meta') $metadata }
    [void](G $remote @('add','--all'))
    if ($TrackGeneratedMetadata) { [void](G $remote @('add','--force','--','Generated.csproj.meta')) }
    [void](G $remote @('commit','-m','Add generated project fixture'))
    $revision = G $remote @('rev-parse','HEAD')
    [void](G $Fixture.Source @('remote','set-url','origin',$remote))
    [void](G $Fixture.Source @('fetch','origin',$revision)); [void](G $Fixture.Source @('checkout','--detach',$revision))
    [void](G $Fixture.Source @('remote','set-url','origin',$Fixture.SourceRepository))
    [void](G $Fixture.PackageSet @('add','--','Sources/Module.Fixture')); [void](G $Fixture.PackageSet @('commit','-m','Pin generated project fixture'))
    [void](G $Fixture.Old @('fetch',$remote,$revision)); [void](G $Fixture.Old @('checkout','--detach',$revision))
    Write-Text (Join-Path $Fixture.Project '.gitignore') "*.csproj`n"
    [void](G $Fixture.Project @('add','--all')); [void](G $Fixture.Project @('commit','-m','Pin generated Project bootstrap fixture'))
    if (-not $TrackGeneratedMetadata) {
        Write-Text (Join-Path $Fixture.Source 'Generated.csproj.meta') $metadata
        Write-Text (Join-Path $Fixture.Old 'Generated.csproj.meta') $metadata
    }
}
function NewPlan($Fixture, [string]$ProjectionState = '', [bool]$WrongBlob = $false) {
    $project = $Fixture.Project; $source = $Fixture.Source
    if ([string]::IsNullOrWhiteSpace($ProjectionState)) { $ProjectionState = $Fixture.InitialState }
    $gitdir = (G $project @('rev-parse','--absolute-git-dir')).Replace('/','\')
    $operation = Join-Path $gitdir ('PackageManagement/operations/' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($operation) | Out-Null
    $original = $Fixture.Old
    $section = ''
    if ($ProjectionState -eq 'CorrectJunction' -or $ProjectionState -eq 'Absent') { $original = $Fixture.Projection }
    if ($ProjectionState -eq 'BootstrapGitlink') { $section = 'submodule.Assets/Old/Module.Fixture' }
    $files = @()
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        $relative = $file.FullName.Substring($source.Length + 1).Replace('\','/')
        if ($relative -eq '.git') { continue }
        try { $blob = G $source @('rev-parse',('HEAD:' + $relative)) }
        catch {
            if ($relative.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) { continue }
            throw
        }
        if ($WrongBlob -and $relative -eq 'Foo.cs') { $blob = '0000000000000000000000000000000000000000' }
        $files += [ordered]@{path=$relative; mode='100644'; sourceBlob=$blob}
    }
    $binding = Join-Path $project 'PackageManagement/project.packageset.json'
    $plan = [ordered]@{
        schemaVersion=2; projectRoot=$project; projectGitRoot=$project; projectGitDirectory=$gitdir;
        packageSetRoot=$Fixture.PackageSet; packageSetRevision=(G $Fixture.PackageSet @('rev-parse','HEAD'));
        projectHead=(G $project @('rev-parse','HEAD')); indexHash=(Hash (Join-Path $gitdir 'index'));
        gitmodulesHash=(Hash (Join-Path $project '.gitmodules')); projectBindingHash=(Hash $binding);
        runtimeBindingsHash=(Hash (Join-Path $project 'PackageManagement/runtime.bindings.json')); unityExecutable='not-used-by-tests';
        entries=@([ordered]@{
            identity='Module.Fixture'; sourcePath=$source; originalPath=$original; projectionPath=$Fixture.Projection;
            sourceRepository=$Fixture.SourceRepository; observedRepository=$Fixture.SourceRepository;
            sourceRevision=(G $source @('rev-parse','HEAD')); exactPin=(G $source @('rev-parse','HEAD')); contentHash=(TreeHash $source);
            originalContentHash=($(if ($ProjectionState -eq 'Absent') { 'missing' } else { TreeHash $original }));
            originalInventoryHash=($(if ($ProjectionState -eq 'Absent') { 'missing' } else { InventoryHash $original }));
            originalMetaHash=(Hash ($original+'.meta')); submoduleSection=$section;
            projectionState=$ProjectionState; needsPublish=$false; files=$files; metadata=@(); projectionExclusions=@()
        });
        writes=@([ordered]@{path=$binding; previousHash=(Hash $binding); content="{`"schemaVersion`":1,`"identity`":`"PackageSet.Fixture`"}`n"})
    }
    $path = Join-Path $operation 'plan.json'; Write-Text $path ($plan | ConvertTo-Json -Depth 14)
    Copy-Item -LiteralPath $Helper -Destination (Join-Path $operation 'ApplyDevelopmentBootstrap.ps1')
    Write-Text (Join-Path $operation 'plan.sha256') (Hash $path)
    Write-Text (Join-Path $operation 'helper.sha256') (Hash (Join-Path $operation 'ApplyDevelopmentBootstrap.ps1'))
    Write-Text (Join-Path $operation 'ready.sha256') (Hash $path)
    return [pscustomobject]@{Path=$path; Operation=$operation; Data=$plan}
}
function RewritePlan($Prepared) {
    Write-Text $Prepared.Path ($Prepared.Data | ConvertTo-Json -Depth 14)
    Write-Text (Join-Path $Prepared.Operation 'plan.sha256') (Hash $Prepared.Path)
    Write-Text (Join-Path $Prepared.Operation 'ready.sha256') (Hash $Prepared.Path)
}
function AddSecondEntryToPlan($Prepared, $Fixture) {
    $files = @()
    foreach ($file in Get-ChildItem -LiteralPath $Fixture.SecondSource -Recurse -File -Force) {
        $relative = $file.FullName.Substring($Fixture.SecondSource.Length + 1).Replace('\','/')
        if ($relative -eq '.git') { continue }
        $blob = G $Fixture.SecondSource @('rev-parse',('HEAD:' + $relative))
        $files += [ordered]@{path=$relative; mode='100644'; sourceBlob=$blob}
    }
    $entry = [ordered]@{
        identity='Framework.Fixture'; sourcePath=$Fixture.SecondSource; originalPath=$Fixture.SecondOld; projectionPath=$Fixture.SecondProjection;
        sourceRepository=$Fixture.SecondRepository; observedRepository=$Fixture.SecondRepository;
        sourceRevision=(G $Fixture.SecondSource @('rev-parse','HEAD')); exactPin=(G $Fixture.SecondSource @('rev-parse','HEAD')); contentHash=(TreeHash $Fixture.SecondSource);
        originalContentHash=(TreeHash $Fixture.SecondOld); originalInventoryHash=(InventoryHash $Fixture.SecondOld);
        originalMetaHash=(Hash ($Fixture.SecondOld+'.meta')); submoduleSection='submodule.Assets/Old/Framework.Fixture';
        projectionState='BootstrapGitlink'; needsPublish=$false; files=$files; metadata=@(); projectionExclusions=@()
    }
    $Prepared.Data.entries += $entry
    RewritePlan $Prepared
}
function AddPairedMetadataExclusionToPlan($Prepared, $Fixture) {
    $entry = $Prepared.Data.entries[0]
    $entry.files = @($entry.files | Where-Object { $_.path -cne 'Generated.csproj' -and $_.path -cne 'Generated.csproj.meta' })
    $blob = G $Fixture.Source @('rev-parse','HEAD:Generated.csproj')
    $metadataPath = Join-Path $Fixture.Source 'Generated.csproj.meta'
    $projectMetadataPath = Join-Path $Fixture.Old 'Generated.csproj.meta'
    $projectMetadataHash = Hash $projectMetadataPath
    $sourceMetadataHash = Hash $metadataPath
    $exclusionMetadataHash = if ($sourceMetadataHash -ceq 'missing') { $projectMetadataHash } else { $sourceMetadataHash }
    $metadataBlob = ''
    try { $metadataBlob = G $Fixture.Source @('rev-parse','HEAD:Generated.csproj.meta') }
    catch { $metadataBlob = '' }
    $entry.metadata = @([ordered]@{path='Generated.csproj.meta'; projectHash=$projectMetadataHash; sourcePreviousHash=$sourceMetadataHash; contentBase64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($projectMetadataPath))})
    $entry.projectionExclusions = @(
        [ordered]@{path='Generated.csproj'; sourceBlob=$blob; sourceHash=(Hash (Join-Path $Fixture.Source 'Generated.csproj')); pairedPath=''; reason='ProjectIgnoredGeneratedCsproj'},
        [ordered]@{path='Generated.csproj.meta'; sourceBlob=$metadataBlob; sourceHash=$exclusionMetadataHash; pairedPath='Generated.csproj'; reason='ProjectIgnoredGeneratedCsprojMetadata'}
    )
    RewritePlan $Prepared
}
function AddMetadataToPlan($Prepared, $Fixture, [string]$Relative) {
    $entry = $Prepared.Data.entries[0]
    $projectMetadata = Join-Path ([string]$entry.originalPath) $Relative
    $sourceMetadata = Join-Path $Fixture.Source $Relative
    $projectionRelative = (G $Fixture.Project @('rev-parse','--show-prefix')) + 'Assets/Managed/Module.Fixture/' + $Relative.Replace('\','/')
    $blob = G $Fixture.Project @('hash-object',('--path=' + $projectionRelative),'--',$projectMetadata)
    $entry.metadata = @([ordered]@{
        path=$Relative.Replace('\','/'); projectHash=(Hash $projectMetadata); sourcePreviousHash=(Hash $sourceMetadata);
        contentBase64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($projectMetadata))
    })
    $entry.files += [ordered]@{path=$Relative.Replace('\','/'); mode='100644'; sourceBlob=$blob}
    RewritePlan $Prepared
}
function InvokePlan($Prepared) {
    $old = $ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $output = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $Prepared.Operation 'ApplyDevelopmentBootstrap.ps1') -Plan $Prepared.Path 2>&1
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$old }
    Write-Host (@($output) -join "`n")
    return $code
}

[IO.Directory]::CreateDirectory($Root) | Out-Null
Write-Host "Disposable fixtures retained for diagnosis: $Root"
$parseErrors=$null; $tokens=$null
[void][Management.Automation.Language.Parser]::ParseFile($Helper, [ref]$tokens, [ref]$parseErrors)
Assert (@($parseErrors).Count -eq 0) 'PowerShell 5.1 helper parses'

$leased=NewFixture 'operation-lease'; $leasedPlan=NewPlan $leased
$leasePath=Join-Path $leasedPlan.Operation 'apply.lock'
$leaseStream=[IO.File]::Open($leasePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $leasedExit=InvokePlan $leasedPlan }
finally { $leaseStream.Dispose() }
Assert ($leasedExit -ne 0) 'occupied operation lease blocks a concurrent apply'
Assert (-not (Test-Path -LiteralPath (Join-Path $leasedPlan.Operation 'journal.json'))) 'lease loser does not create or overwrite journal evidence'
Assert (-not (Test-Path -LiteralPath $leased.Projection)) 'lease contention performs no projection cutover'
Assert ((Hash (Join-Path $leased.Project '.git/index')) -eq $leasedPlan.Data.indexHash) 'lease contention preserves Project index'
Remove-Item -LiteralPath $leasePath

$journalDirectory=NewFixture 'journal-directory'; $journalDirectoryPlan=NewPlan $journalDirectory
$journalDirectoryPath=Join-Path $journalDirectoryPlan.Operation 'journal.json'
[IO.Directory]::CreateDirectory($journalDirectoryPath) | Out-Null
$journalDirectoryIndex=Hash (Join-Path $journalDirectory.Project '.git/index')
Assert ((InvokePlan $journalDirectoryPlan) -ne 0) 'occupied non-file journal path blocks apply'
Assert ((Get-Item -LiteralPath $journalDirectoryPath -Force).PSIsContainer) 'non-file journal evidence is preserved for inspection'
Assert ((Hash (Join-Path $journalDirectory.Project '.git/index')) -eq $journalDirectoryIndex) 'non-file journal gate preserves Project index'
Assert (-not (Test-Path -LiteralPath (Join-Path $journalDirectoryPlan.Operation 'apply.lock'))) 'journal path failure releases only the owned operation lease'

$backupOccupancy=NewFixture 'backup-occupancy'; $backupOccupancyPlan=NewPlan $backupOccupancy
$occupiedBackup=Join-Path $backupOccupancyPlan.Operation 'backup'
[IO.Directory]::CreateDirectory($occupiedBackup) | Out-Null
Assert ((InvokePlan $backupOccupancyPlan) -ne 0) 'unexpected prior backup path blocks apply'
Assert ((Get-Item -LiteralPath $occupiedBackup -Force).PSIsContainer) 'unexpected backup evidence is preserved'
Assert (-not (Test-Path -LiteralPath (Join-Path $backupOccupancyPlan.Operation 'journal.json'))) 'prior backup evidence is not relabeled as a new preflight journal'

$missingReady=NewFixture 'missing-ready'; $missingReadyPlan=NewPlan $missingReady
Remove-Item -LiteralPath (Join-Path $missingReadyPlan.Operation 'ready.sha256')
$missingReadyIndex=Hash (Join-Path $missingReady.Project '.git/index')
Assert ((InvokePlan $missingReadyPlan) -ne 0) 'missing successful-Prepare ready marker blocks apply'
$missingReadyJournal=Get-Content -LiteralPath (Join-Path $missingReadyPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($missingReadyJournal.status -eq 'preflight-failed') 'missing ready marker records preflight failure'
Assert ((Hash (Join-Path $missingReady.Project '.git/index')) -eq $missingReadyIndex) 'missing ready marker preserves Project index'
Assert (-not (Test-Path -LiteralPath $missingReady.Projection)) 'missing ready marker performs no projection cutover'

$wrongReady=NewFixture 'wrong-ready'; $wrongReadyPlan=NewPlan $wrongReady
Write-Text (Join-Path $wrongReadyPlan.Operation 'ready.sha256') ('0' * 64)
$wrongReadyIndex=Hash (Join-Path $wrongReady.Project '.git/index')
Assert ((InvokePlan $wrongReadyPlan) -ne 0) 'wrong successful-Prepare ready marker blocks apply'
$wrongReadyJournal=Get-Content -LiteralPath (Join-Path $wrongReadyPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($wrongReadyJournal.status -eq 'preflight-failed') 'wrong ready marker records preflight failure'
Assert ((Hash (Join-Path $wrongReady.Project '.git/index')) -eq $wrongReadyIndex) 'wrong ready marker preserves Project index'
Assert (-not (Test-Path -LiteralPath $wrongReady.Projection)) 'wrong ready marker performs no projection cutover'

$unityLockDirectory=NewFixture 'unity-lock-directory'; $unityLockDirectoryPlan=NewPlan $unityLockDirectory
$unityLockPath=Join-Path $unityLockDirectory.Project 'Temp/UnityLockfile'
[IO.Directory]::CreateDirectory($unityLockPath) | Out-Null
$unityLockDirectoryIndex=Hash (Join-Path $unityLockDirectory.Project '.git/index')
Assert ((InvokePlan $unityLockDirectoryPlan) -ne 0) 'UnityLockfile directory occupant fails closed'
$unityLockDirectoryJournal=Get-Content -LiteralPath (Join-Path $unityLockDirectoryPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($unityLockDirectoryJournal.status -eq 'preflight-failed') 'UnityLockfile directory occupant records preflight failure'
Assert ((Hash (Join-Path $unityLockDirectory.Project '.git/index')) -eq $unityLockDirectoryIndex) 'UnityLockfile directory occupant preserves Project index'
Assert (-not (Test-Path -LiteralPath $unityLockDirectory.Projection)) 'UnityLockfile directory occupant performs no projection cutover'

$recoveryGate=NewFixture 'recovery-gate'; $recoveryGatePlan=NewPlan $recoveryGate
$operationsRoot=Split-Path $recoveryGatePlan.Operation -Parent
$siblingOperation=Join-Path $operationsRoot 'existing-recovery-operation'
[IO.Directory]::CreateDirectory($siblingOperation) | Out-Null
Write-Text (Join-Path $siblingOperation 'journal.json') '{"status":"needs-recovery","moves":[],"links":[],"files":[],"error":"preserved fixture"}'
$recoveryGateIndex=Hash (Join-Path $recoveryGate.Project '.git/index')
Assert ((InvokePlan $recoveryGatePlan) -ne 0) 'sibling operation requiring recovery blocks apply'
Assert ((Hash (Join-Path $recoveryGate.Project '.git/index')) -eq $recoveryGateIndex) 'recovery gate preserves Project index'
Assert (-not (Test-Path -LiteralPath $recoveryGate.Projection)) 'recovery gate performs no projection cutover'

$duplicateJournal=NewFixture 'duplicate-journal-property'; $duplicateJournalPlan=NewPlan $duplicateJournal
$duplicateOperationsRoot=Split-Path $duplicateJournalPlan.Operation -Parent
$duplicateOperation=Join-Path $duplicateOperationsRoot 'existing-duplicate-operation'
[IO.Directory]::CreateDirectory($duplicateOperation) | Out-Null
Write-Text (Join-Path $duplicateOperation 'journal.json') '{"status":"applied","status":"applied","moves":[],"links":[],"files":[],"error":""}'
$duplicateJournalIndex=Hash (Join-Path $duplicateJournal.Project '.git/index')
Assert ((InvokePlan $duplicateJournalPlan) -ne 0) 'duplicate top-level journal status is rejected before ConvertFrom-Json can collapse it'
Assert ((Hash (Join-Path $duplicateJournal.Project '.git/index')) -eq $duplicateJournalIndex) 'duplicate journal property gate preserves Project index'
Assert (-not (Test-Path -LiteralPath $duplicateJournal.Projection)) 'duplicate journal property gate performs no cutover'

$validatedSibling=NewFixture 'validated-sibling'; $validatedSiblingPlan=NewPlan $validatedSibling
$validatedOperationsRoot=Split-Path $validatedSiblingPlan.Operation -Parent
$validatedOperation=Join-Path $validatedOperationsRoot 'existing-validated-operation'
[IO.Directory]::CreateDirectory($validatedOperation) | Out-Null
Write-Text (Join-Path $validatedOperation 'journal.json') '{"status":"validated","moves":[],"links":[],"files":[],"error":""}'
Assert ((InvokePlan $validatedSiblingPlan) -eq 0) 'sibling validated journal without a lease does not claim Project mutation'
Assert ((Get-Item -LiteralPath $validatedSibling.Projection -Force).LinkType -eq 'Junction') 'validated sibling permits the prepared projection cutover'

$gitmodulesLease=NewFixture 'gitmodules-exclusive-write'; $gitmodulesLeasePlan=NewPlan $gitmodulesLease
$gitmodulesLeasePath=Join-Path $gitmodulesLease.Project '.gitmodules'
$gitmodulesLeaseHash=Hash $gitmodulesLeasePath
$gitmodulesLeaseIndex=Hash (Join-Path $gitmodulesLease.Project '.git/index')
$gitmodulesReader=[IO.File]::Open($gitmodulesLeasePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { $gitmodulesLeaseExit=InvokePlan $gitmodulesLeasePlan }
finally { $gitmodulesReader.Dispose() }
Assert ($gitmodulesLeaseExit -ne 0) '.gitmodules mutation requires an exclusive compare-and-write handle'
$gitmodulesLeaseJournal=Get-Content -LiteralPath (Join-Path $gitmodulesLeasePlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($gitmodulesLeaseJournal.status -eq 'rolled-back') '.gitmodules sharing conflict records a rolled-back transaction'
Assert ((Hash $gitmodulesLeasePath) -eq $gitmodulesLeaseHash) '.gitmodules sharing conflict preserves declaration bytes'
Assert ((Hash (Join-Path $gitmodulesLease.Project '.git/index')) -eq $gitmodulesLeaseIndex) '.gitmodules sharing conflict preserves Project index'
Assert (Test-Path -LiteralPath $gitmodulesLease.Old) '.gitmodules sharing conflict preserves the bootstrap checkout'
Assert (-not (Test-Path -LiteralPath $gitmodulesLease.Projection)) '.gitmodules sharing conflict performs no projection cutover'

$rootMetaLease=NewFixture 'root-meta-exclusive-write'; $rootMetaLeasePlan=NewPlan $rootMetaLease
$rootMetaLeasePath=$rootMetaLease.Old+'.meta'
$rootMetaLeaseHash=Hash $rootMetaLeasePath
$rootMetaLeaseIndex=Hash (Join-Path $rootMetaLease.Project '.git/index')
$rootMetaReader=[IO.File]::Open($rootMetaLeasePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { $rootMetaLeaseExit=InvokePlan $rootMetaLeasePlan }
finally { $rootMetaReader.Dispose() }
Assert ($rootMetaLeaseExit -ne 0) 'projection-root metadata relocation requires exclusive source ownership'
$rootMetaLeaseJournal=Get-Content -LiteralPath (Join-Path $rootMetaLeasePlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($rootMetaLeaseJournal.status -eq 'rolled-back') 'projection-root metadata sharing conflict rolls back the cutover'
Assert ((Hash $rootMetaLeasePath) -eq $rootMetaLeaseHash) 'projection-root metadata sharing conflict preserves GUID bytes'
Assert ((Hash (Join-Path $rootMetaLease.Project '.git/index')) -eq $rootMetaLeaseIndex) 'projection-root metadata sharing conflict preserves Project index'
Assert (Test-Path -LiteralPath $rootMetaLease.Old) 'projection-root metadata sharing conflict restores the bootstrap checkout'
Assert (-not (Test-Path -LiteralPath $rootMetaLease.Projection)) 'projection-root metadata sharing conflict removes its Junction'
Assert (-not (Test-Path -LiteralPath (Join-Path $rootMetaLease.Project 'Assets/Managed'))) 'projection-root metadata rollback removes its helper-created empty parent'

$fixture=NewFixture 'success'; $prepared=NewPlan $fixture
$meta=Hash ($fixture.Old+'.meta'); $owned=Hash (Join-Path $fixture.Project 'Assets/ProjectOwned.txt')
Assert ((InvokePlan $prepared) -eq 0) 'declared runtime conversion applies'
Assert ((Get-Item -LiteralPath $fixture.Projection -Force).LinkType -eq 'Junction') 'projection is a Junction'
Assert (-not (Test-Path -LiteralPath $fixture.Old)) 'old source location was relocated'
Assert ((Hash ($fixture.Projection+'.meta')) -eq $meta) 'folder GUID metadata preserved'
Assert ((Hash (Join-Path $fixture.Project 'Assets/ProjectOwned.txt')) -eq $owned) 'product-owned content unchanged'
$sourceBlob=G $fixture.Source @('rev-parse','HEAD:Foo.cs')
$indexLine=G $fixture.Project @('ls-files','--stage','--','Assets/Managed/Module.Fixture/Foo.cs')
Assert ($indexLine.StartsWith('100644 '+$sourceBlob)) 'Project index has the canonical ordinary file blob'
Assert ((G $fixture.Project @('config','--file','.gitmodules','--list')).Length -eq 0) 'old Project submodule registration removed'
$journal=Get-Content -LiteralPath (Join-Path $prepared.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($journal.status -eq 'applied') 'journal records successful apply'
Assert (Test-Path -LiteralPath (Join-Path $prepared.Operation 'backup/project.index')) 'original index backup retained'
$expectedBindingPath=Join-Path $fixture.Project 'PackageManagement/project.packageset.json'
$bindingRecord=$journal.files[$journal.files.Length - 1]
Assert ([StringComparer]::OrdinalIgnoreCase.Equals([string]$bindingRecord.path,$expectedBindingPath)) 'journal ends with the approved binding write'
Assert (-not $bindingRecord.existed -and $bindingRecord.created) 'journal distinguishes a binding file created by this apply'
$localState=Get-Content -LiteralPath (Join-Path $fixture.Project '.git/PackageManagement/local-checkout.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($localState.schemaVersion -eq 1) 'local checkout state uses the C# schema version'
$persistedPlan=Get-Content -LiteralPath $prepared.Path -Raw -Encoding UTF8 | ConvertFrom-Json
$localCheckout=[IO.Path]::GetFullPath([string]$localState.checkoutPath)
$expectedCheckout=[IO.Path]::GetFullPath([string]$persistedPlan.packageSetRoot)
Assert ([StringComparer]::OrdinalIgnoreCase.Equals($localCheckout,$expectedCheckout)) 'local checkout state uses the C# checkoutPath field'
$journalHash=Hash (Join-Path $prepared.Operation 'journal.json')
Assert ((InvokePlan $prepared) -ne 0) 'replaying a used plan is blocked'
Assert ((Hash (Join-Path $prepared.Operation 'journal.json')) -eq $journalHash) 'replay does not overwrite recovery evidence'
[void](G $fixture.Project @('commit','-m','Committed ordinary Project snapshot'))
$fresh=Join-Path $fixture.Base 'Fresh'
[void](G $fixture.Base @('clone','--no-local','--',$fixture.Project,$fresh))
$freshDirectory=Get-Item -LiteralPath (Join-Path $fresh 'Assets/Managed/Module.Fixture') -Force
Assert (($freshDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'fresh clone has an ordinary physical directory'
Assert ((Hash (Join-Path $fresh 'Assets/Managed/Module.Fixture/Foo.cs')) -eq (Hash (Join-Path $fixture.Source 'Foo.cs'))) 'fresh clone content matches without source acquisition'
$again=NewPlan $fixture 'CorrectJunction'
Assert ((InvokePlan $again) -eq 0) 'already-correct backing can be applied idempotently'
$againJournal=Get-Content -LiteralPath (Join-Path $again.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (@($againJournal.links).Count -eq 0 -and @($againJournal.moves).Count -eq 0) 'idempotent apply does not recreate Junctions'
Assert (@($againJournal.directories).Count -eq 0) 'idempotent apply does not claim existing parent directories'

$physical=NewFixture 'physical' 'PhysicalSnapshot'
[byte[]]$physicalMetaBytes=[byte[]](0xef,0xbb,0xbf)+$Utf8.GetBytes("fileFormatVersion: 2`nguid: c521b206252b414ca1df3a9c15551234`nfolderAsset: yes`n")
[IO.File]::WriteAllBytes(($physical.Old+'.meta'),$physicalMetaBytes)
[void](G $physical.Project @('add','--','Assets/Old/Module.Fixture.meta'))
[void](G $physical.Project @('commit','-m','Preserve byte-sensitive folder metadata'))
$physicalPlan=NewPlan $physical
$physicalMeta=Hash ($physical.Old+'.meta')
Assert ((InvokePlan $physicalPlan) -eq 0) 'physical ordinary snapshot applies'
Assert ((Get-Item -LiteralPath $physical.Projection -Force).LinkType -eq 'Junction') 'physical snapshot becomes a Junction'
Assert ((Hash ($physical.Projection+'.meta')) -eq $physicalMeta) 'physical snapshot folder metadata bytes are preserved exactly'

$physicalGit=NewFixture 'physical-root-git' 'PhysicalSnapshot'; $physicalGitPlan=NewPlan $physicalGit
$physicalGitMetadata=Join-Path $physicalGit.Old '.git'
[IO.Directory]::CreateDirectory($physicalGitMetadata) | Out-Null
$physicalGitIndex=Hash (Join-Path $physicalGit.Project '.git/index')
Assert ((InvokePlan $physicalGitPlan) -ne 0) 'physical snapshot with root Git metadata is rejected'
Assert ((Get-Item -LiteralPath $physicalGitMetadata -Force).PSIsContainer) 'rejected physical root Git metadata is preserved'
Assert ((Hash (Join-Path $physicalGit.Project '.git/index')) -eq $physicalGitIndex) 'physical root Git metadata rejection preserves Project index'
Assert (-not (Test-Path -LiteralPath $physicalGit.Projection)) 'physical root Git metadata rejection performs no cutover'

$multi=NewFixture 'multi-entry'; AddSecondFixtureEntry $multi
$multiPlan=NewPlan $multi; AddSecondEntryToPlan $multiPlan $multi
Assert ((InvokePlan $multiPlan) -eq 0) 'multi-entry apply keeps validation inventory scoped to each entry'
Assert ((Get-Item -LiteralPath $multi.Projection -Force).LinkType -eq 'Junction') 'multi-entry apply converts the first projection'
Assert ((Get-Item -LiteralPath $multi.SecondProjection -Force).LinkType -eq 'Junction') 'multi-entry apply converts the second projection'

$paired=NewFixture 'paired-generated-metadata'; ConfigurePairedMetadataFixture $paired
$pairedPlan=NewPlan $paired; AddPairedMetadataExclusionToPlan $pairedPlan $paired
Assert ((InvokePlan $pairedPlan) -eq 0) 'approved generated csproj and paired metadata exclusions apply'
$pairedCsprojIndex=G $paired.Project @('ls-files','--','Assets/Managed/Module.Fixture/Generated.csproj')
$pairedMetaIndex=G $paired.Project @('ls-files','--','Assets/Managed/Module.Fixture/Generated.csproj.meta')
Assert (-not $pairedCsprojIndex -and -not $pairedMetaIndex) 'paired generated csproj and metadata remain excluded from the Project index'

$pairedTracked=NewFixture 'paired-generated-metadata-tracked'; ConfigurePairedMetadataFixture $pairedTracked $false $true
$pairedTrackedPlan=NewPlan $pairedTracked; AddPairedMetadataExclusionToPlan $pairedTrackedPlan $pairedTracked
Assert ((InvokePlan $pairedTrackedPlan) -eq 0) 'tracked paired csproj metadata is excluded with its ignored generated project'
$pairedTrackedCsprojIndex=G $pairedTracked.Project @('ls-files','--','Assets/Managed/Module.Fixture/Generated.csproj')
$pairedTrackedMetaIndex=G $pairedTracked.Project @('ls-files','--','Assets/Managed/Module.Fixture/Generated.csproj.meta')
Assert (-not $pairedTrackedCsprojIndex -and -not $pairedTrackedMetaIndex) 'tracked paired csproj and metadata remain excluded from the Project index'

$pairedFinalIgnore=NewFixture 'paired-final-ignore-change'; ConfigurePairedMetadataFixture $pairedFinalIgnore $true
$pairedFinalIgnorePlan=NewPlan $pairedFinalIgnore; AddPairedMetadataExclusionToPlan $pairedFinalIgnorePlan $pairedFinalIgnore
$pairedFinalIgnoreIndex=Hash (Join-Path $pairedFinalIgnore.Project '.git/index')
Assert ((InvokePlan $pairedFinalIgnorePlan) -ne 0) 'final projected .csproj ignore policy is rechecked after Junction creation'
$pairedFinalIgnoreJournal=Get-Content -LiteralPath (Join-Path $pairedFinalIgnorePlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($pairedFinalIgnoreJournal.status -eq 'rolled-back') 'final projected .csproj ignore drift rolls back the cutover'
Assert ((Hash (Join-Path $pairedFinalIgnore.Project '.git/index')) -eq $pairedFinalIgnoreIndex) 'final projected .csproj ignore drift preserves Project index'
Assert (Test-Path -LiteralPath $pairedFinalIgnore.Old) 'final projected .csproj ignore drift restores the bootstrap checkout'
Assert (-not (Test-Path -LiteralPath $pairedFinalIgnore.Projection)) 'final projected .csproj ignore drift removes only its own Junction'

$pairedFresh=NewFixture 'paired-generated-metadata-fresh-source'; ConfigurePairedMetadataFixture $pairedFresh
Remove-Item -LiteralPath (Join-Path $pairedFresh.Source 'Generated.csproj.meta')
$pairedFreshPlan=NewPlan $pairedFresh; AddPairedMetadataExclusionToPlan $pairedFreshPlan $pairedFresh
Assert ((InvokePlan $pairedFreshPlan) -eq 0) 'missing canonical paired metadata materializes transactionally without entering the Project index'
Assert (Test-Path -LiteralPath (Join-Path $pairedFresh.Source 'Generated.csproj.meta')) 'fresh canonical source receives the Project-owned paired metadata bytes'
$pairedFreshMetaIndex=G $pairedFresh.Project @('ls-files','--','Assets/Managed/Module.Fixture/Generated.csproj.meta')
Assert (-not $pairedFreshMetaIndex) 'materialized paired metadata remains excluded from the Project index'

$pairedDrift=NewFixture 'paired-generated-metadata-drift'; ConfigurePairedMetadataFixture $pairedDrift
$pairedDriftPlan=NewPlan $pairedDrift; AddPairedMetadataExclusionToPlan $pairedDriftPlan $pairedDrift
Write-Text (Join-Path $pairedDrift.Source 'Generated.csproj.meta') "fileFormatVersion: 2`nguid: drifted-after-plan`n"
$pairedDriftIndex=Hash (Join-Path $pairedDrift.Project '.git/index')
Assert ((InvokePlan $pairedDriftPlan) -ne 0) 'paired generated metadata drift fails preflight'
Assert ((Hash (Join-Path $pairedDrift.Project '.git/index')) -eq $pairedDriftIndex) 'paired generated metadata drift preserves Project index'
Assert (-not (Test-Path -LiteralPath $pairedDrift.Projection)) 'paired generated metadata drift performs no projection cutover'

$metadata=NewFixture 'metadata-materialization' 'PhysicalSnapshot'
Write-Text (Join-Path $metadata.Old '.gitattributes.meta') "fileFormatVersion: 2`nguid: 42f44dfca1cc4b25a3db31cc704eca11`n"
[void](G $metadata.Project @('add','--','Assets/Old/Module.Fixture/.gitattributes.meta'))
[void](G $metadata.Project @('commit','-m','Track Project-owned nested metadata'))
Write-SourceExclude $metadata.Source "*.meta`n"
$metadataPlan=NewPlan $metadata 'PhysicalSnapshot'; AddMetadataToPlan $metadataPlan $metadata '.gitattributes.meta'
$metadataHash=Hash (Join-Path $metadata.Old '.gitattributes.meta')
Assert ((InvokePlan $metadataPlan) -eq 0) 'Project-owned nested metadata materializes inside canonical source transactionally'
Assert ((Hash (Join-Path $metadata.Source '.gitattributes.meta')) -eq $metadataHash) 'materialized canonical metadata preserves the Project GUID bytes'
$metadataIndex=G $metadata.Project @('ls-files','--stage','--','Assets/Managed/Module.Fixture/.gitattributes.meta')
Assert ($metadataIndex.StartsWith('100644 ')) 'materialized nested metadata is staged as an ordinary Project blob'

$metadataDrift=NewFixture 'metadata-drift' 'PhysicalSnapshot'
Write-Text (Join-Path $metadataDrift.Old '.gitattributes.meta') "fileFormatVersion: 2`nguid: 77777777777777777777777777777777`n"
[void](G $metadataDrift.Project @('add','--','Assets/Old/Module.Fixture/.gitattributes.meta'))
[void](G $metadataDrift.Project @('commit','-m','Track metadata drift fixture'))
Write-SourceExclude $metadataDrift.Source "*.meta`n"
$metadataDriftPlan=NewPlan $metadataDrift 'PhysicalSnapshot'; AddMetadataToPlan $metadataDriftPlan $metadataDrift '.gitattributes.meta'
Write-Text (Join-Path $metadataDrift.Old '.gitattributes.meta') "fileFormatVersion: 2`nguid: changed-after-plan`n"
$metadataDriftIndex=Hash (Join-Path $metadataDrift.Project '.git/index')
Assert ((InvokePlan $metadataDriftPlan) -ne 0) 'nested metadata hash drift fails before mutation'
Assert (-not (Test-Path -LiteralPath (Join-Path $metadataDrift.Source '.gitattributes.meta'))) 'metadata preflight failure does not materialize canonical metadata'
Assert ((Hash (Join-Path $metadataDrift.Project '.git/index')) -eq $metadataDriftIndex) 'metadata preflight failure preserves Project index'

$sourceMetadataLease=NewFixture 'source-metadata-exclusive-write' 'PhysicalSnapshot'
Write-Text (Join-Path $sourceMetadataLease.Old '.gitattributes.meta') "fileFormatVersion: 2`nguid: 1234567890abcdef1234567890abcdef`n"
[void](G $sourceMetadataLease.Project @('add','--','Assets/Old/Module.Fixture/.gitattributes.meta'))
[void](G $sourceMetadataLease.Project @('commit','-m','Track source metadata lease fixture'))
Write-SourceExclude $sourceMetadataLease.Source "*.meta`n"
Write-Text (Join-Path $sourceMetadataLease.Source '.gitattributes.meta') "fileFormatVersion: 2`nguid: 1234567890abcdef1234567890abcdef`n"
$sourceMetadataLeasePlan=NewPlan $sourceMetadataLease 'PhysicalSnapshot'
AddMetadataToPlan $sourceMetadataLeasePlan $sourceMetadataLease '.gitattributes.meta'
$sourceMetadataLeasePath=Join-Path $sourceMetadataLease.Source '.gitattributes.meta'
$sourceMetadataLeaseHash=Hash $sourceMetadataLeasePath
$sourceMetadataLeaseIndex=Hash (Join-Path $sourceMetadataLease.Project '.git/index')
$sourceMetadataReader=[IO.File]::Open($sourceMetadataLeasePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { $sourceMetadataLeaseExit=InvokePlan $sourceMetadataLeasePlan }
finally { $sourceMetadataReader.Dispose() }
Assert ($sourceMetadataLeaseExit -ne 0) 'canonical source metadata mutation requires an exclusive compare-and-write handle'
$sourceMetadataLeaseJournal=Get-Content -LiteralPath (Join-Path $sourceMetadataLeasePlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($sourceMetadataLeaseJournal.status -eq 'rolled-back') 'source metadata sharing conflict records a rolled-back transaction'
Assert ((Hash $sourceMetadataLeasePath) -eq $sourceMetadataLeaseHash) 'source metadata sharing conflict preserves competing bytes'
Assert ((Hash (Join-Path $sourceMetadataLease.Project '.git/index')) -eq $sourceMetadataLeaseIndex) 'source metadata sharing conflict preserves Project index'
Assert (Test-Path -LiteralPath $sourceMetadataLease.Old) 'source metadata sharing conflict preserves the physical Project snapshot'
Assert (-not (Test-Path -LiteralPath $sourceMetadataLease.Projection)) 'source metadata sharing conflict performs no projection cutover'

$absent=NewFixture 'absent' 'Absent'; $absentPlan=NewPlan $absent
Assert ((InvokePlan $absentPlan) -eq 0) 'absent canonical projection materializes'
Assert ((Get-Item -LiteralPath $absent.Projection -Force).LinkType -eq 'Junction') 'absent projection becomes a Junction'
$absentJournal=Get-Content -LiteralPath (Join-Path $absentPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (@($absentJournal.moves).Count -eq 0) 'absent projection does not invent a source backup'

$occupied=NewFixture 'occupied' 'Absent'; $occupiedPlan=NewPlan $occupied
Write-Text (Join-Path $occupied.Projection 'UserOwned.txt') "unrelated`n"
$occupiedHash=Hash (Join-Path $occupied.Projection 'UserOwned.txt')
Assert ((InvokePlan $occupiedPlan) -ne 0) 'occupied absent projection fails preflight'
Assert ((Hash (Join-Path $occupied.Projection 'UserOwned.txt')) -eq $occupiedHash) 'occupied-path data remains unchanged'
$occupiedJournal=Get-Content -LiteralPath (Join-Path $occupiedPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($occupiedJournal.status -eq 'preflight-failed') 'occupied absent projection records preflight failure'

$originDrift=NewFixture 'origin-drift'; $originDriftPlan=NewPlan $originDrift
[void](G $originDrift.Source @('remote','set-url','origin','https://example.invalid/crafty-racoon/Other.Module.git'))
$originDriftIndex=Hash (Join-Path $originDrift.Project '.git/index')
Assert ((InvokePlan $originDriftPlan) -ne 0) 'source origin drift fails preflight'
Assert ((Hash (Join-Path $originDrift.Project '.git/index')) -eq $originDriftIndex) 'origin drift preserves Project index'

$bootstrapOriginDrift=NewFixture 'bootstrap-origin-drift'; $bootstrapOriginDriftPlan=NewPlan $bootstrapOriginDrift
[void](G $bootstrapOriginDrift.Old @('remote','set-url','origin','https://example.invalid/crafty-racoon/Other.Bootstrap.git'))
$bootstrapOriginDriftIndex=Hash (Join-Path $bootstrapOriginDrift.Project '.git/index')
Assert ((InvokePlan $bootstrapOriginDriftPlan) -ne 0) 'bootstrap checkout origin drift fails preflight'
Assert ((Hash (Join-Path $bootstrapOriginDrift.Project '.git/index')) -eq $bootstrapOriginDriftIndex) 'bootstrap origin drift preserves Project index'
Assert (Test-Path -LiteralPath $bootstrapOriginDrift.Old) 'bootstrap origin drift preserves the independent checkout'

$pinMismatch=NewFixture 'pin-mismatch'; $pinMismatchPlan=NewPlan $pinMismatch
$pinMismatchPlan.Data.entries[0].exactPin = '1111111111111111111111111111111111111111'
RewritePlan $pinMismatchPlan
$pinMismatchIndex=Hash (Join-Path $pinMismatch.Project '.git/index')
Assert ((InvokePlan $pinMismatchPlan) -ne 0) 'source exact pin mismatch fails preflight'
Assert ((Hash (Join-Path $pinMismatch.Project '.git/index')) -eq $pinMismatchIndex) 'pin mismatch preserves Project index'

$modeMismatch=NewFixture 'source-mode-mismatch'; $modeMismatchPlan=NewPlan $modeMismatch
$modeMismatchRecord=@($modeMismatchPlan.Data.entries[0].files | Where-Object { $_.path -ceq 'Foo.cs' })[0]
$modeMismatchRecord.mode='100755'
RewritePlan $modeMismatchPlan
$modeMismatchIndex=Hash (Join-Path $modeMismatch.Project '.git/index')
Assert ((InvokePlan $modeMismatchPlan) -ne 0) 'planned source mode must match the exact HEAD tree entry'
Assert ((Hash (Join-Path $modeMismatch.Project '.git/index')) -eq $modeMismatchIndex) 'source mode mismatch preserves Project index'
Assert (Test-Path -LiteralPath $modeMismatch.Old) 'source mode mismatch preserves the bootstrap checkout'
Assert (-not (Test-Path -LiteralPath $modeMismatch.Projection)) 'source mode mismatch performs no projection cutover'

$missingRepository=NewFixture 'missing-repository'; $missingRepositoryPlan=NewPlan $missingRepository
$missingRepositoryPlan.Data.entries[0].sourceRepository = ''
$missingRepositoryPlan.Data.entries[0].observedRepository = ''
RewritePlan $missingRepositoryPlan
$missingRepositoryIndex=Hash (Join-Path $missingRepository.Project '.git/index')
Assert ((InvokePlan $missingRepositoryPlan) -ne 0) 'missing Existing source repository fields fail preflight'
Assert ((Hash (Join-Path $missingRepository.Project '.git/index')) -eq $missingRepositoryIndex) 'missing repository authority preserves Project index'

$bindingDrift=NewFixture 'binding-drift'; $bindingDriftPlan=NewPlan $bindingDrift
Write-Text (Join-Path $bindingDrift.Project 'PackageManagement/project.packageset.json') "{`"schemaVersion`":3,`"trackingRef`":`"drift`"}`n"
$bindingDriftIndex=Hash (Join-Path $bindingDrift.Project '.git/index')
Assert ((InvokePlan $bindingDriftPlan) -ne 0) 'durable binding hash drift fails preflight even when the observed file was absent'
Assert ((Hash (Join-Path $bindingDrift.Project '.git/index')) -eq $bindingDriftIndex) 'binding hash drift preserves Project index'

$bindingLease=NewFixture 'binding-exclusive-write'
$bindingLeasePath=Join-Path $bindingLease.Project 'PackageManagement/project.packageset.json'
Write-Text $bindingLeasePath "{`"schemaVersion`":1,`"identity`":`"Before.Apply`"}`n"
[void](G $bindingLease.Project @('add','--','PackageManagement/project.packageset.json'))
[void](G $bindingLease.Project @('commit','-m','Track binding lease fixture'))
$bindingLeasePlan=NewPlan $bindingLease
$bindingLeaseHash=Hash $bindingLeasePath
$bindingReader=[IO.File]::Open($bindingLeasePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { $bindingLeaseExit=InvokePlan $bindingLeasePlan }
finally { $bindingReader.Dispose() }
Assert ($bindingLeaseExit -ne 0) 'binding write requires an exclusive compare-and-write handle'
$bindingLeaseJournal=Get-Content -LiteralPath (Join-Path $bindingLeasePlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($bindingLeaseJournal.status -eq 'rolled-back') 'exclusive binding conflict rolls back an apply that reached cutover'
Assert ((Hash $bindingLeasePath) -eq $bindingLeaseHash) 'exclusive binding conflict preserves the competing binding bytes'
Assert (Test-Path -LiteralPath $bindingLease.Old) 'exclusive binding conflict restores the bootstrap checkout'
Assert (-not (Test-Path -LiteralPath $bindingLease.Projection)) 'exclusive binding conflict removes only its own Junction'
Assert (-not (Test-Path -LiteralPath (Join-Path $bindingLease.Project 'Assets/Managed'))) 'binding rollback removes the helper-created empty projection parent'

$stale=NewFixture 'stale'; $stalePlan=NewPlan $stale
Write-Text (Join-Path $stale.Old 'Foo.cs') "public sealed class Foo { public const int Value = 99; }`n"
$edited=Hash (Join-Path $stale.Old 'Foo.cs')
Assert ((InvokePlan $stalePlan) -ne 0) 'Project-only changes after preparation block apply'
Assert ((Hash (Join-Path $stale.Project '.git/index')) -eq $stalePlan.Data.indexHash) 'preflight failure preserves Project index'
Assert ((Hash (Join-Path $stale.Old 'Foo.cs')) -eq $edited) 'preflight failure preserves independent edits'
Assert (-not (Test-Path -LiteralPath $stale.Projection)) 'preflight failure performs no projection cutover'

$rollback=NewFixture 'rollback'
$rollbackBindingPath=Join-Path $rollback.Project 'PackageManagement/project.packageset.json'
Write-Text $rollbackBindingPath "{`"schemaVersion`":1,`"identity`":`"Rollback.Before`"}`n"
[void](G $rollback.Project @('add','--','PackageManagement/project.packageset.json'))
[void](G $rollback.Project @('commit','-m','Track rollback binding fixture'))
$badPlan=NewPlan $rollback
$oldHash=TreeHash $rollback.Old
$rollbackBindingReader=[IO.File]::Open($rollbackBindingPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { $rollbackExit=InvokePlan $badPlan }
finally { $rollbackBindingReader.Dispose() }
Assert ($rollbackExit -ne 0) 'caught apply-phase failure enters rollback'
$recovery=Get-Content -LiteralPath (Join-Path $badPlan.Operation 'journal.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($recovery.status -eq 'rolled-back') 'failed apply rolls back'
Assert ((TreeHash $rollback.Old) -eq $oldHash) 'rollback restores original source directory'
Assert ((Hash (Join-Path $rollback.Project '.git/index')) -eq $badPlan.Data.indexHash) 'rollback preserves original Project index'
Assert ((Hash (Join-Path $rollback.Project '.gitmodules')) -eq $badPlan.Data.gitmodulesHash) 'rollback restores gitmodules'
Assert (-not (Test-Path -LiteralPath $rollback.Projection)) 'rollback removes only the created Junction'
Assert (-not (Test-Path -LiteralPath (Join-Path $rollback.Project '.git/index.lock'))) 'owned Git lock released'

$metadataRollback=NewFixture 'metadata-rollback' 'PhysicalSnapshot'
Write-Text (Join-Path $metadataRollback.Old '.gitattributes.meta') "fileFormatVersion: 2`nguid: 99999999999999999999999999999999`n"
[void](G $metadataRollback.Project @('add','--','Assets/Old/Module.Fixture/.gitattributes.meta'))
[void](G $metadataRollback.Project @('commit','-m','Track metadata rollback fixture'))
Write-SourceExclude $metadataRollback.Source "*.meta`n"
$metadataRollbackPlan=NewPlan $metadataRollback 'PhysicalSnapshot' $true
AddMetadataToPlan $metadataRollbackPlan $metadataRollback '.gitattributes.meta'
$metadataRollbackIndex=Hash (Join-Path $metadataRollback.Project '.git/index')
Assert ((InvokePlan $metadataRollbackPlan) -ne 0) 'invalid source blob fails before metadata materialization'
Assert (-not (Test-Path -LiteralPath (Join-Path $metadataRollback.Source '.gitattributes.meta'))) 'metadata preflight leaves canonical metadata absent'
Assert (Test-Path -LiteralPath (Join-Path $metadataRollback.Old '.gitattributes.meta')) 'metadata preflight preserves the original Project metadata location'
Assert ((Hash (Join-Path $metadataRollback.Project '.git/index')) -eq $metadataRollbackIndex) 'metadata preflight preserves Project index'
Write-Host "PASS: $Checks offline apply contracts. Unity Editor compilation is a separate manual gate."
exit 0
