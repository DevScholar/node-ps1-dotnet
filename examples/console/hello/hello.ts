import dotnet from '../../../src/index.ts';

const System = dotnet.System as any;
const Console = System.Console;
const Task = System.Threading.Tasks.Task;
const Path = System.IO.Path;

Console.WriteLine('Hello from .NET!');
await Task.Delay(1000);
await Console.Out.WriteAsync("Hello, ");
await Task.Delay(1000);
await Console.Out.WriteLineAsync("World!");
await Task.Delay(1000);
