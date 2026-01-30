const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');

const gcRegistry = new FinalizationRegistry((id) => {
    try { node_ps51.release(id); } catch {}
});

const callbackRegistry = new Map();
const typeMetadataCache = new Map();

let ipc = null;
let proc = null;
let initialized = false;

function readLineSync(fd) {
    let line = '';
    const buf = Buffer.alloc(1);
    while (true) {
        try {
            const r = fs.readSync(fd, buf, 0, 1, null);
            if (r === 0) return null;
            if (buf[0] === 10) break;
            line += String.fromCharCode(buf[0]);
        } catch (e) {
            return null;
        }
    }
    return line;
}

class IpcSync {
    constructor(pipeName, onEvent) {
        this.pipeName = pipeName;
        this.onEvent = onEvent;
        this.fd = 0;
    }

    connect() {
        const pipePath = `\\\\.\\pipe\\${this.pipeName}`;
        const start = Date.now();
        while (true) {
            try {
                this.fd = fs.openSync(pipePath, 'r+');
                break;
            } catch (e) {
                if (Date.now() - start > 5000) throw new Error(`Timeout connecting pipe: ${pipePath}`);
                const s = Date.now() + 50;
                while (Date.now() < s);
            }
        }
    }

    send(cmd) {
        try {
            fs.writeSync(this.fd, JSON.stringify(cmd) + '\n');
        } catch (e) {
            throw new Error("Pipe closed (Write failed)");
        }

        while (true) {
            const line = readLineSync(this.fd);
            if (line === null) throw new Error("Pipe closed (Read EOF)");
            if (!line.trim()) continue;

            let res;
            try {
                res = JSON.parse(line);
            } catch (e) {
                throw new Error(`Pipe closed (Invalid JSON): ${line}`);
            }

            if (res.type === 'event') {
                let result = null;
                try {
                    result = this.onEvent(res);
                } catch (e) {
                    console.error("Callback Error:", e);
                }
                
                const reply = { type: 'reply', result: result };
                try {
                    fs.writeSync(this.fd, JSON.stringify(reply) + '\n');
                } catch {}
                continue;
            }

            if (res.type === 'error') throw new Error(`Host Error: ${res.message}`);
            return res;
        }
    }
}

function initialize() {
    if (initialized) return;
    
    const pipeName = `PsNode_${process.pid}_${Math.floor(Math.random() * 10000)}`;
    const scriptPath = path.join(__dirname, 'scripts', 'PsHost.ps1');

    if (!fs.existsSync(scriptPath)) {
        throw new Error(`Cannot find PsHost.ps1: ${scriptPath}`);
    }

    console.log("DEBUG: Starting PowerShell...");

    proc = cp.spawn('powershell.exe', [
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-Command', `& '${scriptPath}' -PipeName '${pipeName}'`
    ], { stdio: 'inherit', windowsHide: false });

    ipc = new IpcSync(pipeName, (res) => {
        const cb = callbackRegistry.get(res.callbackId);
        if (cb) {
            return cb(...(res.args || []));
        }
        return null;
    });

    ipc.connect();
    initialized = true;
}

function createProxy(meta) {
    if (meta.type === 'primitive' || meta.type === 'null') return meta.value;

    if (meta.type === 'namespace') {
        const nsName = meta.value;
        return new Proxy({}, {
            get: (target, prop) => {
                if (typeof prop !== 'string') return undefined;
                return node_ps51.load(`${nsName}.${prop}`);
            }
        });
    }

    if (meta.type !== 'ref') return null;

    const id = meta.id;

    if (!typeMetadataCache.has(id)) {
        typeMetadataCache.set(id, new Map());
    }
    const memberCache = typeMetadataCache.get(id);

    class Stub {}

    const proxy = new Proxy(Stub, {
        get: (target, prop) => {
            if (prop === '__ref') return id;
            if (typeof prop !== 'string') return undefined;

            if (prop.startsWith('add_')) {
                const eventName = prop.substring(4);
                return (callback) => {
                    const cbId = `cb_${Date.now()}_${Math.random()}`;
                    callbackRegistry.set(cbId, callback);
                    ipc.send({ action: 'AddEvent', targetId: id, eventName, callbackId: cbId });
                };
            }

            let memType = memberCache.get(prop);

            if (!memType) {
                try {
                    const inspectRes = ipc.send({ action: 'Inspect', targetId: id, memberName: prop });
                    memType = inspectRes.memberType;
                    memberCache.set(prop, memType);
                } catch (e) {
                    memType = 'method';
                }
            }

            if (memType === 'property') {
                const res = ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: [] });
                return createProxy(res);
            } else {
                return (...args) => {
                    const netArgs = args.map((a) => (a && a.__ref) ? { __ref: a.__ref } : a);
                    const res = ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: netArgs });
                    return createProxy(res);
                };
            }
        },

        set: (target, prop, value) => {
            if (typeof prop !== 'string') return false;
            const netArg = (value && value.__ref) ? { __ref: value.__ref } : value;
            ipc.send({ action: 'Invoke', targetId: id, methodName: prop, args: [netArg] });
            memberCache.set(prop, 'property');
            return true;
        },

        construct: (target, args) => {
            const netArgs = args.map((a) => (a && a.__ref) ? { __ref: a.__ref } : a);
            const res = ipc.send({ action: 'New', typeId: id, args: netArgs });
            return createProxy(res);
        },

        apply: () => { throw new Error("Cannot call .NET object as a function. Need 'new'?"); }
    });

    gcRegistry.register(proxy, id);
    return proxy;
}

const node_ps51 = {
    load(typeName) {
        initialize();
        const res = ipc.send({ action: 'GetType', typeName });
        return createProxy(res);
    },

    release(id) {
        if (ipc) {
            try { ipc.send({ action: 'Release', targetId: id }); } catch {}
        }
    },

    createProxy,

    close() {
        if (proc) proc.kill();
        proc = null;
        ipc = null;
        initialized = false;
    },

    getAssembly(assemblyName) {
        return this.load(assemblyName);
    }
};

module.exports = node_ps51;
