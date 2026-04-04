// scripts/PsBridge/Reflection.Stream.cs
using System;
using System.Collections.Generic;

public static partial class Reflection
{
    // Read up to `size` bytes from a .NET Stream; returns base64 string or {type:'null'} on EOF.
    // Note: Stream.Read() is synchronous and will block the IPC thread until data is available.
    // This is acceptable for local streams (MemoryStream, FileStream). Avoid slow network streams.
    private static Dictionary<string, object> HandleReadChunk(Dictionary<string, object> cmd)
    {
        var stream = BridgeState.ObjectStore[cmd["targetId"].ToString()] as System.IO.Stream;
        if (stream == null)
            throw new Exception("ReadChunk: target is not a Stream");

        var size = cmd.ContainsKey("size") ? Convert.ToInt32(cmd["size"]) : 4096;
        if (size <= 0) size = 4096;

        var buffer = new byte[size];
        var bytesRead = stream.Read(buffer, 0, size);

        if (bytesRead == 0)
            return new Dictionary<string, object> { { "type", "null" } };

        // Only encode the bytes actually read
        var actual = new byte[bytesRead];
        Array.Copy(buffer, actual, bytesRead);
        return new Dictionary<string, object>
        {
            { "type", "primitive" },
            { "value", Convert.ToBase64String(actual) }
        };
    }

    // Write base64-encoded bytes to a .NET Stream.
    private static Dictionary<string, object> HandleWriteChunk(Dictionary<string, object> cmd)
    {
        var stream = BridgeState.ObjectStore[cmd["targetId"].ToString()] as System.IO.Stream;
        if (stream == null)
            throw new Exception("WriteChunk: target is not a Stream");

        var data = Convert.FromBase64String(cmd["data"].ToString());
        stream.Write(data, 0, data.Length);
        return new Dictionary<string, object> { { "type", "void" } };
    }

    // Seek a seekable .NET Stream to a given position.
    // origin: 0=Begin, 1=Current, 2=End  (matches System.IO.SeekOrigin enum values)
    private static Dictionary<string, object> HandleSeekStream(Dictionary<string, object> cmd)
    {
        var stream = BridgeState.ObjectStore[cmd["targetId"].ToString()] as System.IO.Stream;
        if (stream == null)
            throw new Exception("SeekStream: target is not a Stream");

        var offset = Convert.ToInt64(cmd["offset"]);
        var origin = cmd.ContainsKey("origin") ? Convert.ToInt32(cmd["origin"]) : 0;
        var newPos = stream.Seek(offset, (System.IO.SeekOrigin)origin);
        return new Dictionary<string, object> { { "type", "primitive" }, { "value", newPos } };
    }
}
