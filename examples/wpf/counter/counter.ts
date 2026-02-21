import dotnet from '../../../src/index.ts';

dotnet.load('PresentationFramework');
dotnet.load('PresentationCore');
dotnet.load('WindowsBase');

const System = dotnet.System as any;
const Windows = System.Windows;
const Controls = System.Windows.Controls;
const Media = System.Windows.Media;

let clickCount = 0;

console.log("--- WPF Counter ---");

const mainWindow = new Windows.Window();
mainWindow.Title = "WPF Counter App";
mainWindow.Width = 400;
mainWindow.Height = 300;
mainWindow.WindowStartupLocation = Windows.WindowStartupLocation.CenterScreen;

const stackPanel = new Controls.StackPanel();
stackPanel.Margin = new Windows.Thickness(20);
stackPanel.HorizontalAlignment = Windows.HorizontalAlignment.Center;
stackPanel.VerticalAlignment = Windows.VerticalAlignment.Center;

const label = new Controls.Label();
label.Content = "Clicks: 0";
label.FontSize = 32;
label.FontFamily = new Media.FontFamily("Arial");
label.HorizontalContentAlignment = Windows.HorizontalAlignment.Center;
label.Margin = new Windows.Thickness(0, 0, 0, 20);
stackPanel.Children.Add(label);

const button = new Controls.Button();
button.Content = "Click to Add";
button.FontSize = 18;
button.Padding = new Windows.Thickness(20, 10, 20, 10);
button.HorizontalAlignment = Windows.HorizontalAlignment.Center;

button.add_Click((sender: any, e: any) => {
    clickCount++;
    const message = `Clicked ${clickCount} times`;
    label.Content = message;
    console.log(message);
});

stackPanel.Children.Add(button);

mainWindow.Content = stackPanel;

console.log("Click the button to increase the counter...");

const app = new Windows.Application();
app.Run(mainWindow);
