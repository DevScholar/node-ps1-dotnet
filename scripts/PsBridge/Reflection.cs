// scripts/PsBridge/Reflection.cs
using System.Collections.Generic;

public static partial class Reflection
{
    public static Dictionary<string, object> InvokeReflectionLogic(Dictionary<string, object> cmd)
    {
        var action = cmd["action"].ToString();

        switch (action)
        {
            case "GetRuntimeInfo":        return HandleGetRuntimeInfo();
            case "Poll":                  return HandlePoll();
            case "GetType":               return HandleGetType(cmd);
            case "Inspect":               return HandleInspect(cmd);
            case "GetTypeName":           return HandleGetTypeName(cmd);
            case "InspectType":           return HandleInspectType(cmd);
            case "RemoveEvent":           return HandleRemoveEvent(cmd);
            case "AddSyncEvent":          return HandleAddSyncEvent(cmd);
            case "AddEvent":              return HandleAddEvent(cmd);
            case "New":                   return HandleNew(cmd);
            case "Invoke":                return HandleInvoke(cmd);
            case "AwaitTask":             return HandleAwaitTask(cmd);
            case "LoadAssembly":          return HandleLoadAssembly(cmd);
            case "LoadFrom":              return HandleLoadFrom(cmd);
            case "Release":               return HandleRelease(cmd);
            case "SetResolvingCallback":  return HandleSetResolvingCallback(cmd);
            case "AddType":               return HandleAddType(cmd);
            case "InvokeDetached":        return HandleInvokeDetached(cmd);
            case "SetConversionBehavior": return HandleSetConversionBehavior(cmd);
            default:                      return new Dictionary<string, object> { { "type", "void" } };
        }
    }
}
