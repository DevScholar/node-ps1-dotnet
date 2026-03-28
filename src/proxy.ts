import { getIpc } from './state.js';

export let node_ps1_dotnetGetter: (() => any) | null = null;

export function setNodePs1Dotnet(fn: () => any) {
    node_ps1_dotnetGetter = fn;
}

export function getNodePs1Dotnet() {
    if (node_ps1_dotnetGetter) {
        return node_ps1_dotnetGetter();
    }
    throw new Error('node_ps1_dotnet not initialized');
}

export const callbackRegistry = new Map<string, Function>();
export const typeMetadataCache = new Map<string, Map<string, string>>();
export const globalTypeCache = new Map<string, Map<string, string>>();
export const typeNameCache = new Map<string, string>();

// Tracks event subscriptions per .NET object: objectId → Map<cbId, {callback, eventName}>.
// Used to clean up callbackRegistry entries when an object is released (GC or manual).
const objectEventMap = new Map<string, Map<string, { callback: Function; eventName: string }>>();

// Cleans up all JS-side state for a released object id.
// Called both from FinalizationRegistry and from releaseObject().
function cleanupObjectById(id: string, isExplicitRelease: boolean = false): void {
    typeMetadataCache.delete(id);
    typeNameCache.delete(id);
    const events = objectEventMap.get(id);
    if (events) {
        // Only delete event callbacks on explicit release, not on GC
        // This prevents menu callbacks from being deleted prematurely
        if (isExplicitRelease) {
            for (const cbId of events.keys()) callbackRegistry.delete(cbId);
            objectEventMap.delete(id);
        }
    }
}

// Immediately releases a .NET proxy: cleans up JS maps and sends Release IPC.
// Use this instead of waiting for V8 GC when you want deterministic cleanup,
// e.g. for IDisposable objects (Streams, bitmaps) or large object graphs.
export function releaseObject(proxy: any): void {
    const id: unknown = proxy?.__ref;
    if (typeof id !== 'string') return;
    cleanupObjectById(id, true);
    try { getNodePs1Dotnet()._release(id); } catch {}
}

const gcRegistry = new FinalizationRegistry((id: string) => {
    cleanupObjectById(id, false);
    try { getNodePs1Dotnet()._release(id); } catch {}
});

// When true (after startApplication), Task results are fire-and-forget instead of
// synchronously blocked via AwaitTask. This prevents deadlocks in polling mode where
// task.Wait() on the WPF UI thread blocks the dispatcher that the task needs to complete.
export let pollingMode = false;
export function setPollingMode(val: boolean) { pollingMode = val; }
export const LARGE_ARRAY_THRESHOLD = 50;

export function getObjectTypeName(id: string): string | null {
    const ipc = getIpc();
    if (typeNameCache.has(id)) {
        return typeNameCache.get(id)!;
    }
    try {
        const res = ipc!.send({ action: 'GetTypeName', targetId: id });
        if (res && res.typeName) {
            typeNameCache.set(id, res.typeName);
            return res.typeName;
        }
    } catch {}
    return null;
}

export function getTypeMembers(typeName: string, memberNames: string[]): Map<string, string> | null {
    const ipc = getIpc();
    if (globalTypeCache.has(typeName)) {
        const cached = globalTypeCache.get(typeName)!;
        const result = new Map<string, string>();
        let allFound = true;
        for (const name of memberNames) {
            if (cached.has(name)) {
                result.set(name, cached.get(name)!);
            } else {
                allFound = false;
            }
        }
        if (allFound) return result;
    }

    try {
        const res = ipc!.send({ action: 'InspectType', typeName, memberNames });
        if (res && res.members) {
            if (!globalTypeCache.has(typeName)) {
                globalTypeCache.set(typeName, new Map());
            }
            const cache = globalTypeCache.get(typeName)!;
            for (const [name, type] of Object.entries(res.members as Record<string, string>)) {
                cache.set(name, type);
            }
            return new Map(Object.entries(res.members as Record<string, string>));
        }
    } catch {}
    return null;
}

export function ensureMemberTypeCached(id: string, memberName: string, memberCache: Map<string, string>) {
    if (memberCache.has(memberName)) return;

    const typeName = getObjectTypeName(id);
    if (typeName) {
        const members = getTypeMembers(typeName, [memberName]);
        if (members && members.has(memberName)) {
            memberCache.set(memberName, members.get(memberName)!);
            return;
        }
    }

    const ipc = getIpc();
    try {
        const inspectRes = ipc!.send({ action: 'Inspect', targetId: id, memberName });
        memberCache.set(memberName, inspectRes.memberType);
    } catch {
        memberCache.set(memberName, 'property');
    }
}

export function createLazyArray(arr: any[]): any {
    // Materialize a fully-proxied copy of the array on demand.
    function proxied(): any[] {
        return arr.map((item: any) => createProxy(item));
    }

    return new Proxy(arr, {
        get(target, prop) {
            if (prop === 'length') return target.length;

            // Index access: proxy individual items without materializing the whole array.
            if (typeof prop === 'string') {
                const index = Number(prop);
                if (!isNaN(index) && index >= 0 && index < target.length) {
                    return createProxy(target[index]);
                }
            }

            // Symbol.iterator: generator that proxies items one-by-one (no full copy).
            if (prop === Symbol.iterator) {
                return function* () {
                    for (let i = 0; i < target.length; i++) {
                        yield createProxy(target[i]);
                    }
                };
            }

            // Other symbols: forward to the underlying array.
            if (typeof prop === 'symbol') return (target as any)[prop];

            // All named array methods: materialize and delegate.
            if (typeof (target as any)[prop] === 'function') {
                return (...args: any[]) => (proxied() as any)[prop](...args);
            }

            return undefined;
        }
    });
}

// Marshal JS args to .NET-compatible format: refs by ID, functions as callbacks.
function marshalArgs(args: any[]): any[] {
    return args.map((a: any) => {
        if (a && a.__ref) return { __ref: a.__ref };
        if (typeof a === 'function') {
            const cbId = `cb_arg_${Date.now()}_${Math.random()}`;
            callbackRegistry.set(cbId, a);
            return { type: 'callback', callbackId: cbId };
        }
        // Date → ISO string so C# ConvertArgsForMethod can parse to DateTime/DateTimeOffset
        if (a instanceof Date) return a.toISOString();
        return a;
    });
}

// Core proxy factory for a .NET object reference.
// inlineProps: pre-fetched property values attached to event args, avoids extra round-trips.
// hasIndexer: true when the .NET type exposes a get_Item/set_Item indexer (e.g. IList, custom indexers).
function makeRefProxy(id: string, inlineProps?: Record<string, any>, hasIndexer?: boolean): any {
    const ipc = getIpc();

    if (!typeMetadataCache.has(id)) {
        typeMetadataCache.set(id, new Map());
    }
    const memberCache = typeMetadataCache.get(id)!;

    class Stub {}

    const proxy = new Proxy(Stub, {
        get: (_target: any, prop: string | symbol) => {
            if (prop === '__ref') return id;
            if (prop === '__inlineProps') return inlineProps;
            if (typeof prop === 'symbol') return undefined;

            // Numeric index → .NET indexer (obj[0], obj[1], ...)
            if (hasIndexer) {
                const idx = Number(prop);
                if (!isNaN(idx) && idx >= 0 && String(idx) === prop) {
                    const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: 'get_Item', args: [idx] });
                    return createProxy(res);
                }
            }

            // Fast path: use pre-fetched inline props (typically on event args).
            if (inlineProps && Object.prototype.hasOwnProperty.call(inlineProps, prop)) {
                memberCache.set(prop, 'property');
                return inlineProps[prop];
            }

            if (prop.startsWith('add_')) {
                const eventName = prop.substring(4);
                return (callback: Function) => {
                    const cbId = `cb_${Math.random().toString(36).slice(2)}_${Date.now()}`;
                    callbackRegistry.set(cbId, callback);
                    // Track so this callback is cleaned up when the object is released
                    if (!objectEventMap.has(id)) objectEventMap.set(id, new Map());
                    objectEventMap.get(id)!.set(cbId, { callback, eventName });
                    ipc!.send({ action: 'AddEvent', targetId: id, eventName, callbackId: cbId });
                };
            }

            // remove_EventName(callback) — mirrors .NET event -= pattern
            if (prop.startsWith('remove_')) {
                const eventName = prop.substring(7);
                return (callback: Function) => {
                    const events = objectEventMap.get(id);
                    if (!events) return;
                    for (const [cbId, entry] of events) {
                        if (entry.callback === callback && entry.eventName === eventName) {
                            callbackRegistry.delete(cbId);
                            events.delete(cbId);
                            try {
                                ipc!.send({ action: 'RemoveEvent', targetId: id, eventName, callbackId: cbId });
                            } catch {}
                            return;
                        }
                    }
                };
            }

            let memType = memberCache.get(prop);
            if (!memType) {
                ensureMemberTypeCached(id, prop, memberCache);
                memType = memberCache.get(prop);
            }

            if (memType === 'property') {
                const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [] });
                return createProxy(res);
            } else {
                return (...args: any[]) => {
                    const res = ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: marshalArgs(args) });
                    return createProxy(res);
                };
            }
        },

        set: (_target: any, prop: string | symbol, value: any) => {
            if (typeof prop === 'symbol') return false;
            // Numeric index → .NET indexer set
            if (hasIndexer) {
                const idx = Number(prop);
                if (!isNaN(idx) && idx >= 0 && String(idx) === prop) {
                    const netVal = (value && value.__ref) ? { __ref: value.__ref } : marshalArgs([value])[0];
                    ipc!.send({ action: 'Invoke', targetId: id, methodName: 'set_Item', args: [idx, netVal] });
                    return true;
                }
            }
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc!.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (_target: any, args: any[]) => {
            const res = ipc!.send({ action: 'New', typeId: id, args: marshalArgs(args) });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

export function createProxyWithInlineProps(meta: any): any {
    if (meta.type !== 'ref') return createProxy(meta);
    return makeRefProxy(meta.id, meta.props || {}, meta.hasIndexer === true);
}

export function createProxy(meta: any): any {
    const ipc = getIpc();

    if (meta.type === 'primitive' || meta.type === 'null') return meta.value;

    // DateTime/DateTimeOffset → JS Date  (node-api-dotnet: DateTime => Date)
    if (meta.type === 'date') return new Date(meta.value);

    // IDictionary<K,V> → JS Map<K,V>  (node-api-dotnet: IDictionary<K,V> => Map<K,V>)
    if (meta.type === 'map') {
        const entries: [any, any][] = (meta.entries as any[][]).map(
            (pair: any[]) => [createProxy(pair[0]), createProxy(pair[1])] as [any, any]
        );
        return new Map(entries);
    }

    // ref/out return — node-api-dotnet style: { result, outParam1, outParam2, ... }
    if (meta.type === 'refout') {
        const out: Record<string, any> = { result: createProxy(meta.result) };
        const outs = meta.outs as Record<string, any>;
        for (const key of Object.keys(outs)) {
            out[key] = createProxy(outs[key]);
        }
        return out;
    }

    if (meta.type === 'array') {
        const arr = meta.value;
        if (arr.length <= LARGE_ARRAY_THRESHOLD) {
            return arr.map((item: any) => createProxy(item));
        }
        return createLazyArray(arr);
    }

    if (meta.type === 'task') {
        const taskId = meta.id;
        // In polling mode, awaiting via task.Wait() on the WPF UI thread deadlocks.
        // Release the task reference and return null (fire-and-forget).
        if (pollingMode) {
            try { ipc!.send({ action: 'Release', targetId: taskId }); } catch {}
            return null;
        }
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
        const dotnet = getNodePs1Dotnet();
        return new Proxy({}, {
            get: (_target: any, prop: string) => {
                if (typeof prop !== 'string') return undefined;
                return dotnet._load(`${nsName}.${prop}`);
            }
        });
    }

    if (meta.type !== 'ref') return null;

    return makeRefProxy(meta.id, undefined, meta.hasIndexer === true);
}
