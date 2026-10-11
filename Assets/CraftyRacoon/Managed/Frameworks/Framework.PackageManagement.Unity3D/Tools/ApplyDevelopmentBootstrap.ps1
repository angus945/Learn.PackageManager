[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Plan, [int]$WaitForProcessId = 0, [switch]$ReopenUnity)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Utf8 = [Text.UTF8Encoding]::new($false)

function Quote-Argument([string]$Value) {
    if ($Value.IndexOf([char]0) -ge 0 -or $Value.Contains("`n") -or $Value.Contains("`r")) { throw 'Unsafe process argument.' }
    $b = [Text.StringBuilder]::new(); [void]$b.Append('"'); $slashes = 0
    foreach ($c in $Value.ToCharArray()) {
        if ($c -eq '\') { $slashes++; continue }
        if ($c -eq '"') { [void]$b.Append(('\' * ($slashes * 2 + 1))).Append($c) }
        else { [void]$b.Append(('\' * $slashes)).Append($c) }
        $slashes = 0
    }
    [void]$b.Append(('\' * ($slashes * 2))).Append('"'); return $b.ToString()
}
function Git([string]$Root, [string[]]$Arguments, [string]$Index = '') {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'; $start.WorkingDirectory = $Root
    $actualArguments = $Arguments
    if ($Arguments.Count -eq 0 -or $Arguments[0] -cne 'check-ignore') { $actualArguments = @('--literal-pathspecs') + $Arguments }
    $start.Arguments = (($actualArguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $Utf8; $start.StandardErrorEncoding = $Utf8
    $start.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $start.EnvironmentVariables['GIT_OPTIONAL_LOCKS'] = '0'
    if ($Index) { $start.EnvironmentVariables['GIT_INDEX_FILE'] = $Index }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outTask = $process.StandardOutput.ReadToEndAsync(); $errTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'Git operation timed out.' }
        $stdout = $outTask.GetAwaiter().GetResult(); $stderr = $errTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git failed ($($process.ExitCode)): $stderr" }
        return $stdout.TrimEnd([char]13, [char]10)
    } finally { $process.Dispose() }
}
function Full([string]$Path) { return [IO.Path]::GetFullPath($Path).TrimEnd([char]92, [char]47) }
function Within([string]$Root, [string]$Path) { return (Full $Path).StartsWith((Full $Root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }
function Relative([string]$Root, [string]$Path) {
    if (-not (Within $Root $Path)) { throw "Outside declared root: $Path" }
    return (Full $Path).Substring((Full $Root).Length + 1).Replace('\', '/')
}
function Hash-File([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return 'missing' }
    $invalidAttributes = [IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint
    if (($item.Attributes -band $invalidAttributes) -ne 0 -or -not [IO.File]::Exists($Path)) { throw "Hash target is not an ordinary file: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Hash-Bytes([byte[]]$Bytes) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function No-ReparseAncestors([string]$Path, [bool]$AllowLeaf = $false) {
    if ($AllowLeaf) { $Path = [IO.Path]::GetDirectoryName($Path) }
    $current = [IO.DirectoryInfo]::new($Path)
    while ($null -ne $current) {
        $item = Get-Item -LiteralPath $current.FullName -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point ancestor: $($current.FullName)" }
        $current = $current.Parent
    }
}
function Require-OrdinaryFile([string]$Path) {
    No-ReparseAncestors $Path
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    $invalidAttributes = [IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint
    if ($null -eq $item -or ($item.Attributes -band $invalidAttributes) -ne 0 -or -not [IO.File]::Exists($Path)) { throw "Expected an ordinary file: $Path" }
}
function Require-OrdinaryDirectory([string]$Path) {
    No-ReparseAncestors $Path
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or -not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Expected an ordinary directory: $Path" }
}
function Ensure-JournaledOrdinaryDirectory([string]$Path) {
    $fullPath = Full $Path
    $missing = [Collections.Generic.List[string]]::new()
    $current = $fullPath
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            Require-OrdinaryDirectory $current
            break
        }
        $missing.Add($current)
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -ceq $current) { throw "Unable to establish an ordinary parent directory for: $Path" }
        $current = $parent
    }
    for ($index = $missing.Count - 1; $index -ge 0; $index--) {
        $directoryPath = $missing[$index]
        Require-OrdinaryDirectory ([IO.Path]::GetDirectoryName($directoryPath))
        Require-UnoccupiedPath $directoryPath
        $record = [ordered]@{ path = $directoryPath; owned = $false }
        [void]$Directories.Add($record)
        Save-Journal 'applying'
        [IO.Directory]::CreateDirectory($directoryPath) | Out-Null
        Require-OrdinaryDirectory $directoryPath
        $record.owned = $true
        Save-Journal 'applying'
    }
}
function Require-UnoccupiedPath([string]$Path) {
    No-ReparseAncestors $Path $true
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item) { throw "Expected an unoccupied path: $Path" }
}
function Read-StreamBytes([IO.FileStream]$Stream) {
    if ($Stream.Length -gt [int]::MaxValue) { throw 'File is too large for a transactional metadata write.' }
    $length = [int]$Stream.Length
    if ($length -eq 0) { return ,[byte[]]@() }
    $bytes = New-Object byte[] $length
    $offset = 0
    $Stream.Position = 0
    while ($offset -lt $length) {
        $read = $Stream.Read($bytes, $offset, $length - $offset)
        if ($read -le 0) { throw 'Unexpected end of file during transactional metadata read.' }
        $offset += $read
    }
    return ,$bytes
}
function Write-NewFile([string]$Path, [byte[]]$Content) {
    Require-UnoccupiedPath $Path
    Require-OrdinaryDirectory ([IO.Path]::GetDirectoryName($Path))
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($Content, 0, $Content.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Remove-OwnedFileIfPresent([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return }
    Require-OrdinaryFile $Path
    [IO.File]::Delete($Path)
}
function Write-OwnedText([string]$Path, [string]$Content) {
    $contentBytes = $Utf8.GetBytes($Content)
    No-ReparseAncestors $Path $true
    Require-OrdinaryDirectory ([IO.Path]::GetDirectoryName($Path))
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        $next = $Path + '.pm-' + [guid]::NewGuid().ToString('N') + '.next'
        try {
            Write-NewFile $next $contentBytes
            [IO.File]::Move($next, $Path)
        } finally {
            Remove-OwnedFileIfPresent $next
        }
        return
    }
    Require-OrdinaryFile $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Require-OrdinaryFile $Path
        $stream.Position = 0
        $stream.SetLength(0)
        $stream.Write($contentBytes, 0, $contentBytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Tree-Files([string]$Root) {
    $files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $rel = Relative $Root $entry
            if ($rel -ceq '.git' -or $rel -ceq '.packageset-provenance.json') { continue }
            if (@($rel.Split('/') | Where-Object { $_ -ceq '.git' }).Count -gt 0) { throw 'Nested Git metadata is not a projection.' }
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Nested reparse point: $entry" }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $pending.Push($entry) } else { $files.Add($rel, $entry) }
        }
    }
    return ,$files
}
function Hash-Tree([string]$Root) {
    $files = Tree-Files $Root
    [string[]]$keys = @($files.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
    $b = [Text.StringBuilder]::new()
    foreach ($key in $keys) {
        if ($key.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) { continue }
        [void]$b.Append($key).Append("`n").Append((Hash-File $files[$key])).Append("`n")
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($Utf8.GetBytes($b.ToString())))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function Hash-Inventory([string]$Root) {
    $files = Tree-Files $Root
    [string[]]$keys = @($files.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
    $b = [Text.StringBuilder]::new()
    foreach ($key in $keys) { [void]$b.Append($key).Append("`n").Append((Hash-File $files[$key])).Append("`n") }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($Utf8.GetBytes($b.ToString())))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function Check-Hash([string]$Path, [string]$Expected) {
    if ((Hash-File $Path) -cne $Expected) { throw "Plan is stale; file changed: $Path" }
}
function Check-DurableHash([string]$Path, [string]$Expected) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($Expected -ceq 'missing') {
        if ($null -ne $item) { throw "Plan is stale; durable path became occupied: $Path" }
        return
    }
    if ($null -eq $item -or -not [IO.File]::Exists($Path)) { throw "Plan is stale; durable file is missing: $Path" }
    if (($item.Attributes -band [IO.FileAttributes]::Directory) -ne 0 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Durable binding must remain an ordinary file: $Path" }
    Check-Hash $Path $Expected
}
function Require-DeclaredMutationPath([string]$Path) {
    if ($null -eq $p) { throw 'Mutation path authority is unavailable.' }
    $insideProject = Within $p.projectGitRoot $Path
    $insidePackageSet = Within $p.packageSetRoot $Path
    if (-not $insideProject -and -not $insidePackageSet) { throw "Mutation path escapes the declared Project and PackageSet roots: $Path" }
    No-ReparseAncestors $Path $true
}
function Require-OwnedBackupPath([string]$Path) {
    if (-not (Within $Backup $Path)) { throw "Recovery backup path escapes the operation backup root: $Path" }
    No-ReparseAncestors $Path $true
}
function Assert-UniqueTopLevelJsonProperties([string]$Json, [string]$Path) {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $objectDepth = 0
    $arrayDepth = 0
    for ($index = 0; $index -lt $Json.Length; $index++) {
        $character = $Json[$index]
        if ($character -eq '{') { $objectDepth++; continue }
        if ($character -eq '}') { $objectDepth--; continue }
        if ($character -eq '[') { $arrayDepth++; continue }
        if ($character -eq ']') { $arrayDepth--; continue }
        if ($character -ne '"') { continue }
        $start = $index
        $escaped = $false
        for ($index = $index + 1; $index -lt $Json.Length; $index++) {
            $character = $Json[$index]
            if ($escaped) { $escaped = $false; continue }
            if ($character -eq '\') { $escaped = $true; continue }
            if ($character -eq '"') { break }
        }
        if ($index -ge $Json.Length) { throw "Package Management recovery journal is malformed at $Path : unterminated JSON string." }
        $after = $index + 1
        while ($after -lt $Json.Length -and [char]::IsWhiteSpace($Json[$after])) { $after++ }
        if ($objectDepth -ne 1 -or $arrayDepth -ne 0 -or $after -ge $Json.Length -or $Json[$after] -ne ':') { continue }
        $token = $Json.Substring($start, $index - $start + 1)
        try { $name = [string]($token | ConvertFrom-Json) }
        catch { throw "Package Management recovery journal is malformed at $Path : $($_.Exception.Message)" }
        if (-not $names.Add($name)) { throw "Package Management recovery journal has a duplicate top-level property '$name': $Path" }
    }
}
function Audit-SiblingOperations([string]$OperationsRoot, [string]$CurrentOperation) {
    $operationsItem = Get-Item -LiteralPath $OperationsRoot -Force -ErrorAction SilentlyContinue
    if ($null -eq $operationsItem) { return }
    Require-OrdinaryDirectory $OperationsRoot
    foreach ($item in Get-ChildItem -LiteralPath $OperationsRoot -Force) {
        if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw "Unknown Package Management operation evidence must be preserved and inspected: $($item.FullName)" }
        if ((Full $item.FullName) -ieq (Full $CurrentOperation)) { continue }
        $leasePath = Join-Path $item.FullName 'apply.lock'
        $leaseItem = Get-Item -LiteralPath $leasePath -Force -ErrorAction SilentlyContinue
        if ($null -ne $leaseItem) { throw "Package Management operation is actively applying or has an unresolved lease: $leasePath" }
        $journalPath = Join-Path $item.FullName 'journal.json'
        $journalItem = Get-Item -LiteralPath $journalPath -Force -ErrorAction SilentlyContinue
        if ($null -eq $journalItem) { continue }
        Require-OrdinaryFile $journalPath
        $journalText = [IO.File]::ReadAllText($journalPath, $Utf8)
        Assert-UniqueTopLevelJsonProperties $journalText $journalPath
        try { $journal = $journalText | ConvertFrom-Json }
        catch { throw "Package Management recovery journal is malformed at $journalPath : $($_.Exception.Message)" }
        [string[]]$names = @($journal.PSObject.Properties.Name); [Array]::Sort($names, [StringComparer]::Ordinal)
        [string[]]$expectedNames = @('directories','error','files','links','moves','status')
        [string[]]$legacyNames = @('error','files','links','moves','status')
        $hasCurrentShape = ($names -join "`n") -ceq ($expectedNames -join "`n")
        $hasLegacyShape = ($names -join "`n") -ceq ($legacyNames -join "`n")
        $directoriesAreValid = $hasLegacyShape -or $journal.directories -is [Array]
        if ((-not $hasCurrentShape -and -not $hasLegacyShape) -or -not $directoriesAreValid -or $journal.status -isnot [string] -or $journal.moves -isnot [Array] -or $journal.links -isnot [Array] -or $journal.files -isnot [Array] -or $journal.error -isnot [string]) { throw "Package Management recovery journal has an unknown format: $journalPath" }
        $status = [string]$journal.status
        if ($status -ceq 'applied' -or $status -ceq 'rolled-back' -or $status -ceq 'preflight-failed' -or $status -ceq 'validated') { continue }
        if ($status -ceq 'applying' -or $status -ceq 'needs-recovery') { throw "Package Management operation requires recovery inspection at $journalPath ($status). Preserve it and do not apply another plan." }
        throw "Package Management recovery journal has unknown status '$status': $journalPath"
    }
}
function Repository-Identity([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'Source repository identity is empty.' }
    $repository = $Value.Trim().TrimEnd('/')
    if ($repository.StartsWith('-', [StringComparison]::Ordinal)) { throw 'Source repository identity is invalid.' }
    foreach ($character in $repository.ToCharArray()) {
        if ([char]::IsControl($character)) { throw 'Source repository identity contains a control character.' }
    }
    if ($repository.StartsWith('git@github.com:', [StringComparison]::OrdinalIgnoreCase)) {
        if ($repository.Length -le 15 -or $repository.Contains('?') -or $repository.Contains('#')) { throw 'Source repository identity is invalid.' }
        $repository = 'https://github.com/' + $repository.Substring(15)
    } else {
        [Uri]$uri = $null
        if (-not [Uri]::TryCreate($repository, [UriKind]::Absolute, [ref]$uri)) { throw 'Source repository must be a portable HTTPS/SSH URL.' }
        if ($uri.Scheme -cne 'https' -and $uri.Scheme -cne 'ssh') { throw 'Source repository must be a portable HTTPS/SSH URL.' }
        if ($uri.Scheme -ceq 'https' -and $uri.UserInfo) { throw 'HTTPS source repository must not contain user information.' }
        if ($uri.UserInfo.Contains(':') -or $uri.Query -or $uri.Fragment) { throw 'Source repository must not contain credentials, query, or fragment.' }
    }
    if ($repository.EndsWith('.git', [StringComparison]::OrdinalIgnoreCase)) { $repository = $repository.Substring(0, $repository.Length - 4) }
    return $repository
}
function Is-Project-Ignored([string]$GitRoot, [string]$Relative) {
    try { [void](Git $GitRoot @('check-ignore','--no-index','--quiet','--',$Relative)); return $true }
    catch { return $false }
}
function Is-CorrectJunction([string]$Path, [string]$Target) {
    $item = Get-Item -LiteralPath $Path -Force
    return $item.LinkType -eq 'Junction' -and (Full ([string]$item.Target[0])).Equals((Full $Target), [StringComparison]::OrdinalIgnoreCase)
}
function Save-Journal([string]$Status) {
    $journal = [ordered]@{ status = $Status; moves = @($Moves); links = @($Links); files = @($Writes); directories = @($Directories); error = $Failure }
    $path = Join-Path $Operation 'journal.json'
    $next = $path + '.next'
    Require-OrdinaryDirectory $Operation
    $journalBytes = $Utf8.GetBytes(($journal | ConvertTo-Json -Depth 12))
    Write-NewFile $next $journalBytes
    try {
        $pathItem = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        if ($null -ne $pathItem) {
            Require-OrdinaryFile $path
            # PS 5.1 converts a bare $null string argument to an empty path. Use a real unique backup.
            $previous = Join-Path $Operation ('journal-' + [guid]::NewGuid().ToString('N') + '.previous.json')
            Require-UnoccupiedPath $previous
            [IO.File]::Replace($next, $path, $previous)
        } else { [IO.File]::Move($next, $path) }
    } finally {
        Remove-OwnedFileIfPresent $next
    }
}
function Backup-CheckedBytes([string]$Path, [byte[]]$Content, [string]$ExpectedHash) {
    Backup-BytesCore $Path $Content $ExpectedHash
}
function Backup-CheckedWrite([string]$Path, [string]$Content, [string]$ExpectedHash) {
    $contentBytes = $Utf8.GetBytes($Content)
    Backup-BytesCore $Path $contentBytes $ExpectedHash
}
function Backup-BytesCore([string]$Path, [byte[]]$Content, [string]$ExpectedHash) {
    Require-DeclaredMutationPath $Path
    Ensure-JournaledOrdinaryDirectory ([IO.Path]::GetDirectoryName($Path))
    $backupPath = Join-Path $Backup ('file-' + $Writes.Count)
    Require-UnoccupiedPath $backupPath
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        if ($ExpectedHash -cne 'missing') { throw "Plan is stale; durable file is missing: $Path" }
        $record = [ordered]@{ path = $Path; backup = $backupPath; existed = $false; created = $false; previousHash = 'missing'; writtenHash = '' }
        [void]$Writes.Add($record)
        Save-Journal 'applying'
        $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $record.created = $true
            Save-Journal 'applying'
            $stream.Write($Content, 0, $Content.Length)
            $stream.Flush($true)
            $record.writtenHash = Hash-Bytes $Content
            Save-Journal 'applying'
        } finally { $stream.Dispose() }
        return
    }
    Require-OrdinaryFile $Path
    if ($ExpectedHash -ceq 'missing') { throw "Plan is stale; durable path became occupied: $Path" }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Require-OrdinaryFile $Path
        $previousBytes = Read-StreamBytes $stream
        $previousHash = Hash-Bytes $previousBytes
        if ($previousHash -cne $ExpectedHash) { throw "Plan is stale; file changed: $Path" }
        Write-NewFile $backupPath $previousBytes
        $record = [ordered]@{ path = $Path; backup = $backupPath; existed = $true; created = $false; previousHash = $previousHash; writtenHash = '' }
        [void]$Writes.Add($record)
        Save-Journal 'applying'
        $stream.Position = 0
        $stream.SetLength(0)
        $stream.Write($Content, 0, $Content.Length)
        $stream.Flush($true)
        $record.writtenHash = Hash-Bytes $Content
        Save-Journal 'applying'
    } finally { $stream.Dispose() }
}
function Find-WriteRecord([string]$Path) {
    foreach ($record in $Writes) {
        if ([StringComparer]::OrdinalIgnoreCase.Equals([string]$record.path, $Path)) { return $record }
    }
    return $null
}
function Remove-GitConfigSectionTransactional([string]$Path, [string]$Section, [string]$ExpectedHash, [string]$GitRoot) {
    Require-DeclaredMutationPath $Path
    Require-OrdinaryDirectory ([IO.Path]::GetDirectoryName($Path))
    Require-OrdinaryFile $Path
    $record = Find-WriteRecord $Path
    $requiredHash = $ExpectedHash
    if ($null -ne $record) {
        $requiredHash = [string]$record.writtenHash
        if ([string]::IsNullOrWhiteSpace($requiredHash)) { throw 'Owned .gitmodules write has no durable content hash.' }
    }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Require-OrdinaryFile $Path
        $previousBytes = Read-StreamBytes $stream
        $previousHash = Hash-Bytes $previousBytes
        if ($previousHash -cne $requiredHash) { throw "Plan is stale; .gitmodules changed: $Path" }
        if ($null -eq $record) {
            $backupPath = Join-Path $Backup ('file-' + $Writes.Count)
            Require-UnoccupiedPath $backupPath
            Write-NewFile $backupPath $previousBytes
            $record = [ordered]@{ path = $Path; backup = $backupPath; existed = $true; created = $false; previousHash = $previousHash; writtenHash = '' }
            [void]$Writes.Add($record)
            Save-Journal 'applying'
        }
        $configCopy = Join-Path $Backup ('gitmodules-' + [guid]::NewGuid().ToString('N') + '.config')
        try {
            Write-NewFile $configCopy $previousBytes
            [void](Git $GitRoot @('config','--file',$configCopy,'--remove-section',$Section))
            Require-OrdinaryFile $configCopy
            $nextBytes = [IO.File]::ReadAllBytes($configCopy)
        } finally {
            Remove-OwnedFileIfPresent $configCopy
        }
        $stream.Position = 0
        $stream.SetLength(0)
        $stream.Write($nextBytes, 0, $nextBytes.Length)
        $stream.Flush($true)
        $record.writtenHash = Hash-Bytes $nextBytes
        Save-Journal 'applying'
    } finally { $stream.Dispose() }
}
function Copy-MovedMetaToProjection([string]$SourceBackup, [string]$Destination, [string]$ExpectedHash) {
    Require-OwnedBackupPath $SourceBackup
    Require-OrdinaryFile $SourceBackup
    $stream = [IO.File]::Open($SourceBackup, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        Require-OrdinaryFile $SourceBackup
        $content = Read-StreamBytes $stream
        if ((Hash-Bytes $content) -cne $ExpectedHash) { throw 'Root folder metadata changed after preparation.' }
        Backup-CheckedBytes $Destination $content 'missing'
    } finally { $stream.Dispose() }
}
function Stage-CheckedFile([string]$Path, [string]$ExpectedHash, [string]$RelativePath, [string]$Mode, [string]$GitRoot, [string]$Index) {
    Require-DeclaredMutationPath $Path
    Require-OrdinaryFile $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        Require-OrdinaryFile $Path
        $content = Read-StreamBytes $stream
        if ((Hash-Bytes $content) -cne $ExpectedHash) { throw "Owned file changed before staging: $Path" }
        $stageCopy = Join-Path $Backup ('stage-' + [guid]::NewGuid().ToString('N') + '.content')
        try {
            Write-NewFile $stageCopy $content
            $blob = Git $GitRoot @('hash-object','-w',('--path=' + $RelativePath),'--',$stageCopy) $Index
            [void](Git $GitRoot @('update-index','--add','--cacheinfo',($Mode + ',' + $blob + ',' + $RelativePath)) $Index)
        } finally {
            Remove-OwnedFileIfPresent $stageCopy
        }
    } finally { $stream.Dispose() }
}
function Remove-RollbackLink([object]$Link) {
    if (-not (Within $p.projectGitRoot ([string]$Link.path))) { throw 'Rollback junction path escapes the declared Project root.' }
    if (-not (Within $p.packageSetRoot ([string]$Link.target))) { throw 'Rollback junction target escapes the declared PackageSet root.' }
    No-ReparseAncestors ([string]$Link.path) $true
    No-ReparseAncestors ([string]$Link.target)
    if (-not (Is-CorrectJunction ([string]$Link.path) ([string]$Link.target))) { throw 'Rollback backing was changed externally.' }
    [IO.Directory]::Delete([string]$Link.path)
}
function Restore-WriteRecord([object]$Write, [int]$Ordinal) {
    $path = [string]$Write.path
    Require-DeclaredMutationPath $path
    if ($Write.existed) {
        $backupPath = [string]$Write.backup
        Require-OwnedBackupPath $backupPath
        Require-OrdinaryFile $backupPath
        $backupStream = [IO.File]::Open($backupPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
            Require-OrdinaryFile $backupPath
            $previousBytes = Read-StreamBytes $backupStream
            $previousHash = Hash-Bytes $previousBytes
            if ($previousHash -cne [string]$Write.previousHash) { throw 'Rollback backup bytes changed externally.' }
            Require-OrdinaryFile $path
            $destinationStream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                Require-OrdinaryFile $path
                $currentBytes = Read-StreamBytes $destinationStream
                $currentHash = Hash-Bytes $currentBytes
                if ($currentHash -cne $previousHash) {
                    if ([string]::IsNullOrWhiteSpace([string]$Write.writtenHash) -or $currentHash -cne [string]$Write.writtenHash) { throw 'Rollback metadata was changed externally.' }
                    $destinationStream.Position = 0
                    $destinationStream.SetLength(0)
                    $destinationStream.Write($previousBytes, 0, $previousBytes.Length)
                    $destinationStream.Flush($true)
                }
            } finally { $destinationStream.Dispose() }
        } finally { $backupStream.Dispose() }
        return
    }
    if (-not $Write.created) { return }
    if ([string]::IsNullOrWhiteSpace([string]$Write.writtenHash)) { throw 'Rollback cannot prove ownership of a created metadata file.' }
    Require-OrdinaryFile $path
    Check-Hash $path ([string]$Write.writtenHash)
    $quarantine = Join-Path $Backup ('rollback-created-' + $Ordinal)
    Require-UnoccupiedPath $quarantine
    Require-OrdinaryFile $path
    Check-Hash $path ([string]$Write.writtenHash)
    [IO.File]::Move($path, $quarantine)
    Require-OwnedBackupPath $quarantine
    Require-OrdinaryFile $quarantine
    Check-Hash $quarantine ([string]$Write.writtenHash)
    [IO.File]::Delete($quarantine)
}
function Restore-MoveRecord([object]$Move) {
    $original = [string]$Move.original
    $backupPath = [string]$Move.backup
    if (-not (Within $p.projectGitRoot $original)) { throw 'Rollback move destination escapes the declared Project root.' }
    Require-OwnedBackupPath $backupPath
    Require-UnoccupiedPath $original
    if ($Move.kind -ceq 'directory') {
        Require-OrdinaryDirectory $backupPath
        if ((Hash-Inventory $backupPath) -cne [string]$Move.expectedHash) { throw 'Rollback directory backup changed externally.' }
        [IO.Directory]::Move($backupPath, $original)
        Require-OrdinaryDirectory $original
        return
    }
    if ($Move.kind -ceq 'file') {
        Require-OrdinaryFile $backupPath
        Check-Hash $backupPath ([string]$Move.expectedHash)
        [IO.File]::Move($backupPath, $original)
        Require-OrdinaryFile $original
        return
    }
    throw 'Rollback move has an unknown path kind.'
}
function Remove-OwnedDirectoryRecord([object]$Directory) {
    if (-not $Directory.owned) { return }
    $path = [string]$Directory.path
    Require-DeclaredMutationPath $path
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return }
    Require-OrdinaryDirectory $path
    $enumerator = [IO.Directory]::EnumerateFileSystemEntries($path).GetEnumerator()
    try { $isEmpty = -not $enumerator.MoveNext() }
    finally {
        if ($enumerator -is [IDisposable]) { $enumerator.Dispose() }
    }
    if (-not $isEmpty) { throw "Rollback-owned directory is no longer empty: $path" }
    [IO.Directory]::Delete($path, $false)
}

$Operation = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Plan))
$Backup = Join-Path $Operation 'backup'
$LeasePath = Join-Path $Operation 'apply.lock'
$Moves = [Collections.ArrayList]::new(); $Links = [Collections.ArrayList]::new(); $Writes = [Collections.ArrayList]::new(); $Directories = [Collections.ArrayList]::new()
$Failure = ''; $Lease = $null; $OwnLease = $false; $IndexLock = $null; $OwnIndexLock = $false; $Committed = $false; $MutationStarted = $false; $NewJournalAllowed = $false; $ExitCode = 0; $p = $null
try {
    if ($env:OS -ne 'Windows_NT') { throw 'Offline apply requires Windows.' }
    if ($WaitForProcessId -gt 0) { Get-Process -Id $WaitForProcessId -ErrorAction SilentlyContinue | Wait-Process }
    Require-OrdinaryDirectory $Operation
    No-ReparseAncestors $LeasePath $true
    $Lease = [IO.File]::Open($LeasePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $OwnLease = $true
    $journalPath = Join-Path $Operation 'journal.json'
    $journalItem = Get-Item -LiteralPath $journalPath -Force -ErrorAction SilentlyContinue
    if ($null -ne $journalItem) { throw 'This plan has already run. Inspect its existing journal/backups and prepare a new plan; do not replay mutation.' }
    foreach ($unusedPath in @((Join-Path $Operation 'journal.json.next'), $Backup, (Join-Path $Operation 'prepared.index'))) {
        $unusedItem = Get-Item -LiteralPath $unusedPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $unusedItem) { throw "This operation contains unexpected prior apply evidence: $unusedPath" }
    }
    $NewJournalAllowed = $true
    $planHashPath = Join-Path $Operation 'plan.sha256'
    $helperHashPath = Join-Path $Operation 'helper.sha256'
    $readyPath = Join-Path $Operation 'ready.sha256'
    if ((Full $Plan) -ine (Full (Join-Path $Operation 'plan.json'))) { throw 'Offline Apply requires the operation-owned plan.json.' }
    if ((Full $PSCommandPath) -ine (Full (Join-Path $Operation 'ApplyDevelopmentBootstrap.ps1'))) { throw 'Offline Apply requires the operation-owned helper copy.' }
    Require-OrdinaryFile $Plan
    Require-OrdinaryFile $PSCommandPath
    Require-OrdinaryFile $planHashPath
    Require-OrdinaryFile $helperHashPath
    Check-Hash $Plan ([IO.File]::ReadAllText($planHashPath).Trim())
    Check-Hash $PSCommandPath ([IO.File]::ReadAllText($helperHashPath).Trim())
    Require-OrdinaryFile $readyPath
    if (([IO.File]::ReadAllText($readyPath).Trim()) -cne (Hash-File $Plan)) { throw 'Offline Apply plan was not finalized by a successful Prepare audit.' }
    $p = Get-Content -LiteralPath $Plan -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($p.schemaVersion -ne 2) { throw 'Unsupported apply plan schema.' }
    foreach ($root in @($p.projectRoot, $p.projectGitRoot, $p.packageSetRoot, $p.projectGitDirectory)) { No-ReparseAncestors $root }
    if ((Full (Git $p.projectGitRoot @('rev-parse','--show-toplevel'))) -ine (Full $p.projectGitRoot)) { throw 'Project Git root changed.' }
    if ((Full (Git $p.projectGitRoot @('rev-parse','--absolute-git-dir'))) -ine (Full $p.projectGitDirectory)) { throw 'Project Git directory changed.' }
    if (-not (Within $p.projectGitDirectory $Operation)) { throw 'Plan must live in the actual Project Git directory.' }
    Audit-SiblingOperations ([IO.Path]::GetDirectoryName($Operation)) $Operation
    if ((Git $p.projectGitRoot @('rev-parse','HEAD')) -cne $p.projectHead) { throw 'Project HEAD changed after preparation.' }
    if ((Git $p.packageSetRoot @('rev-parse','HEAD')) -cne $p.packageSetRevision -or (Git $p.packageSetRoot @('status','--porcelain')).Length -ne 0) { throw 'PackageSet changed after preparation.' }
    $setGitDirectory = Full (Git $p.packageSetRoot @('rev-parse','--absolute-git-dir'))
    $unityLock = Join-Path $p.projectRoot 'Temp/UnityLockfile'
    No-ReparseAncestors $unityLock $true
    $unityLockExists = $false
    try {
        $unityLockAttributes = [IO.File]::GetAttributes($unityLock)
        $unityLockExists = $true
    } catch [IO.FileNotFoundException] {
        $unityLockExists = $false
    } catch [IO.DirectoryNotFoundException] {
        $unityLockExists = $false
    } catch {
        throw "Unity lockfile occupancy cannot be classified safely: $unityLock"
    }
    if ($unityLockExists) {
        $invalidUnityLockAttributes = [IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint
        if (($unityLockAttributes -band $invalidUnityLockAttributes) -ne 0) { throw "Unity lockfile path is occupied by a non-file or reparse point: $unityLock" }
        No-ReparseAncestors $unityLock $true
        $testLock = [IO.File]::Open($unityLock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Require-OrdinaryFile $unityLock }
        finally { $testLock.Dispose() }
    }
    $indexPath = Join-Path $p.projectGitDirectory 'index'; $indexLockPath = $indexPath + '.lock'
    No-ReparseAncestors $indexPath
    $IndexLock = [IO.File]::Open($indexLockPath, 'CreateNew', 'Write', 'None'); $OwnIndexLock = $true
    Audit-SiblingOperations ([IO.Path]::GetDirectoryName($Operation)) $Operation
    Check-Hash $indexPath $p.indexHash
    $gitmodulesPath = Join-Path $p.projectGitRoot '.gitmodules'
    $projectBindingPath = Join-Path $p.projectGitRoot 'PackageManagement/project.packageset.json'
    $runtimeBindingsPath = Join-Path $p.projectGitRoot 'PackageManagement/runtime.bindings.json'
    No-ReparseAncestors $gitmodulesPath
    No-ReparseAncestors $projectBindingPath
    No-ReparseAncestors $runtimeBindingsPath
    Check-Hash $gitmodulesPath $p.gitmodulesHash
    Check-DurableHash $projectBindingPath $p.projectBindingHash
    Check-DurableHash $runtimeBindingsPath $p.runtimeBindingsHash
    if ((Git $p.projectGitRoot @('status','--porcelain=v2','--untracked-files=all')).Length -ne 0) { throw 'Project worktree or index changed after preparation.' }
    if ((Git $p.projectGitRoot @('diff','--name-only','--','.gitmodules')).Length -ne 0) { throw 'Resolve unrelated unstaged .gitmodules changes before migration.' }
    foreach ($entry in @($p.entries)) {
        if (-not (Within (Join-Path $p.projectRoot 'Assets') $entry.originalPath) -or
            -not (Within (Join-Path $p.projectRoot 'Assets') $entry.projectionPath) -or
            -not (Within $p.packageSetRoot $entry.sourcePath)) { throw 'Entry escapes its declared tree.' }
        No-ReparseAncestors $entry.originalPath $true; No-ReparseAncestors $entry.projectionPath $true
        No-ReparseAncestors ($entry.originalPath + '.meta'); No-ReparseAncestors $entry.sourcePath
        $projectionState = [string]$entry.projectionState
        if ($projectionState -notin @('Absent','PhysicalSnapshot','BootstrapGitlink','CorrectJunction')) { throw "Unsupported projection state: $projectionState" }
        if ($projectionState -ne 'Absent' -and $projectionState -ne 'CorrectJunction' -and [IO.Path]::GetPathRoot($entry.originalPath) -ine [IO.Path]::GetPathRoot($Backup)) { throw 'Cross-volume directory backups are unsupported.' }
        if (-not (Within $setGitDirectory (Git $entry.sourcePath @('rev-parse','--absolute-git-dir')))) { throw 'Canonical Git metadata no longer belongs to PackageSet.' }
        $sourceHead = Git $entry.sourcePath @('rev-parse','HEAD')
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($sourceHead, [string]$entry.sourceRevision) -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals($sourceHead, [string]$entry.exactPin) -or
            (Git $entry.sourcePath @('status','--porcelain')).Length -ne 0) { throw "Source authority changed: $($entry.identity)" }
        $sourceRepository = [string]$entry.sourceRepository
        $observedRepository = [string]$entry.observedRepository
        if ($entry.needsPublish) {
            if ($sourceRepository -or $observedRepository) { throw 'NeedsPublish source repository fields must be empty.' }
        } else {
            if (-not $sourceRepository -or -not $observedRepository) { throw 'Gitlink-backed source repository authority is missing.' }
            $currentRepository = Git $entry.sourcePath @('remote','get-url','origin')
            $sourceIdentity = Repository-Identity $sourceRepository
            $observedIdentity = Repository-Identity $observedRepository
            $currentIdentity = Repository-Identity $currentRepository
            if ($sourceRepository -cne $observedRepository -or $observedRepository -cne $currentRepository -or
                $sourceIdentity -cne $observedIdentity -or $observedIdentity -cne $currentIdentity) { throw 'Source repository authority changed after preparation.' }
        }
        if (-not $entry.needsPublish) {
            $tree = Git $p.packageSetRoot @('ls-tree','HEAD','--',(Relative $p.packageSetRoot $entry.sourcePath))
            if (-not $tree.StartsWith('160000 commit ' + $entry.exactPin + "`t", [StringComparison]::OrdinalIgnoreCase)) { throw 'PackageSet gitlink no longer matches exact source pin.' }
        }
        if ((Hash-Tree $entry.sourcePath) -cne $entry.contentHash) { throw "Source content changed: $($entry.identity)" }
        if ($projectionState -eq 'Absent') {
            if ($entry.originalPath -ine $entry.projectionPath) { throw 'Absent projection must use the canonical projection path.' }
            if ((Test-Path -LiteralPath $entry.originalPath) -or (Test-Path -LiteralPath ($entry.originalPath + '.meta'))) { throw 'Absent projection became occupied.' }
            if ($entry.originalMetaHash -cne 'missing' -or $entry.submoduleSection) { throw 'Absent projection contains stale metadata or submodule ownership.' }
        } elseif ($projectionState -eq 'CorrectJunction') {
            if ($entry.originalPath -ine $entry.projectionPath -or -not (Is-CorrectJunction $entry.originalPath $entry.sourcePath)) { throw 'Backing changed after preparation.' }
        } else {
            if (-not [IO.Directory]::Exists($entry.originalPath)) { throw 'Approved Project source is missing.' }
            if (((Get-Item -LiteralPath $entry.originalPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unexpected backing at original path.' }
            if ((Hash-Tree $entry.originalPath) -cne $entry.originalContentHash) { throw 'Project content changed after preparation.' }
            if ((Hash-Inventory $entry.originalPath) -cne $entry.originalInventoryHash) { throw 'Project file inventory changed after preparation.' }
            $originalGitPath = Join-Path $entry.originalPath '.git'
            $originalGitItem = Get-Item -LiteralPath $originalGitPath -Force -ErrorAction SilentlyContinue
            if ($projectionState -eq 'PhysicalSnapshot') {
                if ($entry.submoduleSection) { throw 'Physical snapshot unexpectedly owns a submodule section.' }
                if ($null -ne $originalGitItem) { throw 'Physical snapshot unexpectedly contains root Git metadata.' }
            }
            if ($projectionState -eq 'BootstrapGitlink') {
                if (-not $entry.submoduleSection) { throw 'Bootstrap gitlink ownership is unavailable.' }
                if ($entry.needsPublish) { throw 'Bootstrap gitlink cannot use unpublished source authority.' }
                if ($null -eq $originalGitItem -or ($originalGitItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Bootstrap checkout root Git metadata is unavailable or unsafe.' }
                if (-not $originalGitItem.PSIsContainer -and -not [IO.File]::Exists($originalGitPath)) { throw 'Bootstrap checkout root Git metadata is not an ordinary file or directory.' }
                $bootstrapRoot = Full (Git $entry.originalPath @('rev-parse','--show-toplevel'))
                if ($bootstrapRoot -ine (Full $entry.originalPath)) { throw 'Bootstrap checkout is not an independent Git worktree root.' }
                $bootstrapGitDirectory = Full (Git $entry.originalPath @('rev-parse','--absolute-git-dir'))
                Require-OrdinaryDirectory $bootstrapGitDirectory
                if (-not (Within $p.projectGitDirectory $bootstrapGitDirectory) -or (Within $setGitDirectory $bootstrapGitDirectory)) { throw 'Bootstrap checkout Git metadata no longer belongs exclusively to the Project.' }
                $bootstrapHead = Git $entry.originalPath @('rev-parse','HEAD')
                if (-not [StringComparer]::OrdinalIgnoreCase.Equals($bootstrapHead, [string]$entry.exactPin)) { throw 'Bootstrap checkout HEAD no longer matches the exact source pin.' }
                if ((Git $entry.originalPath @('status','--porcelain')).Length -ne 0) { throw 'Bootstrap checkout is no longer clean.' }
                $bootstrapRepository = Git $entry.originalPath @('remote','get-url','origin')
                $bootstrapIdentity = Repository-Identity $bootstrapRepository
                if ($bootstrapRepository -cne $sourceRepository -or $bootstrapIdentity -cne $sourceIdentity) { throw 'Bootstrap checkout repository identity changed after preparation.' }
                if ((Hash-Tree $entry.originalPath) -cne $entry.contentHash) { throw 'Bootstrap checkout worktree bytes no longer match the canonical source.' }
                $bootstrapRelative = Relative $p.projectGitRoot $entry.originalPath
                $bootstrapSectionPath = Git $p.projectGitRoot @('config','--file',$gitmodulesPath,'--get',([string]$entry.submoduleSection + '.path'))
                $bootstrapSectionRepository = Git $p.projectGitRoot @('config','--file',$gitmodulesPath,'--get',([string]$entry.submoduleSection + '.url'))
                $bootstrapSectionIdentity = Repository-Identity $bootstrapSectionRepository
                if ($bootstrapSectionPath -cne $bootstrapRelative -or $bootstrapSectionRepository -cne $sourceRepository -or $bootstrapSectionIdentity -cne $sourceIdentity) { throw 'Bootstrap submodule declaration no longer proves the exact Project source ownership.' }
                $bootstrapIndex = Git $p.projectGitRoot @('ls-files','--stage','--',$bootstrapRelative)
                $expectedBootstrapIndex = '160000 ' + [string]$entry.exactPin + " 0`t" + $bootstrapRelative
                if (-not $bootstrapIndex.Equals($expectedBootstrapIndex, [StringComparison]::OrdinalIgnoreCase)) { throw 'Project index no longer owns the exact bootstrap gitlink.' }
            }
        }
        Check-Hash ($entry.originalPath + '.meta') $entry.originalMetaHash
        if ($entry.originalPath -ine $entry.projectionPath -and ((Test-Path -LiteralPath $entry.projectionPath) -or (Test-Path -LiteralPath ($entry.projectionPath + '.meta')))) { throw 'Projection destination became occupied.' }
        $known = Tree-Files $entry.sourcePath
        $plannedMetadata = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($metadata in @($entry.metadata)) {
            $metadataPath = [string]$metadata.path
            if (-not $metadataPath.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) { throw 'Planned metadata path is not a Unity .meta file.' }
            $projectMetadata = Join-Path $entry.originalPath $metadataPath
            $sourceMetadata = Join-Path $entry.sourcePath $metadataPath
            if (-not (Within $entry.originalPath $projectMetadata) -or -not (Within $entry.sourcePath $sourceMetadata)) { throw 'Planned metadata escapes its entry root.' }
            Check-Hash $projectMetadata ([string]$metadata.projectHash)
            Check-Hash $sourceMetadata ([string]$metadata.sourcePreviousHash)
            $metadataBytes = [Convert]::FromBase64String([string]$metadata.contentBase64)
            if ((Hash-Bytes $metadataBytes) -cne [string]$metadata.projectHash) { throw 'Planned metadata content does not match its Project evidence.' }
            if ($metadata.sourcePreviousHash -cne 'missing' -and $metadata.sourcePreviousHash -cne $metadata.projectHash) { throw 'Canonical source metadata differs from the Project-owned metadata plan.' }
            $metadataCompanion = $sourceMetadata.Substring(0, $sourceMetadata.Length - '.meta'.Length)
            if (-not [IO.File]::Exists($metadataCompanion) -and -not [IO.Directory]::Exists($metadataCompanion)) { throw 'Planned metadata has no canonical source companion.' }
            if (-not (Is-Project-Ignored $entry.sourcePath $metadataPath)) { throw 'Canonical source no longer ignores planned Project-owned metadata.' }
            $plannedMetadata.Add($metadataPath, $metadata)
        }
        $plannedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $plannedExclusions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $exclusionRecords = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($exclusion in @($entry.projectionExclusions)) {
            $excludedPath = [string]$exclusion.path
            if (-not $plannedExclusions.Add($excludedPath)) { throw 'Duplicate generated projection exclusion.' }
            $exclusionRecords.Add($excludedPath, $exclusion)
        }
        foreach ($exclusion in @($entry.projectionExclusions)) {
            $excludedPath = [string]$exclusion.path
            $excludedSource = Join-Path $entry.sourcePath $excludedPath
            if (-not (Within $entry.sourcePath $excludedSource)) { throw 'Excluded generated source file escapes its entry root.' }
            if ($exclusion.reason -ceq 'ProjectIgnoredGeneratedCsproj') {
                if (-not $excludedPath.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase) -or $exclusion.pairedPath -or -not $exclusion.sourceBlob) { throw 'Invalid generated .csproj projection exclusion.' }
                if (-not [IO.File]::Exists($excludedSource)) { throw 'Excluded generated source file is unavailable.' }
                Check-Hash $excludedSource ([string]$exclusion.sourceHash)
                $excludedProjection = (Relative $p.projectGitRoot $entry.projectionPath) + '/' + $excludedPath.Replace('\','/')
                if (-not (Is-Project-Ignored $p.projectGitRoot $excludedProjection)) { throw 'Generated .csproj projection exclusion is no longer ignored by the Project.' }
                $sourceTree = Git $entry.sourcePath @('ls-tree','HEAD','--',$excludedPath)
                $expectedTreePrefix = '100644 blob ' + [string]$exclusion.sourceBlob + "`t"
                $expectedExecutablePrefix = '100755 blob ' + [string]$exclusion.sourceBlob + "`t"
                if (-not $sourceTree.StartsWith($expectedTreePrefix, [StringComparison]::OrdinalIgnoreCase) -and -not $sourceTree.StartsWith($expectedExecutablePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated projection exclusion no longer matches source HEAD.' }
                $sourceWorktreeBlob = Git $entry.sourcePath @('hash-object',('--path=' + $excludedPath),'--',$excludedSource)
                if ($sourceWorktreeBlob -cne $exclusion.sourceBlob) { throw 'Excluded generated source worktree bytes differ from source HEAD.' }
            } elseif ($exclusion.reason -ceq 'ProjectIgnoredGeneratedCsprojMetadata') {
                if (-not $excludedPath.EndsWith('.csproj.meta', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid generated .csproj metadata exclusion.' }
                $expectedPair = $excludedPath.Substring(0, $excludedPath.Length - '.meta'.Length)
                if ($exclusion.pairedPath -cne $expectedPair -or -not $exclusionRecords.ContainsKey($expectedPair)) { throw 'Generated .csproj metadata exclusion is missing its approved companion.' }
                $pairedRecord = $exclusionRecords[$expectedPair]
                if ($pairedRecord.reason -cne 'ProjectIgnoredGeneratedCsproj') { throw 'Generated .csproj metadata companion is not an approved exclusion.' }
                $metadataTree = Git $entry.sourcePath @('ls-tree','HEAD','--',$excludedPath)
                if ($metadataTree) {
                    if (-not $exclusion.sourceBlob) { throw 'Tracked generated .csproj metadata exclusion is missing its source blob.' }
                    $expectedMetadataTree = '100644 blob ' + [string]$exclusion.sourceBlob + "`t" + $excludedPath
                    $expectedExecutableMetadataTree = '100755 blob ' + [string]$exclusion.sourceBlob + "`t" + $excludedPath
                    if ($metadataTree -cne $expectedMetadataTree -and $metadataTree -cne $expectedExecutableMetadataTree) { throw 'Tracked generated .csproj metadata exclusion no longer matches source HEAD mode, blob, and path.' }
                    if (-not [IO.File]::Exists($excludedSource)) { throw 'Tracked generated .csproj metadata worktree file is unavailable.' }
                    Check-Hash $excludedSource ([string]$exclusion.sourceHash)
                    $metadataWorktreeBlob = Git $entry.sourcePath @('hash-object',('--path=' + $excludedPath),'--',$excludedSource)
                    if ($metadataWorktreeBlob -cne $exclusion.sourceBlob) { throw 'Tracked generated .csproj metadata worktree bytes differ from source HEAD.' }
                } elseif ($exclusion.sourceBlob) { throw 'Untracked generated .csproj metadata exclusion must not declare a source blob.' }
                if (-not (Is-Project-Ignored $entry.sourcePath $excludedPath)) { throw 'Generated .csproj metadata is no longer ignored by its source repository.' }
                if ($plannedMetadata.ContainsKey($excludedPath)) {
                    $pairedMetadata = $plannedMetadata[$excludedPath]
                    if ($exclusion.sourceHash -cne $pairedMetadata.projectHash) { throw 'Generated .csproj metadata exclusion does not match its materialization plan.' }
                } else {
                    if (-not [IO.File]::Exists($excludedSource)) { throw 'Excluded generated .csproj metadata is unavailable.' }
                    Check-Hash $excludedSource ([string]$exclusion.sourceHash)
                }
            } else { throw 'Invalid generated projection exclusion reason.' }
        }
        foreach ($file in @($entry.files)) {
            $filePath = [string]$file.path
            if (-not $plannedFiles.Add($filePath) -or $file.mode -notin @('100644','100755')) { throw 'Invalid ordinary-file projection record.' }
            if ($plannedExclusions.Contains($filePath)) { throw 'Projection file is both included and excluded.' }
            if (-not $known.ContainsKey($filePath) -and -not $plannedMetadata.ContainsKey($filePath)) { throw 'Projection file is unavailable from canonical source or planned metadata.' }
            $projectedFile = Join-Path $entry.projectionPath $filePath
            if (-not (Within $entry.projectionPath $projectedFile)) { throw 'Projection file escapes its entry root.' }
            $evidenceFile = $null
            if ($known.ContainsKey($filePath)) { $evidenceFile = $known[$filePath] }
            else { $evidenceFile = Join-Path $entry.originalPath $filePath }
            $sourceTreeEntry = Git $entry.sourcePath @('ls-tree','HEAD','--',$filePath)
            if ($sourceTreeEntry) {
                $expectedSourceTree = [string]$file.mode + ' blob ' + [string]$file.sourceBlob + "`t" + $filePath
                if ($sourceTreeEntry -cne $expectedSourceTree) { throw 'Planned source file no longer matches source HEAD mode, blob, and path.' }
                $sourceFile = Join-Path $entry.sourcePath $filePath
                $sourceWorktreeBlob = Git $entry.sourcePath @('hash-object',('--path=' + $filePath),'--',$sourceFile)
                if ($sourceWorktreeBlob -cne $file.sourceBlob) { throw 'Canonical source worktree bytes differ from source HEAD.' }
            }
            $evidenceRelative = (Relative $p.projectGitRoot $entry.projectionPath) + '/' + $filePath.Replace('\','/')
            $evidenceBlob = Git $p.projectGitRoot @('hash-object',('--path=' + $evidenceRelative),'--',$evidenceFile)
            if ($file.sourceBlob -and $evidenceBlob -cne $file.sourceBlob) { throw 'Planned projection blob no longer matches its immutable file evidence.' }
        }
        foreach ($knownPath in @($known.Keys)) {
            if (-not $plannedFiles.Contains($knownPath) -and -not $plannedExclusions.Contains($knownPath)) { throw 'Canonical source contains an unplanned file.' }
        }
        foreach ($excludedPath in @($plannedExclusions)) {
            if (-not $known.ContainsKey($excludedPath) -and -not $plannedMetadata.ContainsKey($excludedPath)) { throw 'Planned projection exclusion is missing from canonical source and metadata plan.' }
        }
    }
    foreach ($write in @($p.writes)) {
        $allowed = @((Join-Path $p.projectGitRoot 'PackageManagement/project.packageset.json'), (Join-Path $p.projectGitRoot 'PackageManagement/runtime.bindings.json'))
        if ((Full $write.path) -notin @($allowed | ForEach-Object { Full $_ })) { throw 'Metadata write is not an approved binding file.' }
        No-ReparseAncestors $write.path; Check-Hash $write.path $write.previousHash
    }
    Require-UnoccupiedPath $Backup
    [IO.Directory]::CreateDirectory($Backup) | Out-Null
    Require-OrdinaryDirectory $Backup
    $newIndex = Join-Path $Operation 'prepared.index'
    Require-UnoccupiedPath $newIndex
    [IO.File]::Copy($indexPath, $newIndex, $false)
    Require-OrdinaryFile $newIndex
    Save-Journal 'validated'; $MutationStarted = $true
    foreach ($entry in @($p.entries)) {
        foreach ($metadata in @($entry.metadata)) {
            $sourceMetadata = Join-Path $entry.sourcePath ([string]$metadata.path)
            $metadataBytes = [Convert]::FromBase64String([string]$metadata.contentBase64)
            Backup-CheckedBytes $sourceMetadata $metadataBytes ([string]$metadata.sourcePreviousHash)
        }
        $materialized = Tree-Files $entry.sourcePath
        $materializedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $materializedExclusions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in @($entry.files)) { [void]$materializedFiles.Add([string]$file.path) }
        foreach ($exclusion in @($entry.projectionExclusions)) { [void]$materializedExclusions.Add([string]$exclusion.path) }
        if ($materialized.Count -ne ($materializedFiles.Count + $materializedExclusions.Count)) { throw 'Canonical source inventory differs from the immutable projection plan.' }
        foreach ($materializedPath in @($materialized.Keys)) {
            if (-not $materializedFiles.Contains($materializedPath) -and -not $materializedExclusions.Contains($materializedPath)) { throw 'Canonical source materialized an unplanned file.' }
        }
        $originalRelative = Relative $p.projectGitRoot $entry.originalPath
        $projectionRelative = Relative $p.projectGitRoot $entry.projectionPath
        $oldFiles = Git $p.projectGitRoot @('ls-files','-z','--',$originalRelative,($originalRelative + '.meta')) $newIndex
        foreach ($old in $oldFiles.Split([char]0)) { if ($old) { [void](Git $p.projectGitRoot @('update-index','--force-remove','--',$old) $newIndex) } }
        if ($entry.submoduleSection) {
            $modules = Join-Path $p.projectGitRoot '.gitmodules'
            Remove-GitConfigSectionTransactional $modules ([string]$entry.submoduleSection) ([string]$p.gitmodulesHash) ([string]$p.projectGitRoot)
        }
        $projectionState = [string]$entry.projectionState
        if ($projectionState -ne 'CorrectJunction') {
            if ($projectionState -ne 'Absent') {
                $directoryBackup = Join-Path $Backup ('directory-' + $Moves.Count)
                Require-UnoccupiedPath $directoryBackup
                Require-DeclaredMutationPath ([string]$entry.originalPath)
                Require-OrdinaryDirectory ([string]$entry.originalPath)
                [void]$Moves.Add([ordered]@{ original = $entry.originalPath; backup = $directoryBackup; kind = 'directory'; expectedHash = [string]$entry.originalInventoryHash; moved = $false }); Save-Journal 'applying'
                [IO.Directory]::Move($entry.originalPath, $directoryBackup)
                $Moves[$Moves.Count - 1].moved = $true; Save-Journal 'applying'
                Require-OrdinaryDirectory $directoryBackup
                if ((Hash-Inventory $directoryBackup) -cne [string]$entry.originalInventoryHash) { throw 'Moved Project directory changed during cutover.' }
            }
            $projectionParent = [IO.Path]::GetDirectoryName($entry.projectionPath)
            Require-DeclaredMutationPath $projectionParent
            Ensure-JournaledOrdinaryDirectory $projectionParent
            Require-UnoccupiedPath $entry.projectionPath
            [void]$Links.Add([ordered]@{ path = $entry.projectionPath; target = $entry.sourcePath; created = $false }); Save-Journal 'applying'
            New-Item -ItemType Junction -Path $entry.projectionPath -Target $entry.sourcePath | Out-Null
            $Links[$Links.Count - 1].created = $true; Save-Journal 'applying'
            if ($projectionState -ne 'Absent' -and $entry.originalMetaHash -ne 'missing' -and $entry.originalPath -ine $entry.projectionPath) {
                $meta = $entry.originalPath + '.meta'
                $metaBackup = Join-Path $Backup ('meta-' + $Moves.Count)
                Require-UnoccupiedPath $metaBackup
                Require-DeclaredMutationPath $meta
                Require-OrdinaryFile $meta
                [void]$Moves.Add([ordered]@{ original = $meta; backup = $metaBackup; kind = 'file'; expectedHash = [string]$entry.originalMetaHash; moved = $false }); Save-Journal 'applying'
                [IO.File]::Move($meta, $metaBackup)
                $Moves[$Moves.Count - 1].moved = $true
                Save-Journal 'applying'
                Copy-MovedMetaToProjection $metaBackup ($entry.projectionPath + '.meta') ([string]$entry.originalMetaHash)
            }
        }
        # Stage explicit ordinary blobs, never an embedded repository directory wholesale.
        foreach ($file in @($entry.files)) {
            $relative = $projectionRelative + '/' + $file.path
            $blob = Git $p.projectGitRoot @('hash-object','-w',('--path=' + $relative),'--',(Join-Path $entry.projectionPath $file.path)) $newIndex
            if ($file.sourceBlob -and $blob -cne $file.sourceBlob) { throw 'Project attributes/filters changed a canonical source blob. Resolve the projection policy before retrying.' }
            [void](Git $p.projectGitRoot @('update-index','--add','--cacheinfo',([string]$file.mode + ',' + $blob + ',' + $relative)) $newIndex)
        }
        $projectionMeta = $entry.projectionPath + '.meta'
        $projectionMetaItem = Get-Item -LiteralPath $projectionMeta -Force -ErrorAction SilentlyContinue
        if ($entry.originalMetaHash -ceq 'missing') {
            if ($null -ne $projectionMetaItem) { throw 'Projection root metadata became unexpectedly occupied.' }
        } else {
            $relative = $projectionRelative + '.meta'
            Stage-CheckedFile $projectionMeta ([string]$entry.originalMetaHash) $relative '100644' ([string]$p.projectGitRoot) $newIndex
        }
        if (-not (Is-CorrectJunction $entry.projectionPath $entry.sourcePath) -or (Hash-Tree $entry.sourcePath) -cne $entry.contentHash) { throw 'Backing verification failed.' }
    }
    foreach ($write in @($p.writes)) {
        Backup-CheckedWrite $write.path $write.content $write.previousHash
        $writeRelative = Relative $p.projectGitRoot $write.path
        $writeHash = Hash-Bytes ($Utf8.GetBytes([string]$write.content))
        Stage-CheckedFile ([string]$write.path) $writeHash $writeRelative '100644' ([string]$p.projectGitRoot) $newIndex
    }
    $modulesPath = Join-Path $p.projectGitRoot '.gitmodules'
    $modulesRecord = Find-WriteRecord $modulesPath
    if ($null -ne $modulesRecord) { Stage-CheckedFile $modulesPath ([string]$modulesRecord.writtenHash) '.gitmodules' '100644' ([string]$p.projectGitRoot) $newIndex }
    foreach ($entry in @($p.entries)) {
        if ((Git $entry.sourcePath @('rev-parse','HEAD')) -cne $entry.sourceRevision -or (Git $entry.sourcePath @('status','--porcelain')).Length -ne 0 -or (Hash-Tree $entry.sourcePath) -cne $entry.contentHash) { throw 'Source changed during apply.' }
        $projectionRelative = Relative $p.projectGitRoot $entry.projectionPath
        foreach ($metadata in @($entry.metadata)) {
            $sourceMetadata = Join-Path $entry.sourcePath ([string]$metadata.path)
            Check-Hash $sourceMetadata ([string]$metadata.projectHash)
            if (-not (Is-Project-Ignored $entry.sourcePath ([string]$metadata.path))) { throw 'Canonical source metadata ignore policy changed during apply.' }
        }
        foreach ($exclusion in @($entry.projectionExclusions)) {
            $excludedSource = Join-Path $entry.sourcePath ([string]$exclusion.path)
            Check-Hash $excludedSource ([string]$exclusion.sourceHash)
            if ($exclusion.reason -ceq 'ProjectIgnoredGeneratedCsproj' -or $exclusion.reason -ceq 'ProjectIgnoredGeneratedCsprojMetadata') {
                $excludedProjection = $projectionRelative + '/' + ([string]$exclusion.path).Replace('\','/')
                if (-not (Is-Project-Ignored $p.projectGitRoot $excludedProjection)) {
                    if ($exclusion.reason -ceq 'ProjectIgnoredGeneratedCsprojMetadata') { throw 'Generated .csproj metadata is not ignored through the final Project projection.' }
                    throw 'Generated .csproj is not ignored through the final Project projection.'
                }
            }
        }
        foreach ($file in @($entry.files)) {
            $sourceFile = Join-Path $entry.sourcePath ([string]$file.path)
            $projectionFileRelative = $projectionRelative + '/' + ([string]$file.path).Replace('\','/')
            $currentProjectionBlob = Git $p.projectGitRoot @('hash-object',('--path=' + $projectionFileRelative),'--',$sourceFile)
            if ($currentProjectionBlob -cne $file.sourceBlob) { throw 'Canonical source projection bytes changed during apply.' }
            $sourceTreeEntry = Git $entry.sourcePath @('ls-tree','HEAD','--',([string]$file.path))
            if ($sourceTreeEntry) {
                $currentSourceBlob = Git $entry.sourcePath @('hash-object',('--path=' + [string]$file.path),'--',$sourceFile)
                if ($currentSourceBlob -cne $file.sourceBlob) { throw 'Canonical source tracked bytes changed during apply.' }
            }
        }
        $finalInventory = Tree-Files $entry.sourcePath
        $expectedInventory = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in @($entry.files)) { [void]$expectedInventory.Add([string]$file.path) }
        foreach ($exclusion in @($entry.projectionExclusions)) { [void]$expectedInventory.Add([string]$exclusion.path) }
        if ($finalInventory.Count -ne $expectedInventory.Count) { throw 'Canonical source inventory changed during apply.' }
        foreach ($finalPath in @($finalInventory.Keys)) { if (-not $expectedInventory.Contains($finalPath)) { throw 'Canonical source gained an unplanned file during apply.' } }
    }
    foreach ($writeRecord in $Writes) {
        if (-not [string]::IsNullOrWhiteSpace([string]$writeRecord.writtenHash)) { Check-Hash ([string]$writeRecord.path) ([string]$writeRecord.writtenHash) }
    }
    $bytes = [IO.File]::ReadAllBytes($newIndex)
    $IndexLock.Write($bytes, 0, $bytes.Length); $IndexLock.Flush($true); $IndexLock.Dispose(); $IndexLock = $null
    $indexBackupPath = Join-Path $Backup 'project.index'
    Require-UnoccupiedPath $indexBackupPath
    [IO.File]::Replace($indexLockPath, $indexPath, $indexBackupPath, $false)
    $OwnIndexLock = $false
    $Committed = $true; Save-Journal 'applied'
    try {
        $localCheckoutPath = Join-Path $p.projectGitDirectory 'PackageManagement/local-checkout.json'
        $localCheckoutContent = [ordered]@{ schemaVersion = 1; checkoutPath = $p.packageSetRoot } | ConvertTo-Json
        Write-OwnedText $localCheckoutPath $localCheckoutContent
    } catch { Write-Warning 'Backing is applied, but machine-local checkout metadata could not be saved.' }
    Write-Host 'Development backing enabled. Review the staged Project snapshot; no Project commit or remote push was performed.'
    Write-Host "Retained recovery data: $Backup"
} catch {
    $Failure = $_.Exception.Message
    if ($MutationStarted -and -not $Committed) {
        try {
            for ($i = $Links.Count - 1; $i -ge 0; $i--) {
                $link = $Links[$i]
                if ($link.created) { Remove-RollbackLink $link }
            }
            for ($i = $Writes.Count - 1; $i -ge 0; $i--) {
                $write = $Writes[$i]
                Restore-WriteRecord $write $i
            }
            for ($i = $Moves.Count - 1; $i -ge 0; $i--) {
                $move = $Moves[$i]
                if ($move.moved) { Restore-MoveRecord $move }
            }
            for ($i = $Directories.Count - 1; $i -ge 0; $i--) {
                Remove-OwnedDirectoryRecord $Directories[$i]
            }
            Save-Journal 'rolled-back'
        } catch { $Failure += '; recovery requires review: ' + $_.Exception.Message; try { Save-Journal 'needs-recovery' } catch {} }
    } elseif (-not $Committed -and $NewJournalAllowed) { try { Save-Journal 'preflight-failed' } catch {} }
    Write-Error $Failure -ErrorAction Continue
    $ExitCode = 1
} finally {
    if ($null -ne $IndexLock) { $IndexLock.Dispose() }
    if ($OwnIndexLock) { Remove-OwnedFileIfPresent $indexLockPath }
    if ($null -ne $Lease) { $Lease.Dispose() }
    if ($OwnLease) { Remove-OwnedFileIfPresent $LeasePath }
}
if ($ReopenUnity -and $ExitCode -eq 0 -and $Committed -and $null -ne $p) {
    try { Start-Process -FilePath $p.unityExecutable -ArgumentList ('-projectPath ' + (Quote-Argument $p.projectRoot)) }
    catch { Write-Warning 'Unity could not be reopened automatically.' }
}
if ($ExitCode -ne 0) { exit $ExitCode }
