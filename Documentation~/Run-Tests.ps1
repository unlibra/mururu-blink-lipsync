#requires -Version 7.0
param(
    [string]$ProjectPath,
    [ValidateSet('Assets','Packages')][string]$Placement = 'Assets',
    [string]$TestFilter = 'MururuBlinkLipSync'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $ProjectPath) { $ProjectPath = Join-Path (Split-Path $repo -Parent) 'mururu-blink-lipsync-tests' }
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if ((Get-Content -LiteralPath "$project/.mbls-test-project" -Raw).Trim() -ne 'mururu-blink-lipsync private test environment') {
    throw 'This is not the dedicated private test project. See maintenance/TEST_ENVIRONMENT.md.'
}
$versions = Get-Content -LiteralPath "$PSScriptRoot/maintenance/test-environment-packages.json" -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath "$project/Packages/manifest.json" -Raw | ConvertFrom-Json
if (@($manifest.dependencies.PSObject.Properties).Count -ne @($versions.packages.PSObject.Properties).Count) { throw 'Package inventory differs from the tested environment.' }
if ((Get-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Raw) -notmatch
    ('(?m)^m_EditorVersion: ' + [regex]::Escape($versions.unityEditor) + '\s*$')) { throw 'Unity version differs from the tested environment.' }
foreach ($dependency in $versions.packages.PSObject.Properties) {
    if ($manifest.dependencies.($dependency.Name) -ne "file:$($dependency.Name)") { throw "Package must be local to this test project: $($dependency.Name)" }
    $package = Get-Content -LiteralPath "$project/Packages/$($dependency.Name)/package.json" -Raw | ConvertFrom-Json
    if ($package.version -ne $dependency.Value) { throw "Package version differs: $($dependency.Name)" }
}
$required = @('Lethal Cats/Mururu/Mururu.prefab', 'Lethal Cats/Mururu/FBX/Mururu.fbx',
    'AxisRepairProbe/units-only.fbx', 'AxisRepairProbe/rotation-repaired.fbx',
    'CustomMouthProbe/Custom.fbx', 'CustomMouthProbe/Merged.asset',
    'PackageMouthProbe/Temp.fbx', 'PackageMouthProbe/TempSdk.asset', 'PackageMouthProbe/TempFX.controller',
    'MBLSFaceEmoDiagnostic/FaceEmo_FX.controller')
foreach ($asset in $required) {
    if (-not (Test-Path -LiteralPath "$project/Assets/$asset" -PathType Leaf)) { throw "Private fixture missing: $asset" }
}
$target = [IO.Path]::GetFullPath("$project/Assets/Addon")
if ($target.StartsWith([IO.Path]::GetFullPath($repo) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The test copy must be outside the source repository.'
}
$files = foreach ($folder in @('Editor','Runtime','Tests')) {
    Get-Item -LiteralPath "$repo/$folder.meta"
    Get-ChildItem -LiteralPath "$repo/$folder" -Recurse -File
}
$files += @('README.md','README.md.meta','CHANGELOG.md','CHANGELOG.md.meta','LICENSE.txt','LICENSE.txt.meta','NOTICE.txt','NOTICE.txt.meta') | ForEach-Object { Get-Item -LiteralPath "$repo/$_" }
if ($Placement -eq 'Packages') { $files += Get-Item -LiteralPath "$repo/package.json" }
$originalManifest = [IO.File]::ReadAllBytes("$project/Packages/manifest.json")
$backup = Join-Path $project ('.mbls-addon-backup-' + [Guid]::NewGuid().ToString('N'))
$assetTarget = $target
$packageTarget = [IO.Path]::GetFullPath("$project/Packages/com.unlibra.mururu-blink-lipsync")
$movedAddon = $false
$movedAddonMeta = $false
$createdPackage = $false
try {
if ($Placement -eq 'Packages') {
    if (Test-Path -LiteralPath $packageTarget) { throw 'Package test target already exists; inspect it instead of replacing it.' }
    if ($assetTarget -ne [IO.Path]::GetFullPath("$project/Assets/Addon") -or
        $packageTarget -ne [IO.Path]::GetFullPath("$project/Packages/com.unlibra.mururu-blink-lipsync") -or
        -not $backup.StartsWith($project + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test layout paths.' }
    if (Test-Path -LiteralPath $assetTarget) {
        Move-Item -LiteralPath $assetTarget -Destination $backup
        $movedAddon = $true
    }
    if (Test-Path -LiteralPath "$assetTarget.meta") {
        Move-Item -LiteralPath "$assetTarget.meta" -Destination "$backup.meta"
        $movedAddonMeta = $true
    }
    $target = $packageTarget
    New-Item -ItemType Directory -Path $target | Out-Null
    $createdPackage = $true
    $manifest.dependencies | Add-Member -NotePropertyName 'com.unlibra.mururu-blink-lipsync' -NotePropertyValue 'file:com.unlibra.mururu-blink-lipsync'
    $testables = @($manifest.testables | Where-Object { $_ }) + @('com.unlibra.mururu-blink-lipsync')
    $manifest | Add-Member -NotePropertyName testables -NotePropertyValue $testables -Force
    [IO.File]::WriteAllText("$project/Packages/manifest.json", ($manifest | ConvertTo-Json -Depth 20))
}
$records = @{}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($repo, $file.FullName)
    $records[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash
}
# Reject stale copies instead of silently running extra or obsolete test code.
foreach ($folder in @('Editor','Runtime','Tests')) {
    if (-not (Test-Path -LiteralPath "$target/$folder")) { continue }
    foreach ($file in Get-ChildItem -LiteralPath "$target/$folder" -Recurse -File -ErrorAction Stop) {
        $relative = [IO.Path]::GetRelativePath($target, $file.FullName)
        if (-not $records.ContainsKey($relative)) { throw "Stale file in test copy; inspect before removing: $relative" }
    }
}
foreach ($file in $files) {
    $destination = Join-Path $target ([IO.Path]::GetRelativePath($repo, $file.FullName))
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
function Confirm-SourceCopy {
    foreach ($relative in $records.Keys) {
        if ((Get-FileHash -LiteralPath "$repo/$relative").Hash -ne $records[$relative] -or
            (Get-FileHash -LiteralPath "$target/$relative").Hash -ne $records[$relative]) { throw "Source changed or test copy differs: $relative" }
    }
}
Confirm-SourceCopy
$output = Join-Path $repo ('Library/MBLS-Tests/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Placement)
New-Item -ItemType Directory -Path $output -Force | Out-Null
& unity test $project --mode EditMode --filter $TestFilter --output "$output/results.xml" -- -nographics -logFile "$output/editor.log"
if ($LASTEXITCODE -ne 0) { throw "Unity CLI failed ($LASTEXITCODE). See $output" }
Confirm-SourceCopy
[xml]$report = Get-Content -LiteralPath "$output/results.xml" -Raw
$cases = @($report.SelectNodes('//test-case'))
if ($cases.Count -eq 0 -or @($cases | Where-Object result -ne 'Passed').Count -gt 0) {
    throw "Some tests failed or were skipped; this is not a full pass. See $output/results.xml"
}
Write-Output "Passed $($cases.Count)/$($cases.Count), no skipped tests. Source hashes match. Results: $output/results.xml"

} finally {
    if ($Placement -eq 'Packages') {
        if ($createdPackage) {
            $resolvedTarget = (Resolve-Path -LiteralPath $packageTarget).Path
            if ($resolvedTarget -ne [IO.Path]::GetFullPath("$project/Packages/com.unlibra.mururu-blink-lipsync")) { throw 'Unsafe package test cleanup target.' }
            Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
        }
        [IO.File]::WriteAllBytes("$project/Packages/manifest.json", $originalManifest)
        if ($movedAddon) { Move-Item -LiteralPath $backup -Destination $assetTarget }
        if ($movedAddonMeta) { Move-Item -LiteralPath "$backup.meta" -Destination "$assetTarget.meta" }
    }
}
