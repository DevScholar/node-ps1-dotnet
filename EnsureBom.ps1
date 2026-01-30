$ErrorActionPreference = 'Stop'

$extensions = @('.c', '.cpp', '.ps1')
$rootPath = (Get-Location).Path
$scriptPath = $MyInvocation.MyCommand.Path

$files = Get-ChildItem -Path $rootPath -Recurse -File | Where-Object { $extensions -contains $_.Extension }

foreach ($file in $files) {
    if ($file.FullName -eq $scriptPath) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF

    if ($hasBom) {
        Write-Host "Skipped (already BOM): $($file.FullName)"
        continue
    }

    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    [System.IO.File]::WriteAllText($file.FullName, $content, [System.Text.UTF8Encoding]::new($true))
    Write-Host "Converted: $($file.FullName)"
}

Write-Host "Done."
