# scripts/PsHost.ps1
param($PipeName)

$ScriptDir = Split-Path $MyInvocation.MyCommand.Path

Import-Module "$ScriptDir\PsBridge" -Force

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture
$ErrorActionPreference = "Stop"

[PsHostEntry]::Run($PipeName)
