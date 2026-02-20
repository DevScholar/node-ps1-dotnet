// scripts/PsBridge/PsHost.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

public static class PsHost
{
    public static Func<object> ProcessNestedCommands { get; set; }

    private static System.Timers.Timer _msgTimer;

    public static object RunProcessNestedCommands()
    {
        var reader = BridgeState.Reader;
        var pipe = BridgeState.PipeServer;
        
        while (pipe.IsConnected)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            
            var cmd = SimpleJsonDeserializer.Deserialize(line) as Dictionary<string, object>;
            
            if (cmd != null && cmd.ContainsKey("type") && cmd["type"].ToString() == "reply")
            {
                return cmd["result"];
            }

            try
            {
                var result = Reflection.InvokeReflectionLogic(cmd);
                var json = SimpleJson.Serialize(result);
                BridgeState.Writer.WriteLine(json);
            }
            catch (Exception ex)
            {
                var errMsg = ex.Message != null ? ex.Message : ex.ToString();
                var errJson = SimpleJson.Serialize(new Dictionary<string, object>
                {
                    { "type", "error" },
                    { "message", errMsg.Replace("\"", "'") }
                });
                BridgeState.Writer.WriteLine(errJson);
            }
        }
        return null;
    }

    public static void StartMessagePump()
    {
        _msgTimer = new System.Timers.Timer();
        _msgTimer.Interval = 10;
        _msgTimer.AutoReset = true;
        
        _msgTimer.Elapsed += (sender, e) =>
        {
            if (BridgeState.IsClosing) return;
            if (!BridgeState.PipeServer.IsConnected)
            {
                BridgeState.IsClosing = true;
                return;
            }
            try
            {
                if (BridgeState.Reader.Peek() >= 0)
                {
                    var line = BridgeState.Reader.ReadLine();
                    HandleLine(line);
                }
            }
            catch
            {
                BridgeState.IsClosing = true;
            }
        };
        
        _msgTimer.Start();
    }

    public static void StopMessagePump()
    {
        if (_msgTimer != null)
        {
            _msgTimer.Stop();
            _msgTimer.Dispose();
            _msgTimer = null;
        }
    }

    public static void ProcessTick()
    {
        if (BridgeState.IsClosing) return;
        if (!BridgeState.PipeServer.IsConnected) return;
        try
        {
            if (BridgeState.Reader.Peek() >= 0)
            {
                var line = BridgeState.Reader.ReadLine();
                HandleLine(line);
            }
        }
        catch { }
    }

    public static void StartGuiLoop(object mainForm)
    {
        StartMessagePump();
        
        ApplicationContext ctx = null;
        if (mainForm is Form)
        {
            ctx = new ApplicationContext((Form)mainForm);
        }
        else
        {
            ctx = new ApplicationContext();
        }

        Application.Run(ctx);
        
        BridgeState.IsClosing = true;
        
        StopMessagePump();
        
        Thread.Sleep(100);
        
        if (BridgeState.Writer != null)
        {
            try
            {
                var exitSignal = SimpleJson.Serialize(new Dictionary<string, object> { { "type", "exit" } });
                BridgeState.Writer.WriteLine(exitSignal);
                BridgeState.Writer.Flush();
            }
            catch { }
        }
        
        Thread.Sleep(50);
        
        if (BridgeState.PipeServer != null)
        {
            try
            {
                if (BridgeState.PipeServer.IsConnected)
                {
                    BridgeState.PipeServer.Close();
                }
                BridgeState.PipeServer.Dispose();
            }
            catch { }
        }
        
        Environment.Exit(0);
    }

    public static bool HandleLine(string line)
    {
        var cmd = SimpleJsonDeserializer.Deserialize(line) as Dictionary<string, object>;
        if (cmd != null && cmd.ContainsKey("type") && cmd["type"].ToString() == "reply")
        {
            return true;
        }

        try
        {
            var result = Reflection.InvokeReflectionLogic(cmd);
            var json = SimpleJson.Serialize(result);
            try
            {
                BridgeState.Writer.WriteLine(json);
            }
            catch (IOException)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (Exception ex)
        {
            var errMsg = ex.Message != null ? ex.Message : ex.ToString();
            var errJson = SimpleJson.Serialize(new Dictionary<string, object>
            {
                { "type", "error" },
                { "message", errMsg.Replace("\"", "'") }
            });
            try
            {
                BridgeState.Writer.WriteLine(errJson);
            }
            catch (IOException) { }
        }
        return false;
    }

    public static void StartServer()
    {
        BridgeState.PipeServer = new NamedPipeServerStream(
            BridgeState.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.None
        );
        
        BridgeState.PipeServer.WaitForConnection();
        
        BridgeState.Reader = new StreamReader(BridgeState.PipeServer);
        BridgeState.Writer = new StreamWriter(BridgeState.PipeServer);
        BridgeState.Writer.AutoFlush = true;

        while (BridgeState.PipeServer.IsConnected)
        {
            try
            {
                var line = BridgeState.Reader.ReadLine();
                if (line == null) break;
                HandleLine(line);
            }
            catch (IOException)
            {
                break;
            }
        }
    }
}

public static class SimpleJsonDeserializer
{
    private static int _index;
    private static string _json;

    public static object Deserialize(string json)
    {
        _json = json;
        _index = 0;
        return ParseValue();
    }

    private static void SkipWhitespace()
    {
        while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
        {
            _index++;
        }
    }

    private static object ParseValue()
    {
        SkipWhitespace();
        
        if (_index >= _json.Length) return null;
        
        var c = _json[_index];
        
        if (c == 'n')
        {
            _index += 4;
            return null;
        }
        
        if (c == 't')
        {
            _index += 4;
            return true;
        }
        
        if (c == 'f')
        {
            _index += 5;
            return false;
        }
        
        if (c == '"')
        {
            return ParseString();
        }
        
        if (c == '{')
        {
            return ParseObject();
        }
        
        if (c == '[')
        {
            return ParseArray();
        }
        
        if (c == '-' || char.IsDigit(c))
        {
            return ParseNumber();
        }
        
        return null;
    }

    private static string ParseString()
    {
        _index++;
        var start = _index;
        var result = new StringBuilder();
        
        while (_index < _json.Length && _json[_index] != '"')
        {
            if (_json[_index] == '\\')
            {
                result.Append(_json.Substring(start, _index - start));
                _index++;
                if (_index < _json.Length)
                {
                    var escaped = _json[_index];
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        default: result.Append(escaped); break;
                    }
                    _index++;
                    start = _index;
                }
            }
            else
            {
                _index++;
            }
        }
        
        result.Append(_json.Substring(start, _index - start));
        _index++;
        
        return result.ToString();
    }

    private static object ParseNumber()
    {
        var start = _index;
        
        if (_json[_index] == '-') _index++;
        
        while (_index < _json.Length && char.IsDigit(_json[_index]))
        {
            _index++;
        }
        
        var isDouble = false;
        if (_index < _json.Length && _json[_index] == '.')
        {
            isDouble = true;
            _index++;
            while (_index < _json.Length && char.IsDigit(_json[_index]))
            {
                _index++;
            }
        }
        
        if (_index < _json.Length && (_json[_index] == 'e' || _json[_index] == 'E'))
        {
            isDouble = true;
            _index++;
            if (_index < _json.Length && (_json[_index] == '+' || _json[_index] == '-'))
            {
                _index++;
            }
            while (_index < _json.Length && char.IsDigit(_json[_index]))
            {
                _index++;
            }
        }
        
        var numStr = _json.Substring(start, _index - start);
        
        if (isDouble)
        {
            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }
        else
        {
            return long.Parse(numStr);
        }
    }

    private static Dictionary<string, object> ParseObject()
    {
        var result = new Dictionary<string, object>();
        _index++;
        
        SkipWhitespace();
        
        if (_index < _json.Length && _json[_index] == '}')
        {
            _index++;
            return result;
        }
        
        while (_index < _json.Length)
        {
            SkipWhitespace();
            
            var key = ParseString();
            
            SkipWhitespace();
            _index++;
            
            var value = ParseValue();
            
            result[key] = value;
            
            SkipWhitespace();
            
            if (_index < _json.Length && _json[_index] == '}')
            {
                _index++;
                break;
            }
            
            if (_index < _json.Length && _json[_index] == ',')
            {
                _index++;
            }
        }
        
        return result;
    }

    private static List<object> ParseArray()
    {
        var result = new List<object>();
        _index++;
        
        SkipWhitespace();
        
        if (_index < _json.Length && _json[_index] == ']')
        {
            _index++;
            return result;
        }
        
        while (_index < _json.Length)
        {
            var value = ParseValue();
            result.Add(value);
            
            SkipWhitespace();
            
            if (_index < _json.Length && _json[_index] == ']')
            {
                _index++;
                break;
            }
            
            if (_index < _json.Length && _json[_index] == ',')
            {
                _index++;
            }
        }
        
        return result;
    }
}
