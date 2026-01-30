// src/index.ts
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { getPowerShellPath } from './utils.ts';
import { IpcSync, readLineSync } from './ipc.ts';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const gcRegistry = new FinalizationRegistry((id: string) => {
    try { node_ps1_dotnet._release(id); } catch {}
});

const callbackRegistry = new Map<string, Function>();
const typeMetadataCache = new Map<string, Map<string, string>>();

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
    initialized = true;
}

function createProxyWithInlineProps(meta: any): any {
    if (meta.type !== 'ref') return createProxy(meta);

    const id = meta.id!;
    const inlineProps = meta.props || {};

    if (!typeMetadataCache.has(id)) {
        typeMetadataCache.set(id, new Map());
    }
    const memberCache = typeMetadataCache.get(id)!;

    class Stub {}

    const proxy = new Proxy(Stub, {
        get: (target: any, prop: string) => {
            if (prop === '__ref') return id;
            if (prop === '__inlineProps') return inlineProps;
            if (typeof prop !== 'string') return undefined;

            if (inlineProps.hasOwnProperty(prop)) {
                memberCache.set(prop, 'property');
                return inlineProps[prop];
            }

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
                    const netArgs = args.map((a: any) => {
                        if (a && a.__ref) return { __ref: a.__ref };
                        if (typeof a === 'function') {
                            const cbId = `cb_arg_${Date.now()}_${Math.random()}`;
                            callbackRegistry.set(cbId, a);
                            return { type: 'callback', callbackId: cbId };
                        }
                        return a;
                    });
                    const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: netArgs });
                    return createProxy(res);
                };
            }
        },

        set: (target: any, prop: string, value: any) => {
            if (typeof prop !== 'string') return false;
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (target: any, args: any[]) => {
            const netArgs = args.map((a: any) => {
                if (a && a.__ref) return { __ref: a.__ref };
                if (typeof a === 'function') {
                    const cbId = `cb_ctor_${Date.now()}_${Math.random()}`;
                    callbackRegistry.set(cbId, a);
                    return { type: 'callback', callbackId: cbId };
                }
                return a;
            });
            const res = ipc!.send({ action: 'New', typeId: id, args: netArgs });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

function createProxy(meta: any): any {
    if (meta.type === 'primitive' || meta.type === 'null') return meta.value;

    if (meta.type === 'array') {
        return meta.value.map((item: any) => createProxy(item));
    }

    if (meta.type === 'task') {
        const taskId = meta.id;
        return new Promise((resolve, reject) => {
            try {
                const res = ipc!.send({ action: 'AwaitTask', taskId: taskId });
                resolve(createProxy(res));
            } catch (e) {
                reject(e);
            } finally {
                try { ipc!.send({ action: 'Release', targetId: taskId }); } catch {}
            }
        });
    }

    if (meta.type === 'namespace') {
        const nsName = meta.value;
        return new Proxy({}, {
            get: (target: any, prop: string) => {
                if (typeof prop !== 'string') return undefined;
                return node_ps1_dotnet._load(`${nsName}.${prop}`);
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
        get: (target: any, prop: string) => {
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
                    const netArgs = args.map((a: any) => {
                        if (a && a.__ref) return { __ref: a.__ref };
                        if (typeof a === 'function') {
                            const cbId = `cb_arg_${Date.now()}_${Math.random()}`;
                            callbackRegistry.set(cbId, a);
                            return { type: 'callback', callbackId: cbId };
                        }
                        return a;
                    });
                    const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: netArgs });
                    return createProxy(res);
                };
            }
        },

        set: (target: any, prop: string, value: any) => {
            if (typeof prop !== 'string') return false;
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (target: any, args: any[]) => {
            const netArgs = args.map((a: any) => {
                if (a && a.__ref) return { __ref: a.__ref };
                if (typeof a === 'function') {
                    const cbId = `cb_ctor_${Date.now()}_${Math.random()}`;
                    callbackRegistry.set(cbId, a);
                    return { type: 'callback', callbackId: cbId };
                }
                return a;
            });
            const res = ipc!.send({ action: 'New', typeId: id, args: netArgs });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

export const node_ps1_dotnet = {
    _load(typeName: string): any {
        initialize();
        const res = ipc!.send({ action: 'GetType', typeName });
        return createProxy(res);
    },

    _release(id: string) {
        if (ipc) {
            try { ipc!.send({ action: 'Release', targetId: id }); } catch {}
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
    }
};

function createNamespaceProxy(assemblyName: string) {
    return new Proxy({}, {
        get: (target: any, prop: string) => {
            if (typeof prop !== 'string') return undefined;
            if (prop === 'then') return undefined;
            return node_ps1_dotnet._load(`${assemblyName}.${prop}`);
        }
    });
}

const dotnetProxy = new Proxy(function() {} as any, {
    get: (target: any, prop: string) => {
        if (prop === 'default') return dotnetProxy;
        if (prop === 'then') return undefined;
        if (prop === '__inspect') {
            return (targetId: string, memberName: string) => ipc!.send({ action: 'Inspect', targetId, memberName });
        }
        return node_ps1_dotnet._load(prop);
    },
    apply: (target: any, argArray: any[], newTarget: any) => {
        return createNamespaceProxy(argArray[0]);
    }
});

export default dotnetProxy;
