// scripts/PsBridge/Reflection.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var typeName = target.GetType().FullName;
            return new Dictionary<string, object> { { "typeName", typeName } };
        }

        if (action == "InspectType")
        {
            var typeName = cmd["typeName"].ToString();
            var rawList = cmd["memberNames"] as System.Collections.Generic.List<object>;
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
                
                Delegate handler = null;

                if (parameters.Length == 0)
                {
                    Action h0 = () => SendEventToJs(cbId, new object[0]);
                    handler = Delegate.CreateDelegate(delegateType, h0.Target, h0.Method);
                }
                else if (parameters.Length == 1)
                {
                    Action<object> h1 = (a1) => SendEventToJs(cbId, new object[] { a1 });
                    handler = Delegate.CreateDelegate(delegateType, h1.Target, h1.Method);
                }
                else if (parameters.Length == 2)
                {
                    Action<object, object> h2 = (a1, a2) => SendEventToJs(cbId, new object[] { a1, a2 });
                    handler = Delegate.CreateDelegate(delegateType, h2.Target, h2.Method);
                }
                else if (parameters.Length == 3)
                {
                    Action<object, object, object> h3 = (a1, a2, a3) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3 });
                    handler = Delegate.CreateDelegate(delegateType, h3.Target, h3.Method);
                }
                else if (parameters.Length == 4)
                {
                    Action<object, object, object, object> h4 = (a1, a2, a3, a4) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3, a4 });
                    handler = Delegate.CreateDelegate(delegateType, h4.Target, h4.Method);
                }
                else if (parameters.Length == 5)
                {
                    Action<object, object, object, object, object> h5 = (a1, a2, a3, a4, a5) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3, a4, a5 });
                    handler = Delegate.CreateDelegate(delegateType, h5.Target, h5.Method);
                }
                else if (parameters.Length == 6)
                {
                    Action<object, object, object, object, object, object> h6 = (a1, a2, a3, a4, a5, a6) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3, a4, a5, a6 });
                    handler = Delegate.CreateDelegate(delegateType, h6.Target, h6.Method);
                }
                else if (parameters.Length == 7)
                {
                    Action<object, object, object, object, object, object, object> h7 = (a1, a2, a3, a4, a5, a6, a7) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3, a4, a5, a6, a7 });
                    handler = Delegate.CreateDelegate(delegateType, h7.Target, h7.Method);
                }
                else if (parameters.Length == 8)
                {
                    Action<object, object, object, object, object, object, object, object> h8 = (a1, a2, a3, a4, a5, a6, a7, a8) =>
                        SendEventToJs(cbId, new object[] { a1, a2, a3, a4, a5, a6, a7, a8 });
                    handler = Delegate.CreateDelegate(delegateType, h8.Target, h8.Method);
                }
                else
                {
                    return new Dictionary<string, object>
                    {
                        { "type", "error" },
                        { "message", string.Format(
                            "Cannot subscribe to event '{0}': its delegate type '{1}' has {2} parameters. Only events with up to 8 parameters are supported.",
                            eventName, delegateType.Name, parameters.Length) }
                    };
                }

                eventInfo.AddEventHandler(target, handler);
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
                        prop.SetValue(target, value);
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
                                        tempArgs[i] = Delegate.CreateDelegate(pType, func.Target, func.Method);
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
#pragma warning disable 618
                try { asm = Assembly.LoadWithPartialName(assemblyName); } catch { }
#pragma warning restore 618
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
            Protocol.RemoveBridgeObject(cmd["targetId"].ToString());
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
        var writer = BridgeState.Writer;
        if (writer == null) return;
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
        var json = SimpleJson.Serialize(msg);
        if (BridgeState.UseQueueMode)
        {
            BridgeState.EventQueue.Enqueue(json);
        }
        else
        {
            writer.WriteLine(json);
            try { if (PsHost.ProcessNestedCommands != null) PsHost.ProcessNestedCommands(); } catch { }
        }
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
