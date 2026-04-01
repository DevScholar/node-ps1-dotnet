import * as fs from 'node:fs';
import type { ProtocolResponse, CommandRequest } from './types.js';

export class IpcSync {
    public fd: number = 0;
    private exited = false;
    private readBuf = Buffer.allocUnsafe(64 * 1024);
    private readBufLen = 0;
    private pipeName: string;

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
        fs.writeSync(this.fd, JSON.stringify(cmd) + '\n');
        return this.readResponse();
    }

    private readResponse(): ProtocolResponse {
        while (true) {
            const line = this.readLine();
            if (line === null) {
                this.exited = true;
                return { type: 'exit', message: '' } as any;
            }
            if (!line) continue;
            let msg: any;
            try { msg = JSON.parse(line); } catch { continue; }

            // If blocking marker, keep reading until actual result
            if (msg.type === '__blocking__') {
                while (true) {
                    const resultLine = this.readLine();
                    if (resultLine === null) {
                        this.exited = true;
                        return { type: 'exit', message: '' } as any;
                    }
                    if (!resultLine) continue;
                    try { msg = JSON.parse(resultLine); } catch { continue; }
                    if (msg.type !== '__blocking__') return msg as ProtocolResponse;
                }
            }

            // Sync callback: C# is blocked waiting for our reply before it can continue.
            // Call the JS handler synchronously, send the return value back, then keep
            // reading for the original response to the command we sent.
            if (msg.type === 'syncEvent') {
                let result: any = null;
                try { result = this.onEvent(msg); } catch {}
                try {
                    fs.writeSync(this.fd, JSON.stringify({ type: 'reply', result: result ?? null }) + '\n');
                } catch {}
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
