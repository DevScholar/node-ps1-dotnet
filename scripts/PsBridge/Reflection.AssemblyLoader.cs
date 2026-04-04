// scripts/PsBridge/Reflection.AssemblyLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public static partial class Reflection
{
    private static Dictionary<string, object> HandleLoadAssembly(Dictionary<string, object> cmd)
    {
        var assemblyName = cmd["assemblyName"].ToString();
        Assembly asm = null;
        try
        {
            asm = Assembly.Load(assemblyName);
        }
        catch
        {
            // Fallback 1: search already-loaded assemblies by simple name
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    asm = a;
                    break;
                }
            }
            // Fallback 2: probe the .NET runtime directory (handles framework assemblies like System.Windows.Forms)
            if (asm == null)
            {
                string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                string dllPath = Path.Combine(runtimeDir, assemblyName + ".dll");
                if (File.Exists(dllPath))
                {
                    try { asm = Assembly.LoadFrom(dllPath); } catch { }
                }
            }
            // Fallback 3: scan the .NET 4 GAC (handles WPF assemblies like PresentationFramework)
            if (asm == null)
            {
                string gacRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\assembly");
                string[] subDirs = new string[] { "GAC_MSIL", "GAC_64", "GAC_32" };
                foreach (var subDir in subDirs)
                {
                    string asmDir = Path.Combine(gacRoot, subDir, assemblyName);
                    if (!Directory.Exists(asmDir)) continue;
                    foreach (var verDir in Directory.GetDirectories(asmDir))
                    {
                        string gacDll = Path.Combine(verDir, assemblyName + ".dll");
                        if (File.Exists(gacDll))
                        {
                            try { asm = Assembly.LoadFrom(gacDll); } catch { }
                            if (asm != null) break;
                        }
                    }
                    if (asm != null) break;
                }
            }
        }
        if (asm == null)
        {
            throw new Exception("Failed to load assembly: " + assemblyName);
        }
        return Protocol.ConvertToProtocol(asm);
    }

    private static Dictionary<string, object> HandleLoadFrom(Dictionary<string, object> cmd)
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

    private static Dictionary<string, object> HandleAddType(Dictionary<string, object> cmd)
    {
        var sourceCode = cmd["source"].ToString();
        var refsRaw = cmd.ContainsKey("references") ? cmd["references"] : null;
        var refList = new List<string>();
        if (refsRaw is List<object>)
            foreach (var r in (List<object>)refsRaw) refList.Add(r.ToString());

        var providerType = Type.GetType("Microsoft.CSharp.CSharpCodeProvider, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
        if (providerType == null)
            providerType = Type.GetType("Microsoft.CSharp.CSharpCodeProvider");
        if (providerType == null)
            throw new Exception("AddType: CSharpCodeProvider not found");

        var provider = Activator.CreateInstance(providerType);
        var paramsType = providerType.Assembly.GetType("System.CodeDom.Compiler.CompilerParameters");
        var compileParams = Activator.CreateInstance(paramsType);
        paramsType.GetProperty("GenerateInMemory").SetValue(compileParams, true, null);
        paramsType.GetProperty("GenerateExecutable").SetValue(compileParams, false, null);

        var referencedAssemblies = (System.Collections.Specialized.StringCollection)
            paramsType.GetProperty("ReferencedAssemblies").GetValue(compileParams, null);
        foreach (var r in refList) referencedAssemblies.Add(r);

        // Add all currently-loaded assemblies by full path (covers WPF, WebView2, etc.)
        // Also try to load WPF assemblies from WPF install dir if not yet loaded.
        var wpfNames = new string[] { "WindowsBase", "PresentationCore", "PresentationFramework" };
        foreach (var wpfName in wpfNames)
        {
            bool found = false;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == wpfName && !string.IsNullOrEmpty(a.Location))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                try
                {
                    System.Reflection.Assembly.Load(wpfName + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                }
                catch { }
            }
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var loc = asm.Location;
                if (!string.IsNullOrEmpty(loc)) referencedAssemblies.Add(loc);
            }
            catch { }
        }

        var compileMethod = providerType.GetMethod("CompileAssemblyFromSource",
            new Type[] { paramsType, typeof(string[]) });
        var result = compileMethod.Invoke(provider,
            new object[] { compileParams, new string[] { sourceCode } });

        var errorsObj = result.GetType().GetProperty("Errors").GetValue(result, null);
        var count = (int)errorsObj.GetType().GetProperty("Count").GetValue(errorsObj, null);
        var indexer = errorsObj.GetType().GetMethod("get_Item");
        var sb = new System.Text.StringBuilder();
        for (var ei = 0; ei < count; ei++)
        {
            var err = indexer.Invoke(errorsObj, new object[] { ei });
            var isWarning = (bool)err.GetType().GetProperty("IsWarning").GetValue(err, null);
            if (!isWarning)
                sb.AppendLine(err.GetType().GetProperty("ErrorText").GetValue(err, null).ToString());
        }
        if (sb.Length > 0)
            throw new Exception("AddType compile errors:\n" + sb.ToString());

        var compiledAsm = (Assembly)result.GetType().GetProperty("CompiledAssembly").GetValue(result, null);
        var asmId = Guid.NewGuid().ToString();
        BridgeState.ObjectStore[asmId] = compiledAsm;
        return new Dictionary<string, object>
        {
            { "type", "ref" }, { "id", asmId }, { "typeName", "Assembly" }
        };
    }
}
