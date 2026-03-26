
// scripts/PsBridge/Protocol.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public static class Protocol
{
    public static Dictionary<string, object> ConvertToProtocol(object inputObject)
    {
        if (inputObject == null)
        {
            return new Dictionary<string, object> { { "type", "null" } };
        }
        
        if (inputObject is bool || inputObject is string)
        {
            return new Dictionary<string, object> { { "type", "primitive" }, { "value", inputObject } };
        }

        if (inputObject.GetType().IsPrimitive)
        {
            var val = inputObject;
            if (val is double || val is float)
            {
                double dVal = Convert.ToDouble(val);
                if (double.IsNaN(dVal) || double.IsInfinity(dVal))
                {
                    val = null;
                }
            }
            return new Dictionary<string, object> { { "type", "primitive" }, { "value", val } };
        }

        if (inputObject is Task)
        {
            var refId = Guid.NewGuid().ToString();
            BridgeState.ObjectStore[refId] = inputObject;
            return new Dictionary<string, object>
            {
                { "type", "task" },
                { "id", refId },
                { "netType", inputObject.GetType().FullName }
            };
        }

        if (inputObject is Array)
        {
            var arr = (Array)inputObject;
            var arrResult = new List<Dictionary<string, object>>();
            foreach (var item in arr)
            {
                arrResult.Add(ConvertToProtocol(item));
            }
            return new Dictionary<string, object> { { "type", "array" }, { "value", arrResult } };
        }

        // Enum → integer primitive
        if (inputObject.GetType().IsEnum)
        {
            return new Dictionary<string, object> { { "type", "primitive" }, { "value", Convert.ToInt32(inputObject) } };
        }

        // Guid → string primitive
        if (inputObject is Guid)
        {
            return new Dictionary<string, object> { { "type", "primitive" }, { "value", ((Guid)inputObject).ToString() } };
        }

        // DateTime → JS Date (ISO 8601 UTC)
        if (inputObject is DateTime)
        {
            var dt = ((DateTime)inputObject).ToUniversalTime();
            return new Dictionary<string, object> { { "type", "date" }, { "value", string.Format("{0:yyyy-MM-ddTHH:mm:ss.fff}Z", dt) } };
        }

        // DateTimeOffset → JS Date
        if (inputObject is DateTimeOffset)
        {
            var dt = ((DateTimeOffset)inputObject).ToUniversalTime().DateTime;
            return new Dictionary<string, object> { { "type", "date" }, { "value", string.Format("{0:yyyy-MM-ddTHH:mm:ss.fff}Z", dt) } };
        }

        // TimeSpan → milliseconds number (like node-api-dotnet)
        if (inputObject is TimeSpan)
        {
            return new Dictionary<string, object> { { "type", "primitive" }, { "value", ((TimeSpan)inputObject).TotalMilliseconds } };
        }

        // Tuple<A,B,...> → JS array (like node-api-dotnet)
        if (IsNetTuple(inputObject.GetType()))
        {
            var tupleItems = new List<Dictionary<string, object>>();
            for (var i = 1; i <= 8; i++)
            {
                var itemProp = inputObject.GetType().GetProperty("Item" + i);
                if (itemProp == null) break;
                tupleItems.Add(ConvertToProtocol(itemProp.GetValue(inputObject, null)));
            }
            return new Dictionary<string, object> { { "type", "array" }, { "value", tupleItems } };
        }

        // Generic IList<T> / List<T> → JS array  (node-api-dotnet: IList<T> => T[])
        // Non-generic IList implementations (ControlCollection, UIElementCollection, etc.)
        // are intentionally left as ref proxies so callers can still use .Add()/.Remove() etc.
        if (!(inputObject is System.Collections.IDictionary))
        {
            var isGenericIList = false;
            foreach (var iface in inputObject.GetType().GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IList<>))
                {
                    isGenericIList = true;
                    break;
                }
            }
            if (isGenericIList)
            {
                var list = (System.Collections.IList)inputObject;
                var listResult = new List<Dictionary<string, object>>();
                foreach (var item in list)
                {
                    listResult.Add(item == null
                        ? new Dictionary<string, object> { { "type", "null" } }
                        : ConvertToProtocol(item));
                }
                return new Dictionary<string, object> { { "type", "array" }, { "value", listResult } };
            }
        }

        // IDictionary<K,V> → JS Map<K,V> (node-api-dotnet: IDictionary<K,V> => Map<K,V>)
        if (inputObject is System.Collections.IDictionary)
        {
            var dict = (System.Collections.IDictionary)inputObject;
            var entries = new List<object>();
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                var pair = new List<object>
                {
                    ConvertToProtocol(entry.Key),
                    entry.Value == null
                        ? new Dictionary<string, object> { { "type", "null" } }
                        : ConvertToProtocol(entry.Value)
                };
                entries.Add(pair);
            }
            return new Dictionary<string, object> { { "type", "map" }, { "entries", entries } };
        }

        var objRefId = Guid.NewGuid().ToString();
        BridgeState.ObjectStore[objRefId] = inputObject;

        var refResult = new Dictionary<string, object>
        {
            { "type", "ref" },
            { "id", objRefId },
            { "netType", inputObject.GetType().FullName }
        };

        // Flag types that support indexed access (obj[i]) so JS proxy can route numeric keys.
        // Use GetMethods loop instead of GetMethod() to avoid AmbiguousMatchException when
        // a type has multiple indexers (e.g. ControlCollection: this[int] and this[string]).
        foreach (var m in inputObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name == "get_Item") { refResult["hasIndexer"] = true; break; }
        }

        return refResult;
    }

    private static bool IsNetTuple(Type type)
    {
        if (type == null) return false;
        var fn = type.FullName;
        // Matches System.Tuple`1, System.Tuple`2, ... System.Tuple`8
        return fn != null && fn.StartsWith("System.Tuple`");
    }

    public static object[] ResolveArgs(object argsObj)
    {
        var realArgs = new List<object>();
        
        IEnumerable<object> cmdArgs = null;
        if (argsObj is object[])
        {
            cmdArgs = ((object[])argsObj);
        }
        else if (argsObj is List<object>)
        {
            cmdArgs = (List<object>)argsObj;
        }
        
        if (cmdArgs != null)
        {
            foreach (var arg in cmdArgs)
            {
                var dict = arg as Dictionary<string, object>;
                if (dict != null && dict.ContainsKey("__ref"))
                {
                    realArgs.Add(BridgeState.ObjectStore[dict["__ref"].ToString()]);
                }
                else if (dict != null && dict.ContainsKey("type") && dict["type"].ToString() == "callback")
                {
                    var cbId = dict["callbackId"].ToString();
                    
                    Func<object, object, object, object, object> callback = (p1, p2, p3, p4) =>
                    {
                        var netCallbackArgs = new object[] { p1, p2, p3, p4 };
                        
                        var validProtoArgs = new List<Dictionary<string, object>>();
                        foreach (var a in netCallbackArgs)
                        {
                            if (a != null)
                            {
                                validProtoArgs.Add(ConvertToProtocol(a));
                            }
                        }

                        var msg = new Dictionary<string, object>
                        {
                            { "type", "event" },
                            { "callbackId", cbId },
                            { "args", validProtoArgs }
                        };
                        
                        var json = SimpleJson.Serialize(msg);
                        
                        BridgeState.Writer.WriteLine(json);
                        
                        object result = null;
                        try
                        {
                            if (PsHost.ProcessNestedCommands != null)
                            {
                                result = PsHost.ProcessNestedCommands();
                            }
                        }
                        catch { }
                        
                        return result;
                    };
                    
                    realArgs.Add(callback);
                }
                else
                {
                    realArgs.Add(arg);
                }
            }
        }
        return realArgs.ToArray();
    }

    public static void RemoveBridgeObject(string id)
    {
        object ignored;
        BridgeState.ObjectStore.TryRemove(id, out ignored);
    }
}

public static class SimpleJson
{
    public static string Serialize(object obj)
    {
        var ser = new JavaScriptSerializer();
        ser.MaxJsonLength = int.MaxValue;
        return ser.Serialize(obj);
    }
}
