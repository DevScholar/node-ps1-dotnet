using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public static class Protocol
{
    public static Dictionary<string, object> GetComObjectProperties(object inputObject)
    {
        var props = new Dictionary<string, object>();
        
        Type type = null;
        
        if (inputObject is Type)
        {
            type = (Type)inputObject;
        }
        else
        {
            type = inputObject.GetType();
        }
        
        bool isComObject = type.FullName == "System.__ComObject";
        if (!isComObject)
        {
            var attrs = type.GetCustomAttributes(false);
            foreach (var attr in attrs)
            {
                if (attr is ComVisibleAttribute)
                {
                    isComObject = true;
                    break;
                }
            }
        }
        
        if (isComObject && !(inputObject is Type))
        {
            props["__comType"] = type.FullName;
            
            var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var prop in allProps)
            {
                var indexParams = prop.GetIndexParameters();
                
                if (indexParams.Length == 0)
                {
                    try
                    {
                        var val = prop.GetValue(inputObject, null);
                        
                        if (val != null)
                        {
                            if (val is bool || val is string || val.GetType().IsPrimitive)
                            {
                                bool isValidValue = true;
                                if (val is double || val is float)
                                {
                                    double dVal = Convert.ToDouble(val);
                                    if (double.IsNaN(dVal) || double.IsInfinity(dVal))
                                    {
                                        isValidValue = false;
                                    }
                                }
                                if (isValidValue)
                                {
                                    props[prop.Name] = val;
                                }
                            }
                            else if (val.GetType().IsValueType)
                            {
                                string strVal = val.ToString();
                                if (strVal != "NaN" && strVal != "Infinity" && strVal != "-Infinity")
                                {
                                    props[prop.Name] = val;
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    var paramType = indexParams[0].ParameterType;
                    if (paramType == typeof(string))
                    {
                        var invoker = typeof(PropertyInfo).GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
                        if (invoker != null)
                        {
                            try
                            {
                                var val = invoker.Invoke(inputObject, new object[] { "AdditionalArgs" });
                                if (val != null)
                                {
                                    props["AdditionalArgs"] = val;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        
        return props;
    }

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

        var objRefId = Guid.NewGuid().ToString();
        BridgeState.ObjectStore[objRefId] = inputObject;
        
        var result = new Dictionary<string, object>
        {
            { "type", "ref" },
            { "id", objRefId },
            { "netType", inputObject.GetType().FullName }
        };
        
        var comProps = GetComObjectProperties(inputObject);
        if (comProps.Count > 0)
        {
            result["props"] = comProps;
        }
        
        return result;
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
        BridgeState.ObjectStore.Remove(id);
    }
}

public static class SimpleJson
{
    public static string Serialize(object obj)
    {
        if (obj == null) return "null";
        
        if (obj is Dictionary<string, object>)
        {
            var dict = (Dictionary<string, object>)obj;
            var parts = new List<string>();
            foreach (var kvp in dict)
            {
                parts.Add(string.Format("\"{0}\":{1}", EscapeString(kvp.Key), Serialize(kvp.Value)));
            }
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }
        
        if (obj is List<Dictionary<string, object>>)
        {
            var list = (List<Dictionary<string, object>>)obj;
            var parts = new List<string>();
            foreach (var item in list)
            {
                parts.Add(Serialize(item));
            }
            return "[" + string.Join(",", parts.ToArray()) + "]";
        }
        
        if (obj is string)
        {
            return "\"" + EscapeString((string)obj) + "\"";
        }
        
        if (obj is bool)
        {
            return (bool)obj ? "true" : "false";
        }
        
        if (obj == null)
        {
            return "null";
        }
        
        if (obj.GetType().IsPrimitive || obj is decimal)
        {
            return obj.ToString();
        }
        
        return "\"" + EscapeString(obj.ToString()) + "\"";
    }
    
    private static string EscapeString(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
