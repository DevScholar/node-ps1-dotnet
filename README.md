# Node PS1 for .NET

⚠️ This project is still in pre-alpha stage, and API is subject to change. 

This is a project that mimics the [Node API for .NET](https://github.com/microsoft/node-api-dotnet), aiming to utilize the built-in PowerShell 5.1 in Windows to replace the full high-version .NET runtime, thereby reducing the program's size. Since this project uses IPC instead of C++ Addon, it is compatible not only with Node but also with Deno and Bun. You can run its example programs in the examples folder.

# Requirements

- **Node.js** 22+ (uses `--experimental-transform-types` for native TypeScript support)
- **PowerShell** 5.1 (built-in on Windows 10/11)
- **.NET Framework** 4.5+ (required by PowerShell 5.1, pre-installed on Windows 10/11)

> Note: This project is Windows-only due to its dependency on PowerShell 5.1.

# Examples

Please visit the [node-ps1-dotnet-examples](https://github.com/devscholar/node-ps1-dotnet-examples) repository for working examples.

# Tests

Run all tests:

```bash
npm test
```

Run specific test files:

```bash
npm test -- --testPathPatterns=winforms
npm test -- --testPathPatterns=wpf
npm test -- --testPathPatterns=dotnet
```

Test files are located in the `__tests__` directory:

- `basic.test.ts` - Basic module functionality tests
- `ipc.test.ts` - IPC communication tests
- `winforms.test.ts` - WinForms GUI component tests
- `wpf.test.ts` - WPF GUI component tests
- `gui-events.test.ts` - GUI event handling tests
- `dotnet-enums.test.ts` - .NET enum tests
- `dotnet-console.test.ts` - Console class tests
- `dotnet-io.test.ts` - System.IO tests
- `dotnet-task.test.ts` - Task asynchronous tests
- `dotnet-misc.test.ts` - String, Array, Environment tests
- `dotnet-runtime.test.ts` - Runtime info and type loading tests

Note: GUI tests will not display windows - they only create objects in memory for verification.


# License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for details.