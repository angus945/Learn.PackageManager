[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$Plan,
    [string]$SourceRoot,
    [switch]$ListPlans,
    [switch]$NonInteractive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$CatalogPath = Join-Path $PSScriptRoot 'config\bootstrap-plans.json'

function Write-Heading {
    param([string]$Text)
    Write-Host ''
    Write-Host "== $Text =="
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$Capture
    )

    if ($Capture) {
        $output = & $Command @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            $newLine = [Environment]::NewLine
            throw "$Command failed (exit $LASTEXITCODE): $($Arguments -join ' ')$newLine$($output -join $newLine)"
        }
        return @($output)
    }

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed (exit $LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function Try-GetGitRoot {
    param([string]$StartPath)

    if ([string]::IsNullOrWhiteSpace($StartPath) -or -not (Test-Path -LiteralPath $StartPath)) {
        return $null
    }

    $resolved = (Resolve-Path -LiteralPath $StartPath).Path
    $output = & git -C $resolved rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $output) {
        return $null
    }

    return [System.IO.Path]::GetFullPath(($output | Select-Object -First 1).Trim())
}

function Get-BootstrapperGitRoot {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        return $null
    }

    $root = Try-GetGitRoot -StartPath $PSScriptRoot
    if (-not $root) {
        return $null
    }

    $origin = & git -C $root remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $origin) {
        return $null
    }

    $originText = ($origin | Select-Object -First 1).Trim()
    if ($originText -notmatch '(?i)(^|[/:])crafty-racoon/Tool\.PackageManager(?:\.git)?$') {
        return $null
    }

    return $root
}

function Resolve-TargetGitRoot {
    param([string]$ExplicitProjectPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitProjectPath)) {
        $root = Try-GetGitRoot -StartPath $ExplicitProjectPath
        if (-not $root) {
            throw "ProjectPath is not inside a Git repository: $ExplicitProjectPath"
        }
        return $root
    }

    $bootstrapperRoot = Get-BootstrapperGitRoot
    $candidates = @((Get-Location).Path, $PSScriptRoot) | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $root = Try-GetGitRoot -StartPath $candidate
        if (-not $root) {
            continue
        }

        if ($bootstrapperRoot -and [System.StringComparer]::OrdinalIgnoreCase.Equals($root, $bootstrapperRoot)) {
            continue
        }

        return $root
    }

    if ($NonInteractive) {
        throw 'Could not locate the target Git root. Pass -ProjectPath explicitly.'
    }

    Write-Host 'Could not infer a target project Git root from the current directory.'
    $inputPath = Read-Host 'Enter a path inside the target Git repository'
    $root = Try-GetGitRoot -StartPath $inputPath
    if (-not $root) {
        throw "The supplied path is not inside a Git repository: $inputPath"
    }

    return $root
}

function Read-BootstrapCatalog {
    if (-not (Test-Path -LiteralPath $CatalogPath)) {
        throw "Bootstrap catalog not found: $CatalogPath"
    }

    $catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 2) {
        throw "Unsupported bootstrap catalog schemaVersion: $($catalog.schemaVersion)"
    }

    if (-not $catalog.plans -or $catalog.plans.Count -eq 0) {
        throw 'Bootstrap catalog contains no plans.'
    }

    $planIds = @{}
    foreach ($candidate in $catalog.plans) {
        if ([string]::IsNullOrWhiteSpace($candidate.id)) {
            throw 'Every bootstrap plan requires an id.'
        }

        if ($planIds.ContainsKey($candidate.id)) {
            throw "Duplicate bootstrap plan id: $($candidate.id)"
        }

        $planIds[$candidate.id] = $true
    }

    return $catalog
}

function Show-Plans {
    param($Catalog)

    Write-Heading 'Bootstrap plans'
    $index = 1

    foreach ($candidate in $Catalog.plans) {
        Write-Host "[$index] $($candidate.displayName)  ($($candidate.id))"
        Write-Host "    $($candidate.description)"

        if ($candidate.plannedComponents -and $candidate.plannedComponents.Count -gt 0) {
            foreach ($planned in $candidate.plannedComponents) {
                Write-Host "    planned: $($planned.id) - $($planned.note)"
            }
        }

        $index++
    }
}

function Show-ConsoleMenu {
    param(
        [Parameter(Mandatory = $true)][object[]]$Items,
        [string]$Title,
        [string[]]$Context = @(),
        [string]$Instruction = 'Use Up/Down arrows and Enter. Esc cancels.'
    )

    if ($Items.Count -eq 0) {
        throw 'Menu contains no items.'
    }

    $selectedIndex = 0

    while ($true) {
        # Redraw from a clean screen instead of relying on a remembered CursorTop.
        # CursorTop becomes invalid when a tall menu causes the console buffer to scroll.
        Clear-Host

        if (-not [string]::IsNullOrWhiteSpace($Title)) {
            Write-Host "== $Title =="
        }

        foreach ($line in $Context) {
            Write-Host $line
        }

        if ($Context.Count -gt 0) {
            Write-Host ''
        }

        Write-Host $Instruction
        Write-Host ''

        # Keep the visible menu inside the current console window.
        # Large directories are rendered a page at a time.
        $reservedLines = 7 + $Context.Count
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
                throw 'Bootstrap cancelled.'
            }
        }
    }
}

function Select-BootstrapPlan {
    param(
        $Catalog,
        [string]$RequestedPlan
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPlan)) {
        $selected = @($Catalog.plans | Where-Object { $_.id -eq $RequestedPlan })
        if ($selected.Count -ne 1) {
            throw "Unknown bootstrap plan: $RequestedPlan"
        }
        return $selected[0]
    }

    if ($NonInteractive) {
        throw 'NonInteractive mode requires -Plan <id>.'
    }

    $items = @()
    foreach ($candidate in $Catalog.plans) {
        $items += [pscustomobject]@{
            Kind = 'Plan'
            Label = "$($candidate.displayName)  [$($candidate.id)]"
            Value = $candidate
        }
    }

    $selection = Show-ConsoleMenu -Items $items -Title 'Select bootstrap plan'
    return $selection.Value
}

function Assert-Prerequisites {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git is required but was not found on PATH.'
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required but was not found on PATH.'
    }

    Write-Heading 'Prerequisites'
    Invoke-CheckedCommand -Command 'git' -Arguments @('--version')
    Invoke-CheckedCommand -Command 'gh' -Arguments @('--version')
    Invoke-CheckedCommand -Command 'gh' -Arguments @('auth', 'status')
    Invoke-CheckedCommand -Command 'gh' -Arguments @('auth', 'setup-git')
}

function Assert-Plan {
    param($SelectedPlan)

    if (-not $SelectedPlan.components -or $SelectedPlan.components.Count -eq 0) {
        throw "Plan '$($SelectedPlan.id)' contains no installable components."
    }

    $ids = @{}

    foreach ($component in $SelectedPlan.components) {
        foreach ($property in @('id', 'repository', 'url', 'revision')) {
            if ([string]::IsNullOrWhiteSpace($component.$property)) {
                throw "Plan '$($SelectedPlan.id)' component is missing '$property'."
            }
        }

        if ($component.id -notmatch '^[A-Za-z0-9._-]+$') {
            throw "Component id '$($component.id)' cannot be used as a source directory name."
        }

        if ($ids.ContainsKey($component.id)) {
            throw "Duplicate component id in plan '$($SelectedPlan.id)': $($component.id)"
        }
        $ids[$component.id] = $true

        if ($component.revision -notmatch '^[0-9a-fA-F]{40}$') {
            throw "Component '$($component.id)' must use an exact 40-character Git revision."
        }

        if ($component.PSObject.Properties['path']) {
            throw "Component '$($component.id)' declares a legacy fixed path. Schema v2 requires runtime initial-layout selection."
        }
    }
}

function Normalize-RelativeSourceRoot {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'Initial source folder cannot be empty.'
    }

    $trimmed = $Path.Trim()

    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        throw "Initial source folder must be relative to the project Git root: $trimmed"
    }

    $segments = @($trimmed -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne '.' })

    if ($segments.Count -eq 0) {
        throw 'Initial source folder cannot be the Git root. Create a new folder for the bootstrap composition.'
    }

    if ($segments -contains '..') {
        throw "Initial source folder cannot escape the project Git root: $trimmed"
    }

    if ($segments[0] -eq '.git') {
        throw 'Initial source folder cannot be inside .git.'
    }

    return ($segments -join '/')
}

function Read-EditableText {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [string]$InitialValue = '',
        [switch]$EscapeReturnsNull
    )

    $buffer = [System.Collections.Generic.List[char]]::new()
    foreach ($character in $InitialValue.ToCharArray()) {
        $buffer.Add($character)
    }

    $cursorIndex = $buffer.Count
    $originLeft = [Console]::CursorLeft
    $originTop = [Console]::CursorTop

    Write-Host -NoNewline $Prompt
    $inputLeft = [Console]::CursorLeft
    $inputTop = [Console]::CursorTop

    function Render-EditableBuffer {
        $text = -join $buffer.ToArray()
        $width = [Math]::Max(1, [Console]::WindowWidth - $inputLeft - 1)

        if ($text.Length -gt $width) {
            $visibleStart = [Math]::Max(0, $cursorIndex - $width + 1)
            $visibleText = $text.Substring($visibleStart, [Math]::Min($width, $text.Length - $visibleStart))
            $visibleCursor = $cursorIndex - $visibleStart
        }
        else {
            $visibleStart = 0
            $visibleText = $text
            $visibleCursor = $cursorIndex
        }

        [Console]::SetCursorPosition($inputLeft, $inputTop)
        Write-Host -NoNewline ($visibleText.PadRight($width))
        [Console]::SetCursorPosition($inputLeft + [Math]::Min($visibleCursor, $width), $inputTop)
    }

    Render-EditableBuffer

    while ($true) {
        $key = [Console]::ReadKey($true)

        switch ($key.Key) {
            'Enter' {
                [Console]::SetCursorPosition(0, $inputTop + 1)
                return (-join $buffer.ToArray())
            }

            'Escape' {
                [Console]::SetCursorPosition(0, $inputTop + 1)

                if ($EscapeReturnsNull) {
                    return $null
                }

                throw 'Bootstrap cancelled.'
            }

            'LeftArrow' {
                if ($cursorIndex -gt 0) {
                    $cursorIndex--
                }
            }

            'RightArrow' {
                if ($cursorIndex -lt $buffer.Count) {
                    $cursorIndex++
                }
            }

            'Home' {
                $cursorIndex = 0
            }

            'End' {
                $cursorIndex = $buffer.Count
            }

            'Backspace' {
                if ($cursorIndex -gt 0) {
                    $buffer.RemoveAt($cursorIndex - 1)
                    $cursorIndex--
                }
            }

            'Delete' {
                if ($cursorIndex -lt $buffer.Count) {
                    $buffer.RemoveAt($cursorIndex)
                }
            }

            default {
                if (-not [char]::IsControl($key.KeyChar)) {
                    $buffer.Insert($cursorIndex, $key.KeyChar)
                    $cursorIndex++
                }
            }
        }

        Render-EditableBuffer
    }
}

function Assert-NewFolderName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw 'Folder name cannot be empty.'
    }

    $trimmed = $Name.Trim()

    if ($trimmed -eq '.' -or $trimmed -eq '..') {
        throw "Invalid folder name: $trimmed"
    }

    if ($trimmed.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "Folder name contains invalid characters: $trimmed"
    }

    if ($trimmed -match '[\\/]') {
        throw 'Enter a single folder name, not a path.'
    }

    if ($trimmed -eq '.git') {
        throw 'The bootstrap source folder cannot be named .git.'
    }

    return $trimmed
}

function Get-RelativePathFromGitRoot {
    param(
        [string]$GitRoot,
        [string]$AbsolutePath
    )

    $gitRootFull = [System.IO.Path]::GetFullPath($GitRoot).TrimEnd('\', '/')
    $pathFull = [System.IO.Path]::GetFullPath($AbsolutePath).TrimEnd('\', '/')

    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($gitRootFull, $pathFull)) {
        return '.'
    }

    $prefix = $gitRootFull + [System.IO.Path]::DirectorySeparatorChar
    if (-not $pathFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the project Git root: $pathFull"
    }

    return $pathFull.Substring($prefix.Length).Replace('\', '/')
}

function Select-InitialSourceRootInteractive {
    param([string]$GitRoot)

    $currentPath = [System.IO.Path]::GetFullPath($GitRoot)

    while ($true) {
        $currentRelative = Get-RelativePathFromGitRoot -GitRoot $GitRoot -AbsolutePath $currentPath
        $displayPath = if ($currentRelative -eq '.') { '<Git Root>/' } else { "$currentRelative/" }

        $items = @()

        if ($currentRelative -ne '.') {
            $items += [pscustomobject]@{
                Kind = 'UseCurrent'
                Label = "[.] Use this folder  ($displayPath)"
                FullPath = $currentPath
            }
        }

        $items += [pscustomobject]@{
            Kind = 'Create'
            Label = "[+] Create new folder here  ($displayPath)"
            FullPath = $null
        }

        if ($currentRelative -ne '.') {
            $parentPath = Split-Path -Parent $currentPath
            $parentRelative = Get-RelativePathFromGitRoot -GitRoot $GitRoot -AbsolutePath $parentPath
            $parentDisplay = if ($parentRelative -eq '.') { '<Git Root>/' } else { "$parentRelative/" }

            $items += [pscustomobject]@{
                Kind = 'Back'
                Label = "[..] Back  ($parentDisplay)"
                FullPath = $parentPath
            }
        }

        $children = @(
            Get-ChildItem -LiteralPath $currentPath -Directory -Force |
                Where-Object { $_.Name -ne '.git' } |
                Sort-Object Name
        )

        foreach ($child in $children) {
            $childRelative = Get-RelativePathFromGitRoot -GitRoot $GitRoot -AbsolutePath $child.FullName

            $items += [pscustomobject]@{
                Kind = 'Directory'
                Label = "$childRelative/"
                FullPath = $child.FullName
            }
        }

        $selection = Show-ConsoleMenu -Items $items -Title 'Choose initial installation folder' -Context @(
            "Git root : $GitRoot",
            "Current  : $displayPath"
        )

        if ($selection.Kind -eq 'UseCurrent') {
            return $currentRelative
        }

        if ($selection.Kind -eq 'Back') {
            $currentPath = $selection.FullPath
            continue
        }

        if ($selection.Kind -eq 'Directory') {
            $currentPath = $selection.FullPath
            continue
        }

        if ($selection.Kind -eq 'Create') {
            while ($true) {
                $newFolderInput = Read-EditableText -Prompt 'New folder name: ' -InitialValue 'PackageManagement' -EscapeReturnsNull

                if ($null -eq $newFolderInput) {
                    break
                }

                try {
                    $newFolderName = Assert-NewFolderName -Name $newFolderInput
                    $newFolderPath = Join-Path $currentPath $newFolderName

                    if (Test-Path -LiteralPath $newFolderPath) {
                        $existingRelative = Get-RelativePathFromGitRoot -GitRoot $GitRoot -AbsolutePath $newFolderPath
                        Write-Host "A file or folder already exists here: $existingRelative"
                        Write-Host 'Press Esc to return to the folder menu and select the existing folder.'
                        continue
                    }

                    New-Item -ItemType Directory -Path $newFolderPath | Out-Null
                    return Get-RelativePathFromGitRoot -GitRoot $GitRoot -AbsolutePath $newFolderPath
                }
                catch {
                    Write-Host "Invalid folder: $($_.Exception.Message)"
                }
            }
        }
    }
}

function Resolve-InitialSourceRoot {
    param(
        [string]$GitRoot,
        [string]$RequestedSourceRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedSourceRoot)) {
        $relativePath = Normalize-RelativeSourceRoot -Path $RequestedSourceRoot
        $absolutePath = Join-Path $GitRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)

        if (-not (Test-Path -LiteralPath $absolutePath)) {
            New-Item -ItemType Directory -Path $absolutePath -Force | Out-Null
        }
        elseif (-not (Test-Path -LiteralPath $absolutePath -PathType Container)) {
            throw "Initial source path exists but is not a directory: $relativePath"
        }

        return $relativePath
    }

    if ($NonInteractive) {
        throw 'NonInteractive mode requires -SourceRoot <relative folder>.'
    }

    return Select-InitialSourceRootInteractive -GitRoot $GitRoot
}

function Test-IsPathWithin {
    param(
        [string]$ParentPath,
        [string]$CandidatePath
    )

    $parentFull = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\', '/')
    $candidateFull = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')

    if ([System.StringComparer]::OrdinalIgnoreCase.Equals($parentFull, $candidateFull)) {
        return $true
    }

    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Find-UnityProjectRootForSourceRoot {
    param(
        [string]$GitRoot,
        [string]$AbsoluteSourceRoot
    )

    $gitRootFull = [System.IO.Path]::GetFullPath($GitRoot)
    $current = Get-Item -LiteralPath $AbsoluteSourceRoot

    while ($current -and (Test-IsPathWithin -ParentPath $gitRootFull -CandidatePath $current.FullName)) {
        $assetsPath = Join-Path $current.FullName 'Assets'
        $packagesPath = Join-Path $current.FullName 'Packages'
        $projectSettingsPath = Join-Path $current.FullName 'ProjectSettings'

        if ((Test-Path -LiteralPath $assetsPath -PathType Container) -and
            (Test-Path -LiteralPath $packagesPath -PathType Container) -and
            (Test-Path -LiteralPath $projectSettingsPath -PathType Container) -and
            (Test-IsPathWithin -ParentPath $assetsPath -CandidatePath $AbsoluteSourceRoot)) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    return $null
}

function Assert-SourceRootConstraint {
    param(
        $SelectedPlan,
        [string]$GitRoot,
        [string]$InitialSourceRoot
    )

    if (-not $SelectedPlan.PSObject.Properties['sourceRootConstraint']) {
        return
    }

    $constraint = [string]$SelectedPlan.sourceRootConstraint

    switch ($constraint) {
        'unity-assets' {
            $absoluteSourceRoot = Join-Path $GitRoot ($InitialSourceRoot -replace '/', [System.IO.Path]::DirectorySeparatorChar)
            $unityProjectRoot = Find-UnityProjectRootForSourceRoot -GitRoot $GitRoot -AbsoluteSourceRoot $absoluteSourceRoot

            if (-not $unityProjectRoot) {
                throw "Unity bootstrap requires the initial source root to be inside a Unity project's Assets tree. Selected: $InitialSourceRoot"
            }

            Write-Host "Unity root  : $unityProjectRoot"
        }

        default {
            throw "Unsupported sourceRootConstraint '$constraint' in plan '$($SelectedPlan.id)'."
        }
    }
}

function Get-ComponentRelativePath {
    param(
        [string]$InitialSourceRoot,
        $Component
    )

    return "$InitialSourceRoot/$($Component.id)"
}

function Test-RegisteredSubmodulePath {
    param(
        [string]$GitRoot,
        [string]$RelativePath
    )

    $gitmodules = Join-Path $GitRoot '.gitmodules'
    if (-not (Test-Path -LiteralPath $gitmodules)) {
        return $false
    }

    $lines = & git -C $GitRoot config -f .gitmodules --get-regexp '^submodule\..*\.path$' 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    foreach ($line in $lines) {
        $parts = $line -split '\s+', 2
        if ($parts.Count -eq 2 -and [System.StringComparer]::OrdinalIgnoreCase.Equals($parts[1].Trim(), $RelativePath)) {
            return $true
        }
    }

    return $false
}

function Get-SubmoduleGitDirectory {
    param(
        [string]$GitRoot,
        [string]$RelativePath
    )

    $gitPathOutput = & git -C $GitRoot rev-parse --git-path "modules/$RelativePath" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $gitPathOutput) {
        return $null
    }

    $gitPath = ($gitPathOutput | Select-Object -First 1).Trim()
    if ([System.IO.Path]::IsPathRooted($gitPath)) {
        return [System.IO.Path]::GetFullPath($gitPath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $GitRoot $gitPath))
}

function Get-GitDirectoryOrigin {
    param([string]$GitDirectory)

    if ([string]::IsNullOrWhiteSpace($GitDirectory) -or -not (Test-Path -LiteralPath $GitDirectory -PathType Container)) {
        return $null
    }

    $configPath = Join-Path $GitDirectory 'config'
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        return $null
    }

    # Read the orphaned repository config as a plain config file.
    # Do not use --git-dir here: Git would honor a stale core.worktree and can
    # fail before we get a chance to reactivate the cached submodule.
    $origin = & git config --file $configPath --get remote.origin.url 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $origin) {
        return $null
    }

    return ($origin | Select-Object -First 1).Trim()
}

function Normalize-GitRepositoryIdentity {
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return ''
    }

    $value = $Url.Trim()

    if ($value -match '^git@github\.com:(.+)$') {
        $value = "https://github.com/$($Matches[1])"
    }
    elseif ($value -match '^ssh://git@github\.com/(.+)$') {
        $value = "https://github.com/$($Matches[1])"
    }

    $value = $value.TrimEnd('/')
    if ($value.EndsWith('.git', [System.StringComparison]::OrdinalIgnoreCase)) {
        $value = $value.Substring(0, $value.Length - 4)
    }

    return $value
}

function Test-GitRepositoryIdentityMatch {
    param(
        [string]$Actual,
        [string]$Expected
    )

    $actualIdentity = Normalize-GitRepositoryIdentity -Url $Actual
    $expectedIdentity = Normalize-GitRepositoryIdentity -Url $Expected

    return [System.StringComparer]::OrdinalIgnoreCase.Equals(
        $actualIdentity,
        $expectedIdentity)
}

function Test-GitCommitExists {
    param(
        [string]$RepositoryPath,
        [string]$Revision
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryPath) -or
        [string]::IsNullOrWhiteSpace($Revision)) {
        return $false
    }

    # A missing revision is an expected probe result, not a bootstrap error.
    # Windows PowerShell can promote native stderr into a terminating error
    # while $ErrorActionPreference is Stop, so make the probe fully quiet.
    $previousErrorActionPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = 'SilentlyContinue'
        & git -C $RepositoryPath rev-parse --verify --quiet "$Revision^{commit}" 1>$null 2>$null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Install-Component {
    param(
        [string]$GitRoot,
        [string]$InitialSourceRoot,
        $Component
    )

    $relativePath = Get-ComponentRelativePath -InitialSourceRoot $InitialSourceRoot -Component $Component
    $absolutePath = Join-Path $GitRoot ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $registered = Test-RegisteredSubmodulePath -GitRoot $GitRoot -RelativePath $relativePath

    Write-Heading "Install $($Component.id)"
    Write-Host "Repository : $($Component.repository)"
    Write-Host "Revision   : $($Component.revision)"
    Write-Host "Path       : $relativePath"

    if (-not $registered) {
        if (Test-Path -LiteralPath $absolutePath) {
            throw "Target path already exists but is not a registered submodule: $absolutePath"
        }

        $cachedGitDirectory = Get-SubmoduleGitDirectory -GitRoot $GitRoot -RelativePath $relativePath
        $reuseCachedGitDirectory = $false

        if ($cachedGitDirectory -and (Test-Path -LiteralPath $cachedGitDirectory -PathType Container)) {
            $cachedOrigin = Get-GitDirectoryOrigin -GitDirectory $cachedGitDirectory

            if ([string]::IsNullOrWhiteSpace($cachedOrigin)) {
                throw "An orphaned submodule Git directory exists but has no origin: $cachedGitDirectory"
            }

            if (-not (Test-GitRepositoryIdentityMatch -Actual $cachedOrigin -Expected $Component.url)) {
                throw "An orphaned submodule Git directory exists for '$relativePath' but points to '$cachedOrigin', expected '$($Component.url)'. Remove or repair the cached Git directory before retrying: $cachedGitDirectory"
            }

            Write-Host "Reuse cache : $cachedGitDirectory"
            $reuseCachedGitDirectory = $true
        }

        $addArguments = @('-C', $GitRoot, 'submodule', 'add')
        if ($reuseCachedGitDirectory) {
            $addArguments += '--force'
        }
        $addArguments += @($Component.url, $relativePath)

        Invoke-CheckedCommand -Command 'git' -Arguments $addArguments
    }
    else {
        Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $GitRoot, 'submodule', 'sync', '--', $relativePath)
        Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $GitRoot, 'submodule', 'update', '--init', '--recursive', '--', $relativePath)

        $origin = (Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $absolutePath, 'remote', 'get-url', 'origin') -Capture | Select-Object -First 1).Trim()
        if (-not (Test-GitRepositoryIdentityMatch -Actual $origin -Expected $Component.url)) {
            throw "Registered submodule '$relativePath' points to '$origin', expected '$($Component.url)'."
        }
    }

    if (-not (Test-GitCommitExists -RepositoryPath $absolutePath -Revision $Component.revision)) {
        Write-Host "Fetch revision: $($Component.revision)"
        Invoke-CheckedCommand -Command 'git' -Arguments @(
            '-C',
            $absolutePath,
            'fetch',
            '--no-tags',
            'origin',
            $Component.revision
        )

        if (-not (Test-GitCommitExists -RepositoryPath $absolutePath -Revision $Component.revision)) {
            throw "Fetched revision is still unavailable for '$($Component.id)': $($Component.revision)"
        }
    }

    Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $absolutePath, 'checkout', '--detach', $Component.revision)

    $actualRevision = (Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $absolutePath, 'rev-parse', 'HEAD') -Capture | Select-Object -First 1).Trim()
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals($actualRevision, $Component.revision)) {
        throw "Component '$($Component.id)' checkout mismatch. Expected $($Component.revision), got $actualRevision."
    }

    Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $GitRoot, 'add', '.gitmodules', $relativePath)
}

function Show-ResolvedLayout {
    param(
        [string]$GitRoot,
        $SelectedPlan,
        [string]$InitialSourceRoot
    )

    Write-Heading 'Initial installation layout'
    Write-Host "Git root    : $GitRoot"
    Write-Host "Plan        : $($SelectedPlan.displayName) ($($SelectedPlan.id))"
    Write-Host "Source root : $InitialSourceRoot"
    Write-Host ''

    foreach ($component in $SelectedPlan.components) {
        $path = Get-ComponentRelativePath -InitialSourceRoot $InitialSourceRoot -Component $component
        Write-Host "  $path"
        Write-Host "    <- $($component.repository) @ $($component.revision.Substring(0, 12))"
    }

    if ($SelectedPlan.plannedComponents -and $SelectedPlan.plannedComponents.Count -gt 0) {
        Write-Host ''
        foreach ($planned in $SelectedPlan.plannedComponents) {
            Write-Host "  planned: $($planned.id)"
        }
    }
}

function Write-HandoffPlan {
    param(
        [string]$GitRoot,
        $Catalog,
        $SelectedPlan,
        [string]$InitialSourceRoot
    )

    $targetDirectory = Join-Path $GitRoot 'PackageManagement'
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    $targetPath = Join-Path $targetDirectory 'bootstrap.plan.json'

    $components = @()
    foreach ($component in $SelectedPlan.components) {
        $components += [ordered]@{
            id = $component.id
            repository = $component.repository
            revision = $component.revision
            path = Get-ComponentRelativePath -InitialSourceRoot $InitialSourceRoot -Component $component
        }
    }

    $handoff = [ordered]@{
        schemaVersion = 2
        bootstrapper = 'Tool.PackageManager'
        catalogVersion = $Catalog.catalogVersion
        planId = $SelectedPlan.id
        initialSourceRoot = $InitialSourceRoot
        components = $components
    }

    $json = $handoff | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $targetPath -Value $json -Encoding UTF8
    Invoke-CheckedCommand -Command 'git' -Arguments @('-C', $GitRoot, 'add', 'PackageManagement/bootstrap.plan.json')
}

try {
    $catalog = Read-BootstrapCatalog

    if ($ListPlans) {
        Show-Plans -Catalog $catalog
        exit 0
    }

    Assert-Prerequisites

    $gitRoot = Resolve-TargetGitRoot -ExplicitProjectPath $ProjectPath
    $selectedPlan = Select-BootstrapPlan -Catalog $catalog -RequestedPlan $Plan
    Assert-Plan -SelectedPlan $selectedPlan

    $initialSourceRoot = Resolve-InitialSourceRoot -GitRoot $gitRoot -RequestedSourceRoot $SourceRoot
    Assert-SourceRootConstraint -SelectedPlan $selectedPlan -GitRoot $gitRoot -InitialSourceRoot $initialSourceRoot

    Show-ResolvedLayout -GitRoot $gitRoot -SelectedPlan $selectedPlan -InitialSourceRoot $initialSourceRoot

    if (-not $NonInteractive) {
        $confirmation = Read-Host 'Install this initial layout? [Y/n]'
        if (-not [string]::IsNullOrWhiteSpace($confirmation) -and $confirmation -notmatch '^[Yy]') {
            Write-Host 'Cancelled.'
            exit 0
        }
    }

    foreach ($component in $selectedPlan.components) {
        Install-Component -GitRoot $gitRoot -InitialSourceRoot $initialSourceRoot -Component $component
    }

    Write-HandoffPlan -GitRoot $gitRoot -Catalog $catalog -SelectedPlan $selectedPlan -InitialSourceRoot $initialSourceRoot

    Write-Heading 'Bootstrap complete'
    Write-Host 'The initial Package Management source layout is now present in the target project.'
    Write-Host 'Future source relocation/reorganization belongs to Framework.PackageManagement, not this bootstrapper.'
    Write-Host ''

    & git -C $gitRoot status --short
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
