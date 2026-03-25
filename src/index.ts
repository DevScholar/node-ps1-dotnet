import * as fs from 'node:fs';
import * as path from 'node:path';
import * as cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getPowerShellPath } from './utils.js';
import { IpcSync } from './ipc.js';
import { getIpc, setIpc, getProc, setProc, getInitialized, setInitialized, getCachedRuntimeInfo, setCachedRuntimeInfo } from './state.js';
import { callbackRegistry, createProxyWithInlineProps, createProxy, setNodePs1Dotnet, setPollingMode, releaseObject } from './proxy.js';
export { releaseObject };
import { createNamespaceProxy, createExportNamespaceProxy } from './namespace.js';

export const __filename = fileURLToPath(import.meta.url);
export const __dirname = path.dirname(__filename);

function cleanup() {
    if (!getInitialized()) return;
    setInitialized(false);
    
    const ipc = getIpc();
    if (ipc) {
        try {
            ipc.close();
        } catch {}
    }
    
    const proc = getProc();
    if (proc && !proc.killed) {
        try {
            proc.kill('SIGKILL');
        } catch {}
    }
    
    setProc(null);
    setIpc(null);
}

function doInitialize() {
    if (getInitialized()) return;
    
    if (process.platform !== 'win32') {
        throw new Error('node-ps1-dotnet is only supported on Windows. Use node-with-gjs for Linux/macOS.');
    }
    
    const pipeName = `PsNode_${process.pid}_${Math.random().toString(36).slice(2, 10)}`;
    const scriptPath = path.join(__dirname, '..', 'scripts', 'PsHost.ps1');

    if (!fs.existsSync(scriptPath)) {
        throw new Error(`Cannot find PsHost.ps1: ${scriptPath}`);
    }

    const powerShellPath = getPowerShellPath();
    const proc = cp.spawn(powerShellPath, [
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-OutputFormat', 'Text',
        '-Command', `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8; $PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'; chcp.com 65001 > $null; & '${scriptPath}' -PipeName '${pipeName}'`
    ], { 
        stdio: 'inherit', 
        windowsHide: false,
        env: { 
            ...process.env, 
            DOTNET_SYSTEM_GLOBALIZATION_INVARIANT: '0',
            DOTNET_SYSTEM_GLOBALIZATION_CULTURE: 'en-US',
            DOTNET_UTF8_GLOBALIZATION: '1'
        },
        shell: false
    });

    setProc(proc);
    proc.unref();

    proc.on('exit', () => {
        cleanup();
        process.exit(0);
    });

    process.on('beforeExit', () => {
        cleanup();
        process.exit(0); 
    });
    
    process.on('exit', () => {
        cleanup();
    });
    
    process.on('SIGINT', () => {
        cleanup();
        process.exit(0);
    });
    
    process.on('SIGTERM', () => {
        cleanup();
        process.exit(0);
    });
    
    process.on('uncaughtException', (err) => {
        console.error('Uncaught Exception:', err);
        cleanup();
        process.exit(1);
    });

    const ipc = new IpcSync(pipeName, (res: any) => {
        const cb = callbackRegistry.get(res.callbackId!);
        if (cb) {
            const wrappedArgs = (res.args || []).map((arg: any) => {
                if (arg && arg.type === 'ref' && arg.props) {
                    return createProxyWithInlineProps(arg);
                }
                return createProxy(arg);
            });
            return cb(...wrappedArgs);
        }
        return null;
    });

    ipc.connect();
    setIpc(ipc);
    setInitialized(true);
}

export const node_ps1_dotnet = {
    _load(typeName: string): any {
        doInitialize();
        const ipc = getIpc();
        const res = ipc!.send({ action: 'GetType', typeName });
        return createProxy(res);
    },

    _release(id: string) {
        const ipc = getIpc();
        if (ipc) {
            try { ipc!.send({ action: 'Release', targetId: id }); } catch {}
        }
    },

    _close() {
        const proc = getProc();
        if (proc) proc.kill();
        cleanup();
    },

    _loadAssembly(assemblyName: string): any {
        doInitialize();
        const ipc = getIpc();
        const res = ipc!.send({ action: 'LoadAssembly', assemblyName });
        return createProxy(res);
    },

    _loadFrom(filePath: string): any {
        doInitialize();
        const ipc = getIpc();
        const res = ipc!.send({ action: 'LoadFrom', filePath });
        return createProxy(res);
    },

    _getRuntimeInfo(): { frameworkMoniker: string; runtimeVersion: string } {
        if (getCachedRuntimeInfo()) return getCachedRuntimeInfo()!;
        doInitialize();
        const ipc = getIpc();
        const res = ipc!.send({ action: 'GetRuntimeInfo' });
        const info = {
            frameworkMoniker: res.frameworkMoniker || 'netstandard2.0',
            runtimeVersion: res.runtimeVersion || '0.0.0'
        };
        setCachedRuntimeInfo(info);
        return info;
    },

    _executeScript(webViewId: string, script: string): string {
        doInitialize();
        const ipc = getIpc();
        const res = ipc!.send({ action: 'ExecuteScript', webViewId, script });
        if (res && res.type === 'error') {
            throw new Error(res.message);
        }
        return res && res.value !== undefined ? res.value : null;
    }
};

setNodePs1Dotnet(() => node_ps1_dotnet);

const dotnetProxy = new Proxy(function() {} as any, {
    get: (target: any, prop: string) => {
        if (prop === 'default') return dotnetProxy;
        if (prop === 'then') return undefined;
        if (prop === 'load') return (nameOrPath: string) => {
            if (nameOrPath.includes('/') || nameOrPath.includes('\\') ||
                nameOrPath.endsWith('.dll') || nameOrPath.endsWith('.exe')) {
                return node_ps1_dotnet._loadFrom(nameOrPath);
            } else {
                return node_ps1_dotnet._loadAssembly(nameOrPath);
            }
        };
        if (prop === 'frameworkMoniker') {
            return node_ps1_dotnet._getRuntimeInfo().frameworkMoniker;
        }
        if (prop === 'runtimeVersion') {
            return node_ps1_dotnet._getRuntimeInfo().runtimeVersion;
        }
        if (prop === 'addListener') return (event: string, fn: Function) => {
            if (event === 'resolving') {
                const cbId = '__resolving__';
                callbackRegistry.set(cbId, fn);
                doInitialize();
                getIpc()!.send({ action: 'SetResolvingCallback', callbackId: cbId });
            }
        };
        if (prop === '__inspect') {
            const ipc = getIpc();
            return (targetId: string, memberName: string) => ipc!.send({ action: 'Inspect', targetId, memberName });
        }

        // ─── WebView2 / WPF Framework API ────────────────────────────────────────
        // The following properties are intended for authors building WebView2-based
        // window frameworks (e.g. @devscholar/node-with-window). General users do
        // not need to call these directly; they may evolve with framework needs.

        // Registers a bridge script and navigates atomically: C# awaits the
        // AddScriptToExecuteOnDocumentCreatedAsync Task via ContinueWith before
        // calling Navigate, preventing the race condition in polling mode.
        if (prop === 'addScriptAndNavigate') {
            return (coreWebView2: any, script: string, url: string) => {
                doInitialize();
                getIpc()!.send({ action: 'AddScriptAndNavigate', targetId: coreWebView2.__ref, script, url });
            };
        }
        if (prop === 'addScriptAndNavigateToString') {
            return (coreWebView2: any, script: string, html: string) => {
                doInitialize();
                getIpc()!.send({ action: 'AddScriptAndNavigateToString', targetId: coreWebView2.__ref, script, html });
            };
        }
        // Polling-mode helpers used by node-with-window on Windows
        if (prop === 'startApplication') {
            return (app: any, window: any, webView?: any) => {
                doInitialize();
                const cmd: any = { action: 'StartApplication', appId: app.__ref, windowId: window.__ref };
                if (webView) cmd.webViewId = webView.__ref;
                getIpc()!.send(cmd);
                // After startApplication, the WPF dispatcher owns the UI thread.
                // Task.Wait() on the UI thread would deadlock; switch to fire-and-forget mode.
                setPollingMode(true);
            };
        }
        if (prop === 'pollEvent') {
            return () => {
                const ipc = getIpc();
                if (!ipc) return false;
                let processed = false;
                while (true) {
                    const res = ipc.send({ action: 'Poll' });
                    if (!res || (res as any).type !== 'ipc') break;
                    const message = (res as any).message;
                    if (!message) break;
                    let eventMsg: any;
                    try { eventMsg = JSON.parse(message); } catch { break; }
                    if (eventMsg && eventMsg.type === 'event' && eventMsg.callbackId) {
                        const cb = callbackRegistry.get(eventMsg.callbackId);
                        if (cb) {
                            const wrappedArgs = (eventMsg.args || []).map((arg: any) => {
                                if (arg && arg.type === 'ref' && arg.props) {
                                    return createProxyWithInlineProps(arg);
                                }
                                return createProxy(arg);
                            });
                            try { cb(...wrappedArgs); } catch (e) { console.error('Event callback error:', e); }
                            processed = true;
                        }
                    }
                }
                return processed;
            };
        }
        return node_ps1_dotnet._load(prop);
    },
    apply: (target: any, argArray: any[], newTarget: any) => {
        return createNamespaceProxy(argArray[0], node_ps1_dotnet);
    }
});

export default dotnetProxy;

let _System: any;
export function getSystem(): any {
    if (!_System) {
        _System = createExportNamespaceProxy('System', node_ps1_dotnet);
    }
    return _System;
}

export const System = new Proxy({} as any, {
    get: (target: any, prop: string) => {
        if (prop === 'then') return undefined;
        return getSystem()[prop];
    }
});
