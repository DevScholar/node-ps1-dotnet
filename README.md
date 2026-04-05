# Node PS1 for .NET

This project is in Beta stage.

This is a project that mimics the [Node API for .NET](https://github.com/microsoft/node-api-dotnet), aiming to utilize the built-in PowerShell 5.1 and .NET Framework 4.x in Windows to replace the full high-version .NET runtime, thereby reducing the program's size. Since this project uses IPC instead of C++ Addon, it is compatible not only with Node but also with Deno and Bun.

Moreover, this project supports .NET events and optional Python.NET-style type conversions, and also provides examples of common built-in .NET GUI frameworks. This makes writing GUI programs in Node PS1 for .NET very easy. However, Node API for .NET does not support these features, so you cannot write any GUI programs using only Node API for .NET.

# Requirements

- **Node.js** 18+ (LTS version recommended)
- **PowerShell** 5.1 (built-in on Windows 10/11)
- **.NET Framework** 4.5+ (required by PowerShell 5.1, pre-installed on Windows 10/11)

> Note: This project is Windows-only due to its dependency on PowerShell 5.1.

# Examples

Please visit the [node-ps1-dotnet-examples](https://github.com/devscholar/node-ps1-dotnet-examples) repository for working examples (WinForms, WPF, WebView2, etc.).

# Testing

Please visit the [testing.md](docs/testing.md) file for testing instructions.

# License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for details.