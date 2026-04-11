// scripts/PsBridge/Reflection.Collections.cs
using System.Collections.Generic;
using System.Reflection;

public static partial class Reflection
{
    // Materialise an IDictionary into a {type:'map', entries:[[k,v],...]} response.
    // Called in pythonnet mode when the caller needs to iterate or snapshot the dict.
    private static Dictionary<string, object> HandleMaterializeDict(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        var dict = target as System.Collections.IDictionary;
        if (dict == null)
            throw new System.Exception("MaterializeDict: target is not an IDictionary");

        var entries = new List<object>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var pair = new List<object>
            {
                Protocol.ConvertToProtocol(entry.Key),
                entry.Value == null
                    ? new Dictionary<string, object> { { "type", "null" } }
                    : Protocol.ConvertToProtocol(entry.Value)
            };
            entries.Add(pair);
        }
        return new Dictionary<string, object> { { "type", "map" }, { "entries", entries } };
    }

    // Materialise an IEnumerable into a {type:'array', value:[...]} response.
    // Called when JS needs to iterate a pythonnet-mode IEnumerable<T> ref proxy.
    private static Dictionary<string, object> HandleMaterializeEnum(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        var result = new List<Dictionary<string, object>>();

        var enumerable = target as System.Collections.IEnumerable;
        if (enumerable != null)
        {
            foreach (var item in enumerable)
            {
                result.Add(item == null
                    ? new Dictionary<string, object> { { "type", "null" } }
                    : Protocol.ConvertToProtocol(item));
            }
            return new Dictionary<string, object> { { "type", "array" }, { "value", result } };
        }

        // COM fallback: late-bound COM objects may not cast to IEnumerable,
        // but expose _NewEnum via IDispatch which returns IEnumerator.
        if (target.GetType().IsCOMObject)
        {
            try
            {
                var newEnum = target.GetType().InvokeMember("_NewEnum",
                    BindingFlags.InvokeMethod | BindingFlags.GetProperty,
                    null, target, null);
                var enumerator = newEnum as System.Collections.IEnumerator;
                if (enumerator != null)
                {
                    while (enumerator.MoveNext())
                    {
                        var item = enumerator.Current;
                        result.Add(item == null
                            ? new Dictionary<string, object> { { "type", "null" } }
                            : Protocol.ConvertToProtocol(item));
                    }
                    return new Dictionary<string, object> { { "type", "array" }, { "value", result } };
                }
            }
            catch { }
        }

        throw new System.Exception("MaterializeEnum: target is not IEnumerable");
    }
}
