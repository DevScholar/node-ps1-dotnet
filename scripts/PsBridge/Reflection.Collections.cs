// scripts/PsBridge/Reflection.Collections.cs
using System.Collections.Generic;

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
        var enumerable = target as System.Collections.IEnumerable;
        if (enumerable == null)
            throw new System.Exception("MaterializeEnum: target is not IEnumerable");

        var result = new List<Dictionary<string, object>>();
        foreach (var item in enumerable)
        {
            result.Add(item == null
                ? new Dictionary<string, object> { { "type", "null" } }
                : Protocol.ConvertToProtocol(item));
        }
        return new Dictionary<string, object> { { "type", "array" }, { "value", result } };
    }
}
