[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repository = 'crafty-racoon/Tool.PackageManager'
$ToolRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ManifestPath = Join-Path $ToolRoot 'config\self-update-manifest.json'
$PolicyPath = Join-Path $ToolRoot 'config\self-update-policy.json'

function Write-Heading {
    param([string]$Text)

    Write-Host ''
    Write-Host "== $Text =="
}

function Wait-ForUser {
    Write-Host ''
    [void](Read-Host 'Press Enter to close this window')
}

function Assert-Prerequisites {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required but was not found on PATH.'
    }

    & gh auth status
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub CLI authentication is required. Run gh auth login first.'
    }
}

function Get-VersionDocument {
    param(
        [string]$Root,
        [string]$Context
    )

    $documents = @(
        Get-ChildItem -LiteralPath $Root -File |
            Where-Object {
                $_.Name -match '^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?\.md$'
            }
    )

    if ($documents.Count -ne 1) {
        $found = if ($documents.Count -eq 0) {
            '<none>'
        }
        else {
            ($documents.Name -join ', ')
        }

        throw "$Context must contain exactly one version document named v<semver>.md. Found: $found"
    }

    return $documents[0]
}

function Read-VersionFromRoot {
    param(
        [string]$Root,
        [string]$Context
    )

    $document = Get-VersionDocument -Root $Root -Context $Context
    return [System.IO.Path]::GetFileNameWithoutExtension($document.Name)
}

function Read-LocalVersion {
    try {
        return Read-VersionFromRoot -Root $ToolRoot -Context 'Current Tool.PackageManager'
    }
    catch {
        return 'unknown'
    }
}

function Get-BlockedUpdateRefs {
    $blocked = @{}

    if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
        return $blocked
    }

    $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
    if ($null -eq $policy -or $policy.schemaVersion -ne 1) {
        throw 'Unsupported self-update policy schema.'
    }

    if ($policy.blockedRefs) {
        foreach ($entry in $policy.blockedRefs) {
            if ($null -eq $entry) {
                continue
            }

            $ref = [string]$entry.ref
            if ([string]::IsNullOrWhiteSpace($ref)) {
                continue
            }

            $blocked[$ref] = [string]$entry.reason
        }
    }

    return $blocked
}

function Get-UpdateCandidates {
    $blockedRefs = Get-BlockedUpdateRefs

    $items = @(
        [pscustomobject]@{
            Ref = 'main'
            Label = 'main  [Development]'
            Kind = 'Development'
        }
    )

    $releaseJson = & gh api "repos/$Repository/releases?per_page=100" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read Tool.PackageManager releases from GitHub: $($releaseJson -join [Environment]::NewLine)"
    }

    $releases = @((($releaseJson -join [Environment]::NewLine) | ConvertFrom-Json))

    foreach ($release in $releases) {
        if ($null -eq $release) {
            continue
        }

        if ([bool]$release.draft) {
            continue
        }

        $tagName = [string]$release.tag_name
        if ([string]::IsNullOrWhiteSpace($tagName)) {
            continue
        }

        if ($blockedRefs.ContainsKey($tagName)) {
            continue
        }

        $kind = if ([bool]$release.prerelease) {
            'Pre-release'
        }
        else {
            'Stable'
        }

        $items += [pscustomobject]@{
            Ref = $tagName
            Label = "$tagName  [$kind]"
            Kind = $kind
        }
    }

    return $items
}

function Select-UpdateCandidate {
    param(
        [Parameter(Mandatory = $true)][object[]]$Items,
        [string]$CurrentVersion
    )

    if ($Items.Count -eq 0) {
        throw 'No update versions are available.'
    }

    $selectedIndex = 0

    while ($true) {
        Clear-Host
        Write-Host '== Update Tool.PackageManager =='
        Write-Host "Current version : $CurrentVersion"
        Write-Host "Tool root       : $ToolRoot"
        Write-Host ''
        Write-Host 'Use Up/Down arrows and Enter. Esc cancels.'
        Write-Host ''

        $reservedLines = 9
        $pageSize = [Math]::Max(4, [Console]::WindowHeight - $reservedLines)
        $pageIndex = [Math]::Floor($selectedIndex / $pageSize)
        $startIndex = $pageIndex * $pageSize
        $endIndex = [Math]::Min($Items.Count - 1, $startIndex + $pageSize - 1)

        for ($i = $startIndex; $i -le $endIndex; $i++) {
            $prefix = if ($i -eq $selectedIndex) { '>' } else { ' ' }
            Write-Host "$prefix $($Items[$i].Label)"
        }

        if ($Items.Count -gt $pageSize) {
            Write-Host ''
            Write-Host "Items $($startIndex + 1)-$($endIndex + 1) of $($Items.Count)"
        }

        $key = [Console]::ReadKey($true)

        switch ($key.Key) {
            'UpArrow' {
                $selectedIndex--
                if ($selectedIndex -lt 0) {
                    $selectedIndex = $Items.Count - 1
                }
            }

            'DownArrow' {
                $selectedIndex++
                if ($selectedIndex -ge $Items.Count) {
                    $selectedIndex = 0
                }
            }

            'Enter' {
                return $Items[$selectedIndex]
            }

            'Escape' {
                return $null
            }
        }
    }
}

function Assert-ManagedRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'Self-update manifest contains an empty file path.'
    }

    $trimmed = $Path.Trim().Replace('\', '/')

    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        throw "Self-update manifest path must be relative: $trimmed"
    }

    $segments = @($trimmed -split '/' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne '.' })
    if ($segments.Count -eq 0 -or $segments -contains '..') {
        throw "Unsafe self-update manifest path: $trimmed"
    }

    if ($segments[0] -eq '.git') {
        throw "Self-update manifest cannot manage .git content: $trimmed"
    }

    return ($segments -join '/')
}

function Read-SelfUpdateManifest {
    param(
        [string]$Path,
        [string]$Context
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Context does not contain config/self-update-manifest.json and cannot be applied safely."
    }

    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json

    if ($null -eq $manifest -or $manifest.schemaVersion -ne 1) {
        throw "$Context uses an unsupported self-update manifest schema."
    }

    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals([string]$manifest.repository, $Repository)) {
        throw "$Context self-update manifest belongs to '$($manifest.repository)', expected '$Repository'."
    }

    if (-not $manifest.files -or $manifest.files.Count -eq 0) {
        throw "$Context self-update manifest contains no managed files."
    }

    $seen = @{}
    $files = @()

    foreach ($entry in $manifest.files) {
        $relativePath = Assert-ManagedRelativePath -Path ([string]$entry)

        if ($seen.ContainsKey($relativePath)) {
            throw "$Context self-update manifest contains duplicate file '$relativePath'."
        }

        $seen[$relativePath] = $true
        $files += $relativePath
    }

    $manifest | Add-Member -NotePropertyName NormalizedFiles -NotePropertyValue $files -Force
    return $manifest
}

function Get-GitHubToken {
    $tokenOutput = & gh auth token 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $tokenOutput) {
        throw 'Could not obtain the GitHub authentication token from gh.'
    }

    $token = ($tokenOutput | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw 'GitHub authentication token is empty.'
    }

    return $token
}

function Download-VersionArchive {
    param(
        [string]$Ref,
        [string]$DestinationZip
    )

    $token = Get-GitHubToken
    $escapedRef = [System.Uri]::EscapeDataString($Ref)
    $uri = "https://api.github.com/repos/$Repository/zipball/$escapedRef"

    $headers = @{
        Authorization = "Bearer $token"
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'Tool.PackageManager-SelfUpdater'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    Invoke-WebRequest -Uri $uri -Headers $headers -OutFile $DestinationZip -UseBasicParsing
}

function Get-ArchiveRoot {
    param([string]$ExtractRoot)

    $directories = @(Get-ChildItem -LiteralPath $ExtractRoot -Directory)
    if ($directories.Count -ne 1) {
        throw "Downloaded archive must contain exactly one repository root directory. Found: $($directories.Count)"
    }

    return $directories[0].FullName
}

function Write-ApplyHelper {
    param([string]$Path)

    $helper = @'
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$TargetRoot,
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [Parameter(Mandatory = $true)][string]$CurrentManifestPath,
    [Parameter(Mandatory = $true)][string]$NewManifestPath,
    [Parameter(Mandatory = $true)][string]$OldVersion,
    [Parameter(Mandatory = $true)][string]$NewVersion,
    [Parameter(Mandatory = $true)][int]$UpdaterPid,
    [int]$LauncherPid = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Wait-ForProcessExit {
    param([int]$ProcessId)

    if ($ProcessId -le 0) {
        return
    }

    while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 100
    }
}

function Normalize-RelativePath {
    param([string]$Path)

    $trimmed = $Path.Trim().Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        throw "Managed path must be relative: $trimmed"
    }

    $segments = @($trimmed -split '/' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne '.' })
    if ($segments.Count -eq 0 -or $segments -contains '..' -or $segments[0] -eq '.git') {
        throw "Unsafe managed path: $trimmed"
    }

    return ($segments -join '/')
}

function Read-ManifestFiles {
    param([string]$ManifestPath)

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($null -eq $manifest -or $manifest.schemaVersion -ne 1 -or -not $manifest.files) {
        throw "Invalid self-update manifest: $ManifestPath"
    }

    $files = @()
    foreach ($entry in $manifest.files) {
        $files += Normalize-RelativePath -Path ([string]$entry)
    }

    return $files
}

function To-NativePath {
    param(
        [string]$Root,
        [string]$RelativePath
    )

    return Join-Path $Root ($RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
}

function Ensure-ParentDirectory {
    param([string]$Path)

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
}

function Show-ResultAndWait {
    param(
        [string]$Heading,
        [string[]]$Lines
    )

    Clear-Host
    Write-Host "== $Heading =="
    foreach ($line in $Lines) {
        Write-Host $line
    }

    Write-Host ''
    [void](Read-Host 'Press Enter to close this window')
}

Wait-ForProcessExit -ProcessId $UpdaterPid
Wait-ForProcessExit -ProcessId $LauncherPid
Start-Sleep -Milliseconds 300

$workRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupRoot = Join-Path $workRoot 'backup'
$currentFiles = Read-ManifestFiles -ManifestPath $CurrentManifestPath
$newFiles = Read-ManifestFiles -ManifestPath $NewManifestPath

$currentSet = @{}
foreach ($path in $currentFiles) {
    $currentSet[$path] = $true
}

$newSet = @{}
foreach ($path in $newFiles) {
    $newSet[$path] = $true
}

$existingCurrentFiles = @{}
$preexistingNewFiles = @{}
$mutationStarted = $false

function Test-FileContentEqual {
    param(
        [string]$LeftPath,
        [string]$RightPath
    )

    if (-not (Test-Path -LiteralPath $LeftPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $RightPath -PathType Leaf)) {
        return $false
    }

    $leftHash = (Get-FileHash -LiteralPath $LeftPath -Algorithm SHA256).Hash
    $rightHash = (Get-FileHash -LiteralPath $RightPath -Algorithm SHA256).Hash

    return [System.StringComparer]::OrdinalIgnoreCase.Equals(
        $leftHash,
        $rightHash)
}

try {
    # Phase 1: preflight only. This phase must never mutate the target.
    foreach ($relativePath in $newFiles) {
        $sourcePath = To-NativePath -Root $StageRoot -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Selected version is missing managed file: $relativePath"
        }

        $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath

        if (-not $currentSet.ContainsKey($relativePath) -and
            (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            if (-not (Test-FileContentEqual -LeftPath $targetPath -RightPath $sourcePath)) {
                throw "Update would overwrite an unmanaged existing file with different content: $relativePath"
            }

            # Safe adoption: the file already exists and is byte-for-byte identical
            # to the selected version. Record that it predated this transaction so
            # rollback must never delete it.
            $preexistingNewFiles[$relativePath] = $true
        }
    }

    # Phase 2: backup every currently managed target file before any mutation.
    foreach ($relativePath in $currentFiles) {
        $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath

        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            $existingCurrentFiles[$relativePath] = $true
            $backupPath = To-NativePath -Root $backupRoot -RelativePath $relativePath
            Ensure-ParentDirectory -Path $backupPath
            Copy-Item -LiteralPath $targetPath -Destination $backupPath -Force
        }
    }

    # Rollback is permitted only after this point.
    $mutationStarted = $true

    # Phase 3: apply selected managed files.
    foreach ($relativePath in $newFiles) {
        $sourcePath = To-NativePath -Root $StageRoot -RelativePath $relativePath
        $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath

        Ensure-ParentDirectory -Path $targetPath
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    }

    # Phase 4: remove files that were managed by the old version but are no
    # longer owned by the selected version.
    foreach ($relativePath in $currentFiles) {
        if ($newSet.ContainsKey($relativePath)) {
            continue
        }

        $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath
        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            Remove-Item -LiteralPath $targetPath -Force
        }
    }

    Show-ResultAndWait -Heading 'Tool.PackageManager update complete' -Lines @(
        "Previous version : $OldVersion",
        "Installed version: $NewVersion",
        "Tool root        : $TargetRoot"
    )

    exit 0
}
catch {
    $applyError = $_.Exception.Message
    $rollbackErrors = @()

    if ($mutationStarted) {
        # Remove only files that did not exist before this transaction.
        foreach ($relativePath in $newFiles) {
            if ($currentSet.ContainsKey($relativePath) -or
                $preexistingNewFiles.ContainsKey($relativePath)) {
                continue
            }

            $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath
            if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
                try {
                    Remove-Item -LiteralPath $targetPath -Force
                }
                catch {
                    $rollbackErrors += "Could not remove newly added file '$relativePath': $($_.Exception.Message)"
                }
            }
        }

        # Restore every file that existed before mutation.
        foreach ($relativePath in $currentFiles) {
            $targetPath = To-NativePath -Root $TargetRoot -RelativePath $relativePath

            if ($existingCurrentFiles.ContainsKey($relativePath)) {
                $backupPath = To-NativePath -Root $backupRoot -RelativePath $relativePath
                try {
                    Ensure-ParentDirectory -Path $targetPath
                    Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
                }
                catch {
                    $rollbackErrors += "Could not restore '$relativePath': $($_.Exception.Message)"
                }
            }
            elseif (Test-Path -LiteralPath $targetPath -PathType Leaf) {
                try {
                    Remove-Item -LiteralPath $targetPath -Force
                }
                catch {
                    $rollbackErrors += "Could not remove '$relativePath' during rollback: $($_.Exception.Message)"
                }
            }
        }
    }

    $lines = @(
        "Update failed: $applyError",
        ''
    )

    if ($mutationStarted) {
        $lines += 'The updater attempted to restore the previous managed files.'
    }
    else {
        $lines += 'Preflight failed before any target file was modified.'
    }

    if ($rollbackErrors.Count -gt 0) {
        $lines += ''
        $lines += 'Rollback warnings:'
        $lines += $rollbackErrors
    }

    Show-ResultAndWait -Heading 'Tool.PackageManager update failed' -Lines $lines
    exit 1
}
'@

    Set-Content -LiteralPath $Path -Value $helper -Encoding UTF8
}

try {
    Assert-Prerequisites

    $currentVersion = Read-LocalVersion
    $candidates = @(Get-UpdateCandidates)
    $selected = Select-UpdateCandidate -Items $candidates -CurrentVersion $currentVersion

    if ($null -eq $selected) {
        Clear-Host
        Write-Host 'Tool.PackageManager update cancelled.'
        Wait-ForUser
        exit 0
    }

    $workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Tool.PackageManager.Update." + [Guid]::NewGuid().ToString('N'))
    $downloadPath = Join-Path $workRoot 'tool.zip'
    $extractRoot = Join-Path $workRoot 'stage'
    $currentManifestSnapshot = Join-Path $workRoot 'current-self-update-manifest.json'
    $helperPath = Join-Path $workRoot 'apply-update.ps1'

    New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

    Write-Heading "Download $($selected.Ref)"
    Download-VersionArchive -Ref ([string]$selected.Ref) -DestinationZip $downloadPath
    Expand-Archive -LiteralPath $downloadPath -DestinationPath $extractRoot -Force

    $stageRoot = Get-ArchiveRoot -ExtractRoot $extractRoot
    $newManifestPath = Join-Path $stageRoot 'config\self-update-manifest.json'

    $currentManifest = Read-SelfUpdateManifest -Path $ManifestPath -Context 'Current Tool.PackageManager'
    $newManifest = Read-SelfUpdateManifest -Path $newManifestPath -Context "Selected version '$($selected.Ref)'"

    foreach ($relativePath in $newManifest.NormalizedFiles) {
        $stagePath = Join-Path $stageRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $stagePath -PathType Leaf)) {
            throw "Selected version '$($selected.Ref)' is missing managed file '$relativePath'."
        }
    }

    Copy-Item -LiteralPath $ManifestPath -Destination $currentManifestSnapshot -Force

    $newVersion = Read-VersionFromRoot -Root $stageRoot -Context "Selected version '$($selected.Ref)'"

    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals([string]$selected.Ref, 'main') -and
        -not [System.StringComparer]::OrdinalIgnoreCase.Equals([string]$selected.Ref, $newVersion)) {
        throw "Selected release '$($selected.Ref)' contains version document '$newVersion.md'."
    }

    Write-ApplyHelper -Path $helperPath

    $launcherPid = 0
    try {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
        if ($null -ne $process) {
            $launcherPid = [int]$process.ParentProcessId
        }
    }
    catch {
        $launcherPid = 0
    }

    Write-Heading 'Update staged'
    Write-Host "Current  : $currentVersion"
    Write-Host "Selected : $newVersion  [$($selected.Ref)]"
    Write-Host "Tool root : $ToolRoot"
    Write-Host ''
    Write-Host 'The current updater will close. A separate window will apply the update and show the final result.'

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $helperPath + '"'),
        '-TargetRoot', ('"' + $ToolRoot + '"'),
        '-StageRoot', ('"' + $stageRoot + '"'),
        '-CurrentManifestPath', ('"' + $currentManifestSnapshot + '"'),
        '-NewManifestPath', ('"' + $newManifestPath + '"'),
        '-OldVersion', ('"' + $currentVersion + '"'),
        '-NewVersion', ('"' + $newVersion + '"'),
        '-UpdaterPid', [string]$PID,
        '-LauncherPid', [string]$launcherPid
    )

    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments | Out-Null
    exit 0
}
catch {
    Clear-Host
    Write-Host '== Tool.PackageManager update failed =='
    Write-Host $_.Exception.Message
    Wait-ForUser
    exit 1
}
