# scripts/PsBridge/Protocol.ps1

# Define global state container
$Global:BridgeState = @{
    ObjectStore = @{}
    Reader      = $null
    Writer      = $null
    PipeServer  = $null
    MsgTimer    = $null
    IsClosing   = $false
}

function Get-ComObjectProperties {
    param($InputObject)
    $props = @{}
    
    $target = $InputObject
    $type = $null
    
    if ($InputObject -is [Type]) {
        $type = $InputObject
    }
    else {
        $type = $InputObject.GetType()
    }
    
    if ($type.FullName -eq "System.__ComObject" -or 
        ($null -ne $type.GetCustomAttributes($false) | Where-Object { $_ -is [System.Runtime.InteropServices.ComVisibleAttribute] })) {
        $props['__comType'] = $type.FullName
        
        $allProps = $type.GetProperties([System.Reflection.BindingFlags]'Public,Instance')
        
        foreach ($prop in $allProps) {
            $indexParams = $prop.GetIndexParameters()
            
            if ($indexParams.Count -eq 0) {
                try {
                    $val = $prop.GetValue($InputObject, $null)
                    
                    if ($null -ne $val) {
                        if ($val -is [System.Boolean] -or $val -is [System.String] -or $val.GetType().IsPrimitive) {
                            # Process NaN and Infinity (JSON does not support these values)
                            $isValidValue = $true
                            if ($val -is [double] -or $val -is [float] -or $val -is [System.Single]) {
                                if ([double]::IsNaN($val) -or [double]::IsInfinity($val)) {
                                    $isValidValue = $false
                                }
                            }
                            if ($isValidValue) {
                                $props[$prop.Name] = $val
                            }
                        }
                        elseif ($val -is [System.ValueType]) {
                            # Process ValueType values (excluding NaN, Infinity, -Infinity)
                            $strVal = $val.ToString()
                            if ($strVal -ne "NaN" -and $strVal -ne "Infinity" -and $strVal -ne "-Infinity") {
                                $props[$prop.Name] = $val
                            }
                        }
                    }
                }
                catch { }
            }
            else {
                $paramType = $indexParams[0].ParameterType
                if ($paramType -eq [System.String]) {
                    $invoker = [System.Reflection.PropertyInfo].GetMethod("get_Item", [System.Reflection.BindingFlags]'Public,Instance', $null, [Type[]]@([System.String]), $null)
                    if ($null -ne $invoker) {
                        $val = $invoker.Invoke($InputObject, "AdditionalArgs")
                        if ($null -ne $val) {
                            $props['AdditionalArgs'] = $val
                        }
                    }
                }
            }
        }
    }
    
    return $props
 }

function Convert-ToProtocol {
    param($InputObject)
    
    if ($null -eq $InputObject) { return @{ type = "null" } }
    
    if ($InputObject -is [Boolean] -or $InputObject -is [String]) {
         return @{ type = "primitive"; value = $InputObject }
    }

    if ($InputObject.GetType().IsPrimitive) {
        $val = $InputObject
        # Process NaN and Infinity (JSON does not support these values)
        if ($val -is [double] -or $val -is [float]) {
            if ([double]::IsNaN($val) -or [double]::IsInfinity($val)) {
                $val = $null
            }
        }
        return @{ type = "primitive"; value = $val }
    }

    if ($InputObject -is [System.Threading.Tasks.Task]) {
        $refId = [Guid]::NewGuid().ToString()
        $Global:BridgeState.ObjectStore[$refId] = $InputObject
        return @{ 
            type = "task"
            id = $refId 
            netType = $InputObject.GetType().FullName 
        }
    }

    if ($InputObject -is [System.Array]) {
        $arrResult = @()
        foreach ($item in $InputObject) {
            $arrResult += Convert-ToProtocol $item
        }
        return @{ type = "array"; value = $arrResult }
    }

    $refId = [Guid]::NewGuid().ToString()
    $Global:BridgeState.ObjectStore[$refId] = $InputObject
    
    $result = @{ type = "ref"; id = $refId; netType = $InputObject.GetType().FullName }
    
    $comProps = Get-ComObjectProperties -InputObject $InputObject
    if ($comProps.Count -gt 0) {
        $result["props"] = $comProps
    }
    
    return $result
}

function Resolve-Args {
    param($CmdArgs)
    $realArgs = @()
    if ($CmdArgs) {
        foreach ($arg in $CmdArgs) {
            if ($arg -is [System.Management.Automation.PSCustomObject] -and $arg.PSObject.Properties['__ref']) {
                $realArgs += $Global:BridgeState.ObjectStore[$arg.__ref]
            }
            elseif ($arg -is [System.Management.Automation.PSCustomObject] -and $arg.type -eq 'callback') {
                $cbId = $arg.callbackId
                
                # Handle Callback Closure
                $callbackBlock = {
                    param($p1, $p2, $p3, $p4)
                    $netCallbackArgs = @($p1, $p2, $p3, $p4)
                    
                    $validProtoArgs = @()
                    foreach ($a in $netCallbackArgs) {
                        # Filter out PowerShell's AutomationNull (null parameter placeholder)
                        if ($null -ne $a -and $a.GetType().Name -ne 'AutomationNull') {
                            $validProtoArgs += Convert-ToProtocol $a
                        }
                    }

                    $msg = @{ 
                        type = "event"; 
                        callbackId = $cbId; 
                        args = $validProtoArgs 
                    }
                    
                    $json = $msg | ConvertTo-Json -Compress -Depth 5
                    
                    $Global:BridgeState.Writer.WriteLine($json)
                    
                    $result = $null
                    if (Get-Command "Process-NestedCommands" -ErrorAction SilentlyContinue) { 
                        $result = Process-NestedCommands 
                    }
                    
                    return $result
                }.GetNewClosure()
                
                $realArgs += $callbackBlock
            }
            else {
                $realArgs += $arg
            }
        }
    }
    return ,$realArgs
}

function Remove-BridgeObject {
    param($Id)
    $Global:BridgeState.ObjectStore.Remove($Id)
}

Export-ModuleMember -Function Convert-ToProtocol, Resolve-Args, Remove-BridgeObject -Variable BridgeState
