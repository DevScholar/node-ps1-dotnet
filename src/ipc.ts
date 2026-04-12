import * as fs from 'node:fs';
import type { ProtocolResponse, CommandRequest } from './types.js';

export class IpcSync {
    public fd: number = 0;
    private exited = false;
    private readBuf = Buffer.allocUnsafe(64 * 1024);
    private readBufLen = 0;
    private pipeName: string;
    // Buffer for out-of-order responses: _reqId → response message.
    // When readResponseForId("n-5") reads a response tagged "n-3" (because C# executed
    // n-3 before n-5 while the WPF dispatcher fired a nested sync event), the response
    // is parked here so readResponseForId("n-3") can pick it up on the next iteration.
    private responseBuffer = new Map<string, any>();
    // Monotonic counter for outgoing command IDs.
    private nextId = 0;

    constructor(pipeName: string, private onEvent: (msg: ProtocolResponse) => any) {
        this.pipeName = pipeName;
    }

    connect(): void {
        const pipePath = `\\\\.\\pipe\\${this.pipeName}`;
        const deadline = Date.now() + 15000;
        const sa = new Int32Array(new SharedArrayBuffer(4));
        while (true) {
            try {
                this.fd = fs.openSync(pipePath, 'r+');
                return;
            } catch {
                if (Date.now() > deadline)
                    throw new Error(`Timeout connecting to named pipe: ${pipePath}`);
                Atomics.wait(sa, 0, 0, 100);
            }
        }
    }

    send(cmd: CommandRequest): ProtocolResponse {
        if (this.exited) return { type: 'exit', message: '' } as any;
        const id = `n-${this.nextId++}`;
        (cmd as any)._reqId = id;
        fs.writeSync(this.fd, JSON.stringify(cmd) + '\n');
        return this.readResponseForId(id);
    }

    private readResponseForId(expectedId: string): ProtocolResponse {
        while (true) {
            // Fast path: a nested call already received and buffered our response.
            if (this.responseBuffer.has(expectedId)) {
                const resp = this.responseBuffer.get(expectedId)!;
                this.responseBuffer.delete(expectedId);
                return resp as ProtocolResponse;
            }

            const line = this.readLine();
            if (line === null) {
                this.exited = true;
                return { type: 'exit', message: '' } as any;
            }
            if (!line) continue;
            let msg: any;
            try { msg = JSON.parse(line); } catch { continue; }

            // Skip __blocking__ markers (legacy, no longer sent by C#)
            if (msg.type === '__blocking__') continue;

            // Sync callback: C# is blocked waiting for our reply before it can continue.
            // Nested IPC calls inside the handler may buffer our expected response while
            // reading their own responses — we pick it up on the next loop iteration.
            if (msg.type === 'syncEvent') {
                let result: any = null;
                try { result = this.onEvent(msg); } catch {}
                try {
                    const reply: any = { type: 'reply', result: result ?? null };
                    if (msg._reqId) reply._reqId = msg._reqId;
                    fs.writeSync(this.fd, JSON.stringify(reply) + '\n');
                } catch {}
                continue; // Loop back — responseBuffer may now hold our expected response
            }

            // If this response belongs to a different pending call (out-of-order due to
            // WPF Dispatcher pumping nested commands while we're still waiting for ours),
            // park it in responseBuffer so the right readResponseForId() caller finds it.
            const rid = msg._reqId as string | undefined;
            if (rid && rid !== expectedId) {
                this.responseBuffer.set(rid, msg);
                continue;
            }

            return msg as ProtocolResponse;
        }
    }

    private readLine(): string | null {
        const chunk = Buffer.allocUnsafe(4096);
        while (true) {
            // Check if newline already in buffer
            for (let i = 0; i < this.readBufLen; i++) {
                if (this.readBuf[i] === 0x0a) {
                    const end = (i > 0 && this.readBuf[i - 1] === 0x0d) ? i - 1 : i;
                    const line = this.readBuf.slice(0, end).toString('utf8');
                    this.readBuf.copy(this.readBuf, 0, i + 1, this.readBufLen);
                    this.readBufLen -= i + 1;
                    return line;
                }
            }
            // Read more data
            let n: number;
            try { n = fs.readSync(this.fd, chunk, 0, chunk.length, null); }
            catch (e) {
                return null;
            }
            if (n === 0) {
                return null;
            }
            if (this.readBufLen + n > this.readBuf.length) {
                const bigger = Buffer.allocUnsafe(Math.max(this.readBuf.length * 2, this.readBufLen + n));
                this.readBuf.copy(bigger);
                this.readBuf = bigger;
            }
            chunk.copy(this.readBuf, this.readBufLen, 0, n);
            this.readBufLen += n;
        }
    }

    close(): void {
        this.exited = true;
        try { fs.closeSync(this.fd); } catch {}
    }
}
