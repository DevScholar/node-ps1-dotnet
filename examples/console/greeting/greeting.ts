import { System } from '../../../src/index.ts';

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
