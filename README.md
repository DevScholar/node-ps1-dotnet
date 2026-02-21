# Node PS1 for .NET

⚠️ This project is still in pre-alpha stage, and API is subject to change. 

This is a project that mimics the [Node API for .NET](https://github.com/microsoft/node-api-dotnet), aiming to utilize the built-in PowerShell 5.1 in Windows to replace the full high-version .NET runtime, thereby reducing the program's size. Since this project uses IPC instead of C++ Addon, it is compatible not only with Node but also with Deno and Bun. You can run its example programs in the examples folder.

# Example Code

```js
// examples/winforms/clock-app/clock-app.ts
import dotnet from '../../../src/index.ts';

dotnet.load('System.Windows.Forms');
dotnet.load('System.Drawing');

const System = dotnet.System as any;
const Forms = System.Windows.Forms;
const Drawing = System.Drawing;

console.log("--- WinForms Clock ---");

const form = new Forms.Form();
form.Text = "Clock App";
form.Width = 400;
form.Height = 300;
form.StartPosition = 1;

const label = new Forms.Label();
label.Dock = 5;
label.TextAlign = 32;
label.Text = "Loading...";

label.Font = new Drawing.Font("Impact", 36);
form.Controls.Add(label);

const timer = new Forms.Timer();
timer.Interval = 1000;

let running = true;

form.add_FormClosing(() => {
    running = false;
    timer.Stop();
});

timer.add_Tick(() => {
    if (!running) return;
    const now = new Date().toLocaleTimeString();
    label.Text = now;

    if (new Date().getSeconds() % 2 === 0) {
        label.ForeColor = Drawing.Color.Red;
    } else {
        label.ForeColor = Drawing.Color.Black;
    }
});

timer.Start();

console.log("Starting application...");

Forms.Application.Run(form);
```

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
## WinForms Counter App

```bat
node start.js examples/winforms/counter/counter.ts
```
## WPF Counter App

```bat
node start.js examples/wpf/counter/counter.ts
```
## WPF WebView2 Browser

```bat
node start.js examples/wpf/webview2-browser/webview2-browser.ts
```
# License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for details.