import { Worker } from 'node:worker_threads';
import type { ProtocolResponse, CommandRequest } from './types.js';

// ── Reader worker + ring buffer ──────────────────────────────────────────────
//
// Why: the named-pipe read side was previously driven only by the 8 ms Poll
// timer (setInterval in index.ts).  A synchronous request (nww:// XHR →
// WebResourceRequested → FireSyncEventAndWait) blocks C# waiting for Node's
// reply, but Node sits idle until the next Poll tick, adding ~4–8 ms of pure
// latency per call.  C# already has a dedicated blocking reader thread; this
// gives Node the equivalent.
//
// Design:
//   - A worker thread owns the single named-pipe connection (C# accepts only
//     one instance).  It uses net.Socket (async, event-driven) so its event
//     loop is never blocked and it can always receive write commands from the
//     main thread.  This matters: fs.readSync would block the worker's loop
//     and starve the 'cmd' postMessage path (deadlock).
//   - Incoming lines are parsed and pushed into a SharedArrayBuffer ring.
//   - The main thread is the single consumer.  Its two consumption sites are
//     mutually exclusive, so no lock is needed:
//       · send() blocks in Atomics.wait while draining the ring — freezing the
//         event loop — so the 'wake' handler cannot run concurrently.
//       · when idle, the 'wake' message drains the ring non-blockingly.
//
// SharedArrayBuffer layout (byte offsets):
//   [0..7]   BigInt64 written : total bytes written by worker (monotonic)
//   [8..15]  BigInt64 read    : total bytes consumed by main (monotonic)
//   [16..19] Int32   state    : 0 connecting, 1 connected, 2 error, 3 closed
//   [24..]   Uint8   data     : ring of records
//
// Record format: [len:uint32 LE][payload:len].  len == 0 is a wrap sentinel.

const STATE_OFF = 16;
const DATA_OFF = 24;
const DATA_SIZE = 4 * 1024 * 1024; // 4 MiB data region
const DATA_BIG = BigInt(DATA_SIZE);

const WORKER_CODE = `
'use strict';
const { workerData, parentPort } = require('worker_threads');
const net = require('net');
const readline = require('readline');

const sab = workerData.sab;
const ctrl = new BigInt64Array(sab, 0, 2);
const state = new Int32Array(sab, 16, 1);
const data = new Uint8Array(sab, 24);
const dv = new DataView(sab);
const DATA_SIZE = data.byteLength;
const DATA_BIG = BigInt(DATA_SIZE);

let socket = null;
let exited = false;

function pushLine(line) {
    const bytes = Buffer.from(line, 'utf8');
    const len = bytes.length;
    const total = 4 + len;
    for (;;) {
        const written = Atomics.load(ctrl, 0);
        const read = Atomics.load(ctrl, 1);
        if (Number(written - read) + total <= DATA_SIZE) break;
        Atomics.wait(ctrl, 1, read);
    }
    let written = Atomics.load(ctrl, 0);
    let pos = Number(written % DATA_BIG);
    if (pos + total > DATA_SIZE) {
        dv.setUint32(24 + pos, 0, true); // wrap sentinel
        written += BigInt(DATA_SIZE - pos);
        Atomics.store(ctrl, 0, written);
        pos = 0;
    }
    dv.setUint32(24 + pos, len, true);
    for (let i = 0; i < len; i++) data[pos + 4 + i] = bytes[i];
    Atomics.store(ctrl, 0, written + BigInt(total));
    Atomics.notify(ctrl, 0, 1); // wake a main thread blocked on "written"
    Atomics.notify(ctrl, 1, 1); // wake a main thread blocked on "read"
    parentPort.postMessage('wake'); // wake an idle event loop
}

function connect(pipeName, deadline) {
    const s = net.createConnection('\\\\\\\\.\\\\pipe\\\\' + pipeName);
    s.once('connect', () => {
        socket = s;
        const rl = readline.createInterface({ input: s, crlfDelay: Infinity });
        rl.on('line', (line) => { if (line.trim()) pushLine(line); });
        s.on('close', () => {
            if (!exited) {
                exited = true;
                Atomics.store(state, 0, 3);
                Atomics.notify(ctrl, 0, 1);
                Atomics.notify(state, 0, 1);
            }
        });
        Atomics.store(state, 0, 1); // connected
        Atomics.notify(state, 0, 1);
    });
    s.once('error', () => {
        s.destroy();
        if (Date.now() > deadline) {
            Atomics.store(state, 0, 2); // connect failed
            Atomics.notify(state, 0, 1);
            return;
        }
        setTimeout(() => connect(pipeName, deadline), 100);
    });
}

connect(workerData.pipeName, Date.now() + 15000);

parentPort.on('message', (m) => {
    if (m.type === 'cmd') {
        if (socket && !exited) {
            try { socket.write(m.cmd); } catch {}
        }
    }
});
`;

export class IpcSync {
    public fd: number = 0; // kept for interface compatibility; unused in worker mode
    private exited = false;
    private pipeName: string;
    // Buffer for out-of-order responses: _reqId → response message.
    private responseBuffer = new Map<string, any>();
    // Monotonic counter for outgoing command IDs.
    private nextId = 0;

    private sab: SharedArrayBuffer;
    private ctrl: BigInt64Array;
    private state: Int32Array;
    private data: Uint8Array;
    private dv: DataView;
    private worker: Worker | null = null;

    constructor(pipeName: string, private onEvent: (msg: ProtocolResponse) => any) {
        this.pipeName = pipeName;
        this.sab = new SharedArrayBuffer(DATA_OFF + DATA_SIZE);
        this.ctrl = new BigInt64Array(this.sab, 0, 2);
        this.state = new Int32Array(this.sab, STATE_OFF, 1);
        this.data = new Uint8Array(this.sab, DATA_OFF);
        this.dv = new DataView(this.sab);
    }

    connect(): void {
        this.worker = new Worker(WORKER_CODE, {
            eval: true,
            workerData: { sab: this.sab, pipeName: this.pipeName },
        });
        // Do not keep the parent process alive solely because of the reader
        // worker — otherwise beforeExit never fires and the process hangs after
        // the user's script finishes (e.g. the await-delay example).
        this.worker.unref();
        this.worker.on('message', (m: unknown) => {
            if (m === 'wake') this.drainIdle();
        });
        this.worker.on('error', () => { /* handled via state flag */ });

        // Block until the worker reports connected (or failed).
        Atomics.wait(this.state, 0, 0, 15000);
        if (Atomics.load(this.state, 0) === 2) {
            throw new Error(`Timeout connecting to named pipe: \\\\.\\pipe\\${this.pipeName}`);
        }
    }

    send(cmd: CommandRequest): ProtocolResponse {
        if (this.exited) return { type: 'exit', message: '' } as any;
        const id = `n-${this.nextId++}`;
        (cmd as any)._reqId = id;
        this.writeLine(JSON.stringify(cmd));
        return this.readResponseForId(id);
    }

    private writeLine(line: string): void {
        this.worker?.postMessage({ type: 'cmd', cmd: line + '\n' });
    }

    private readResponseForId(expectedId: string): ProtocolResponse {
        while (true) {
            // Fast path: a nested call already received and buffered our response.
            if (this.responseBuffer.has(expectedId)) {
                const resp = this.responseBuffer.get(expectedId)!;
                this.responseBuffer.delete(expectedId);
                return resp as ProtocolResponse;
            }

            const msg = this.readMessage();
            if (msg === null) {
                this.exited = true;
                return { type: 'exit', message: '' } as any;
            }

            // Sync callback: C# is blocked waiting for our reply before it can continue.
            if (msg.type === 'syncEvent') {
                this.handleSyncEvent(msg);
                continue; // Loop back — responseBuffer may now hold our expected response
            }

            // Out-of-order response (WPF dispatcher pumped a nested command): park it.
            const rid = msg._reqId as string | undefined;
            if (rid && rid !== expectedId) {
                this.responseBuffer.set(rid, msg);
                continue;
            }

            return msg as ProtocolResponse;
        }
    }

    /** Run a sync-event callback and write its reply back to C#. */
    private handleSyncEvent(msg: any): void {
        let result: any = null;
        try { result = this.onEvent(msg); } catch {}
        try {
            const reply: any = { type: 'reply', result: result ?? null };
            if (msg._reqId) reply._reqId = msg._reqId;
            this.writeLine(JSON.stringify(reply));
        } catch {}
    }

    /**
     * Drain the ring buffer non-blockingly (called from the worker's 'wake'
     * message when the main thread is idle). Only syncEvents arrive outside a
     * send(); command responses are consumed by readResponseForId.
     */
    private drainIdle(): void {
        while (Atomics.load(this.ctrl, 0) !== Atomics.load(this.ctrl, 1)) {
            const msg = this.readMessage();
            if (msg === null) break;
            if (msg.type === 'syncEvent') this.handleSyncEvent(msg);
        }
    }

    /** Read one complete record from the ring buffer, blocking if empty. */
    private readMessage(): any {
        while (true) {
            const written = Atomics.load(this.ctrl, 0);
            const read = Atomics.load(this.ctrl, 1);
            if (written === read) {
                if (Atomics.load(this.state, 0) === 3) return null; // worker closed
                const r = Atomics.wait(this.ctrl, 0, written, 60000);
                if (r === 'timed-out') return null;
                continue;
            }

            const pos = Number(read % DATA_BIG);
            const len = this.dv.getUint32(DATA_OFF + pos, true);

            if (len === 0) {
                // Wrap sentinel: skip to the head of the ring.
                Atomics.store(this.ctrl, 1, read + DATA_BIG - BigInt(pos));
                Atomics.notify(this.ctrl, 1, 1);
                continue;
            }

            const bytes = this.data.slice(pos + 4, pos + 4 + len);
            Atomics.store(this.ctrl, 1, read + BigInt(4 + len));
            Atomics.notify(this.ctrl, 1, 1);

            const line = Buffer.from(bytes).toString('utf8');
            if (!line) continue;
            let msg: any;
            try { msg = JSON.parse(line); } catch { continue; }
            if (msg.type === '__blocking__') continue; // legacy marker
            return msg;
        }
    }

    close(): void {
        this.exited = true;
        if (this.worker) {
            this.worker.terminate().catch(() => {});
            this.worker = null;
        }
    }
}
