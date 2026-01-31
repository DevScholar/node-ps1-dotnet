# scripts/PsBridge/PsBridge.psm1
$scriptDir = Split-Path $MyInvocation.MyCommand.Path

. "$scriptDir\Protocol.ps1"
. "$scriptDir\Reflection.ps1"

Export-ModuleMember -Function ConvertTo-Protocol, Resolve-Args, Invoke-ReflectionLogic, Remove-BridgeObject -Variable BridgeState