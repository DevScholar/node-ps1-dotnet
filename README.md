# Node PS1 for .NET

⚠️ This project is still in pre-alpha stage, and API is subject to change. 

This is a project that mimics the [Node API for .NET](https://github.com/microsoft/node-api-dotnet), aiming to utilize the built-in PowerShell 5.1 in Windows to replace the full high-version .NET runtime, thereby reducing the program's size. Since this project uses IPC instead of C++ Addon, it is compatible not only with Node but also with Deno and Bun. You can run its example programs in the examples folder.

# Example Code

```js
import dotnet from '../../../src/index.ts';

const System = dotnet.System as any;
const Forms = System.Windows.Forms;
const Drawing = System.Drawing;

let clickCount = 0;

async function main() {
    console.log("--- WinForms Counter ---");

    const form = new Forms.Form();
    form.Text = "Counter App";
    form.Width = 350;
    form.Height = 200;
    form.StartPosition = 1;

    const label = new Forms.Label();
    label.Text = "Clicks: 0";
    label.Font = new Drawing.Font("Arial", 24);
    label.AutoSize = true;
    label.Location = new Drawing.Point(90, 30);
    form.Controls.Add(label);

    const button = new Forms.Button();
    button.Text = "Click to Add";
    button.Font = new Drawing.Font("Arial", 14);
    button.AutoSize = true;
    button.Location = new Drawing.Point(100, 90);
    
    button.add_Click(() => {
        clickCount++;
        const message = `Button clicked ${clickCount} times`;
        label.Text = message;
        console.log(message);
    });
    
    form.Controls.Add(button);

    console.log("Click the button to increase the counter...");
    Forms.Application.Run(form);
}

main().catch(console.error);

```

# Examples

You can use the `--runtime=[node|deno|bun]` option to specify the runtime. For example:

```bat
node start.js examples/clock-app/clock-app.ts --runtime=deno
```

## Clock App

```bat
node start.js examples/winforms/clock-app/clock-app.ts
```
## Counter App

```bat
node start.js examples/winforms/counter/counter.ts
```

## WebView2 Browser

```bat
node start.js examples/wpf/webview2-browser/webview2-browser.ts
```
# License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for details.