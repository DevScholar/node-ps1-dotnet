// examples/winforms/drag-box/drag-box.ts
import dotnet from '../../../src/index.ts';

dotnet.load('System.Windows.Forms');
dotnet.load('System.Drawing');

const System = dotnet.System as any;
const Forms = System.Windows.Forms;
const Drawing = System.Drawing;

console.log("--- WinForms Draggable Box ---");

Forms.Application.EnableVisualStyles();
Forms.Application.SetCompatibleTextRenderingDefault(false);

const form = new Forms.Form();
form.Text = "Draggable Box Example (High Frequency IPC)";
form.Width = 800;
form.Height = 600;
form.StartPosition = 1; // CenterScreen

const box = new Forms.Panel();
box.BackColor = Drawing.Color.Red;
box.Width = 100;
box.Height = 100;

// Track position locally to avoid synchronous read overhead over IPC
let currentX = 350;
let currentY = 250;
box.Location = new Drawing.Point(currentX, currentY);

form.Controls.Add(box);

// State tracking for drag and drop
let isDragging = false;
let startDragOffsetX = 0;
let startDragOffsetY = 0;

box.add_MouseDown((sender: any, e: any) => {
    isDragging = true;
    startDragOffsetX = e.X;
    startDragOffsetY = e.Y;
    box.BackColor = Drawing.Color.DarkRed; // Visual feedback
});

box.add_MouseUp((sender: any, e: any) => {
    isDragging = false;
    box.BackColor = Drawing.Color.Red;
});

box.add_MouseMove((sender: any, e: any) => {
    if (isDragging) {
        // e.X and e.Y are coordinates relative to the Box itself
        currentX = currentX + e.X - startDragOffsetX;
        currentY = currentY + e.Y - startDragOffsetY;
        
        // Push the new location to WinForms
        box.Location = new Drawing.Point(currentX, currentY);
    }
});

console.log("Initialization complete. Try dragging the red box!");
Forms.Application.Run(form);