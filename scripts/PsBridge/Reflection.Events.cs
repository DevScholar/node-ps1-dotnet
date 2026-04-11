// scripts/PsBridge/Reflection.Events.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public static partial class Reflection
{
    private static Dictionary<string, object> HandleRemoveEvent(Dictionary<string, object> cmd)
    {
        var targetId = cmd["targetId"].ToString();
        var eventName = cmd["eventName"].ToString();
        var cbId = cmd["callbackId"].ToString();
        var storeKey = targetId + ":" + eventName + ":" + cbId;

        Delegate handler;
        if (BridgeState.EventHandlerStore.TryRemove(storeKey, out handler))
        {
            object target;
            if (BridgeState.ObjectStore.TryGetValue(targetId, out target))
            {
                var eventInfo = target.GetType().GetEvent(eventName);
                if (eventInfo != null)
                {
                    try { eventInfo.RemoveEventHandler(target, handler); } catch { }
                }
            }
        }
        return new Dictionary<string, object> { { "type", "void" } };
    }

    private static Dictionary<string, object> HandleAddEvent(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        var eventName = cmd["eventName"].ToString();
        var cbId = cmd["callbackId"].ToString();

        var eventInfo = target.GetType().GetEvent(eventName);
        if (eventInfo != null)
        {
            var delegateType = eventInfo.EventHandlerType;
            var invokeMethod = delegateType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();

            var paramExprs = new System.Linq.Expressions.ParameterExpression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                paramExprs[pi] = System.Linq.Expressions.Expression.Parameter(parameters[pi].ParameterType, "p" + pi);

            var boxedExprs = new System.Linq.Expressions.Expression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                boxedExprs[pi] = System.Linq.Expressions.Expression.Convert(paramExprs[pi], typeof(object));

            var argsArrayExpr = System.Linq.Expressions.Expression.NewArrayInit(typeof(object), boxedExprs);
            var fireMethod = typeof(Reflection).GetMethod("FireSyncEventAndWait", BindingFlags.NonPublic | BindingFlags.Static);
            var cbIdExpr = System.Linq.Expressions.Expression.Constant(cbId, typeof(string));
            var callExpr = System.Linq.Expressions.Expression.Call(fireMethod, cbIdExpr, argsArrayExpr);

            // The delegate return type may be void or bool (e.g. close-request).
            // If void: discard the object return value from FireSyncEventAndWait.
            // If non-void: convert the object result to the required type.
            System.Linq.Expressions.Expression body;
            if (invokeMethod.ReturnType == typeof(void))
            {
                body = System.Linq.Expressions.Expression.Block(callExpr, System.Linq.Expressions.Expression.Empty());
            }
            else
            {
                body = System.Linq.Expressions.Expression.Convert(callExpr, invokeMethod.ReturnType);
            }

            var lambdaExpr = System.Linq.Expressions.Expression.Lambda(delegateType, body, paramExprs);
            Delegate handler = lambdaExpr.Compile();

            eventInfo.AddEventHandler(target, handler);
            var storeKey = cmd["targetId"].ToString() + ":" + eventName + ":" + cbId;
            BridgeState.EventHandlerStore[storeKey] = handler;
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }

    private static Dictionary<string, object> HandleSetResolvingCallback(Dictionary<string, object> cmd)
    {
        var cbId = cmd["callbackId"].ToString();

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var protoArgs = new List<Dictionary<string, object>>();
            protoArgs.Add(new Dictionary<string, object> { { "type", "primitive" }, { "value", args.Name } });

            var msg = new Dictionary<string, object>
            {
                { "type", "syncEvent" },
                { "callbackId", cbId },
                { "args", protoArgs }
            };
            lock (BridgeState.Writer)
            {
                BridgeState.Writer.WriteLine(SimpleJson.Serialize(msg));
            }

            object result = null;
            try { result = PsHost.RunProcessNestedCommands(); } catch { }

            if (result is string)
            {
                var filePath = (string)result;
                if (filePath.Length > 0)
                {
                    try { return Assembly.LoadFrom(filePath); } catch { }
                }
            }
            return null;
        };

        return new Dictionary<string, object> { { "type", "void" } };
    }

    // ── Async (fire-and-forget) event: enqueue to EventQueue, don't block ──────
    private static Dictionary<string, object> HandleAddAsyncEvent(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        var eventName = cmd["eventName"].ToString();
        var cbId = cmd["callbackId"].ToString();

        var eventInfo = target.GetType().GetEvent(eventName);
        if (eventInfo != null)
        {
            var delegateType = eventInfo.EventHandlerType;
            var invokeMethod = delegateType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();

            var paramExprs = new System.Linq.Expressions.ParameterExpression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                paramExprs[pi] = System.Linq.Expressions.Expression.Parameter(parameters[pi].ParameterType, "p" + pi);

            var boxedExprs = new System.Linq.Expressions.Expression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                boxedExprs[pi] = System.Linq.Expressions.Expression.Convert(paramExprs[pi], typeof(object));

            var argsArrayExpr = System.Linq.Expressions.Expression.NewArrayInit(typeof(object), boxedExprs);
            var fireMethod = typeof(Reflection).GetMethod("FireAsyncEvent", BindingFlags.NonPublic | BindingFlags.Static);
            var cbIdExpr = System.Linq.Expressions.Expression.Constant(cbId, typeof(string));
            var callExpr = System.Linq.Expressions.Expression.Call(fireMethod, cbIdExpr, argsArrayExpr);

            // FireAsyncEvent returns void — always wrap as void body.
            var body = System.Linq.Expressions.Expression.Block(
                callExpr, System.Linq.Expressions.Expression.Empty());

            var lambdaExpr = System.Linq.Expressions.Expression.Lambda(delegateType, body, paramExprs);
            Delegate handler = lambdaExpr.Compile();

            eventInfo.AddEventHandler(target, handler);
            var storeKey = cmd["targetId"].ToString() + ":" + eventName + ":" + cbId;
            BridgeState.EventHandlerStore[storeKey] = handler;
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }

    // ── Deferred event: GetDeferral() + enqueue, completed later by Node.js ─────
    private static Dictionary<string, object> HandleAddDeferredEvent(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        var eventName = cmd["eventName"].ToString();
        var cbId = cmd["callbackId"].ToString();

        var eventInfo = target.GetType().GetEvent(eventName);
        if (eventInfo != null)
        {
            var delegateType = eventInfo.EventHandlerType;
            var invokeMethod = delegateType.GetMethod("Invoke");
            var parameters = invokeMethod.GetParameters();

            var paramExprs = new System.Linq.Expressions.ParameterExpression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                paramExprs[pi] = System.Linq.Expressions.Expression.Parameter(parameters[pi].ParameterType, "p" + pi);

            var boxedExprs = new System.Linq.Expressions.Expression[parameters.Length];
            for (var pi = 0; pi < parameters.Length; pi++)
                boxedExprs[pi] = System.Linq.Expressions.Expression.Convert(paramExprs[pi], typeof(object));

            var argsArrayExpr = System.Linq.Expressions.Expression.NewArrayInit(typeof(object), boxedExprs);
            var fireMethod = typeof(Reflection).GetMethod("FireDeferredEvent", BindingFlags.NonPublic | BindingFlags.Static);
            var cbIdExpr = System.Linq.Expressions.Expression.Constant(cbId, typeof(string));
            var callExpr = System.Linq.Expressions.Expression.Call(fireMethod, cbIdExpr, argsArrayExpr);

            var body = System.Linq.Expressions.Expression.Block(
                callExpr, System.Linq.Expressions.Expression.Empty());

            var lambdaExpr = System.Linq.Expressions.Expression.Lambda(delegateType, body, paramExprs);
            Delegate handler = lambdaExpr.Compile();

            eventInfo.AddEventHandler(target, handler);
            var storeKey = cmd["targetId"].ToString() + ":" + eventName + ":" + cbId;
            BridgeState.EventHandlerStore[storeKey] = handler;
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }

    /// <summary>
    /// Deferred event: call GetDeferral() on the event args so the handler can
    /// return immediately without invalidating the args.  The deferral, event args
    /// and sender are stored in BridgeState.DeferralStore.  Node.js picks up the
    /// event via Poll, processes it, then sends CompleteDeferral to finish.
    /// </summary>
    private static void FireDeferredEvent(string cbId, object[] args)
    {
        // args[1] is the event args (sender, e) pattern
        object eventArgs = args.Length >= 2 ? args[1] : null;
        object sender = args.Length >= 1 ? args[0] : null;
        string deferralId = Guid.NewGuid().ToString();

        // Try to get a deferral — if the event args support it
        if (eventArgs != null)
        {
            var getDeferralMethod = eventArgs.GetType().GetMethod("GetDeferral");
            if (getDeferralMethod != null)
            {
                try
                {
                    var deferral = getDeferralMethod.Invoke(eventArgs, null);
                    BridgeState.DeferralStore[deferralId] = new object[] { deferral, eventArgs, sender };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[FireDeferredEvent] GetDeferral failed: " + ex.Message);
                    // Fall back to async (fire-and-forget) if deferral fails
                    FireAsyncEvent(cbId, args);
                    return;
                }
            }
        }

        // Build protocol args with inline property extraction
        var protoArgs = new List<Dictionary<string, object>>();
        foreach (var arg in args)
        {
            if (arg == null)
            {
                protoArgs.Add(new Dictionary<string, object> { { "type", "null" } });
                continue;
            }

            var argType = arg.GetType();

            // CoreWebView2WebResourceRequestedEventArgs: extract Request.Uri and Request.Method inline
            if (argType.Name == "CoreWebView2WebResourceRequestedEventArgs")
            {
                var refId = Guid.NewGuid().ToString();
                BridgeState.ObjectStore[refId] = arg;

                var inlineProps = new Dictionary<string, object>();
                var requestProp = argType.GetProperty("Request");
                var requestObj = requestProp != null ? requestProp.GetValue(arg, null) : null;

                if (requestObj != null)
                {
                    var requestType = requestObj.GetType();
                    var requestRefId = Guid.NewGuid().ToString();
                    BridgeState.ObjectStore[requestRefId] = requestObj;

                    var requestInlineProps = new Dictionary<string, object>();
                    var uriProp = requestType.GetProperty("Uri");
                    if (uriProp != null)
                    {
                        var uriVal = uriProp.GetValue(requestObj, null);
                        requestInlineProps["Uri"] = uriVal != null ? uriVal.ToString() : null;
                    }
                    var methodProp = requestType.GetProperty("Method");
                    if (methodProp != null)
                    {
                        var methodVal = methodProp.GetValue(requestObj, null);
                        requestInlineProps["Method"] = methodVal != null ? methodVal.ToString() : null;
                    }

                    inlineProps["Request"] = new Dictionary<string, object>
                    {
                        { "type", "ref" },
                        { "id", requestRefId },
                        { "netType", requestType.FullName },
                        { "props", requestInlineProps }
                    };
                }

                protoArgs.Add(new Dictionary<string, object>
                {
                    { "type", "ref" },
                    { "id", refId },
                    { "netType", argType.FullName },
                    { "props", inlineProps }
                });
            }
            else
            {
                protoArgs.Add(Protocol.ConvertToProtocol(arg));
            }
        }

        var msg = SimpleJson.Serialize(new Dictionary<string, object>
        {
            { "type", "deferredEvent" },
            { "callbackId", cbId },
            { "deferralId", deferralId },
            { "args", protoArgs }
        });
        BridgeState.EventQueue.Enqueue(msg);
    }

    /// <summary>
    /// Complete a deferred event: look up the stored deferral, apply response data
    /// (for WebResourceRequested), then call deferral.Complete().
    /// </summary>
    private static Dictionary<string, object> HandleCompleteDeferral(Dictionary<string, object> cmd)
    {
        var deferralId = cmd["deferralId"].ToString();
        object[] stored;
        if (!BridgeState.DeferralStore.TryRemove(deferralId, out stored))
        {
            return new Dictionary<string, object> { { "type", "error" }, { "message", "Unknown deferralId: " + deferralId } };
        }

        var deferral = stored[0];
        var eventArgs = stored[1];
        var sender = stored[2];

        // If response data is provided (WebResourceRequested), create WebResourceResponse
        if (cmd.ContainsKey("html"))
        {
            var htmlStr = cmd.ContainsKey("html") && cmd["html"] != null ? cmd["html"].ToString() : "";
            var statusCode = cmd.ContainsKey("statusCode") ? Convert.ToInt32(cmd["statusCode"]) : 200;
            var reasonPhrase = cmd.ContainsKey("reasonPhrase") ? cmd["reasonPhrase"].ToString() : "OK";
            var headers = cmd.ContainsKey("headers") ? cmd["headers"].ToString() : "Content-Type: text/html; charset=utf-8";
            var isBase64 = cmd.ContainsKey("base64") && cmd["base64"] is bool && (bool)cmd["base64"];

            try
            {
                var htmlBytes = isBase64
                    ? Convert.FromBase64String(htmlStr)
                    : System.Text.Encoding.UTF8.GetBytes(htmlStr);
                var memStream = new MemoryStream(htmlBytes);

                // Get environment from sender (CoreWebView2)
                if (sender != null)
                {
                    var envProp = sender.GetType().GetProperty("Environment");
                    if (envProp != null)
                    {
                        var env = envProp.GetValue(sender, null);
                        var createRespMethod = env.GetType().GetMethod("CreateWebResourceResponse");
                        if (createRespMethod != null)
                        {
                            var response = createRespMethod.Invoke(env, new object[] { (Stream)memStream, statusCode, reasonPhrase, headers });
                            var responseProp = eventArgs.GetType().GetProperty("Response");
                            if (responseProp != null)
                            {
                                responseProp.SetValue(eventArgs, response, null);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[CompleteDeferral] Error setting response: " + ex.Message);
            }
        }

        // Complete the deferral
        try
        {
            var completeMethod = deferral.GetType().GetMethod("Complete");
            if (completeMethod != null)
            {
                completeMethod.Invoke(deferral, null);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[CompleteDeferral] Error completing: " + ex.Message);
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }

    /// <summary>
    /// Fire-and-forget event: convert args to protocol format and enqueue to
    /// EventQueue. Node.js picks them up via Poll — no pipe blocking, no syncEventDepth.
    /// For COM event args (e.g. WebView2) that may be invalidated after the handler
    /// returns, key properties are extracted inline as inlineProps.
    /// </summary>
    private static void FireAsyncEvent(string cbId, object[] args)
    {
        var protoArgs = new List<Dictionary<string, object>>();
        foreach (var arg in args)
        {
            if (arg == null)
            {
                protoArgs.Add(new Dictionary<string, object> { { "type", "null" } });
                continue;
            }

            var argType = arg.GetType();

            // CoreWebView2WebMessageReceivedEventArgs: extract WebMessageAsJson inline
            // because the underlying COM object is invalidated after the handler returns.
            if (argType.Name == "CoreWebView2WebMessageReceivedEventArgs")
            {
                var refId = Guid.NewGuid().ToString();
                BridgeState.ObjectStore[refId] = arg;

                var inlineProps = new Dictionary<string, object>();
                var jsonProp = argType.GetProperty("WebMessageAsJson");
                if (jsonProp != null)
                {
                    var val = jsonProp.GetValue(arg, null);
                    inlineProps["WebMessageAsJson"] = val != null ? val.ToString() : null;
                }

                protoArgs.Add(new Dictionary<string, object>
                {
                    { "type", "ref" },
                    { "id", refId },
                    { "netType", argType.FullName },
                    { "props", inlineProps }
                });
            }
            else
            {
                protoArgs.Add(Protocol.ConvertToProtocol(arg));
            }
        }
        var msg = SimpleJson.Serialize(new Dictionary<string, object>
        {
            { "type", "event" },
            { "callbackId", cbId },
            { "args", protoArgs }
        });
        BridgeState.EventQueue.Enqueue(msg);
    }

    // Guard against re-entrant sync events. When RunProcessNestedCommands pumps
    // the WPF dispatcher (PumpDispatcherMessages), it can fire new events (e.g.
    // MouseMove) on the same thread. If those also use FireSyncEventAndWait, the
    // inner event's syncEvent is written to the pipe before the outer event's
    // nested IPC calls are answered, causing response ordering mixup on the
    // Node.js side (the inner callback reads responses meant for the outer one).
    // Fix: if already inside a sync event, fall back to async (EventQueue → Poll).
    private static int _syncEventDepth = 0;

    // Fire a sync event to Node.js and block until the JS handler returns a value.
    // C# is suspended in RunProcessNestedCommands(), processing any proxy calls the
    // JS handler makes, until Node.js sends back {type:'reply', result:...}.
    private static object FireSyncEventAndWait(string cbId, object[] args)
    {
        if (_syncEventDepth > 0)
        {
            // Re-entrant: convert to async to avoid response ordering corruption.
            // The callback will run during the next Poll cycle (~8ms latency).
            FireAsyncEvent(cbId, args);
            return null;
        }
        _syncEventDepth++;
        try
        {
        var protoArgs = new List<Dictionary<string, object>>();
        foreach (var arg in args)
        {
            if (arg == null)
            {
                protoArgs.Add(new Dictionary<string, object> { { "type", "null" } });
            }
            else
            {
                // Check if this is CoreWebView2WebResourceRequestedEventArgs
                // If so, extract Request property as inline props to avoid IPC calls in sync handler
                var argType = arg.GetType();
                if (argType.Name == "CoreWebView2WebResourceRequestedEventArgs")
                {
                    var refId = Guid.NewGuid().ToString();
                    BridgeState.ObjectStore[refId] = arg;

                    // Extract Request property as inline props
                    var requestProp = argType.GetProperty("Request");
                    var requestObj = requestProp != null ? requestProp.GetValue(arg, null) : null;

                    var inlineProps = new Dictionary<string, object>();
                    if (requestObj != null)
                    {
                        var requestType = requestObj.GetType();
                        var requestRefId = Guid.NewGuid().ToString();
                        BridgeState.ObjectStore[requestRefId] = requestObj;

                        // Extract Uri and Method from Request
                        var uriProp = requestType.GetProperty("Uri");
                        var methodProp = requestType.GetProperty("Method");

                        var requestInlineProps = new Dictionary<string, object>();
                        if (uriProp != null)
                        {
                            var uriVal = uriProp.GetValue(requestObj, null);
                            requestInlineProps["Uri"] = uriVal != null ? uriVal.ToString() : null;
                        }
                        if (methodProp != null)
                        {
                            var methodVal = methodProp.GetValue(requestObj, null);
                            requestInlineProps["Method"] = methodVal != null ? methodVal.ToString() : null;
                        }

                        inlineProps["Request"] = new Dictionary<string, object>
                        {
                            { "type", "ref" },
                            { "id", requestRefId },
                            { "netType", requestType.FullName },
                            { "props", requestInlineProps }
                        };
                    }

                    protoArgs.Add(new Dictionary<string, object>
                    {
                        { "type", "ref" },
                        { "id", refId },
                        { "netType", argType.FullName },
                        { "props", inlineProps }
                    });
                }
                else
                {
                    protoArgs.Add(Protocol.ConvertToProtocol(arg));
                }
            }
        }
        var msg = new Dictionary<string, object>
        {
            { "type", "syncEvent" },
            { "callbackId", cbId },
            { "args", protoArgs }
        };
        lock (BridgeState.Writer)
        {
            BridgeState.Writer.WriteLine(SimpleJson.Serialize(msg));
        }
        var result = PsHost.RunProcessNestedCommands();

        // If JS returned response data for WebResourceRequested, create the response in C#.
        // This avoids nested IPC calls which deadlock due to pipe FlushFileBuffers blocking.
        foreach (var arg in args)
        {
            if (arg == null) continue;
            if (arg.GetType().Name != "CoreWebView2WebResourceRequestedEventArgs") continue;
            var resultDict = result as Dictionary<string, object>;
            if (resultDict == null || !resultDict.ContainsKey("html")) break;

            var htmlStr = resultDict["html"] != null ? resultDict["html"].ToString() : "";
            var statusCode = resultDict.ContainsKey("statusCode") ? Convert.ToInt32(resultDict["statusCode"]) : 200;
            var reasonPhrase = resultDict.ContainsKey("reasonPhrase") ? resultDict["reasonPhrase"].ToString() : "OK";
            var headers = resultDict.ContainsKey("headers") ? resultDict["headers"].ToString() : "Content-Type: text/html; charset=utf-8";
            var isBase64 = resultDict.ContainsKey("base64") && resultDict["base64"] is bool && (bool)resultDict["base64"];

            try
            {
                var htmlBytes = isBase64
                    ? Convert.FromBase64String(htmlStr)
                    : System.Text.Encoding.UTF8.GetBytes(htmlStr);
                var memStream = new MemoryStream(htmlBytes);

                // Get environment from sender (args[0] is the CoreWebView2)
                var sender = args[0];
                var envProp = sender.GetType().GetProperty("Environment");
                if (envProp != null)
                {
                    var env = envProp.GetValue(sender, null);
                    var createRespMethod = env.GetType().GetMethod("CreateWebResourceResponse");
                    if (createRespMethod != null)
                    {
                        var response = createRespMethod.Invoke(env, new object[] { (Stream)memStream, statusCode, reasonPhrase, headers });
                        var responseProp = arg.GetType().GetProperty("Response");
                        if (responseProp != null)
                        {
                            responseProp.SetValue(arg, response, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FireSyncEventAndWait] Error setting response: " + ex.Message);
            }
            break;
        }

        return result;
        }
        finally
        {
            _syncEventDepth--;
        }
    }

}
