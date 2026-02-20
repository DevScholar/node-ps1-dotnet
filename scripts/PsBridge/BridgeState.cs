// scripts/PsBridge/BridgeState.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Timers;

public static class BridgeState
{
    private static Dictionary<string, object> _objectStore;
    public static Dictionary<string, object> ObjectStore
    {
        get { return _objectStore; }
    }
    
    public static StreamReader Reader { get; set; }
    public static StreamWriter Writer { get; set; }
    public static NamedPipeServerStream PipeServer { get; set; }
    public static Timer MsgTimer { get; set; }
    public static bool IsClosing { get; set; }
    public static string PipeName { get; set; }
    
    static BridgeState()
    {
        _objectStore = new Dictionary<string, object>();
    }
}
