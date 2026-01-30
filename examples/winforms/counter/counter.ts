import dotnet from '../../../src/index.ts';

const System = dotnet.System as any;
const Forms = System.Windows.Forms;
const Drawing = System.Drawing;

let clickCount = 0;

async function main() {
    console.log("--- WinForms Counter ---");

    const form = new Forms.Form();
    form.Text = "Counter App";
    form.Width = 640;
    form.Height = 480;
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
        const message = `Clicked ${clickCount} times`;
        label.Text = message;
        console.log(message);
    });
    
    form.Controls.Add(button);

    console.log("Click the button to increase the counter...");
    Forms.Application.Run(form);
}

main().catch(console.error);
