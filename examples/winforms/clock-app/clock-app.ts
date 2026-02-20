// examples/winforms/clock-app/clock-app.ts
import dotnet from '../../../src/index.ts';

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
