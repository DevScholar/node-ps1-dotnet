# scripts/PsHost.ps1
param($PipeName)

$ScriptDir = Split-Path $MyInvocation.MyCommand.Path

Import-Module "$ScriptDir\PsBridge" -Force

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

[PsHostEntry]::Run($PipeName)
