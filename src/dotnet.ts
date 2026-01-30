// src/dotnet.ts
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { IpcSync } from './ipc.js';
import { getPowerShellPath } from './utils.ts';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const gcRegistry = new FinalizationRegistry((id: string) => {
    try { Dotnet._release(id); } catch {}
});

const callbackRegistry = new Map<string, Function>();
const typeMetadataCache = new Map<string, Map<string, string>>();
const resolvingListeners = new Map<string, Function>();
let resolvingListenerCount = 0;

let ipc: IpcSync | null = null;
let proc: cp.ChildProcess | null = null;
let initialized = false;

function initialize() {
    if (initialized) return;
    
    const pipeName = `PsNode_${process.pid}_${Math.floor(Math.random() * 10000)}`;
    const scriptPath = path.join(__dirname, '..', 'scripts', 'PsHost.ps1');

    if (!fs.existsSync(scriptPath)) {
        throw new Error(`Cannot find PsHost.ps1: ${scriptPath}`);
    }

    const powerShellPath = getPowerShellPath();
    proc = cp.spawn(powerShellPath, [
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-Command', `& '${scriptPath}' -PipeName '${pipeName}'`
    ], { stdio: 'inherit', windowsHide: false });

    proc.on('exit', (code) => {
        process.exit(0);
    });

    ipc = new IpcSync(pipeName, (res: any) => {
        if (res.type === 'resolving') {
            const listeners = Array.from(resolvingListeners.values());
            for (const listener of listeners) {
                try {
                    const result = listener(res.assemblyName, res.assemblyVersion, (resolvedPath: string) => {
                        if (ipc) {
                            return ipc.send({ action: 'Resolved', resolvedPath, callbackId: res.callbackId });
                        }
                    });
                    if (result !== undefined) return result;
                } catch (e) {
                    console.error("Resolving listener error:", e);
                }
            }
            return null;
        }
        
        const cb = callbackRegistry.get(res.callbackId!);
        if (cb) {
            return cb(...(res.args || []));
        }
        return null;
    });

    ipc.connect();
    initialized = true;
}

function createProxy(meta: any): any {
    if (meta.type === 'primitive' || meta.type === 'null') return meta.value;

    if (meta.type === 'namespace') {
        const nsName = meta.value;
        return new Proxy({}, {
            get: (target, prop) => {
                if (typeof prop !== 'string') return undefined;
                if (prop === '__inspect') {
                    return (memberName: string) => ipc!.send({ action: 'Inspect', targetId: meta.id, memberName });
                }
                return Dotnet._load(`${nsName}.${prop}`);
            }
        });
    }

    if (meta.type !== 'ref') return null;

    const id = meta.id!;

    if (!typeMetadataCache.has(id)) {
        typeMetadataCache.set(id, new Map());
    }
    const memberCache = typeMetadataCache.get(id)!;

    class Stub {}

    const proxy = new Proxy(Stub, {
        get: (target, prop) => {
            if (prop === '__ref') return id;
            if (typeof prop !== 'string') return undefined;

            if (prop.startsWith('add_')) {
                const eventName = prop.substring(4);
                return (callback: Function) => {
                    const cbId = `cb_${Date.now()}_${Math.random()}`;
                    callbackRegistry.set(cbId, callback);
                    ipc!.send({ action: 'AddEvent', targetId: id, eventName, callbackId: cbId });
                };
            }

            let memType = memberCache.get(prop);

            if (!memType) {
                try {
                    const inspectRes = ipc!.send({ action: 'Inspect', targetId: id, memberName: prop });
                    memType = inspectRes.memberType;
                    memberCache.set(prop, memType!);
                } catch (e) {
                    memType = 'method';
                }
            }

            if (memType === 'property') {
                const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [] });
                return createProxy(res);
            } else {
                return (...args: any[]) => {
                    const netArgs = args.map((a: any) => (a && a.__ref) ? { __ref: a.__ref } : a);
                    const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: netArgs });
                    return createProxy(res);
                };
            }
        },

        set: (target, prop, value) => {
            if (typeof prop !== 'string') return false;
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (target, args) => {
            const netArgs = args.map((a: any) => (a && a.__ref) ? { __ref: a.__ref } : a);
            const res = ipc!.send({ action: 'New', typeId: id, args: netArgs });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

export const Dotnet = {
    _load(typeName: string): any {
        initialize();
        const res = ipc!.send({ action: 'GetType', typeName });
        return createProxy(res);
    },

    _release(id: string) {
        if (ipc) {
            try { ipc.send({ action: 'Release', targetId: id }); } catch {}
        }
    },

    _close() {
        if (proc) proc.kill();
        proc = null;
        ipc = null;
        initialized = false;
    },

    _getAssembly(assemblyName: string): any {
        return this._load(assemblyName);
    },

    get frameworkMoniker(): string {
        initialize();
        const res = ipc!.send({ action: 'GetFrameworkInfo' });
        return res.frameworkMoniker!;
    },

    get runtimeVersion(): string {
        initialize();
        const res = ipc!.send({ action: 'GetFrameworkInfo' });
        return res.runtimeVersion!;
    },

    addListener(
        event: "resolving",
        listener: (assemblyName: string, assemblyVersion: string, resolve: (resolvedPath: string) => void) => void
    ): void {
        const listenerId = `listener_${++resolvingListenerCount}`;
        resolvingListeners.set(listenerId, listener);
    },

    removeListener(
        event: "resolving",
        listener: (assemblyName: string, assemblyVersion: string) => void
    ): void {
        for (const [id, existingListener] of resolvingListeners.entries()) {
            if (existingListener === listener) {
                resolvingListeners.delete(id);
                break;
            }
        }
    },

    load(assemblyNameOrFilePath: string): void {
        initialize();
        ipc!.send({ action: 'LoadAssembly', assemblyPath: assemblyNameOrFilePath });
    },

    require(dotnetAssemblyFilePath: string): any {
        initialize();
        const res = ipc!.send({ action: 'RequireModule', assemblyPath: dotnetAssemblyFilePath });
        return createProxy(res);
    }
};

export default Dotnet;
