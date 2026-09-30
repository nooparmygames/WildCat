# Copies the shared wire protocol into the Unity SDK. The Windows twin of tools/sync-protocol.sh;
# both write exactly the same files.
#
# Source: backend/WildCat.Protocol/**/*.cs, except ServerOnly/ (messages that carry a developer
# secret and must never ship in a game client) and the obj/ and bin/ build folders.
# Target: unity-sdk/Assets/Backend/Plugins/Protocol/, which is deleted and written again, so files
# removed from the source disappear from the copy too.
#
# Unity needs a .meta file with a GUID next to every file and folder. The GUIDs are the MD5 of
# "wildcat-protocol:" plus the path relative to the target folder ("" for the folder itself), so
# they never change between runs or machines and running the script twice changes nothing.
#
# Usage, from anywhere: pwsh tools/sync-protocol.ps1 (Windows PowerShell 5.1 works too)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'backend/WildCat.Protocol'
$targetDir = Join-Path $repoRoot 'unity-sdk/Assets/Backend/Plugins/Protocol'
$readme = Join-Path $repoRoot 'tools/protocol-copy-readme.md'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$md5 = [System.Security.Cryptography.MD5]::Create()

function Get-Guid([string] $relativePath) {
    $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("wildcat-protocol:$relativePath"))
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

# Written with "`n" line endings and no BOM, like the bash script.
function Write-Meta([string] $path, [string] $relativePath, [string] $importer) {
    $guid = Get-Guid $relativePath
    switch ($importer) {
        'script' {
            $lines = @('fileFormatVersion: 2', "guid: $guid", 'MonoImporter:', '  externalObjects: {}',
                '  serializedVersion: 2', '  defaultReferences: []', '  executionOrder: 0',
                '  icon: {instanceID: 0}', '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ')
        }
        'text' {
            $lines = @('fileFormatVersion: 2', "guid: $guid", 'TextScriptImporter:', '  externalObjects: {}',
                '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ')
        }
        'folder' {
            $lines = @('fileFormatVersion: 2', "guid: $guid", 'folderAsset: yes', 'DefaultImporter:',
                '  externalObjects: {}', '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ')
        }
    }
    [System.IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"), $utf8NoBom)
}

if (Test-Path $targetDir) {
    Remove-Item -Recurse -Force $targetDir
}
New-Item -ItemType Directory -Path $targetDir | Out-Null
Write-Meta "$targetDir.meta" '' 'folder'

$sourceFull = (Resolve-Path $sourceDir).Path.TrimEnd('\', '/')
$files = Get-ChildItem -Path $sourceFull -Recurse -File -Filter '*.cs' | Where-Object { $_.Extension -eq '.cs' } | ForEach-Object {
    $_.FullName.Substring($sourceFull.Length + 1).Replace('\', '/')
} | Where-Object { $_ -notmatch '^(ServerOnly|obj|bin)/' } | Sort-Object -CaseSensitive

$count = 0
foreach ($rel in $files) {
    $parts = $rel.Split('/')
    $sub = ''
    for ($i = 0; $i -lt $parts.Length - 1; $i++) {
        if ($sub -eq '') { $sub = $parts[$i] } else { $sub = "$sub/$($parts[$i])" }
        $folder = Join-Path $targetDir $sub
        if (-not (Test-Path $folder)) {
            New-Item -ItemType Directory -Path $folder | Out-Null
            Write-Meta "$folder.meta" $sub 'folder'
        }
    }
    $destination = Join-Path $targetDir $rel
    Copy-Item -LiteralPath (Join-Path $sourceFull $rel) -Destination $destination
    Write-Meta "$destination.meta" $rel 'script'
    $count++
}

Copy-Item -LiteralPath $readme -Destination (Join-Path $targetDir 'README.md')
Write-Meta (Join-Path $targetDir 'README.md.meta') 'README.md' 'text'

Write-Host "sync-protocol: copied $count files to unity-sdk/Assets/Backend/Plugins/Protocol"
