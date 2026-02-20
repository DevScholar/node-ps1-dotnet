// examples/console/console-input/console-input.ts
import dotnet from '../../../src/index.ts';

const System = dotnet.System as any;
const Console = System.Console;

Console.WriteLine("=== Greeting Program ===");
Console.Write("Please enter your name: ");

const name = Console.ReadLine();

if (name && name.trim() !== "") {
    Console.WriteLine(`Hello, ${name}! Welcome to this program!`);
} else {
    Console.WriteLine("Hello, friend! Welcome to this program!");
}

Console.WriteLine("Program ended.");
