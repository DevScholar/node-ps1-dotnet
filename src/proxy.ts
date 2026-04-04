import { getIpc } from './state.js';
import { randomUUID } from 'node:crypto';
import { Duplex } from 'node:stream';

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

// Dispatch a batch of serialised event strings (from a Poll response) synchronously.
// Used both by the regular setInterval poller and by the ShowDialog spin-poll loop.
//
// Coalescing: if the same callbackId appears multiple times in one batch, only the
// last occurrence is dispatched. This matches OS-level WM_MOUSEMOVE coalescing —
// for high-frequency positional events (drag, scroll) only the latest value matters,
// and processing stale intermediate values would accumulate errors.
export function dispatchPollEvents(events: string[]): void {
    // Parse all events, keeping only the last entry per callbackId.
    const coalesced = new Map<string, any>();
    for (const evtStr of events) {
        try {
            const evt = JSON.parse(evtStr);
            coalesced.set(evt.callbackId, evt);
        } catch {}
    }
    for (const evt of coalesced.values()) {
        const cb = callbackRegistry.get(evt.callbackId);
        if (cb) {
            const wrappedArgs = (evt.args || []).map((arg: any) => {
                if (arg && arg.type === 'ref' && arg.props) return createProxyWithInlineProps(arg);
                return createProxy(arg);
            });
            try { 
                cb(...wrappedArgs, evt.error || null);
            } catch {}
        }
    }
}
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
            const cbId = `cb_arg_${randomUUID()}`;
            callbackRegistry.set(cbId, a);
            return { type: 'callback', callbackId: cbId };
        }
        // Date → ISO string so C# ConvertArgsForMethod can parse to DateTime/DateTimeOffset
        if (a instanceof Date) return a.toISOString();
        if (Array.isArray(a)) return marshalArgs(a);
        return a;
    });
}

// Core proxy factory for a .NET object reference.
// inlineProps: pre-fetched property values attached to event args, avoids extra round-trips.
// hasIndexer: true when the .NET type exposes a get_Item/set_Item indexer (e.g. IList, custom indexers).
// isTask: true when the object is a Task, enabling await support via then() method.
function makeRefProxy(id: string, inlineProps?: Record<string, any>, hasIndexer?: boolean, isTask?: boolean, collectionKind?: 'list' | 'map' | 'enumerable'): any {
    const ipc = getIpc();
    if (!ipc) throw new Error('IPC not initialized');

    if (!typeMetadataCache.has(id)) {
        typeMetadataCache.set(id, new Map());
    }
    const memberCache = typeMetadataCache.get(id)!;

    // ── List collection helpers (pythonnet mode) ───────────────────────────────
    // Only created when collectionKind === 'list'. The generator lazily fetches
    // each item via indexed IPC; _listToArr() materialises the full JS array.
    // These are defined once per proxy instance (not per property access).
    const _listIter: (() => Generator<any, void, unknown>) | null =
        collectionKind === 'list'
            ? function* () {
                const cr = ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Count', args: [] }) as any;
                const n = (cr.value as number) | 0;
                for (let i = 0; i < n; i++) {
                    yield createProxy(ipc!.send({ action: 'Invoke', targetId: id, methodName: 'get_Item', args: [i] }));
                }
              }
            : null;
    const _listToArr: (() => any[]) | null = _listIter ? () => [..._listIter!()] : null;

    // ── Map collection helpers (pythonnet mode for IDictionary) ───────────────
    // Materialises the full dict via IPC; used for iteration/snapshot methods.
    const _mapToMap: (() => Map<any, any>) | null =
        collectionKind === 'map'
            ? () => {
                const res = ipc!.send({ action: 'MaterializeDict', targetId: id } as any) as any;
                return new Map(
                    (res.entries as any[][]).map(
                        (pair: any[]) => [createProxy(pair[0]), createProxy(pair[1])] as [any, any]
                    )
                );
              }
            : null;

    // ── Enumerable helpers (IEnumerable<T> ref proxy) ─────────────────────────
    // Materialises via IPC; used for iteration and array-style read methods.
    const _enumToArr: (() => any[]) | null =
        collectionKind === 'enumerable'
            ? () => {
                const res = ipc!.send({ action: 'MaterializeEnum', targetId: id } as any) as any;
                if (res.type === 'array') return (res.value as any[]).map((item: any) => createProxy(item));
                return [];
              }
            : null;

    class Stub {}

    const proxy = new Proxy(Stub, {
        get: (_target: any, prop: string | symbol) => {
            if (prop === '__ref') return id;
            if (prop === '__inlineProps') return inlineProps;
            
            // Task await support: make the proxy thenable
            // Uses asynchronous polling to allow Promise resolution to work correctly
            if (isTask && prop === 'then') {
                return (onFulfilled: any, onRejected: any) => {
                    return new Promise((resolve, reject) => {
                        const cbId = 'awaittask_' + randomUUID();
                        
                        callbackRegistry.set(cbId, (result: any, error?: string) => {
                            callbackRegistry.delete(cbId);
                            if (error) {
                                if (onRejected) {
                                    try { onRejected(new Error(error)); } catch {}
                                }
                                reject(new Error(error));
                            } else {
                                // CRITICAL: Call onFulfilled first to notify the JS engine
                                // This is what allows await to continue
                                if (onFulfilled) {
                                    try { onFulfilled(result); } catch {}
                                }
                                resolve(result);
                            }
                        });
                        
                        ipc.send({ action: 'AwaitTask', targetId: id, callbackId: cbId });
                        
                        // Async polling - allows Promise resolution to work correctly
                        const sa = new Int32Array(new SharedArrayBuffer(4));
                        const poll = () => {
                            if (!callbackRegistry.has(cbId)) return;
                            
                            try {
                                const pollRes = ipc.send({ action: 'Poll' } as any) as any;
                                if (pollRes?.type === 'poll' && Array.isArray(pollRes.events)) {
                                    dispatchPollEvents(pollRes.events);
                                }
                            } catch {}
                            
                            if (callbackRegistry.has(cbId)) {
                                Atomics.wait(sa, 0, 0, 16);
                                setImmediate(poll);
                            }
                        };
                        
                        setImmediate(poll);
                    });
                };
            }
            
            // CRITICAL: Non-Task proxies must NOT have a 'then' property.
            // If they do, Promise.resolve will treat them as thenables and call then(),
            // which causes infinite loops or wrong behavior.
            if (!isTask && prop === 'then') {
                return undefined;
            }
            
            if (typeof prop === 'symbol') {
                if (_listIter && prop === Symbol.iterator) return _listIter;
                if (_listIter && prop === Symbol.toStringTag) return 'DotNetList';
                if (_mapToMap && prop === Symbol.iterator) return () => _mapToMap!()[Symbol.iterator]();
                if (_mapToMap && prop === Symbol.toStringTag) return 'DotNetMap';
                if (_enumToArr && prop === Symbol.iterator) return function*() { for (const item of _enumToArr!()) yield item; };
                if (_enumToArr && prop === Symbol.toStringTag) return 'DotNetEnumerable';
                return undefined;
            }

            // ── List collection JS interface (pythonnet mode) ──────────────────
            // Lowercase names never clash with .NET PascalCase member names.
            if (_listIter !== null) {
                if (prop === 'length')     return (ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Count', args: [] }) as any).value;
                if (prop === 'map')        return (fn: any) => _listToArr!().map(fn);
                if (prop === 'filter')     return (fn: any) => _listToArr!().filter(fn);
                if (prop === 'forEach')    return (fn: any) => _listToArr!().forEach(fn);
                if (prop === 'find')       return (fn: any) => _listToArr!().find(fn);
                if (prop === 'findIndex')  return (fn: any) => _listToArr!().findIndex(fn);
                if (prop === 'some')       return (fn: any) => _listToArr!().some(fn);
                if (prop === 'every')      return (fn: any) => _listToArr!().every(fn);
                if (prop === 'reduce')     return (...a: any[]) => (_listToArr!() as any).reduce(...a);
                if (prop === 'includes')   return (item: any) => _listToArr!().includes(item);
                if (prop === 'indexOf')    return (item: any) => _listToArr!().indexOf(item);
                if (prop === 'at')         return (i: number) => { const a = _listToArr!(); return i < 0 ? a[a.length + i] : a[i]; };
                if (prop === 'join')       return (...a: any[]) => (_listToArr!() as any).join(...a);
                if (prop === 'slice')      return (...a: any[]) => _listToArr!().slice(...a);
                if (prop === 'entries')    return () => _listToArr!().entries();
                if (prop === 'keys')       return () => _listToArr!().keys();
                if (prop === 'values')     return () => _listToArr!().values();
                if (prop === 'flat')       return (...a: any[]) => (_listToArr!() as any).flat(...a);
                if (prop === 'flatMap')    return (fn: any) => _listToArr!().flatMap(fn);
                if (prop === 'toString')   return () => '[DotNetList]';
            }

            // ── Map collection JS interface (pythonnet mode for IDictionary) ───
            // Lowercase names (get/set/has/delete/size...) never clash with .NET PascalCase.
            if (_mapToMap !== null) {
                if (prop === 'size')    return (ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Count', args: [] }) as any).value;
                if (prop === 'get')     return (key: any) => createProxy(ipc!.send({ action: 'Invoke', targetId: id, methodName: 'get_Item', args: marshalArgs([key]) }));
                if (prop === 'set')     return (key: any, val: any) => { ipc!.send({ action: 'Invoke', targetId: id, methodName: 'set_Item', args: marshalArgs([key, val]) }); };
                if (prop === 'has')     return (key: any) => createProxy(ipc!.send({ action: 'Invoke', targetId: id, methodName: 'ContainsKey', args: marshalArgs([key]) }));
                if (prop === 'delete')  return (key: any) => createProxy(ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Remove', args: marshalArgs([key]) }));
                if (prop === 'clear')   return () => { ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Clear', args: [] }); };
                if (prop === 'keys')    return () => _mapToMap!().keys();
                if (prop === 'values')  return () => _mapToMap!().values();
                if (prop === 'entries') return () => _mapToMap!().entries();
                if (prop === 'forEach') return (fn: any) => _mapToMap!().forEach(fn);
                if (prop === 'toString') return () => '[DotNetMap]';
            }

            // ── Enumerable JS interface (IEnumerable<T> ref proxy) ───────────
            if (_enumToArr !== null) {
                if (prop === 'length')     return _enumToArr!().length;
                if (prop === 'map')        return (fn: any) => _enumToArr!().map(fn);
                if (prop === 'filter')     return (fn: any) => _enumToArr!().filter(fn);
                if (prop === 'forEach')    return (fn: any) => _enumToArr!().forEach(fn);
                if (prop === 'find')       return (fn: any) => _enumToArr!().find(fn);
                if (prop === 'findIndex')  return (fn: any) => _enumToArr!().findIndex(fn);
                if (prop === 'some')       return (fn: any) => _enumToArr!().some(fn);
                if (prop === 'every')      return (fn: any) => _enumToArr!().every(fn);
                if (prop === 'reduce')     return (...a: any[]) => (_enumToArr!() as any).reduce(...a);
                if (prop === 'includes')   return (item: any) => _enumToArr!().includes(item);
                if (prop === 'indexOf')    return (item: any) => _enumToArr!().indexOf(item);
                if (prop === 'at')         return (i: number) => { const a = _enumToArr!(); return i < 0 ? a[a.length + i] : a[i]; };
                if (prop === 'join')       return (...a: any[]) => (_enumToArr!() as any).join(...a);
                if (prop === 'slice')      return (...a: any[]) => _enumToArr!().slice(...a);
                if (prop === 'entries')    return () => _enumToArr!().entries();
                if (prop === 'keys')       return () => _enumToArr!().keys();
                if (prop === 'values')     return () => _enumToArr!().values();
                if (prop === 'flat')       return (...a: any[]) => (_enumToArr!() as any).flat(...a);
                if (prop === 'flatMap')    return (fn: any) => _enumToArr!().flatMap(fn);
                if (prop === 'toString')   return () => '[DotNetEnumerable]';
            }

            // Numeric index → .NET indexer (obj[0], obj[1], ...)
            if (hasIndexer) {
                const idx = Number(prop);
                if (!isNaN(idx) && idx >= 0 && String(idx) === prop) {
                    const res = ipc.send({ action: 'Invoke', targetId: id, methodName: 'get_Item', args: [idx] });
                    return createProxy(res);
                }
            }

            // Fast path: use pre-fetched inline props (typically on event args).
            if (inlineProps && Object.prototype.hasOwnProperty.call(inlineProps, prop)) {
                memberCache.set(prop, 'property');
                const val = inlineProps[prop];
                // If the stored value is a protocol-format object, wrap it as a proxy.
                // e.g. Request: {type:'ref', id:'...', props:{Uri:'...', Method:'...'}}
                if (val !== null && val !== undefined && typeof val === 'object' && typeof val.type === 'string') {
                    if (val.type === 'ref' && val.props) return createProxyWithInlineProps(val);
                    return createProxy(val);
                }
                return val;
            }

            if (prop.startsWith('add_')) {
                const eventName = prop.substring(4);
                return (callback: Function) => {
                    const cbId = `cb_${randomUUID()}`;
                    callbackRegistry.set(cbId, callback);
                    // Track so this callback is cleaned up when the object is released
                    if (!objectEventMap.has(id)) objectEventMap.set(id, new Map());
                    objectEventMap.get(id)!.set(cbId, { callback, eventName });
                    ipc.send({ action: 'AddEvent', targetId: id, eventName, callbackId: cbId });
                };
            }

            // addSync_EventName: like add_EventName but C# blocks until the JS handler
            // returns, and the return value is forwarded back to the .NET event handler.
            // Use this for events where the handler must influence .NET behavior synchronously,
            // e.g. obj.addSync_Closing((s, e) => { e.Cancel = true; })
            if (prop.startsWith('addSync_')) {
                const eventName = prop.substring(8);
                return (callback: Function) => {
                    const cbId = `cbsync_${randomUUID()}`;
                    callbackRegistry.set(cbId, callback);
                    if (!objectEventMap.has(id)) objectEventMap.set(id, new Map());
                    objectEventMap.get(id)!.set(cbId, { callback, eventName });
                    ipc.send({ action: 'AddSyncEvent', targetId: id, eventName, callbackId: cbId });
                };
            }

            // remove_EventName(callback) — mirrors .NET event -= pattern
            if (prop.startsWith('remove_') || prop.startsWith('removeSync_')) {
                const eventName = prop.startsWith('removeSync_') ? prop.substring(11) : prop.substring(7);
                return (callback: Function) => {
                    const events = objectEventMap.get(id);
                    if (!events) return;
                    for (const [cbId, entry] of events) {
                        if (entry.callback === callback && entry.eventName === eventName) {
                            callbackRegistry.delete(cbId);
                            events.delete(cbId);
                            if (events.size === 0) objectEventMap.delete(id);
                            try {
                                ipc.send({ action: 'RemoveEvent', targetId: id, eventName, callbackId: cbId });
                            } catch (e) {
                                console.error('[RemoveEvent] Error:', (e as any).message);
                            }
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
                const res = ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: [] });
                return createProxy(res);
            } else {
                // Special handling for ShowDialog: synchronous spin-poll loop that pumps
                // events while the WPF nested dialog loop runs on the C# side.
                // C# pre-sends {type:'showDialogPending',callbackId} then runs ShowDialog;
                // Node.js keeps polling until the dialog-result event arrives, dispatching
                // all other events (e.g. button clicks) along the way.
                // This preserves the synchronous `const result = dialog.ShowDialog()` API.
                if (prop === 'ShowDialog') {
                    return (...args: any[]) => {
                        const res = ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: marshalArgs(args) });
                        if (res && (res as any).type === 'showDialogPending') {
                            const cbId = (res as any).callbackId as string;
                            let done = false;
                            let result: any = undefined;
                            let error: string | undefined;
                            callbackRegistry.set(cbId, (r: any, e?: string) => {
                                callbackRegistry.delete(cbId);
                                error = e || undefined;
                                result = r;
                                done = true;
                            });
                            // Spin-poll: keep sending Poll until done is set by dialog result callback.
                            // Atomics.wait provides a short sleep to avoid busy-waiting without
                            // releasing the event loop (which would allow setInterval polls to race).
                            const sa = new Int32Array(new SharedArrayBuffer(4));
                            while (!done) {
                                try {
                                    const pollRes = ipc.send({ action: 'Poll' } as any) as any;
                                    if (pollRes?.type === 'poll' && Array.isArray(pollRes.events)) {
                                        dispatchPollEvents(pollRes.events);
                                    }
                                } catch {}
                                if (!done) Atomics.wait(sa, 0, 0, 16);
                            }
                            if (error) throw new Error(error);
                            return result;
                        }
                        // Fallback: sync result from old C# or non-WPF ShowDialog
                        if (res && res.type === 'primitive') return res.value;
                        if (res && res.type === 'null') return null;
                        return res;
                    };
                }
                return (...args: any[]) => {
                    const res = ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: marshalArgs(args) });
                    if (res && (res as any).type === 'appStart') {
                        try { getNodePs1Dotnet()._onAppStart(); } catch {}
                        return true;
                    }
                    return createProxy(res);
                };
            }
        },

        set: (_target: any, prop: string | symbol, value: any) => {
            if (typeof prop === 'symbol') return false;
            if (!ipc) throw new Error('IPC not initialized');
            // Numeric index → .NET indexer set
            if (hasIndexer) {
                const idx = Number(prop);
                if (!isNaN(idx) && idx >= 0 && String(idx) === prop) {
                    const netVal = (value && value.__ref) ? { __ref: value.__ref } : marshalArgs([value])[0];
                    ipc.send({ action: 'Invoke', targetId: id, methodName: 'set_Item', args: [idx, netVal] });
                    return true;
                }
            }
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (_target: any, args: any[]) => {
            if (!ipc) throw new Error('IPC not initialized');
            const res = ipc.send({ action: 'New', typeId: id, args: marshalArgs(args) });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

// Wraps a .NET System.IO.Stream ref as a Node.js Duplex stream.
// Data is transferred as base64 strings over the synchronous IPC channel.
// WARNING: _read() calls Stream.Read() synchronously — avoid slow network streams
// as they will block the Node.js thread until data arrives.
function createStreamDuplex(meta: any): Duplex {
    const id: string = meta.id;
    const ipc = getIpc();

    const duplex = new Duplex({
        read(size: number) {
            try {
                const res = ipc!.send({ action: 'ReadChunk', targetId: id, size } as any) as any;
                if (res.type === 'null') {
                    this.push(null); // EOF
                } else {
                    this.push(Buffer.from(res.value as string, 'base64'));
                }
            } catch (err) {
                this.destroy(err as Error);
            }
        },
        write(chunk: Buffer | string, encoding: BufferEncoding, callback: (err?: Error | null) => void) {
            try {
                const buf = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as string, encoding);
                ipc!.send({ action: 'WriteChunk', targetId: id, data: buf.toString('base64') } as any);
                callback();
            } catch (err) {
                callback(err as Error);
            }
        },
        final(callback: (err?: Error | null) => void) {
            try {
                ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Flush', args: [] });
                callback();
            } catch (err) {
                callback(err as Error);
            }
        },
        destroy(err: Error | null, callback: (err?: Error | null) => void) {
            try {
                ipc!.send({ action: 'Invoke', targetId: id, methodName: 'Close', args: [] });
            } catch {}
            callback(err);
        }
    });

    // If stream is read-only, end writable side immediately
    if (!meta.canWrite) duplex.end();
    // If stream is write-only, end readable side immediately
    if (!meta.canRead) duplex.push(null);

    // Expose .NET ref so callers can still access .NET stream properties (Length, Position, etc.)
    (duplex as any).__ref = id;
    // Expose seek helper for seekable streams
    if (meta.canSeek) {
        (duplex as any).seek = (offset: number, origin: 0 | 1 | 2 = 0) =>
            (ipc!.send({ action: 'SeekStream', targetId: id, offset, origin } as any) as any).value;
    }

    gcRegistry.register(duplex, id);
    return duplex;
}

export function createProxyWithInlineProps(meta: any): any {
    if (meta.type !== 'ref') return createProxy(meta);
    return makeRefProxy(meta.id, meta.props || {}, meta.hasIndexer === true);
}

export function createProxy(meta: any): any {
    const ipc = getIpc();
    if (!ipc) throw new Error('IPC not initialized');

    if (meta.type === 'error') throw new Error(meta.message || 'Unknown .NET error');

    if (meta.type === 'primitive' || meta.type === 'null') return meta.value;

    // BigInteger → JS bigint
    if (meta.type === 'bigint') return BigInt(meta.value);

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

    // System.IO.Stream → Node.js Duplex  (node-api-dotnet: Stream => StreamDuplex)
    if (meta.type === 'stream') return createStreamDuplex(meta);

    if (meta.type === 'task') {
        // Return a proxy ref to the Task — do NOT auto-await here.
        // Sync task.Wait() on the UI thread causes deadlocks for Tasks that need
        // the dispatcher (e.g. WebView2 CreateAsync).  Let dotnet.awaitTask() handle
        // awaiting via the async ContinueWith path instead.
        // The proxy is thenable, so `await task` works naturally.
        return makeRefProxy(meta.id, undefined, undefined, true);
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

    const ck: 'list' | 'map' | 'enumerable' | undefined =
        (meta as any).collectionKind === 'list'       ? 'list' :
        (meta as any).collectionKind === 'map'        ? 'map' :
        (meta as any).collectionKind === 'enumerable' ? 'enumerable' :
        undefined;
    return makeRefProxy(meta.id, undefined, meta.hasIndexer === true, false, ck);
}
