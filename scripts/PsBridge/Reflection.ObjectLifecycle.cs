// scripts/PsBridge/Reflection.ObjectLifecycle.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static partial class Reflection
{
    private static Dictionary<string, object> HandlePoll()
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

    private static Dictionary<string, object> HandleCreateCOMObject(Dictionary<string, object> cmd)
    {
        var progId = cmd["progId"].ToString();
        try
        {
            var type = Type.GetTypeFromProgID(progId, true);
            var obj = Activator.CreateInstance(type);
            return Protocol.ConvertToProtocol(obj);
        }
        catch (Exception ex)
        {
            throw new Exception("CreateCOMObject Error: " + ex.Message);
        }
    }

    private static Dictionary<string, object> HandleGetCOMObject(Dictionary<string, object> cmd)
    {
        var pathname = cmd.ContainsKey("pathname") ? cmd["pathname"].ToString() : "";
        var cls = cmd.ContainsKey("cls") ? cmd["cls"].ToString() : "";
        try
        {
            object obj;
            if (pathname == "")
            {
                // GetObject(, "ProgID") — get running instance via Marshal.GetActiveObject
                if (cls == "")
                    throw new Exception("GetObject: must provide either pathname or class");
                obj = System.Runtime.InteropServices.Marshal.GetActiveObject(cls);
            }
            else
            {
                // GetObject("winmgmts:") or GetObject("C:\file.xls") — bind to moniker
                obj = System.Runtime.InteropServices.Marshal.BindToMoniker(pathname);
            }
            return Protocol.ConvertToProtocol(obj);
        }
        catch (Exception ex)
        {
            throw new Exception("GetCOMObject Error: " + ex.Message);
        }
    }

    private static Dictionary<string, object> HandleNew(Dictionary<string, object> cmd)
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
                        else if (arg is object[] && pType.IsGenericType)
                        {
                            // Convert object[] → List<T> or IList<T>
                            var genDef = pType.GetGenericTypeDefinition();
                            if (genDef == typeof(List<>) || genDef == typeof(IList<>)
                                || genDef == typeof(ICollection<>) || genDef == typeof(IEnumerable<>))
                            {
                                var elemType = pType.GetGenericArguments()[0];
                                var listType = typeof(List<>).MakeGenericType(elemType);
                                var list = Activator.CreateInstance(listType);
                                var addMethod = listType.GetMethod("Add");
                                var arrItems = (object[])arg;
                                var allOk = true;
                                foreach (var item in arrItems)
                                {
                                    if (item != null && !elemType.IsAssignableFrom(item.GetType()))
                                    {
                                        allOk = false;
                                        break;
                                    }
                                    addMethod.Invoke(list, new object[] { item });
                                }
                                if (allOk)
                                {
                                    convertedArgs[i] = list;
                                }
                                else
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
                        else if (arg is object[] && pType.IsArray)
                        {
                            // Convert object[] → typed array (byte[], int[], string[], etc.)
                            var elemType = pType.GetElementType();
                            var arrItems = (object[])arg;
                            var typedArr = Array.CreateInstance(elemType, arrItems.Length);
                            var allOk = true;
                            for (var j = 0; j < arrItems.Length; j++)
                            {
                                try
                                {
                                    var item = arrItems[j];
                                    if (item == null)
                                    {
                                        typedArr.SetValue(null, j);
                                    }
                                    else if (elemType.IsAssignableFrom(item.GetType()))
                                    {
                                        typedArr.SetValue(item, j);
                                    }
                                    else if (item is IConvertible)
                                    {
                                        typedArr.SetValue(Convert.ChangeType(item, elemType), j);
                                    }
                                    else
                                    {
                                        allOk = false;
                                        break;
                                    }
                                }
                                catch { allOk = false; break; }
                            }
                            if (allOk)
                            {
                                convertedArgs[i] = typedArr;
                            }
                            else
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

    private static Dictionary<string, object> HandleRelease(Dictionary<string, object> cmd)
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

    private static Dictionary<string, object> HandleSetConversionBehavior(Dictionary<string, object> cmd)
    {
        var mode = cmd.ContainsKey("mode") ? cmd["mode"].ToString() : "node-api-dotnet";
        Protocol.PythonNetMode = (mode == "pythonnet");
        return new Dictionary<string, object> { { "type", "void" } };
    }
}
