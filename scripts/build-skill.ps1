#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}

$root = Split-Path $PSScriptRoot -Parent
$modSource = Join-Path $root 'RimSearcher_DataMod'
$skill = Join-Path $root 'skills/rimsearcher'
$cliProject = Join-Path $root 'Sources/RimSearcher.Cli/RimSearcher.Cli.csproj'
$modProject = Join-Path $root 'Sources/RimSearcher.DataMod/RimSearcher.DataMod.csproj'
$releaseDir = Join-Path $root '.release'

# 1. Version alignment check
$version = ([xml](Get-Content $cliProject -Raw)).Project.PropertyGroup.Version
if ($version -ne ([xml](Get-Content $modProject -Raw)).Project.PropertyGroup.Version -or
    $version -ne ([xml](Get-Content "$modSource/About/About.xml" -Raw)).ModMetaData.modVersion) {
    throw 'CLI, DataMod and About.xml versions must match.'
}

# Frozen 1.6 DataMod is the released 3.2.0 build: checked and packaged as-is, never rebuilt
if ([Reflection.AssemblyName]::GetAssemblyName("$modSource/1.6/Assemblies/RimSearcher.DataMod.dll").Version.ToString(3) -ne '3.2.0') {
    throw 'Frozen 1.6 DataMod assembly must be version 3.2.0.'
}

$work = Join-Path $releaseDir "build-skill-$([guid]::NewGuid().ToString('N'))"
New-Item $work -ItemType Directory -Force | Out-Null
Push-Location $root
try {
    # 2. Build CLI & DataMod in isolated work directory
    Invoke-Dotnet publish $cliProject -c Release -r win-x64 -o "$work/cli"
    Invoke-Dotnet build $modProject -c Release '-t:Rebuild' -o "$work/mod/1.7/Assemblies" "-p:NativeOutputPath=$work/mod/Native"

    # 3. Sanity verification
    $cli = "$work/cli/rimsearcher.exe"
    $builtVersion = & $cli --version
    if ($LASTEXITCODE -ne 0 -or $builtVersion.Trim() -ne $version) { throw 'CLI startup or version check failed.' }
    if ([Reflection.AssemblyName]::GetAssemblyName("$work/mod/1.7/Assemblies/RimSearcher.DataMod.dll").Version.ToString(3) -ne $version) {
        throw 'Built DataMod version does not match CLI.'
    }

    $native = @('e_sqlite3.dll', 'libe_sqlite3.so', 'libe_sqlite3.dylib', 'libe_sqlite3.arm64.dylib')
    $managed = @(Get-ChildItem "$work/mod/1.7/Assemblies" -File -Filter '*.dll')
    $nativeFiles = @($native | ForEach-Object { Get-Item "$work/mod/Native/$_" })
    $translations = @(Get-ChildItem "$modSource/Languages" -Recurse -File -Filter '*.xml')
    if ($translations.Count -eq 0) { throw 'DataMod translations are missing.' }

    # 4. Refresh mod 1.7 Assemblies & shared Native
    foreach ($dir in @('1.7/Assemblies', 'Native')) {
        if (Test-Path "$modSource/$dir") { Remove-Item "$modSource/$dir" -Recurse -Force }
        New-Item "$modSource/$dir" -ItemType Directory | Out-Null
    }
    $managed | Copy-Item -Destination "$modSource/1.7/Assemblies"
    $nativeFiles | Copy-Item -Destination "$modSource/Native"

    # 5. Package DataMod ZIP (excludes databases, PDBs, game assemblies)
    New-Item "$skill/bin", "$skill/assets", $releaseDir -ItemType Directory -Force | Out-Null
    $modZip = "$skill/assets/RimSearcher_DataMod.zip"
    if (Test-Path $modZip) { Remove-Item $modZip -Force }

    $files = @(
        Get-Item "$modSource/About/About.xml"
        Get-Item "$modSource/LoadFolders.xml"
        Get-ChildItem "$modSource/1.6/Assemblies" -File -Filter '*.dll'
        Get-ChildItem "$modSource/1.7/Assemblies" -File -Filter '*.dll'
        $native | ForEach-Object { Get-Item "$modSource/Native/$_" }
        $translations
    )
    $archive = [IO.Compression.ZipFile]::Open($modZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($modSource, $file.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "RimSearcher_DataMod/$relative") | Out-Null
        }
    }
    finally { $archive.Dispose() }

    # 6. Update CLI in skill
    Copy-Item $cli -Destination "$skill/bin/rimsearcher.exe" -Force

    # 7. Package complete release skill archive
    $releaseZip = "$releaseDir/rimsearcher.zip"
    if (Test-Path $releaseZip) { Remove-Item $releaseZip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($skill, $releaseZip, [IO.Compression.CompressionLevel]::Optimal, $true)

    Write-Host "Built RimSearcher ${version}: $skill"
    Write-Host "Release Skill archive: $releaseZip"
}
finally {
    Pop-Location
    Remove-Item $work -Recurse -Force
}
