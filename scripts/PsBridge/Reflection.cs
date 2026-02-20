#pragma warning disable 618
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public static class Reflection
{
    public static Dictionary<string, object> InvokeReflectionLogic(Dictionary<string, object> cmd)
    {
        var action = cmd["action"].ToString();

        if (action == "GetType")
        {
            var name = cmd["typeName"].ToString();
            var type = Type.GetType(name);
            if (type == null)
            {
                try { Assembly.LoadWithPartialName(name); } catch { }
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
                
                if (parameters.Length == 2)
                {
                    var senderType = parameters[0].ParameterType;
                    var eType = parameters[1].ParameterType;
                    
                    Action<object, object> handlerAction = (sender, e) =>
                    {
                        var writer = BridgeState.Writer;
                        if (writer == null) return;
                        
                        var protoArgs = new List<Dictionary<string, object>>();
                        
                        foreach (var arg in new object[] { sender, e })
                        {
                            if (arg == null)
                            {
                                protoArgs.Add(new Dictionary<string, object> { { "type", "null" } });
                            }
                            else
                            {
                                var converted = Protocol.ConvertToProtocol(arg);
                                
                                bool isEventArgs = false;
                                var propsToInclude = new Dictionary<string, object>();
                                
                                try
                                {
                                    var typeName = arg.GetType().FullName;
                                    isEventArgs = typeName.EndsWith("EventArgs") || 
                                                  typeName.Contains("InitializationCompleted") ||
                                                  typeName == "System.__ComObject";
                                }
                                catch { }
                                
                                if (isEventArgs)
                                {
                                    try
                                    {
                                        var members = arg.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                        foreach (var member in members)
                                        {
                                            try
                                            {
                                                var val = member.GetValue(arg);
                                                if (val != null && !(val is MarshalByRefObject))
                                                {
                                                    if (val is bool || val is string || val.GetType().IsPrimitive)
                                                    {
                                                        propsToInclude[member.Name] = val;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                }
                                
                                if (propsToInclude.Count > 0)
                                {
                                    converted["props"] = propsToInclude;
                                }
                                
                                protoArgs.Add(converted);
                            }
                        }
                        
                        var msg = new Dictionary<string, object>
                        {
                            { "type", "event" },
                            { "callbackId", cbId },
                            { "args", protoArgs }
                        };
                        
                        var json = SimpleJson.Serialize(msg);
                        writer.WriteLine(json);
                        
                        try
                        {
                            if (PsHost.ProcessNestedCommands != null)
                            {
                                PsHost.ProcessNestedCommands();
                            }
                        }
                        catch { }
                    };
                    
                    handler = Delegate.CreateDelegate(delegateType, handlerAction.Target, handlerAction.Method);
                }
                else
                {
                    Action<object[]> handlerAction = (args) =>
                    {
                        var writer = BridgeState.Writer;
                        if (writer == null) return;
                        
                        var protoArgs = new List<Dictionary<string, object>>();
                        
                        if (args != null)
                        {
                            foreach (var arg in args)
                            {
                                if (arg == null)
                                {
                                    protoArgs.Add(new Dictionary<string, object> { { "type", "null" } });
                                }
                                else
                                {
                                    protoArgs.Add(Protocol.ConvertToProtocol(arg));
                                }
                            }
                        }
                        
                        var msg = new Dictionary<string, object>
                        {
                            { "type", "event" },
                            { "callbackId", cbId },
                            { "args", protoArgs }
                        };
                        
                        var json = SimpleJson.Serialize(msg);
                        writer.WriteLine(json);
                        
                        try
                        {
                            if (PsHost.ProcessNestedCommands != null)
                            {
                                PsHost.ProcessNestedCommands();
                            }
                        }
                        catch { }
                    };
                    
                    handler = Delegate.CreateDelegate(delegateType, handlerAction.Target, handlerAction.Method);
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
                    
                    throw lastException ?? new Exception("No matching constructor found");
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

            if (name == "Run" && target.ToString() == "System.Windows.Forms.Application")
            {
                var form = realArgs.Length > 0 ? realArgs[0] : null;
                PsHost.StartGuiLoop(form);
                return new Dictionary<string, object> { { "type", "void" } };
            }

            var isStatic = target is Type;
            var targetType = isStatic ? (Type)target : target.GetType();

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

                var needsManualShim = false;
                foreach (var arg in realArgs)
                {
                    if (arg is Delegate || arg is Func<object, object, object, object, object>)
                    {
                        needsManualShim = true;
                        break;
                    }
                }

                var manualSuccess = false;

                if (needsManualShim)
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
                            else if (pType.IsEnum && arg is int)
                            {
                                tempArgs[i] = Enum.ToObject(pType, arg);
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
                    MethodInfo method = null;
                    
                    var methods = targetType.GetMethods(bindingFlags).Where(m => m.Name == name).ToArray();
                    if (methods.Length == 1)
                    {
                        method = methods[0];
                    }
                    else if (methods.Length > 1 && realArgs.Length > 0)
                    {
                        foreach (var m in methods)
                        {
                            var parameters = m.GetParameters();
                            if (parameters.Length != realArgs.Length) continue;
                            
                            var match = true;
                            for (var i = 0; i < parameters.Length; i++)
                            {
                                var pType = parameters[i].ParameterType;
                                var argType = realArgs[i].GetType();
                                if (!pType.IsAssignableFrom(argType))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                method = m;
                                break;
                            }
                        }
                    }
                    
                    if (method != null)
                    {
                        var instanceToCall = isStatic ? null : target;
                        result = method.Invoke(instanceToCall, realArgs);
                    }
                    else if (methods.Length > 0)
                    {
                        result = methods[0].Invoke(isStatic ? null : target, realArgs);
                    }
                    else
                    {
                        var member = targetType.GetMember(name);
                        if (member != null && member.Length > 0 && member[0] is PropertyInfo)
                        {
                            result = ((PropertyInfo)member[0]).GetValue(isStatic ? null : target);
                        }
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

        if (action == "Release")
        {
            Protocol.RemoveBridgeObject(cmd["targetId"].ToString());
            return new Dictionary<string, object> { { "type", "void" } };
        }
        
        if (action == "GetFrameworkInfo")
        {
            return new Dictionary<string, object>
            {
                { "type", "frameworkInfo" },
                { "frameworkMoniker", "net481" },
                { "runtimeVersion", Environment.Version.ToString() }
            };
        }
        
        if (action == "LoadAssembly")
        {
            var assemblyPath = cmd["assemblyPath"].ToString();
            if (File.Exists(assemblyPath))
            {
                Assembly.LoadFrom(assemblyPath);
            }
            else
            {
                Assembly.LoadWithPartialName(assemblyPath);
            }
            return new Dictionary<string, object> { { "type", "void" } };
        }
        
        if (action == "RequireModule")
        {
            var assemblyPath = cmd["assemblyPath"].ToString();
            if (File.Exists(assemblyPath))
            {
                var asm = Assembly.LoadFrom(assemblyPath);
                return new Dictionary<string, object> { { "type", "namespace" }, { "value", asm.GetName().Name } };
            }
            throw new Exception("Module not found");
        }
        
        if (action == "Resolved")
        {
            return new Dictionary<string, object> { { "type", "void" } };
        }
        
        if (action == "AwaitTask")
        {
            var task = (Task)BridgeState.ObjectStore[cmd["taskId"].ToString()];
            try
            {
                task.GetAwaiter().GetResult();
                var prop = task.GetType().GetProperty("Result");
                var res = prop != null ? prop.GetValue(task, null) : null;
                return Protocol.ConvertToProtocol(res);
            }
            catch (Exception ex)
            {
                throw new Exception("Task Error: " + ex.Message);
            }
        }

        return new Dictionary<string, object> { { "type", "void" } };
    }
}
