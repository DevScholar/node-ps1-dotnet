# Node PS1 for .NET

⚠️ This project is still in pre-alpha stage, and API is subject to change. 

This is a project that mimics the [Node API for .NET](https://github.com/microsoft/node-api-dotnet), aiming to utilize the built-in PowerShell 5.1 in Windows to replace the full high-version .NET runtime, thereby reducing the program's size. Since this project uses IPC instead of C++ Addon, it is compatible not only with Node but also with Deno and Bun. You can run its example programs in the examples folder.

# Requirements

- **Node.js** 22+ (uses `--experimental-transform-types` for native TypeScript support)
- **PowerShell** 5.1 (built-in on Windows 10/11)
- **.NET Framework** 4.5+ (required by PowerShell 5.1, pre-installed on Windows 10/11)

> Note: This project is Windows-only due to its dependency on PowerShell 5.1.

# Examples

You can use the `--runtime=[node|deno|bun]` option to specify the runtime. For example:

```bat
node start.js examples/console/console-input/console-input.ts --runtime=deno
```

## Console Apps

### Console Input App

```bat
node start.js examples/console/console-input/console-input.ts
```
### Await Delay App

```bat
node start.js examples/console/await-delay/await-delay.ts
```

## GUI Apps

### WinForms Counter App

```bat
node start.js examples/winforms/counter/counter.ts
```
### WinForms Drag Box App

```bat
node start.js examples/winforms/drag-box/drag-box.ts
```
### WPF Counter App

```bat
node start.js examples/wpf/counter/counter.ts
```
### WPF WebView2 Browser

```bat
node start.js examples/wpf/webview2-browser/webview2-browser.ts
```
# License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for details.