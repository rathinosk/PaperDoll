<#
.SYNOPSIS
    Publishes PaperDoll's release binaries and archives them with their licence notices.

.DESCRIPTION
    Builds in Release and archives the result with PaperDoll's LICENSE.md and attribution.md, the licence texts of
    the bundled code in lib/, and the licence of every NuGet package the builds used (its licence file, or its
    licence expression or URL when the package has none). The archive is <name>-<version>-<runtime>.zip, or .tar.gz
    on Linux and macOS so the executable keeps its permissions.

    Kinds:
      demo   PaperDoll-Demo, Windows x64 only: the FNA demo (NativeAOT; needs the MSVC linker) and the MonoGame demo
             on DesktopGL and on WindowsDX (self-contained), each in its own folder, with samples/release-readme.md
             as README.md. Binaries only; no puppets.
      tools  paperdoll, the command-line tool: self-contained, trimmed single file, for any runtime.

.PARAMETER Kind
    What to build: demo or tools.

.PARAMETER Runtime
    The runtime identifier. Default win-x64, the only one the demo supports.

.PARAMETER Output
    The folder the archive is written to. Default: artifacts/release.

.EXAMPLE
    pwsh .github/scripts/package-release.ps1 -Kind demo
    pwsh .github/scripts/package-release.ps1 -Kind tools -Runtime linux-x64 -Output out
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('demo', 'tools')][string]$Kind,
    [string]$Runtime = 'win-x64',
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $Output) { $Output = Join-Path $root 'artifacts/release' }
$Output = [IO.Path]::GetFullPath($Output)
if ($Kind -eq 'demo' -and $Runtime -ne 'win-x64') { throw 'The demo is built for win-x64 only.' }

# Each build: the project, the folder inside the archive ('' for the top), its publish arguments, and its bundled lib/ code.
$imgui = 'lib/ImGui.NET/*LICENSE*'
$fna = 'lib/FNA/licenses/*', 'lib/FNA/lib/*/LICENSE', 'lib/FNA/lib/*/csharp/LICENSE', 'lib/FNA/src/Graphics/Effect/StockEffects/LICENSE', 'lib/fnalibs/licenses/*'
$builds = switch ($Kind) {
    'demo' {
        @{ Project = 'samples/PaperDoll.Demo';          Folder = 'FNA';                Args = @('-p:PublishAot=true');                               Bundled = $fna + $imgui }
        @{ Project = 'samples/PaperDoll.Demo.MonoGame'; Folder = 'MonoGame-DesktopGL'; Args = @('--self-contained', '-p:MonoGameBackend=DesktopGL'); Bundled = @($imgui) }
        @{ Project = 'samples/PaperDoll.Demo.MonoGame'; Folder = 'MonoGame-WindowsDX'; Args = @('--self-contained', '-p:MonoGameBackend=WindowsDX'); Bundled = @($imgui) }
    }
    'tools' {
        @{ Project = 'src/PaperDoll.Tools'; Folder = ''; Args = @('--self-contained', '-p:PublishSingleFile=true', '-p:PublishTrimmed=true'); Bundled = @() }
    }
}
$archiveName = @{ demo = 'PaperDoll-Demo'; tools = 'paperdoll' }[$Kind]

$version = (& dotnet msbuild (Join-Path $root 'src/PaperDoll.Core') -getProperty:Version).Trim()
$name = "$archiveName-$version-$Runtime"
$stage = Join-Path $Output $name
$licenses = Join-Path $stage 'licenses'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

# Runtime packs are download dependencies; the ASP.NET Core and Windows Desktop ones are fetched for the runtime
# identifier but never shipped, and ILLink is a build task.
$unshipped = '^(Microsoft\.AspNetCore\.App\.|Microsoft\.WindowsDesktop\.App\.|Microsoft\.NET\.ILLink\.Tasks$)'
$packages = [Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$packageFolders = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($build in $builds) {
    $project = Join-Path $root $build.Project
    $to = Join-Path $stage $build.Folder
    Write-Host "Publishing $($build.Project) $($build.Args -join ' ') ($Runtime, $version)"
    & dotnet publish $project -c Release -r $Runtime -p:ContinuousIntegrationBuild=true -p:DebugType=none -p:GenerateDocumentationFile=false @($build.Args) -o $to
    # DebugType=none leaves NativeAOT's native symbols; releases ship none.
    Get-ChildItem $to -Recurse -Filter *.pdb | Remove-Item

    # Licence texts of the bundled code in lib/, under licenses/ with their lib/ paths.
    foreach ($pattern in $build.Bundled) {
        foreach ($file in Get-ChildItem (Join-Path $root $pattern) -File) {
            $target = Join-Path $licenses ([IO.Path]::GetRelativePath($root, $file.FullName))
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item $file.FullName $target
        }
    }

    # The NuGet packages this publish restored (the .NET runtime packs included), read now: the next build of the
    # same project rewrites its assets file.
    $assets = Get-Content (Join-Path $project 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    $found = @{}
    foreach ($key in $assets.libraries.Keys) {
        if ($assets.libraries[$key].type -eq 'package') { $id, $ver = $key -split '/', 2; $found[$id] = $ver }
    }
    foreach ($framework in $assets.project.frameworks.Values) {
        foreach ($download in @($framework.downloadDependencies)) {
            if ($download) { $found[$download.name] = ($download.version -replace '[\[\]\s]', '' -split ',')[0] }
        }
    }
    foreach ($id in $found.Keys) {
        if ($id -match $unshipped) { continue }
        $ver = $found[$id]
        $folder = @($assets.packageFolders.Keys) | ForEach-Object { Join-Path $_ "$($id.ToLowerInvariant())/$($ver.ToLowerInvariant())" } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $folder) { throw "Package $id $ver is not in the packages folder." }
        $packages["$id $ver"] = $id
        $packageFolders["$id $ver"] = $folder
    }
}

$summary = [Text.StringBuilder]::new()
[void]$summary.AppendLine('# NuGet packages used by these binaries').AppendLine()
[void]$summary.AppendLine('Each package''s licence file, when it has one, is in nuget/ under the package''s name. LICENSE.md lists the components these binaries bundle.').AppendLine()
[void]$summary.AppendLine('| Package | Version | Licence |').AppendLine('| --- | --- | --- |')
foreach ($key in $packages.Keys) {
    $id = $packages[$key]
    $ver = $key.Substring($id.Length + 1)
    $folder = $packageFolders[$key]

    $licence = ''
    $nuspec = Get-ChildItem $folder -Filter *.nuspec | Select-Object -First 1
    if ($nuspec) {
        $metadata = ([xml](Get-Content $nuspec.FullName -Raw)).package.metadata
        if ($metadata.license -and $metadata.license.type -eq 'expression') { $licence = $metadata.license.'#text' }
        elseif ($metadata.license) { $licence = "file: $($metadata.license.'#text')" }
        elseif ($metadata.licenseUrl) { $licence = $metadata.licenseUrl }
        else { $licence = "not declared by the package; see LICENSE.md and $($metadata.projectUrl)" }
    }
    [void]$summary.AppendLine("| $id | $ver | $licence |")

    $texts = Get-ChildItem $folder -File | Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|THIRD-PARTY-NOTICES|NOTICE)' }
    if ($texts) {
        $target = Join-Path $licenses "nuget/$id"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $texts | Copy-Item -Destination $target -Force
    }
}
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Set-Content -Path (Join-Path $licenses 'nuget-packages.md') -Value $summary.ToString() -NoNewline

# PaperDoll's own licence (with the upstream notices and the third-party table), the attribution summary, and the
# demo's README.
Copy-Item (Join-Path $root 'LICENSE.md') $stage
Copy-Item (Join-Path $root 'docs/attribution.md') $stage
if ($Kind -eq 'demo') { Copy-Item (Join-Path $root 'samples/release-readme.md') (Join-Path $stage 'README.md') }

New-Item -ItemType Directory -Path $Output -Force | Out-Null
if ($Runtime -like 'win-*') {
    $archive = Join-Path $Output "$name.zip"
    if (Test-Path $archive) { Remove-Item $archive }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive
} else {
    $archive = Join-Path $Output "$name.tar.gz"
    & tar -czf $archive -C $stage .
}
Remove-Item -Recurse -Force $stage
Write-Host "Wrote $archive"
