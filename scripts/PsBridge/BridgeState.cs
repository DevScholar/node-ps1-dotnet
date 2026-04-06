// scripts/PsBridge/BridgeState.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;

public static class BridgeState
{
    public static ConcurrentDictionary<string, object> ObjectStore { get; private set; }

    public static StreamReader Reader { get; set; }
    public static StreamWriter Writer { get; set; }
    public static NamedPipeServerStream PipeServer { get; set; }
    public static string PipeName { get; set; }

    // Stores event handler delegates keyed by "{targetId}:{eventName}:{callbackId}".
    // Required for RemoveEvent and for bulk cleanup when a target object is Released.
    public static ConcurrentDictionary<string, Delegate> EventHandlerStore { get; private set; }

    // Queue of serialized event JSON strings. Filled by SendEventToJs on any thread;
    // drained by the Poll command on the UI/command thread.
    public static ConcurrentQueue<string> EventQueue { get; private set; }

    // Stores pending deferrals for async event completion (e.g. WebResourceRequested).
    // Key: deferralId → value: object[] { deferral, eventArgs, sender }
    public static ConcurrentDictionary<string, object[]> DeferralStore { get; private set; }

    static BridgeState()
    {
        ObjectStore = new ConcurrentDictionary<string, object>();
        EventHandlerStore = new ConcurrentDictionary<string, Delegate>();
        EventQueue = new ConcurrentQueue<string>();
        DeferralStore = new ConcurrentDictionary<string, object[]>();
    }
}
