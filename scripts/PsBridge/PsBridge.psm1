# scripts/PsBridge/PsBridge.psm1
$scriptDir = Split-Path $MyInvocation.MyCommand.Path

# Automatically include every .cs file in this directory.
# Add-Type compiles them all in one batch so partial classes and cross-file
# references are resolved correctly; listing order is irrelevant.
$csFiles = Get-ChildItem -Path $scriptDir -Filter "*.cs" |
           Select-Object -ExpandProperty FullName

$referencedAssemblies = @(
    'System.dll',
    'System.Core.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'System.Runtime.InteropServices.dll',
    'System.Web.Extensions.dll'
)

Add-Type -Path $csFiles -ReferencedAssemblies $referencedAssemblies

Export-ModuleMember -Function ConvertTo-Protocol, Resolve-Args, Invoke-ReflectionLogic, Remove-BridgeObject -Variable BridgeState
