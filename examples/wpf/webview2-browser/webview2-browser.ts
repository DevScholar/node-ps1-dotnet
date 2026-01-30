import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

import dotnet from '../../../src/index.ts';

const System = dotnet.System as any;
const Windows = System.Windows;
const Controls = System.Windows.Controls;

const webview2LibPath = path.join(__dirname, 'WebView2Libs');

try {
    System.Reflection.Assembly.LoadFrom(path.join(webview2LibPath, 'Microsoft.Web.WebView2.Core.dll'));
    System.Reflection.Assembly.LoadFrom(path.join(webview2LibPath, 'Microsoft.Web.WebView2.Wpf.dll'));
} catch (e) {
    console.error("Core Dll Load Failed:", e);
}

const WebView2WpfAssembly = System.Reflection.Assembly.LoadFrom(path.join(webview2LibPath, 'Microsoft.Web.WebView2.Wpf.dll'));

let browserWindow: any = null;
let webView: any = null;
const USER_DATA_FOLDER = path.join(__dirname, 'WebView2_Data');
const COUNTER_HTML_PATH = path.join(__dirname, 'counter.html');

async function createBrowser(): Promise<void> {
    console.log('--- Initializing WebView2 (Counter App) ---');

    const WebView2Type = WebView2WpfAssembly.GetType('Microsoft.Web.WebView2.Wpf.WebView2');
    webView = new WebView2Type();

    const CreationPropertiesType = WebView2WpfAssembly.GetType('Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties');
    const props = new CreationPropertiesType();
    if (!fs.existsSync(USER_DATA_FOLDER)) fs.mkdirSync(USER_DATA_FOLDER, { recursive: true });
    props.UserDataFolder = USER_DATA_FOLDER;
    props.Language = "zh-CN";
    webView.CreationProperties = props;

    browserWindow = new Windows.Window();
    browserWindow.Title = 'Counter App - WebView2';
    browserWindow.Width = 500;
    browserWindow.Height = 400;
    browserWindow.WindowStartupLocation = Windows.WindowStartupLocation.CenterScreen;

    const grid = new Controls.Grid();
    browserWindow.Content = grid;
    grid.Children.Add(webView);

    webView.add_CoreWebView2InitializationCompleted((sender: any, e: any) => {
        if (e.IsSuccess) {
            console.log('WebView2 Initialized Successfully');
            
            const coreWebView2 = webView.CoreWebView2;

            coreWebView2.add_WebMessageReceived((sender2: any, e2: any) => {
                const message = e2.TryGetWebMessageAsString();
                if (message) {
                    console.log('[WebView2] ' + message);
                }
            });

            const script = `
                (function() {
                    var originalLog = console.log;
                    console.log = function(msg) {
                        originalLog(msg);
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage(msg);
                        }
                    };
                })();
            `;
            coreWebView2.ExecuteJavaScript(script);
        } else {
            console.error('FAILURE: Init failed', e.InitializationException?.Message);
        }
    });

    webView.add_NavigationCompleted((sender: any, e: any) => {
        console.log('Page Loaded Successfully');
    });

    webView.Source = new System.Uri(COUNTER_HTML_PATH);

    browserWindow.add_Closed((sender: any, e: any) => {
        process.exit(0);
    });

    const app = new Windows.Application();
    app.Run(browserWindow);
}

createBrowser().catch(err => {
    console.error(err);
    process.exit(1);
});
