// scripts/PsBridge/PsHost.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

public static class PsHost
{
    public static Func<object> ProcessNestedCommands { get; set; }

    public static BlockingCollection<Dictionary<string, object>> CommandQueue = new BlockingCollection<Dictionary<string, object>>();
    public static BlockingCollection<Dictionary<string, object>> ReplyQueue = new BlockingCollection<Dictionary<string, object>>();
    public static SynchronizationContext MainSyncContext { get; set; }

    // Cached WPF Dispatcher reflection — lazily initialised by EnsureDispatcherReflection().
    private static bool _dispatcherReflectionChecked;
    private static MethodInfo _pushFrameMethod;   // Dispatcher.PushFrame(DispatcherFrame)
    private static Type _frameType;               // System.Windows.Threading.DispatcherFrame
    private static PropertyInfo _frameContinueProp; // DispatcherFrame.Continue
    private static PropertyInfo _currentDispatcherProp; // Dispatcher.CurrentDispatcher
    private static MethodInfo _beginInvokeMethod;  // Dispatcher.BeginInvoke(DispatcherPriority, Delegate)
    private static object _backgroundPriority;     // DispatcherPriority.Background

    private static void EnsureDispatcherReflection()
    {
        if (_dispatcherReflectionChecked) return;
        _dispatcherReflectionChecked = true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "WindowsBase") continue;
            var dispType = asm.GetType("System.Windows.Threading.Dispatcher");
            var fType = asm.GetType("System.Windows.Threading.DispatcherFrame");
            var priorityType = asm.GetType("System.Windows.Threading.DispatcherPriority");
            if (dispType == null || fType == null || priorityType == null) break;

            _frameType = fType;
            _frameContinueProp = _frameType.GetProperty("Continue");
            _currentDispatcherProp = dispType.GetProperty("CurrentDispatcher", BindingFlags.Public | BindingFlags.Static);
            _pushFrameMethod = dispType.GetMethod("PushFrame", BindingFlags.Public | BindingFlags.Static, null, new Type[] { _frameType }, null);
            _beginInvokeMethod = dispType.GetMethod("BeginInvoke", new Type[] { priorityType, typeof(Delegate) });
            _backgroundPriority = Enum.Parse(priorityType, "Background");
            break;
        }
    }

    /// <summary>
    /// Process all pending WPF Dispatcher messages (DoEvents pattern).
    /// Pushes a DispatcherFrame that pumps Normal-priority and above messages,
    /// then exits when the Background-priority callback fires.
    /// </summary>
    private static void PumpDispatcherMessages()
    {
        if (_pushFrameMethod == null) return;

        var frame = Activator.CreateInstance(_frameType);
        var dispatcher = _currentDispatcherProp.GetValue(null, null);
        if (dispatcher == null) return;

        Action exitAction = delegate { _frameContinueProp.SetValue(frame, false, null); };
        _beginInvokeMethod.Invoke(dispatcher, new object[] { _backgroundPriority, exitAction });
        _pushFrameMethod.Invoke(null, new object[] { frame });
    }

    public static object RunProcessNestedCommands()
    {
        EnsureDispatcherReflection();
        bool hasDispatcher = (_pushFrameMethod != null);

        var queues = new BlockingCollection<Dictionary<string, object>>[] { ReplyQueue, CommandQueue };

        while (BridgeState.PipeServer != null && BridgeState.PipeServer.IsConnected)
        {
            Dictionary<string, object> item;
            int index;

            if (hasDispatcher)
            {
                // WPF mode: pump Dispatcher messages (allows WebResourceRequested to fire),
                // then do a non-blocking check on the queues.
                PumpDispatcherMessages();
                index = BlockingCollection<Dictionary<string, object>>.TryTakeFromAny(queues, out item, 0);
                if (index < 0)
                {
                    // Thread.Sleep(0) yields to equal-priority threads without sleeping
                    // a full timer tick (~15ms). PumpDispatcherMessages() already provides
                    // work per iteration, so true tight-spinning does not occur.
                    Thread.Sleep(0);
                    continue;
                }
            }
            else
            {
                // Non-WPF (WinForms / console): original blocking wait.
                index = BlockingCollection<Dictionary<string, object>>.TakeFromAny(queues, out item);
            }

            if (index == 0)
            {
                if (item != null && item.ContainsKey("result"))
                {
                    return item["result"];
                }
                return null;
            }
            else if (index == 1)
            {
                if (item != null)
                {
                    ExecuteCommand(item);
                }
            }
        }
        return null;
    }

    private static void UpdateSyncContext()
    {
        if (SynchronizationContext.Current != null && MainSyncContext != SynchronizationContext.Current)
        {
            MainSyncContext = SynchronizationContext.Current;
        }
    }

    public static void ExecuteCommand(Dictionary<string, object> cmd)
    {
        if (cmd == null) return;
        
        try
        {
            var result = Reflection.InvokeReflectionLogic(cmd);
            UpdateSyncContext();

            // StartApplication pre-sends its own response and then blocks; skip the normal write.
            if (result != null && result.ContainsKey("__skipResponse"))
                return;

            var json = SimpleJson.Serialize(result);
            lock (BridgeState.Writer)
            {
                BridgeState.Writer.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            UpdateSyncContext();
            var errMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            var errJson = SimpleJson.Serialize(new Dictionary<string, object>
            {
                { "type", "error" },
                { "message", errMsg.Replace("\"", "'") }
            });
            lock (BridgeState.Writer)
            {
                BridgeState.Writer.WriteLine(errJson);
            }
        }
    }

    public static void DrainCommandQueue(object state)
    {
        Dictionary<string, object> cmd;
        while (CommandQueue.TryTake(out cmd))
        {
            UpdateSyncContext();
            ExecuteCommand(cmd);
        }
    }

    public static void StartServer()
    {
        // Use async pipe with explicit 64KB buffers. Default buffer size (0) means unbuffered,
        // causing writes to block until the client reads — which deadlocks sync events.
        BridgeState.PipeServer = new NamedPipeServerStream(
            BridgeState.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        
        BridgeState.PipeServer.WaitForConnection();
        var utf8Encoding = new System.Text.UTF8Encoding(false);
        BridgeState.Reader = new StreamReader(BridgeState.PipeServer, utf8Encoding);
        BridgeState.Writer = new StreamWriter(BridgeState.PipeServer, utf8Encoding);
        BridgeState.Writer.AutoFlush = true;

        var readerThread = new Thread(() =>
        {
            try
            {
                while (BridgeState.PipeServer.IsConnected)
                {
                    var line = BridgeState.Reader.ReadLine();
                    if (line == null) break;

                    var ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = int.MaxValue;
                    var msg = ser.DeserializeObject(line) as Dictionary<string, object>;
                    if (msg == null) continue;

                    if (msg.ContainsKey("type") && msg["type"].ToString() == "reply")
                    {
                        ReplyQueue.Add(msg);
                    }
                    else if (msg.ContainsKey("action") && msg["action"].ToString() == "Poll")
                    {
                        // Poll is pure I/O — handle on reader thread to avoid deadlock
                        ExecuteCommand(msg);
                    }
                    else
                    {
                        // Other commands: queue for Dispatcher thread via MainSyncContext
                        CommandQueue.Add(msg);
                        if (MainSyncContext != null)
                        {
                            MainSyncContext.Post(DrainCommandQueue, null);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                CommandQueue.CompleteAdding();
                ReplyQueue.CompleteAdding();
                // After Application.Run() blocks the main thread, the foreach loop
                // can't reach Environment.Exit(0). Force exit when pipe disconnects.
                Environment.Exit(0);
            }
        });
        readerThread.IsBackground = true;
        readerThread.Start();

        try
        {
            foreach (var cmd in CommandQueue.GetConsumingEnumerable())
            {
                UpdateSyncContext();
                ExecuteCommand(cmd);
            }
        }
        catch { }

        Environment.Exit(0);
    }
}