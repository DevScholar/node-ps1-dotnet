// scripts/PsBridge/Reflection.TypeResolution.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

public static partial class Reflection
{
    private static Dictionary<string, object> HandleGetRuntimeInfo()
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

    private static Dictionary<string, object> HandleGetType(Dictionary<string, object> cmd)
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

    private static Dictionary<string, object> HandleInspect(Dictionary<string, object> cmd)
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

    private static Dictionary<string, object> HandleGetTypeName(Dictionary<string, object> cmd)
    {
        var target = BridgeState.ObjectStore[cmd["targetId"].ToString()];
        string typeName;
        if (target is Type)
            typeName = ((Type)target).FullName;
        else
            typeName = target.GetType().FullName;
        return new Dictionary<string, object> { { "typeName", typeName } };
    }

    private static Dictionary<string, object> HandleInspectType(Dictionary<string, object> cmd)
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
}
