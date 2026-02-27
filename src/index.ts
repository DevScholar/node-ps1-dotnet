import * as fs from 'node:fs';
import * as path from 'node:path';
import * as cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getPowerShellPath } from './utils.js';
import { IpcSync } from './ipc.js';
import { getIpc, setIpc, getProc, setProc, getInitialized, setInitialized, getCachedRuntimeInfo, setCachedRuntimeInfo } from './state.js';
import { callbackRegistry, createProxyWithInlineProps, createProxy, setNodePs1Dotnet } from './proxy.js';
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
    
    const pipeName = `PsNode_${process.pid}_${Math.floor(Math.random() * 10000)}`;
    const scriptPath = path.join(__dirname, '..', 'scripts', 'PsHost.ps1');

    if (!fs.existsSync(scriptPath)) {
        throw new Error(`Cannot find PsHost.ps1: ${scriptPath}`);
    }

    const powerShellPath = getPowerShellPath();
    const proc = cp.spawn(powerShellPath, [
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-Command', `& '${scriptPath}' -PipeName '${pipeName}'`
    ], { stdio: 'inherit', windowsHide: false });

    setProc(proc);
    proc.unref();

    proc.on('exit', (code) => {
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

    _getAssembly(assemblyName: string): any {
        return this._load(assemblyName);
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
    }
};

setNodePs1Dotnet(() => node_ps1_dotnet);

const dotnetProxy = new Proxy(function() {} as any, {
    get: (target: any, prop: string) => {
        if (prop === 'default') return dotnetProxy;
        if (prop === 'then') return undefined;
        if (prop === 'load') return (assemblyNameOrFilePath: string) => {
            node_ps1_dotnet._loadAssembly(assemblyNameOrFilePath);
        };
        if (prop === 'frameworkMoniker') {
            return node_ps1_dotnet._getRuntimeInfo().frameworkMoniker;
        }
        if (prop === 'runtimeVersion') {
            return node_ps1_dotnet._getRuntimeInfo().runtimeVersion;
        }
        if (prop === '__inspect') {
            const ipc = getIpc();
            return (targetId: string, memberName: string) => ipc!.send({ action: 'Inspect', targetId, memberName });
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
