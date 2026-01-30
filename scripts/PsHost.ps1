# scripts/PsHost.ps1
param($PipeName)

$ScriptDir = Split-Path $MyInvocation.MyCommand.Path

Import-Module "$ScriptDir\PsBridge" -Force

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$Global:BridgeState.PipeName = $PipeName

function Global:Process-NestedCommands {
    $reader = $Global:BridgeState.Reader
    $pipe = $Global:BridgeState.PipeServer
    while ($pipe.IsConnected) {
        $line = $reader.ReadLine()
        if ($null -eq $line) { break }
        
        $cmd = $line | ConvertFrom-Json
        
        if ($cmd.type -eq "reply") { 
            return $cmd.result 
        } 

        try {
            $result = Invoke-ReflectionLogic -Cmd $cmd
            $json = $result | ConvertTo-Json -Compress -Depth 1
            $Global:BridgeState.Writer.WriteLine($json)
        } catch {
            $errMsg = $_.Exception.Message
            if ($null -eq $errMsg) { $errMsg = $_.ToString() }
            $errJson = @{ type = "error"; message = $errMsg.Replace('"', "'") } | ConvertTo-Json -Compress
            $Global:BridgeState.Writer.WriteLine($errJson)
        }
    }
    return $null
}

$Global:BridgeState.MsgTimer = $null

function Global:Start-MessagePump {
    $Global:BridgeState.MsgTimer = New-Object System.Timers.Timer
    $Global:BridgeState.MsgTimer.Interval = 10
    $Global:BridgeState.MsgTimer.AutoReset = $true
    
    $Global:BridgeState.MsgTimer.Add_Elapsed({
        if ($Global:BridgeState.IsClosing) { return }
        if (-not $Global:BridgeState.PipeServer.IsConnected) { 
            $Global:BridgeState.IsClosing = $true
            return 
        }
        try {
            if ($Global:BridgeState.Reader.Peek() -ge 0) {
                $line = $Global:BridgeState.Reader.ReadLine()
                Handle-Line -line $line | Out-Null
            }
        } catch {
            $Global:BridgeState.IsClosing = $true
        }
    })
    
    $Global:BridgeState.MsgTimer.Start()
}

function Global:Stop-MessagePump {
    if ($Global:BridgeState.MsgTimer) {
        $Global:BridgeState.MsgTimer.Stop()
        $Global:BridgeState.MsgTimer.Dispose()
        $Global:BridgeState.MsgTimer = $null
    }
}

function Global:Process-Tick {
    if ($Global:BridgeState.IsClosing) { return }
    if (-not $Global:BridgeState.PipeServer.IsConnected) { return }
    try {
        if ($Global:BridgeState.Reader.Peek() -ge 0) {
            $line = $Global:BridgeState.Reader.ReadLine()
            Handle-Line -line $line | Out-Null
        }
    } catch { }
}

function Global:Start-GuiLoop {
    param($MainForm)
    Start-MessagePump
    
    $ctx = $null
    if ($MainForm) { $ctx = New-Object System.Windows.Forms.ApplicationContext($MainForm) } 
    else { $ctx = New-Object System.Windows.Forms.ApplicationContext }

    [System.Windows.Forms.Application]::Run($ctx)
    
    $Global:BridgeState.IsClosing = $true
    
    Stop-MessagePump
    
    Start-Sleep -Milliseconds 100
    
    if ($Global:BridgeState.Writer) {
        try {
            $exitSignal = @{ type = "exit" } | ConvertTo-Json -Compress
            $Global:BridgeState.Writer.WriteLine($exitSignal)
            $Global:BridgeState.Writer.Flush()
        } catch { }
    }
    
    Start-Sleep -Milliseconds 50
    
    if ($Global:BridgeState.PipeServer) {
        try {
            if ($Global:BridgeState.PipeServer.IsConnected) {
                $Global:BridgeState.PipeServer.Close()
            }
            $Global:BridgeState.PipeServer.Dispose()
        } catch { }
    }
    
    exit 0
}

function Handle-Line {
    param($line)
    $cmd = $line | ConvertFrom-Json
    if ($cmd.type -eq "reply") { return $true } 

    try {
        $result = Invoke-ReflectionLogic -Cmd $cmd
        $json = $result | ConvertTo-Json -Compress -Depth 1
        $Global:BridgeState.Writer.WriteLine($json)
    } catch {
        $errMsg = $_.Exception.Message
        if ($null -eq $errMsg) { $errMsg = $_.ToString() }
        $errJson = @{ type = "error"; message = $errMsg.Replace('"', "'") } | ConvertTo-Json -Compress
        $Global:BridgeState.Writer.WriteLine($errJson)
    }
    return $false
}

function Start-Server {
    $Global:BridgeState.PipeServer = New-Object System.IO.Pipes.NamedPipeServerStream($Global:BridgeState.PipeName, [System.IO.Pipes.PipeDirection]::InOut, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, 0)
    $Global:BridgeState.PipeServer.WaitForConnection()
    
    $Global:BridgeState.Reader = New-Object System.IO.StreamReader($Global:BridgeState.PipeServer)
    $Global:BridgeState.Writer = New-Object System.IO.StreamWriter($Global:BridgeState.PipeServer)
    $Global:BridgeState.Writer.AutoFlush = $true

    while ($Global:BridgeState.PipeServer.IsConnected) {
        $line = $Global:BridgeState.Reader.ReadLine()
        if ($null -eq $line) { break }
        Handle-Line -line $line | Out-Null
    }
}

try { Start-Server } finally { 
    if ($Global:BridgeState.PipeServer) { $Global:BridgeState.PipeServer.Dispose() } 
}
