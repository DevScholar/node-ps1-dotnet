using System;

public static class PsHostEntry
{
    public static void Run(string pipeName)
    {
        BridgeState.PipeName = pipeName;
        PsHost.ProcessNestedCommands = PsHost.RunProcessNestedCommands;
        
        try
        {
            PsHost.StartServer();
        }
        finally
        {
            if (BridgeState.PipeServer != null)
            {
                BridgeState.PipeServer.Dispose();
            }
        }
    }
}
