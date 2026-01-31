# scripts/PsBridge/Reflection.ps1

function Invoke-ReflectionLogic {
    param($Cmd)
    
    # --- GetType ---
    if ($Cmd.action -eq "GetType") {
        $name = $Cmd.typeName
        $type = [Type]::GetType($name)
        if ($null -eq $type) {
            try { [System.Reflection.Assembly]::LoadWithPartialName($name) | Out-Null } catch {}
            $assemblies = [AppDomain]::CurrentDomain.GetAssemblies()
            foreach ($asm in $assemblies) {
                $type = $asm.GetType($name)
                if ($null -ne $type) { break }
            }
        }
        if ($null -eq $type) { return @{ type = "namespace"; value = $name } }
        return Convert-ToProtocol $type
    }

    # --- Inspect ---
    if ($Cmd.action -eq "Inspect") {
        $target = $Global:BridgeState.ObjectStore[$Cmd.targetId]
        $name = $Cmd.memberName
        if ($target -is [Type]) {
            $prop = $target.GetProperty($name, [System.Reflection.BindingFlags]'Public,Static')
            if ($prop) { return @{ type = "meta"; memberType = "property" } }
        }
        $member = $target.PSObject.Members[$name]
        if ($null -ne $member -and ($member.MemberType -match "Property")) {
            return @{ type = "meta"; memberType = "property" }
        }
        return @{ type = "meta"; memberType = "method" }
    }

    # --- AddEvent ---
    if ($Cmd.action -eq "AddEvent") {
        $target = $Global:BridgeState.ObjectStore[$Cmd.targetId]
        $eventName = $Cmd.eventName
        $cbId = $Cmd.callbackId

        $addMethod = "add_$eventName"
        $hasAddMethod = $null -ne $target.PSObject.Methods[$addMethod]

        $handler = {
            param($sender, $e)
            $writer = $Global:BridgeState.Writer
            if ($null -eq $writer) {
                return
            }
            
            $protoArgs = @()
            
            foreach ($arg in @($sender, $e)) {
                if ($null -eq $arg) {
                    $protoArgs += @{ type = "null" }
                }
                else {
                    $converted = Convert-ToProtocol $arg
                    
                    $isEventArgs = $false
                    $propsToInclude = @{}
                    
                    try {
                        $comEventArgs = 
                            $typeName -match "EventArgs$" -or 
                            $typeName -match "InitializationCompleted" -or
                            $arg.GetType().FullName -eq "System.__ComObject"
                        if ($comEventArgs) {
                            $isEventArgs = $true
                        }
                    }
                    catch { }
                    
                    if ($isEventArgs) {
                        try {
                            $members = $arg.PSObject.Members | Where-Object { $_.MemberType -match "Property" -and $_.Name -notmatch "^(PSObject|BaseObject|ImmediateBaseObject)$" }
                            foreach ($member in $members) {
                                try {
                                    $val = $null
                                    $val = $member.Value
                                    if ($null -ne $val -and -not ($val -is [System.MarshalByRefObject]) -and -not ($val -is [System.Object])) {
                                        if ($val -is [System.Boolean] -or $val -is [System.String] -or $val.GetType().IsPrimitive) {
                                            $propsToInclude[$member.Name] = $val
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    
                    if ($propsToInclude.Count -gt 0) {
                        $converted["props"] = $propsToInclude
                    }
                    
                    $protoArgs += $converted
                }
            }
            
            $msg = @{ type = "event"; callbackId = $cbId; args = $protoArgs } 
            $json = $msg | ConvertTo-Json -Compress
            $writer.WriteLine($json)
            if (Get-Command "Process-NestedCommands" -ErrorAction SilentlyContinue) { Process-NestedCommands }
        }.GetNewClosure()

        $addMethod = "add_$eventName"
        if ($target.PSObject.Methods[$addMethod]) {
            $target.$addMethod.Invoke($handler)
        }
        return @{ type = "void" }
    }

    # --- New ---
    if ($Cmd.action -eq "New") {
        $type = $Global:BridgeState.ObjectStore[$Cmd.typeId]
        $realArgs = Resolve-Args $Cmd.args
        try { $obj = [Activator]::CreateInstance($type, $realArgs) } 
        catch { throw "New Error: $($_.Exception.Message)" }
        return Convert-ToProtocol $obj
    }

    # --- Invoke ---
    if ($Cmd.action -eq "Invoke") {
        $target = $Global:BridgeState.ObjectStore[$Cmd.targetId]
        $name = $Cmd.methodName
        $realArgs = Resolve-Args $Cmd.args

        # Simple GUI Loop Invocation
        if ($name -eq "Run" -and $target.ToString() -eq "System.Windows.Forms.Application") {
            $form = $null
            if ($realArgs.Count -gt 0) { $form = $realArgs[0] }
            if (Get-Command "Start-GuiLoop" -ErrorAction SilentlyContinue) { Start-GuiLoop -MainForm $form }
            return @{ type = "void" }
        }

        $isStatic = $target -is [Type]
        $targetType = if ($isStatic) { $target } else { $target.GetType() }
        
        # Static attribute set
        if ($isStatic -and $realArgs.Count -eq 0) {
            try {
                $prop = $targetType.GetProperty($name, [System.Reflection.BindingFlags]'Public,Static')
                if ($prop) {
                    $result = $prop.GetValue($null)
                    return Convert-ToProtocol $result
                }
                
                $field = $targetType.GetField($name, [System.Reflection.BindingFlags]'Public,Static')
                if ($field) {
                    $result = $field.GetValue($null)
                    return Convert-ToProtocol $result
                }
            }
            catch { }
        }

        # Instance attribute set
        if (-not $isStatic -and $realArgs.Count -gt 0) {
            $member = $target.PSObject.Members[$name]
            if ($null -ne $member -and ($member.MemberType -match "Property")) {
                try { 
                    $member.Value = $realArgs[0] 
                    return @{ type = "void" }
                }
                catch { throw "Set Property Error '$name': $($_.Exception.Message)" }
            }
        }

        # Instance attribute get (no args) - using PSObject
        if (-not $isStatic -and $realArgs.Count -eq 0) {
            $member = $target.PSObject.Members[$name]
            if ($null -ne $member -and ($member.MemberType -match "Property")) {
                return Convert-ToProtocol $member.Value 
            }
        }
        
        # Instance attribute get (no args) - using reflection (for COM objects)
        if (-not $isStatic -and $realArgs.Count -eq 0) {
            try {
                $prop = $targetType.GetProperty($name, [System.Reflection.BindingFlags]'Public,Instance')
                if ($prop) {
                    $result = $prop.GetValue($target)
                    return Convert-ToProtocol $result
                }
            }
            catch { }
        }

        # Method Call
        try {
            $result = $null
            $bindingFlags = [System.Reflection.BindingFlags]'Public,Instance,Static,FlattenHierarchy,IgnoreCase'

            $needsManualShim = $false
            foreach ($arg in $realArgs) {
                if ($arg -is [ScriptBlock]) { $needsManualShim = $true; break }
            }

            $manualSuccess = $false

            if ($needsManualShim) {
                $methods = $targetType.GetMethods($bindingFlags) | Where-Object { $_.Name -eq $name }
                
                foreach ($method in $methods) {
                    $params = $method.GetParameters()
                    if ($params.Count -ne $realArgs.Count) { continue }
                    
                    $tempArgs = @($realArgs)
                    $match = $true
                    
                    for ($i = 0; $i -lt $params.Count; $i++) {
                        $pType = $params[$i].ParameterType
                        $arg = $tempArgs[$i]
                        
                        if ($arg -is [ScriptBlock]) {
                            if ($pType -eq [System.Delegate]) {
                                try { $tempArgs[$i] = $arg -as [System.Action] } 
                                catch { $match = $false; break }
                            }
                            elseif ([System.Delegate].IsAssignableFrom($pType)) {
                                try { $tempArgs[$i] = $arg -as $pType } 
                                catch { $match = $false; break }
                            }
                            else { $match = $false; break }

                            if ($null -eq $tempArgs[$i]) { $match = $false; break }
                        }
                        elseif ($pType.IsEnum -and $arg -is [int]) {
                            $tempArgs[$i] = [Enum]::ToObject($pType, $arg)
                        }
                    }
                    
                    if ($match) {
                        try {
                            $instanceToCall = if ($isStatic) { $null } else { $target }
                            if ($instanceToCall -is [System.Management.Automation.PSObject]) {
                                $instanceToCall = $instanceToCall.BaseObject
                            }
                            $result = $method.Invoke($instanceToCall, $tempArgs)
                            $manualSuccess = $true
                            break
                        } catch { }
                    }
                }
            }

            if (-not $manualSuccess) {
                if ($isStatic) {
                    $methodObj = $target::($name)
                    if ($null -ne $methodObj) {
                        $result = $methodObj.Invoke($realArgs)
                    }
                } else {
                    $methodObj = $target.($name)
                    if ($null -ne $methodObj) {
                        $result = $methodObj.Invoke($realArgs)
                    }
                }
            }

            return Convert-ToProtocol $result

        } catch {
            if ($realArgs.Count -eq 0) {
                try {
                    if ($isStatic) {
                        $val = $target::$name
                    } else {
                        $val = $target.$name
                    }
                    if ($null -ne $val) { return Convert-ToProtocol $val }
                } catch {}
            }
            throw "Invoke Error ($name): $($_.Exception.Message)" 
        }
    }

    if ($Cmd.action -eq "Release") {
        Remove-BridgeObject $Cmd.targetId
        return @{ type="void" }
    }
    if ($Cmd.action -eq "GetFrameworkInfo") {
        return @{ type = "frameworkInfo"; frameworkMoniker = "net481"; runtimeVersion = [System.Environment]::Version.ToString() }
    }
    if ($Cmd.action -eq "LoadAssembly") {
        if (Test-Path $Cmd.assemblyPath) { [System.Reflection.Assembly]::LoadFrom($Cmd.assemblyPath) | Out-Null }
        else { [System.Reflection.Assembly]::LoadWithPartialName($Cmd.assemblyPath) | Out-Null }
        return @{ type = "void" }
    }
    if ($Cmd.action -eq "RequireModule") {
        if (Test-Path $Cmd.assemblyPath) { return @{ type = "namespace"; value = [System.Reflection.Assembly]::LoadFrom($Cmd.assemblyPath).GetName().Name } }
        throw "Module not found"
    }
    if ($Cmd.action -eq "Resolved") { return @{ type="void" } }
    if ($Cmd.action -eq "AwaitTask") {
        $task = $Global:BridgeState.ObjectStore[$Cmd.taskId]
        try {
            $task.GetAwaiter().GetResult()
            $prop = $task.GetType().GetProperty("Result")
            $res = if ($prop) { $prop.GetValue($task, $null) } else { $null }
            return Convert-ToProtocol $res
        } catch { throw "Task Error: $($_.Exception.Message)" }
    }
}

Export-ModuleMember -Function Invoke-ReflectionLogic
