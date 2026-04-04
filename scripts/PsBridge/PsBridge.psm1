# scripts/PsBridge/PsBridge.psm1
$scriptDir = Split-Path $MyInvocation.MyCommand.Path

$csFiles = @(
    "$scriptDir\BridgeState.cs",
    "$scriptDir\Protocol.cs",
    "$scriptDir\Reflection.cs",
    "$scriptDir\Reflection.TypeResolution.cs",
    "$scriptDir\Reflection.ObjectLifecycle.cs",
    "$scriptDir\Reflection.Events.cs",
    "$scriptDir\Reflection.Invocation.cs",
    "$scriptDir\Reflection.AssemblyLoader.cs",
    "$scriptDir\PsHost.cs",
    "$scriptDir\PsHostEntry.cs"
)

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
