// scripts/PsBridge/Reflection.cs
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static partial class Reflection
{
    // Recover the COM HRESULT from a possibly deeply-wrapped exception chain.
    // A COM method failure (e.g. FileSystemObject.OpenTextFile on a missing file) surfaces as
    // TargetInvocationException -> FileNotFoundException / COMException, whose base HResult
    // carries the real COM error (0x800A0035, FACILITY_VBS). That HRESULT is exactly what a
    // VBScript host needs to report Err.Number (its low 16 bits); without this recovery it is
    // flattened into a message string and lost. Walk to the innermost exception and return its
    // HResult only when it is a genuine COM HRESULT (severity bit set, not a CLR-internal
    // 0x8013xxxx COR_E_* / PowerShell 0x80131501 wrapping code).
    public static int FindComHResult(System.Exception ex)
    {
        System.Exception cur = ex;
        int deepest = 0;
        while (cur != null)
        {
            int hr = cur.HResult;
            bool severityBit = (hr & unchecked((int)0x80000000)) != 0;
            bool clrInternal = (hr & unchecked((int)0xFFFF0000)) == unchecked((int)0x80130000);
            if (severityBit && !clrInternal)
                deepest = hr;
            cur = cur.InnerException;
        }
        return deepest;
    }

    // Wrap a failure for the JS host while preserving the COM HRESULT. Rethrowing a plain
    // Exception drops the ErrorCode/HResult (0x800A...), which is the field a VBScript host
    // needs to report the real Err.Number.
    public static System.Exception WrapError(string prefix, System.Exception ex)
    {
        int hr = FindComHResult(ex);
        var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        if (hr != 0)
            return new COMException(prefix + innerMsg, hr);
        return new System.Exception(prefix + innerMsg);
    }

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
            case "AddEvent":              return HandleAddEvent(cmd);
            case "AddAsyncEvent":         return HandleAddAsyncEvent(cmd);
            case "AddDeferredEvent":      return HandleAddDeferredEvent(cmd);
            case "CompleteDeferral":      return HandleCompleteDeferral(cmd);
            case "New":                   return HandleNew(cmd);
            case "CreateCOMObject":       return HandleCreateCOMObject(cmd);
            case "GetCOMObject":          return HandleGetCOMObject(cmd);
            case "Invoke":                return HandleInvoke(cmd);
            case "AwaitTask":             return HandleAwaitTask(cmd);
            case "LoadAssembly":          return HandleLoadAssembly(cmd);
            case "LoadFrom":              return HandleLoadFrom(cmd);
            case "Release":               return HandleRelease(cmd);
            case "SetResolvingCallback":  return HandleSetResolvingCallback(cmd);
            case "AddType":               return HandleAddType(cmd);
            case "InvokeDetached":        return HandleInvokeDetached(cmd);
            case "SetConversionBehavior": return HandleSetConversionBehavior(cmd);
            case "MaterializeDict":       return HandleMaterializeDict(cmd);
            case "MaterializeEnum":       return HandleMaterializeEnum(cmd);
            case "ReadChunk":             return HandleReadChunk(cmd);
            case "WriteChunk":            return HandleWriteChunk(cmd);
            case "SeekStream":            return HandleSeekStream(cmd);
            default:                      return new Dictionary<string, object> { { "type", "void" } };
        }
    }
}
