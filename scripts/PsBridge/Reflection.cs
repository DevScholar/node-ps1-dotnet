// scripts/PsBridge/Reflection.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public static class Reflection
{
    public static Dictionary<string, object> InvokeReflectionLogic(Dictionary<string, object> cmd)
    {
        var action = cmd["action"].ToString();

        if (action == "GetRuntimeInfo")
        {
            var frameworkDescription = RuntimeInformation.FrameworkDescription;
            var environmentVersion = Environment.Version.ToString();
            string frameworkMoniker = InferFrameworkMoniker();
            
            return new Dictionary<string, object>
            {
                { "type", "runtimeInfo" },
                { "frameworkMoniker", frameworkMoniker },
                { "runtimeVersion", environmentVersion },
                { "frameworkDescription", frameworkDescription }
            };
        }

        if (action == "Poll")
        {
            var events = new List<string>();
            string evt;
            while (BridgeState.EventQueue.TryDequeue(out evt))
                events.Add(evt);
            return new Dictionary<string, object>
            {
                { "type", "poll" },
                { "events", events }
            };
        }

        if (action == "GetType")
        {
            var name = cmd["typeName"].ToString();
            var type = Type.GetType(name);
            if (type == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    type = asm.GetType(name);
                    if (type != null) break;
                }
            }
            if (type == null)
            {
                return new Dictionary<string, object> { { "type", "namespace" }, { "value", name } };
            }
            return Protocol.ConvertToProtocol(type);
        }

        if (action == "Inspect")
        {
            var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
            var memberName = cmd["memberName"].ToString();
            
            if (target is Type)
            {
                var prop = ((Type)target).GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    return new Dictionary<string, object> { { "type", "meta" }, { "memberType", "property" } };
                }
            }
            
            var members = target.GetType().GetMember(memberName);
            if (members != null && members.Length > 0)
            {
                var member = members[0];
                if (member is PropertyInfo)
                {
                    return new Dictionary<string, object> { { "type", "meta" }, { "memberType", "property" } };
                }
            }
            
            return new Dictionary<string, object> { { "type", "meta" }, { "memberType", "method" } };
        }

        if (action == "GetTypeName")
        {
            var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
            string typeName;
            if (target is Type)
                typeName = ((Type)target).FullName;
            else
                typeName = target.GetType().FullName;
            return new Dictionary<string, object> { { "typeName", typeName } };
        }

        if (action == "InspectType")
        {
            var typeName = cmd["typeName"].ToString();
            var rawList = cmd["memberNames"] as System.Collections.IEnumerable;
            var emptyResult = new Dictionary<string, object>
            {
                { "typeName", typeName },
                { "members", new Dictionary<string, string>() }
            };
            if (rawList == null) return emptyResult;

            var type = Type.GetType(typeName);
            if (type == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    type = asm.GetType(typeName);
                    if (type != null) break;
                }
            }
            if (type == null) return emptyResult;

            var result = new Dictionary<string, object>();
            result["typeName"] = typeName;
            var members = new Dictionary<string, string>();

            foreach (object item in rawList)
            {
                var memberName = item.ToString();
                var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (prop != null)
                {
                    members[memberName] = "property";
                    continue;
                }
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                var found = false;
                foreach (var m in methods)
                {
                    if (m.Name == memberName) { found = true; break; }
                }
                members[memberName] = found ? "method" : "unknown";
            }

            result["members"] = members;
            return result;
        }

        if (action == "RemoveEvent")
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

        if (action == "AddEvent")
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
                
                // Build a lambda matching the exact delegate signature, box all args, call SendEventToJs.
                // Expression.Lambda handles any parameter count with no fixed upper limit.
                var paramExprs = new System.Linq.Expressions.ParameterExpression[parameters.Length];
                for (var pi = 0; pi < parameters.Length; pi++)
                {
                    paramExprs[pi] = System.Linq.Expressions.Expression.Parameter(parameters[pi].ParameterType, "p" + pi);
                }

                var boxedExprs = new System.Linq.Expressions.Expression[parameters.Length];
                for (var pi = 0; pi < parameters.Length; pi++)
                {
                    boxedExprs[pi] = System.Linq.Expressions.Expression.Convert(paramExprs[pi], typeof(object));
                }

                var argsArrayExpr = System.Linq.Expressions.Expression.NewArrayInit(typeof(object), boxedExprs);
                var sendMethod = typeof(Reflection).GetMethod("SendEventToJs", BindingFlags.NonPublic | BindingFlags.Static);
                var cbIdExpr = System.Linq.Expressions.Expression.Constant(cbId, typeof(string));
                var callExpr = System.Linq.Expressions.Expression.Call(sendMethod, cbIdExpr, argsArrayExpr);
                var lambdaExpr = System.Linq.Expressions.Expression.Lambda(delegateType, callExpr, paramExprs);
                Delegate handler = lambdaExpr.Compile();

                eventInfo.AddEventHandler(target, handler);
                // Persist handler so RemoveEvent can unsubscribe later
                var storeKey = cmd["targetId"].ToString() + ":" + eventName + ":" + cbId;
                BridgeState.EventHandlerStore[storeKey] = handler;
            }
            
            return new Dictionary<string, object> { { "type", "void" } };
        }

        if (action == "New")
        {
            var type = (Type)BridgeState.ObjectStore[cmd["typeId"].ToString()];
            var argsObj = cmd.ContainsKey("args") ? cmd["args"] : null;
            var args = Protocol.ResolveArgs(argsObj);
            
            object obj;
            try
            {
                if (args.Length == 0)
                {
                    obj = Activator.CreateInstance(type);
                }
                else
                {
                    var constructors = type.GetConstructors();
                    Exception lastException = null;
                    
                    foreach (var ctor in constructors)
                    {
                        var parameters = ctor.GetParameters();
                        if (parameters.Length != args.Length) continue;
                        
                        var convertedArgs = new object[args.Length];
                        var match = true;
                        
                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var pType = parameters[i].ParameterType;
                            var arg = args[i];
                            
                            if (arg == null)
                            {
                                convertedArgs[i] = null;
                            }
                            else if (pType.IsAssignableFrom(arg.GetType()))
                            {
                                convertedArgs[i] = arg;
                            }
                            else if (arg is IConvertible && !pType.IsAssignableFrom(typeof(string)))
                            {
                                try
                                {
                                    convertedArgs[i] = Convert.ChangeType(arg, pType);
                                }
                                catch
                                {
                                    match = false;
                                    break;
                                }
                            }
                            else if (pType.IsEnum && arg is int)
                            {
                                convertedArgs[i] = Enum.ToObject(pType, arg);
                            }
                            else
                            {
                                match = false;
                                break;
                            }
                        }
                        
                        if (match)
                        {
                            try
                            {
                                obj = ctor.Invoke(convertedArgs);
                                return Protocol.ConvertToProtocol(obj);
                            }
                            catch (Exception ex)
                            {
                                lastException = ex;
                            }
                        }
                    }
                    
                    if (lastException != null)
                    {
                        throw lastException;
                    }
                    
                    obj = Activator.CreateInstance(type);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("New Error: " + ex.Message);
            }
            
            return Protocol.ConvertToProtocol(obj);
        }

        if (action == "Invoke")
        {
            var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
            var name = cmd["methodName"].ToString();
            var argsObj = cmd.ContainsKey("args") ? cmd["args"] : null;
            var realArgs = Protocol.ResolveArgs(argsObj);

            var isStatic = target is Type;
            var targetType = isStatic ? (Type)target : target.GetType();

            // Auto-detect Application.Run() — treat as InvokeDetached so Node.js is not blocked.
            // Works for WinForms (System.Windows.Forms.Application) and WPF (System.Windows.Application).
            if (name == "Run" && targetType.Name.IndexOf("Application", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                MethodInfo runMethod = null;
                var bindFlagsRun = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                foreach (var m in targetType.GetMethods(bindFlagsRun))
                {
                    if (m.Name != "Run") continue;
                    if (m.GetParameters().Length != realArgs.Length) continue;
                    runMethod = m;
                    break;
                }
                if (runMethod != null)
                {
                    // Install WinForms WindowsFormsSynchronizationContext before Application.Run starts
                    foreach (var loadedAsm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (loadedAsm.GetName().Name != "System.Windows.Forms") continue;
                        var wfCtxType = loadedAsm.GetType("System.Windows.Forms.WindowsFormsSynchronizationContext");
                        if (wfCtxType == null) break;
                        var wfSc = Activator.CreateInstance(wfCtxType) as SynchronizationContext;
                        if (wfSc != null)
                        {
                            SynchronizationContext.SetSynchronizationContext(wfSc);
                            PsHost.MainSyncContext = wfSc;
                        }
                        break;
                    }
                    // Install WPF DispatcherSynchronizationContext
                    foreach (var loadedAsm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (loadedAsm.GetName().Name != "WindowsBase") continue;
                        var dispType = loadedAsm.GetType("System.Windows.Threading.Dispatcher");
                        var syncCtxType = loadedAsm.GetType("System.Windows.Threading.DispatcherSynchronizationContext");
                        if (dispType == null || syncCtxType == null) break;
                        var dispProp = dispType.GetProperty("CurrentDispatcher");
                        if (dispProp == null) break;
                        var dispatcher = dispProp.GetValue(null, null);
                        if (dispatcher == null) break;
                        var wpfSc = Activator.CreateInstance(syncCtxType, new object[] { dispatcher }) as SynchronizationContext;
                        if (wpfSc != null) PsHost.MainSyncContext = wpfSc;
                        break;
                    }

                    // Pre-send ok so Node.js is unblocked before we block in Application.Run
                    var preResp = SimpleJson.Serialize(new Dictionary<string, object>
                    {
                        { "type", "primitive" }, { "value", true }
                    });
                    lock (BridgeState.Writer) { BridgeState.Writer.WriteLine(preResp); }

                    // Block here — SynchronizationContext.Post routes incoming commands to this message loop
                    var convertedRunArgs = ConvertArgsForMethod(runMethod, realArgs);
                    runMethod.Invoke(isStatic ? null : target, convertedRunArgs);
                    Environment.Exit(0);
                    return new Dictionary<string, object> { { "__skipResponse", true } };
                }
            }

            // Auto-detect ShowDialog() — async pending pattern so Node.js can poll events during dialog.
            // 1. Ensure a WPF DispatcherSynchronizationContext exists for this thread so the reader
            //    thread can post incoming commands (e.g. DialogResult=true) to the nested WPF loop.
            // 2. Pre-send {type:'showDialogPending', callbackId} so Node.js resumes polling immediately.
            // 3. Call ShowDialog (blocks here in WPF nested loop; commands dispatched via Post).
            // 4. Enqueue result as an event so Node.js spin-poll picks it up synchronously.
            if (name == "ShowDialog" && realArgs.Length == 0)
            {
                MethodInfo showDialogMethod = null;
                var bindFlagsDialog = BindingFlags.Public | BindingFlags.Instance;
                foreach (var m in targetType.GetMethods(bindFlagsDialog))
                {
                    if (m.Name != "ShowDialog") continue;
                    if (m.GetParameters().Length != 0) continue;
                    showDialogMethod = m;
                    break;
                }
                if (showDialogMethod != null)
                {
                    // Ensure a WPF DispatcherSynchronizationContext is set BEFORE pre-sending,
                    // so commands arriving after the pre-send are routed to the nested WPF loop.
                    // Dispatcher.CurrentDispatcher creates a dispatcher for this thread if needed.
                    if (PsHost.MainSyncContext == null)
                    {
                        foreach (var loadedAsm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            if (loadedAsm.GetName().Name != "WindowsBase") continue;
                            var dispType2 = loadedAsm.GetType("System.Windows.Threading.Dispatcher");
                            var syncCtxType2 = loadedAsm.GetType("System.Windows.Threading.DispatcherSynchronizationContext");
                            if (dispType2 == null || syncCtxType2 == null) break;
                            var dispProp2 = dispType2.GetProperty("CurrentDispatcher");
                            if (dispProp2 == null) break;
                            var dispatcher2 = dispProp2.GetValue(null, null);
                            if (dispatcher2 == null) break;
                            var sc2 = Activator.CreateInstance(syncCtxType2, new object[] { dispatcher2 }) as SynchronizationContext;
                            if (sc2 != null)
                            {
                                PsHost.MainSyncContext = sc2;
                                SynchronizationContext.SetSynchronizationContext(sc2);
                            }
                            break;
                        }
                    }

                    var dialogCallbackId = Guid.NewGuid().ToString();

                    // Pre-send pending token — Node.js resumes its event loop for polling
                    var pendingResp = SimpleJson.Serialize(new Dictionary<string, object>
                    {
                        { "type", "showDialogPending" }, { "callbackId", dialogCallbackId }
                    });
                    lock (BridgeState.Writer) { BridgeState.Writer.WriteLine(pendingResp); }

                    // ShowDialog runs a nested WPF dispatcher loop.
                    // Incoming IPC commands (e.g. setting DialogResult from a click handler) are
                    // dispatched via MainSyncContext.Post → DrainCommandQueue during that loop.
                    try
                    {
                        var dialogResult = showDialogMethod.Invoke(target, new object[0]);
                        var protoArgs = new List<object> { Protocol.ConvertToProtocol(dialogResult) };
                        var resultEvt = SimpleJson.Serialize(new Dictionary<string, object>
                        {
                            { "type", "event" }, { "callbackId", dialogCallbackId }, { "args", protoArgs }
                        });
                        BridgeState.EventQueue.Enqueue(resultEvt);
                    }
                    catch (Exception ex)
                    {
                        var innerMsg2 = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        var errEvt = SimpleJson.Serialize(new Dictionary<string, object>
                        {
                            { "type", "event" }, { "callbackId", dialogCallbackId }, { "error", innerMsg2 }
                        });
                        BridgeState.EventQueue.Enqueue(errEvt);
                    }

                    return new Dictionary<string, object> { { "__skipResponse", true } };
                }
            }

            // Detect ref/out methods early and dispatch to dedicated handler.
            // node-api-dotnet style: C# bool F(ref string a, out int b) => JS { result, a, b }
            {
                var bindingFlagsCheck = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;
                var methodsCheck = targetType.GetMethods(bindingFlagsCheck);
                foreach (var m in methodsCheck)
                {
                    if (m.Name != name) continue;
                    foreach (var p in m.GetParameters())
                    {
                        if (p.IsOut || p.ParameterType.IsByRef)
                        {
                            return InvokeRefOutMethod(targetType, name, isStatic,
                                isStatic ? null : target, realArgs, bindingFlagsCheck);
                        }
                    }
                }
            }

            if (isStatic && realArgs.Length == 0)
            {
                try
                {
                    var prop = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        var result = prop.GetValue(null);
                        return Protocol.ConvertToProtocol(result);
                    }
                    
                    var field = targetType.GetField(name, BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        var result = field.GetValue(null);
                        return Protocol.ConvertToProtocol(result);
                    }
                }
                catch { }
            }

            if (!isStatic && realArgs.Length > 0)
            {
                var prop = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        var value = realArgs[0];
                        if (value != null && !prop.PropertyType.IsAssignableFrom(value.GetType()))
                        {
                            if (prop.PropertyType.IsEnum)
                            {
                                var intValue = value is long ? (int)(long)value : (int)value;
                                value = Enum.ToObject(prop.PropertyType, intValue);
                            }
                            else if (prop.PropertyType.IsValueType && !prop.PropertyType.IsPrimitive)
                            {
                                // Handle structs like FontWeight - try to create from integer
                                var intValue = value is long ? (int)(long)value : (int)value;
                                var ctor = prop.PropertyType.GetConstructor(new[] { typeof(int) });
                                if (ctor != null)
                                {
                                    value = ctor.Invoke(new object[] { intValue });
                                }
                                else if (value is IConvertible)
                                {
                                    value = Convert.ChangeType(value, prop.PropertyType);
                                }
                            }
                            else if (value is IConvertible)
                            {
                                value = Convert.ChangeType(value, prop.PropertyType);
                            }
                        }

                        // If we captured a UI thread SynchronizationContext, use it to marshal the property set
                        if (PsHost.MainSyncContext != null)
                        {
                            PsHost.MainSyncContext.Send((_) => prop.SetValue(target, value), null);
                        }
                        else
                        {
                            // Fallback: for WinForms Control, use Invoke if available
                            var targetAsControl = target as System.Windows.Forms.Control;
                            if (targetAsControl != null && targetAsControl.InvokeRequired)
                            {
                                targetAsControl.Invoke(new System.Action(() => prop.SetValue(target, value)));
                            }
                            else
                            {
                                prop.SetValue(target, value);
                            }
                        }
                        return new Dictionary<string, object> { { "type", "void" } };
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Set Property Error '" + name + "': " + ex.Message);
                    }
                }
            }

            if (!isStatic && realArgs.Length == 0)
            {
                var prop = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    return Protocol.ConvertToProtocol(prop.GetValue(target));
                }
            }

            try
            {
                object result = null;
                var bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;

                var hasDelegateArg = false;
                foreach (var arg in realArgs)
                {
                    if (arg is Delegate || arg is Func<object, object, object, object, object>)
                    {
                        hasDelegateArg = true;
                        break;
                    }
                }

                var manualSuccess = false;

                if (hasDelegateArg)
                {
                    var methods = targetType.GetMethods(bindingFlags).Where(m => m.Name == name).ToArray();
                    
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length != realArgs.Length) continue;
                        
                        var tempArgs = new object[realArgs.Length];
                        Array.Copy(realArgs, tempArgs, realArgs.Length);
                        var match = true;
                        
                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var pType = parameters[i].ParameterType;
                            var arg = tempArgs[i];
                            
                            if (arg is Func<object, object, object, object, object>)
                            {
                                var func = (Func<object, object, object, object, object>)arg;
                                if (pType == typeof(Delegate))
                                {
                                    try
                                    {
                                        tempArgs[i] = (Action)(() => func(null, null, null, null));
                                    }
                                    catch
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                                else if (typeof(Delegate).IsAssignableFrom(pType))
                                {
                                    try
                                    {
                                        var invokeMeth = pType.GetMethod("Invoke");
                                        var dlgParams = invokeMeth.GetParameters();
                                        var pExprs = new System.Linq.Expressions.ParameterExpression[dlgParams.Length];
                                        for (var pi = 0; pi < dlgParams.Length; pi++)
                                            pExprs[pi] = System.Linq.Expressions.Expression.Parameter(dlgParams[pi].ParameterType, "p" + pi);

                                        var callArgs = new System.Linq.Expressions.Expression[4];
                                        for (var pi = 0; pi < 4; pi++)
                                        {
                                            if (pi < dlgParams.Length)
                                                callArgs[pi] = System.Linq.Expressions.Expression.Convert(pExprs[pi], typeof(object));
                                            else
                                                callArgs[pi] = System.Linq.Expressions.Expression.Constant(null, typeof(object));
                                        }

                                        var funcConst = System.Linq.Expressions.Expression.Constant(func);
                                        var funcInvokeMeth = typeof(Func<object, object, object, object, object>).GetMethod("Invoke");
                                        var callExpr = System.Linq.Expressions.Expression.Call(funcConst, funcInvokeMeth, callArgs);

                                        System.Linq.Expressions.Expression body;
                                        if (invokeMeth.ReturnType == typeof(void))
                                            body = System.Linq.Expressions.Expression.Block(
                                                callExpr,
                                                System.Linq.Expressions.Expression.Empty());
                                        else
                                            body = System.Linq.Expressions.Expression.Convert(callExpr, invokeMeth.ReturnType);

                                        tempArgs[i] = System.Linq.Expressions.Expression.Lambda(pType, body, pExprs).Compile();
                                    }
                                    catch
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    match = false;
                                    break;
                                }

                                if (tempArgs[i] == null)
                                {
                                    match = false;
                                    break;
                                }
                            }
                            else if (arg != null && !pType.IsAssignableFrom(arg.GetType()))
                            {
                                if (pType.IsEnum && (arg is int || arg is long))
                                {
                                    var intVal = arg is long ? (int)(long)arg : (int)arg;
                                    tempArgs[i] = Enum.ToObject(pType, intVal);
                                }
                                else if (IsNumericType(arg.GetType()) && IsNumericType(pType))
                                {
                                    try
                                    {
                                        tempArgs[i] = Convert.ChangeType(arg, pType);
                                    }
                                    catch
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                                else
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }
                        
                        if (match)
                        {
                            try
                            {
                                var instanceToCall = isStatic ? null : target;
                                result = method.Invoke(instanceToCall, tempArgs);
                                manualSuccess = true;
                                break;
                            }
                            catch { }
                        }
                    }
                }

                if (!manualSuccess)
                {
                    var methods = targetType.GetMethods(bindingFlags).Where(m => m.Name == name).ToArray();
                    
                    if (methods.Length == 1)
                    {
                        var method = methods[0];
                        var convertedArgs = ConvertArgsForMethod(method, realArgs);
                        result = method.Invoke(isStatic ? null : target, convertedArgs);
                    }
                    else if (methods.Length > 1 && realArgs.Length > 0)
                    {
                        MethodInfo bestMethod = FindBestMatchingMethod(methods, realArgs);
                        if (bestMethod != null)
                        {
                            var convertedArgs = ConvertArgsForMethod(bestMethod, realArgs);
                            result = bestMethod.Invoke(isStatic ? null : target, convertedArgs);
                        }
                        else
                        {
                            result = methods[0].Invoke(isStatic ? null : target, realArgs);
                        }
                    }
                    else if (methods.Length > 0)
                    {
                        result = methods[0].Invoke(isStatic ? null : target, realArgs);
                    }
                }

                return Protocol.ConvertToProtocol(result);

            }
            catch (Exception ex)
            {
                if (realArgs.Length == 0)
                {
                    try
                    {
                        object val = null;
                        if (isStatic)
                        {
                            var prop = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                            if (prop != null) val = prop.GetValue(null);
                            else
                            {
                                var field = targetType.GetField(name, BindingFlags.Public | BindingFlags.Static);
                                if (field != null) val = field.GetValue(null);
                            }
                        }
                        else
                        {
                            var prop = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                            if (prop != null) val = prop.GetValue(target);
                            else
                            {
                                var field = targetType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                                if (field != null) val = field.GetValue(target);
                            }
                        }
                        if (val != null)
                        {
                            return Protocol.ConvertToProtocol(val);
                        }
                    }
                    catch { }
                }
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception("Invoke Error (" + name + "): " + innerMsg);
            }
        }
        
        if (action == "AwaitTask")
        {
            // Async mode (new): callbackId present — attaches ContinueWith, returns immediately.
            if (cmd.ContainsKey("callbackId"))
            {
                var targetId2 = cmd["targetId"].ToString();
                var cbId = cmd["callbackId"].ToString();
                var task2 = BridgeState.ObjectStore[targetId2] as Task;
                if (task2 == null)
                    throw new Exception("AwaitTask: target is not a Task");

                var capturedWriter = BridgeState.Writer;
                task2.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var errMsg = t.Exception != null
                            ? (t.Exception.InnerException != null
                                ? t.Exception.InnerException.Message
                                : t.Exception.Message)
                            : "Task faulted";
                        var errJson = SimpleJson.Serialize(new Dictionary<string, object>
                        {
                            { "type", "event" }, { "callbackId", cbId }, { "error", errMsg }
                        });
                        BridgeState.EventQueue.Enqueue(errJson);
                        return;
                    }
                    object resultVal;
                    var resultProp = t.GetType().GetProperty("Result");
                    if (resultProp != null)
                    {
                        try { resultVal = Protocol.ConvertToProtocol(resultProp.GetValue(t, null)); }
                        catch { resultVal = new Dictionary<string, object> { { "type", "void" } }; }
                    }
                    else
                    {
                        resultVal = new Dictionary<string, object> { { "type", "void" } };
                    }
                    var protoArgs = new List<object> { resultVal };
                    var msgJson = SimpleJson.Serialize(new Dictionary<string, object>
                    {
                        { "type", "event" }, { "callbackId", cbId }, { "args", protoArgs }
                    });
                    BridgeState.EventQueue.Enqueue(msgJson);
                });
                return new Dictionary<string, object> { { "type", "void" } };
            }

            // Sync mode (legacy): no callbackId — blocks on task.Wait() and returns result.
            var taskId = cmd["taskId"].ToString();
            var task = (Task)BridgeState.ObjectStore[taskId];
            try
            {
                task.Wait();
                var taskType = task.GetType();
                if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var result = taskType.GetProperty("Result").GetValue(task);
                    return Protocol.ConvertToProtocol(result);
                }
                return new Dictionary<string, object> { { "type", "void" } };
            }
            catch (AggregateException ae)
            {
                var innerMsg = ae.InnerException != null ? ae.InnerException.Message : ae.ToString();
                throw new Exception("Task Error: " + innerMsg);
            }
        }
        
        if (action == "LoadAssembly")
        {
            var assemblyName = cmd["assemblyName"].ToString();
            Assembly asm = null;
            try
            {
                asm = Assembly.Load(assemblyName);
            }
            catch
            {
                // Fallback 1: search already-loaded assemblies by simple name
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        asm = a;
                        break;
                    }
                }
                // Fallback 2: probe the .NET runtime directory (handles framework assemblies like System.Windows.Forms)
                if (asm == null)
                {
                    string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                    string dllPath = Path.Combine(runtimeDir, assemblyName + ".dll");
                    if (File.Exists(dllPath))
                    {
                        try { asm = Assembly.LoadFrom(dllPath); } catch { }
                    }
                }
                // Fallback 3: scan the .NET 4 GAC (handles WPF assemblies like PresentationFramework)
                if (asm == null)
                {
                    string gacRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        @"Microsoft.NET\assembly");
                    string[] subDirs = new string[] { "GAC_MSIL", "GAC_64", "GAC_32" };
                    foreach (var subDir in subDirs)
                    {
                        string asmDir = Path.Combine(gacRoot, subDir, assemblyName);
                        if (!Directory.Exists(asmDir)) continue;
                        foreach (var verDir in Directory.GetDirectories(asmDir))
                        {
                            string gacDll = Path.Combine(verDir, assemblyName + ".dll");
                            if (File.Exists(gacDll))
                            {
                                try { asm = Assembly.LoadFrom(gacDll); } catch { }
                                if (asm != null) break;
                            }
                        }
                        if (asm != null) break;
                    }
                }
            }
            if (asm == null)
            {
                throw new Exception("Failed to load assembly: " + assemblyName);
            }
            return Protocol.ConvertToProtocol(asm);
        }
        
        if (action == "LoadFrom")
        {
            var filePath = cmd["filePath"].ToString();
            if (!File.Exists(filePath))
            {
                throw new Exception("File not found: " + filePath);
            }
            // Add the DLL's directory to PATH so native side-by-side dependencies
            // (e.g. WebView2Loader.dll) are discoverable by the Windows DLL loader.
            string dllDir = Path.GetDirectoryName(filePath);
            if (dllDir != null && dllDir.Length > 0)
            {
                string currentPath = Environment.GetEnvironmentVariable("PATH");
                if (currentPath == null) currentPath = "";
                if (currentPath.IndexOf(dllDir, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Environment.SetEnvironmentVariable("PATH", dllDir + ";" + currentPath);
                }
            }
            Assembly asm = null;
            try
            {
                asm = Assembly.LoadFrom(filePath);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load assembly from file: " + ex.Message);
            }
            return Protocol.ConvertToProtocol(asm);
        }

        if (action == "Release")
        {
            var releaseId = cmd["targetId"].ToString();
            // Remove all event handlers stored for this object so delegates can be GC'd
            var prefix = releaseId + ":";
            foreach (var key in BridgeState.EventHandlerStore.Keys.ToArray())
            {
                if (key.StartsWith(prefix))
                {
                    Delegate ignored;
                    BridgeState.EventHandlerStore.TryRemove(key, out ignored);
                }
            }
            Protocol.RemoveBridgeObject(releaseId);
            return new Dictionary<string, object> { { "type", "void" } };
        }

        if (action == "SetResolvingCallback")
        {
            var cbId = cmd["callbackId"].ToString();

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var writer = BridgeState.Writer;
                if (writer == null) return null;

                var protoArgs = new List<Dictionary<string, object>>();
                protoArgs.Add(new Dictionary<string, object> { { "type", "primitive" }, { "value", args.Name } });

                var msg = new Dictionary<string, object>
                {
                    { "type", "event" },
                    { "callbackId", cbId },
                    { "args", protoArgs }
                };
                var json = SimpleJson.Serialize(msg);
                writer.WriteLine(json);

                object result = null;
                try
                {
                    if (PsHost.ProcessNestedCommands != null)
                        result = PsHost.ProcessNestedCommands();
                }
                catch { }

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
        
        if (action == "AddType")
        {
            var sourceCode = cmd["source"].ToString();
            var refsRaw = cmd.ContainsKey("references") ? cmd["references"] : null;
            var refList = new List<string>();
            if (refsRaw is List<object>)
                foreach (var r in (List<object>)refsRaw) refList.Add(r.ToString());

            var providerType = Type.GetType("Microsoft.CSharp.CSharpCodeProvider, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            if (providerType == null)
                providerType = Type.GetType("Microsoft.CSharp.CSharpCodeProvider");
            if (providerType == null)
                throw new Exception("AddType: CSharpCodeProvider not found");

            var provider = Activator.CreateInstance(providerType);
            var paramsType = providerType.Assembly.GetType("System.CodeDom.Compiler.CompilerParameters");
            var compileParams = Activator.CreateInstance(paramsType);
            paramsType.GetProperty("GenerateInMemory").SetValue(compileParams, true, null);
            paramsType.GetProperty("GenerateExecutable").SetValue(compileParams, false, null);

            var referencedAssemblies = (System.Collections.Specialized.StringCollection)
                paramsType.GetProperty("ReferencedAssemblies").GetValue(compileParams, null);
            foreach (var r in refList) referencedAssemblies.Add(r);

            // Add all currently-loaded assemblies by full path (covers WPF, WebView2, etc.)
            // Also try to load WPF assemblies from WPF install dir if not yet loaded.
            var wpfNames = new string[] { "WindowsBase", "PresentationCore", "PresentationFramework" };
            foreach (var wpfName in wpfNames)
            {
                bool found = false;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == wpfName && !string.IsNullOrEmpty(a.Location))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    try
                    {
                        System.Reflection.Assembly.Load(wpfName + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    }
                    catch { }
                }
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var loc = asm.Location;
                    if (!string.IsNullOrEmpty(loc)) referencedAssemblies.Add(loc);
                }
                catch { }
            }

            var compileMethod = providerType.GetMethod("CompileAssemblyFromSource",
                new Type[] { paramsType, typeof(string[]) });
            var result = compileMethod.Invoke(provider,
                new object[] { compileParams, new string[] { sourceCode } });

            var errorsObj = result.GetType().GetProperty("Errors").GetValue(result, null);
            var count = (int)errorsObj.GetType().GetProperty("Count").GetValue(errorsObj, null);
            var indexer = errorsObj.GetType().GetMethod("get_Item");
            var sb = new System.Text.StringBuilder();
            for (var ei = 0; ei < count; ei++)
            {
                var err = indexer.Invoke(errorsObj, new object[] { ei });
                var isWarning = (bool)err.GetType().GetProperty("IsWarning").GetValue(err, null);
                if (!isWarning)
                    sb.AppendLine(err.GetType().GetProperty("ErrorText").GetValue(err, null).ToString());
            }
            if (sb.Length > 0)
                throw new Exception("AddType compile errors:\n" + sb.ToString());

            var compiledAsm = (Assembly)result.GetType().GetProperty("CompiledAssembly").GetValue(result, null);
            var asmId = Guid.NewGuid().ToString();
            BridgeState.ObjectStore[asmId] = compiledAsm;
            return new Dictionary<string, object>
            {
                { "type", "ref" }, { "id", asmId }, { "typeName", "Assembly" }
            };
        }

        if (action == "InvokeDetached")
        {
            var targetId = cmd["targetId"].ToString();
            var methodName = cmd["methodName"].ToString();
            var target = BridgeState.ObjectStore[targetId];
            var rawArgs = Protocol.ResolveArgs(cmd.ContainsKey("args") ? cmd["args"] : null);

            MethodInfo detachedMethod = null;
            foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != methodName) continue;
                if (m.GetParameters().Length != rawArgs.Length) continue;
                detachedMethod = m;
                break;
            }
            if (detachedMethod == null)
                throw new Exception("InvokeDetached: method not found: " + methodName);

            // Capture WPF DispatcherSynchronizationContext before blocking
            foreach (var loadedAsm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loadedAsm.GetName().Name != "WindowsBase") continue;
                var dispType = loadedAsm.GetType("System.Windows.Threading.Dispatcher");
                var syncCtxType = loadedAsm.GetType("System.Windows.Threading.DispatcherSynchronizationContext");
                if (dispType == null || syncCtxType == null) break;
                var dispProp = dispType.GetProperty("CurrentDispatcher");
                if (dispProp == null) break;
                var dispatcher = dispProp.GetValue(null, null);
                if (dispatcher == null) break;
                var sc = Activator.CreateInstance(syncCtxType, new object[] { dispatcher }) as SynchronizationContext;
                if (sc != null) PsHost.MainSyncContext = sc;
                break;
            }

            // Pre-send ok response BEFORE the blocking call
            var preResponse = SimpleJson.Serialize(new Dictionary<string, object>
            {
                { "type", "primitive" }, { "value", true }
            });
            lock (BridgeState.Writer) { BridgeState.Writer.WriteLine(preResponse); }

            detachedMethod.Invoke(target, rawArgs);
            Environment.Exit(0);
            return new Dictionary<string, object> { { "__skipResponse", true } };
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }

    private static object[] ConvertArgsForMethod(MethodInfo method, object[] args)
    {
        if (args == null || args.Length == 0) return args;
        
        var parameters = method.GetParameters();
        if (parameters.Length != args.Length) return args;
        
        var convertedArgs = new object[args.Length];
        Array.Copy(args, convertedArgs, args.Length);
        
        for (var i = 0; i < parameters.Length; i++)
        {
            var pType = parameters[i].ParameterType;
            var arg = args[i];
            
            if (arg == null || pType.IsAssignableFrom(arg.GetType()))
            {
                continue;
            }
            
            var argType = arg.GetType();
            
            if (pType.IsEnum)
            {
                if (arg is int || arg is long)
                {
                    var intVal = arg is long ? (int)(long)arg : (int)arg;
                    convertedArgs[i] = Enum.ToObject(pType, intVal);
                }
            }
            else if (pType == typeof(TimeSpan))
            {
                if (arg is long)
                    convertedArgs[i] = TimeSpan.FromMilliseconds((long)arg);
                else if (arg is int)
                    convertedArgs[i] = TimeSpan.FromMilliseconds((int)arg);
                else if (arg is double)
                    convertedArgs[i] = TimeSpan.FromMilliseconds((double)arg);
            }
            else if ((pType == typeof(DateTime) || pType == typeof(DateTimeOffset)) && arg is string)
            {
                DateTime dt;
                if (DateTime.TryParse((string)arg, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                    convertedArgs[i] = pType == typeof(DateTimeOffset)
                        ? (object)new DateTimeOffset(dt) : dt;
            }
            else if (argType == typeof(string) && (pType == typeof(string) || pType == typeof(object)))
            {
                convertedArgs[i] = arg;
            }
            else if (IsNumericType(argType) && IsNumericType(pType))
            {
                try
                {
                    convertedArgs[i] = Convert.ChangeType(arg, pType);
                }
                catch { }
            }
            else if (arg is IConvertible && pType != typeof(string))
            {
                try
                {
                    convertedArgs[i] = Convert.ChangeType(arg, pType);
                }
                catch { }
            }
            else if (pType.IsArray && !argType.IsArray)
            {
                // params T[] — single value supplied for a params array parameter
                var elemType = pType.GetElementType();
                if (elemType != null && elemType.IsAssignableFrom(argType))
                {
                    var arr = Array.CreateInstance(elemType, 1);
                    arr.SetValue(arg, 0);
                    convertedArgs[i] = arr;
                }
            }
        }

        return convertedArgs;
    }
    
    private static MethodInfo FindBestMatchingMethod(MethodInfo[] methods, object[] args)
    {
        if (methods == null || methods.Length == 0 || args == null || args.Length == 0)
            return null;
        
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            // Count only non-out params for arity matching (out params are output-only)
            var nonOutCount = 0;
            foreach (var p in parameters) { if (!p.IsOut) nonOutCount++; }
            if (nonOutCount != args.Length) continue;
            
            var match = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                var arg = args[i];
                var argType = arg != null ? arg.GetType() : null;
                
                if (argType == null) continue;
                
                if (!pType.IsAssignableFrom(argType))
                {
                    if (argType == typeof(string))
                    {
                        if (pType != typeof(string) && pType != typeof(object))
                        {
                            match = false;
                            break;
                        }
                        continue;
                    }
                    
                    if (pType.IsEnum)
                    {
                        if (!(arg is int || arg is long))
                        {
                            match = false;
                            break;
                        }
                    }
                    else if (pType == typeof(TimeSpan) && IsNumericType(argType))
                    {
                        continue;
                    }
                    else if (IsNumericType(argType) && IsNumericType(pType))
                    {
                        continue;
                    }
                    else if (args[i] is IConvertible && pType != typeof(string))
                    {
                        continue;
                    }
                    else
                    {
                        match = false;
                        break;
                    }
                }
            }
            
            if (match) return method;
        }
        
        return null;
    }
    
    // Convert a single JS argument to the expected .NET type.
    private static object ConvertSingleArg(object arg, Type targetType)
    {
        if (arg == null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        if (targetType.IsAssignableFrom(arg.GetType())) return arg;
        if (targetType.IsEnum && (arg is int || arg is long))
            return Enum.ToObject(targetType, arg is long ? (int)(long)arg : (int)arg);
        if (targetType == typeof(TimeSpan) && (arg is long || arg is int || arg is double))
            return TimeSpan.FromMilliseconds(Convert.ToDouble(arg));
        if ((targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset)) && arg is string)
        {
            DateTime dt;
            if (DateTime.TryParse((string)arg, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return targetType == typeof(DateTimeOffset) ? (object)new DateTimeOffset(dt) : dt;
        }
        if (IsNumericType(arg.GetType()) && IsNumericType(targetType))
        {
            try { return Convert.ChangeType(arg, targetType); } catch { }
        }
        if (arg is IConvertible && targetType != typeof(string))
        {
            try { return Convert.ChangeType(arg, targetType); } catch { }
        }
        return arg;
    }

    // Invoke a method that has ref/out parameters.
    // node-api-dotnet style: C# bool F(ref string a, out int b) => JS { result, a, b }
    private static Dictionary<string, object> InvokeRefOutMethod(
        Type targetType, string name, bool isStatic, object target,
        object[] realArgs, BindingFlags bindingFlags)
    {
        var methods = targetType.GetMethods(bindingFlags);
        MethodInfo bestMethod = null;
        foreach (var m in methods)
        {
            if (m.Name != name) continue;
            var parms = m.GetParameters();
            var nonOutCount = 0;
            foreach (var p in parms) { if (!p.IsOut) nonOutCount++; }
            if (nonOutCount == realArgs.Length) { bestMethod = m; break; }
        }
        if (bestMethod == null)
            throw new Exception("No matching ref/out overload found for: " + name);

        var parameters = bestMethod.GetParameters();
        var invokeArgs = new object[parameters.Length];
        var inputIdx = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var actualType = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
            if (p.IsOut)
                invokeArgs[i] = actualType.IsValueType ? Activator.CreateInstance(actualType) : null;
            else
            {
                var rawArg = inputIdx < realArgs.Length ? realArgs[inputIdx] : null;
                invokeArgs[i] = ConvertSingleArg(rawArg, actualType);
                inputIdx++;
            }
        }

        object result = null;
        try
        {
            result = bestMethod.Invoke(isStatic ? null : target, invokeArgs);
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception("Invoke Error (" + name + "): " + inner);
        }

        // Collect ref/out output values
        var outs = new Dictionary<string, object>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (!p.IsOut && !p.ParameterType.IsByRef) continue;
            outs[p.Name] = invokeArgs[i] == null
                ? new Dictionary<string, object> { { "type", "null" } }
                : Protocol.ConvertToProtocol(invokeArgs[i]);
        }

        var resultProto = result == null
            ? new Dictionary<string, object> { { "type", "null" } }
            : Protocol.ConvertToProtocol(result);

        // Non-void method with no actual out/ref outputs: return plain result
        if (outs.Count == 0)
            return resultProto;

        return new Dictionary<string, object>
        {
            { "type", "refout" },
            { "result", resultProto },
            { "outs", outs }
        };
    }

    private static void SendEventToJs(string cbId, object[] args)
    {
        var protoArgs = new List<Dictionary<string, object>>();
        foreach (var arg in args)
        {
            protoArgs.Add(arg == null
                ? new Dictionary<string, object> { { "type", "null" } }
                : Protocol.ConvertToProtocol(arg));
        }
        var msg = new Dictionary<string, object>
        {
            { "type", "event" },
            { "callbackId", cbId },
            { "args", protoArgs }
        };
        BridgeState.EventQueue.Enqueue(SimpleJson.Serialize(msg));
    }

    private static string InferFrameworkMoniker()
    {
        var frameworkDescription = RuntimeInformation.FrameworkDescription;
        var environmentVersion = Environment.Version;
        
        if (frameworkDescription.StartsWith(".NET Framework"))
        {
            var versionParts = environmentVersion.ToString().Split('.');
            if (versionParts.Length >= 2)
            {
                int major = int.Parse(versionParts[0]);
                int minor = int.Parse(versionParts[1]);
                return "net" + major + minor;
            }
            return "net472";
        }
        
        if (frameworkDescription.StartsWith(".NET") && !frameworkDescription.StartsWith(".NET Framework"))
        {
            var versionParts = environmentVersion.ToString().Split('.');
            if (versionParts.Length >= 1)
            {
                int major = int.Parse(versionParts[0]);
                return "net" + major + ".0";
            }
            return "net8.0";
        }
        
        if (frameworkDescription.StartsWith(".NET Core"))
        {
            var versionParts = environmentVersion.ToString().Split('.');
            if (versionParts.Length >= 2)
            {
                int major = int.Parse(versionParts[0]);
                int minor = int.Parse(versionParts[1]);
                return "netcoreapp" + major + "." + minor;
            }
            return "netcoreapp3.1";
        }
        
        return "netstandard2.0";
    }
    
    private static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(double) || type == typeof(float) ||
               type == typeof(decimal) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }
}
