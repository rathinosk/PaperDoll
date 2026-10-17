# Compiles PaperDoll's effect (src/PaperDoll.FNA/Shaders/PaperDoll.fx, shared with PaperDoll.FNA) to MonoGame's MGFX
# format, once per backend: PaperDoll.ogl.mgfxo (DesktopGL) and PaperDoll.dx11.mgfxo (WindowsDX).
# Run it after changing the shader and commit the .mgfxo and .fx.sha256 files with the source: building PaperDoll never
# compiles shaders, so no shader compiler is needed to build it.
#
# mgfxc is the dotnet-mgfxc tool pinned in dotnet-tools.json at the repository root (restored by `dotnet tool restore`).
# It compiles HLSL with Windows' D3DCompiler, so it runs on Windows. The .fx.sha256 file records the source the effects
# were compiled from (line endings normalised to LF), so a test can tell when they are out of date.

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$source = Join-Path $root 'src\PaperDoll.FNA\Shaders\PaperDoll.fx'

Push-Location $root
try {
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet tool restore failed'
    }

    foreach ($backend in @(@{ Profile = 'OpenGL'; Suffix = 'ogl' }, @{ Profile = 'DirectX_11'; Suffix = 'dx11' })) {
        $output = Join-Path $PSScriptRoot "PaperDoll.$($backend.Suffix).mgfxo"
        dotnet mgfxc $source $output "/Profile:$($backend.Profile)"
        if ($LASTEXITCODE -ne 0) {
            throw "mgfxc failed for $($backend.Profile)"
        }
    }
}
finally {
    Pop-Location
}

$text = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
Set-Content -Path (Join-Path $PSScriptRoot 'PaperDoll.fx.sha256') -Value $hash.ToLowerInvariant() -NoNewline -Encoding ascii
Write-Host 'PaperDoll.fx -> PaperDoll.ogl.mgfxo, PaperDoll.dx11.mgfxo'
