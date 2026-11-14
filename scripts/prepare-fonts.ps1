param(
    [string]$Destination = "src/Yetta.Desktop/Assets/Inter.ttf"
)
$ErrorActionPreference = "Stop"
# Official Inter font, redistributed under the bundled SIL OFL 1.1 license.
# The Git blob SHA-1 is pinned to prevent the upstream branch from changing
# the expected font without a corresponding source review.
$expectedGitBlob = "047c92f6e2212473dc436020afed689527076d44"
$url = "https://raw.githubusercontent.com/google/fonts/main/ofl/inter/Inter%5Bopsz%2Cwght%5D.ttf"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
if (!(Test-Path $Destination)) {
    Invoke-WebRequest -Uri $url -OutFile $Destination -MaximumRedirection 5
}
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Destination))
$prefix = [System.Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)" + [char]0)
$toHash = New-Object byte[] ($prefix.Length + $bytes.Length)
[Buffer]::BlockCopy($prefix, 0, $toHash, 0, $prefix.Length)
[Buffer]::BlockCopy($bytes, 0, $toHash, $prefix.Length, $bytes.Length)
$sha1 = [System.Security.Cryptography.SHA1]::Create()
try { $actual = ([BitConverter]::ToString($sha1.ComputeHash($toHash))).Replace("-", "").ToLowerInvariant() }
finally { $sha1.Dispose() }
if ($actual -ne $expectedGitBlob) {
    Remove-Item -Force $Destination
    throw "Inter font hash mismatch: got $actual; expected $expectedGitBlob"
}
Write-Host "Verified Inter font (SIL OFL 1.1): $actual"
