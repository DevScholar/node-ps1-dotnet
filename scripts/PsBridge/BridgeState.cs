// scripts/PsBridge/BridgeState.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;

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
    public static string PipeName { get; set; }
    
    // SynchronizationContext for UI framework dispatching
    public static SynchronizationContext MainSyncContext { get; set; }
    
    // Flag to indicate the bridge is closing
    public static bool IsClosing { get; set; }
    
    // Command queue for thread-safe communication
    public static BlockingCollection<Dictionary<string, object>> CommandQueue { get; set; }
    public static BlockingCollection<Dictionary<string, object>> ReplyQueue { get; set; }
    
    // Queue for events dispatched in polling mode (set by StartApplication)
    public static System.Collections.Concurrent.ConcurrentQueue<string> EventQueue { get; set; }

    // When true, AddEvent handlers enqueue instead of blocking on ProcessNestedCommands
    public static bool UseQueueMode { get; set; }

    static BridgeState()
    {
        _objectStore = new Dictionary<string, object>();
        CommandQueue = new BlockingCollection<Dictionary<string, object>>();
        ReplyQueue = new BlockingCollection<Dictionary<string, object>>();
        IsClosing = false;
        EventQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        UseQueueMode = false;
    }
}
