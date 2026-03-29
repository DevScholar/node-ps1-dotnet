import * as fs from 'node:fs';
import * as path from 'node:path';
import * as cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getPowerShellPath } from './utils.js';
import { IpcSync } from './ipc.js';
import { getIpc, setIpc, getProc, setProc, getInitialized, setInitialized, getCachedRuntimeInfo, setCachedRuntimeInfo } from './state.js';
import { callbackRegistry, createProxyWithInlineProps, createProxy, setNodePs1Dotnet, releaseObject, dispatchPollEvents } from './proxy.js';
export { releaseObject, callbackRegistry, createProxy, createProxyWithInlineProps };
import { createNamespaceProxy, createExportNamespaceProxy } from './namespace.js';

export const __filename = fileURLToPath(import.meta.url);
export const __dirname = path.dirname(__filename);

let stopPolling: (() => void) | null = null;
let initializingPromise: Promise<void> | null = null;

function startPolling() {
    if (stopPolling) return;
    let active = true;
    const timer = setInterval(() => {
        if (!active) { clearInterval(timer); stopPolling = null; return; }
        const ipc = getIpc();
        if (!ipc) return;
        try {
            const res = ipc.send({ action: 'Poll' } as any) as any;
            if (res && res.type === 'poll' && Array.isArray(res.events)) {
                dispatchPollEvents(res.events);
            }
        } catch (e) {
            console.error('[Poll] Error:', (e as any).message);
        }
    }, 8);
    timer.unref();
    stopPolling = () => { active = false; clearInterval(timer); };
}

function cleanup() {
    if (!getInitialized()) return;
    setInitialized(false);

    if (stopPolling) { stopPolling(); stopPolling = null; }

    const ipc = getIpc();
    if (ipc) {
        try {
            ipc.close();
        } catch (e) {
            console.error('[Poll] Error:', (e as any).message);
        }
    }
    
    const proc = getProc();
    if (proc && !proc.killed) {
        try {
            proc.kill('SIGKILL');
        } catch (e) {
            console.error('[Poll] Error:', (e as any).message);
        }
    }
    
    setProc(null);
    setIpc(null);
}

function doInitialize() {
    if (getInitialized()) return;
    if (initializingPromise) {
        // Wait for ongoing initialization synchronously (blocking)
        // This is acceptable since doInitialize is called from sync context
        return;
    }

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
            return cb(...wrappedArgs, res.error || null);
        }
        return null;
    });

    ipc.connect();
    setIpc(ipc);
    setInitialized(true);
    startPolling();
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
    },

    _addType(source: string, references?: string[]): any {
        doInitialize();
        const ipc = getIpc();
        const cmd: any = { action: 'AddType', source };
        if (references && references.length > 0) cmd.references = references;
        const res = ipc!.send(cmd);
        if (res && (res as any).type === 'error') {
            throw new Error('AddType failed: ' + (res as any).message);
        }
        return createProxy(res);
    },

    _awaitTask(task: any): Promise<any> {
        doInitialize();
        return new Promise<any>((resolve, reject) => {
            const cbId = 'awaittask_' + Math.random().toString(36).slice(2, 10);
            callbackRegistry.set(cbId, (result: any, error?: string) => {
                callbackRegistry.delete(cbId);
                if (error) { reject(new Error(error)); return; }
                resolve(result);
            });
            getIpc()!.send({ action: 'AwaitTask', targetId: task.__ref, callbackId: cbId });
        });
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
                const res = getIpc()!.send({ action: 'LoadFrom', filePath: nameOrPath });
                return createProxy(res);
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
        if (prop === 'addType') {
            return (source: string, references?: string[]) => {
                doInitialize();
                return node_ps1_dotnet._addType(source, references);
            };
        }
        if (prop === 'awaitTask') {
            return (task: any) => node_ps1_dotnet._awaitTask(task);
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
            return (app: any, window: any) => {
                doInitialize();
                getIpc()!.send({ action: 'InvokeDetached', targetId: app.__ref, methodName: 'Run', args: [{ __ref: window.__ref }] });
            };
        }
        if (prop === 'pollEvent') {
            return () => false;
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
