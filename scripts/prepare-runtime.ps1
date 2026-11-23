$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $root 'runtime.lock.json') -Raw | ConvertFrom-Json
if ($lock.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Runtime migration is awaiting publication and a pinned SHA-256.' }
if ($lock.repository -ne 'Ketsuneban/Yetta' -or $lock.tag -notmatch '^runtime-[0-9.]+-[0-9]+$' -or $lock.asset -ne 'YettaRuntime-win-x64.zip') { throw 'Invalid runtime coordinates.' }
$zip = Join-Path $env:TEMP ('Yetta-runtime-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
  Invoke-WebRequest "https://github.com/$($lock.repository)/releases/download/$($lock.tag)/$($lock.asset)" -OutFile $zip
  if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $lock.sha256) { throw 'Runtime checksum mismatch.' }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $archive = [IO.Compression.ZipFile]::OpenRead($zip)
  try {
    foreach ($entry in $archive.Entries) {
      if ($entry.FullName -in @('runtime/','tray/','setup/')) { continue }
      if ($entry.FullName -notmatch '^(runtime/(Yetta[.]Runtime|Yetta[.]Runtime[.]Windows)[.]dll|tray/Yetta[.]DAX[.]exe|setup/YettaDaxAdministratorSetup[.]exe|manifest[.]json|LICENSE[.]imported[.]txt)$') { throw "Unexpected runtime entry: $($entry.FullName)" }
    }
  } finally { $archive.Dispose() }
  $artifacts = Join-Path $root 'artifacts'
  Expand-Archive $zip -DestinationPath $artifacts -Force
  foreach ($file in @('runtime/Yetta.Runtime.dll','runtime/Yetta.Runtime.Windows.dll','tray/Yetta.DAX.exe','setup/YettaDaxAdministratorSetup.exe')) {
    if (!(Test-Path (Join-Path $artifacts $file))) { throw "Runtime file missing: $file" }
  }
} finally { Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue }
