# Compiles PaperDoll.FNA's effects (*.fx in this folder) to FNA's effect format (fx_2_0) with fxc.
# Run it after changing a shader and commit the .fxb and .fx.sha256 files with the source: building PaperDoll never
# compiles shaders, so no shader compiler is needed to build it.
#
# fxc is found through the FXC environment variable, or else as the newest Windows SDK's x64 fxc.exe. It is a Windows
# tool; on other systems point FXC at a wrapper that runs it under Wine. The .fx.sha256 file records the source each
# .fxb was compiled from (line endings normalised to LF), so a test can tell when the .fxb is out of date.

$ErrorActionPreference = 'Stop'

$fxc = $env:FXC
if (-not $fxc) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $fxc = Get-ChildItem -Path $kits -Filter fxc.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $fxc -or -not (Test-Path $fxc)) {
    throw 'fxc not found: install the Windows SDK or set FXC to fxc.exe (or a wrapper that runs it).'
}

Write-Host "fxc: $fxc"
foreach ($source in Get-ChildItem -Path $PSScriptRoot -Filter *.fx) {
    $output = [IO.Path]::ChangeExtension($source.FullName, '.fxb')
    # D3DCompiler_47 warns that effects are deprecated (X4717); fx_2_0 is still what FNA loads.
    & $fxc /nologo /T fx_2_0 /O3 /Fo $output $source.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "fxc failed on $($source.Name)"
    }

    $text = [IO.File]::ReadAllText($source.FullName).Replace("`r`n", "`n")
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
    Set-Content -Path "$($source.FullName).sha256" -Value $hash.ToLowerInvariant() -NoNewline -Encoding ascii
    Write-Host "$($source.Name) -> $([IO.Path]::GetFileName($output))"
}
