[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ModuleProject, [string]$OutputRoot = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RelativeRoot = Join-Path $PSScriptRoot '..'
$Root = [IO.Path]::GetFullPath($RelativeRoot)
$Framework = Join-Path $Root 'src\Framework.PackageManagement\Framework.PackageManagement.csproj'
$Module = [IO.Path]::GetFullPath($ModuleProject)
if (-not [IO.File]::Exists($Module)) { throw "Module project does not exist: $Module" }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $Root 'obj\BreakingApi.Contracts' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$OutputId = [guid]::NewGuid().ToString('N')
$Output = Join-Path $OutputRoot $OutputId
[IO.Directory]::CreateDirectory($Output) | Out-Null
$Project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$Framework" AdditionalProperties="PackageManagementModuleProject=$Module" /></ItemGroup>
</Project>
"@
$NegativeProject = Join-Path $Output 'Negative.csproj'
$LegacyCaller = Join-Path $Output 'LegacyCaller.cs'
[IO.File]::WriteAllText($NegativeProject, $Project)
$AdoptionsSource = @"
using CraftyRacoon.PackageManagement.Framework.Development;
internal sealed class LegacyCaller
{
    internal object ReadRemovedAdoptions(UseExistingPackageSetRequest value) { return value.Adoptions; }
}
"@
[IO.File]::WriteAllText($LegacyCaller, $AdoptionsSource)
$AdoptionsResult = & dotnet build $NegativeProject --nologo 2>&1
$AdoptionsExitCode = $LASTEXITCODE
$AdoptionsText = @($AdoptionsResult) -join "`n"
if ($AdoptionsExitCode -eq 0 -or -not $AdoptionsText.Contains('Adoptions')) { throw "FAILED: removed Existing Adoptions unexpectedly compiled or was not diagnosed.`n$AdoptionsText" }
$ModesSource = @"
using CraftyRacoon.PackageManagement.Framework.Development;
internal sealed class LegacyCaller
{
    private DevelopmentInitializationMode mode;
    private DevelopmentInitializationRequest? request;
}
"@
[IO.File]::WriteAllText($LegacyCaller, $ModesSource)
$ModesResult = & dotnet build $NegativeProject --nologo --no-restore 2>&1
$ModesExitCode = $LASTEXITCODE
$ModesText = @($ModesResult) -join "`n"
if ($ModesExitCode -eq 0) { throw 'FAILED: removed V1 modes and requests unexpectedly compiled.' }
foreach ($Symbol in @('DevelopmentInitializationMode','DevelopmentInitializationRequest')) {
    if (-not $ModesText.Contains($Symbol)) { throw "FAILED: compiler did not reject removed API symbol $Symbol.`n$ModesText" }
}
Write-Host 'PASS: removed V1 modes, requests and Existing adoptions fail isolated compilation'
