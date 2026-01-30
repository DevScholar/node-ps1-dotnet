// src/ipc.ts
import * as fs from 'node:fs';
import type { ProtocolResponse, CommandRequest } from './types.ts';

declare const Deno: any;

function readLineSync(fd: number): string | null {
    const chunks: Buffer[] = [];
    let totalLength = 0;
    const isDeno = typeof Deno !== 'undefined';
    
    if (isDeno) {
        const buf = new Uint8Array(1);
        const bytesData: number[] = [];
        while (true) {
            try {
                const r = fs.readSync(fd, buf, 0, 1, null);
                if (r === 0) {
                    if (bytesData.length === 0) return null;
                    break;
                }
                if (buf[0] === 10) break;
                bytesData.push(buf[0]);
            } catch (e) {
                return null;
            }
        }
        if (bytesData.length === 0) return '';
        return new TextDecoder().decode(new Uint8Array(bytesData));
    } else {
        const buf = Buffer.alloc(1);
        while (true) {
            try {
                const r = fs.readSync(fd, buf, 0, 1, null);
                if (r === 0) {
                    if (chunks.length === 0) return null;
                    break;
                }
                if (buf[0] === 10) break;
                const chunk = Buffer.alloc(1);
                buf.copy(chunk);
                chunks.push(chunk);
                totalLength += 1;
            } catch (e) {
                return null;
            }
        }
        if (chunks.length === 0) return '';
        const completeBuffer = Buffer.concat(chunks, totalLength);
        return completeBuffer.toString('utf8');
    }
}

export class IpcSync {
    public fd: number = 0;
    private exited: boolean = false;

    constructor(
        private pipeName: string,
        // Inject event handler to decouple business logic
        private onEvent: (msg: ProtocolResponse) => any 
    ) {}

    connect() {
        const pipePath = `\\\\.\\pipe\\${this.pipeName}`;
        const start = Date.now();
        while (true) {
            try {
                this.fd = fs.openSync(pipePath, 'r+');
                break;
            } catch (e: any) {
                if (Date.now() - start > 5000) throw new Error(`Timeout connecting pipe: ${pipePath}`);
                const s = Date.now() + 50;
                while (Date.now() < s);
            }
        }
    }

    send(cmd: CommandRequest): ProtocolResponse {
        if (this.exited) {
            return { type: 'exit', message: '' };
        }

        try {
            fs.writeSync(this.fd, JSON.stringify(cmd) + '\n');
        } catch (e) {
            throw new Error("Pipe closed (Write failed)");
        }

        while (true) {
            const line = readLineSync(this.fd);
            if (line === null) throw new Error("Pipe closed (Read EOF)");
            if (!line.trim()) continue;

            let res: ProtocolResponse;
            try {
                res = JSON.parse(line);
            } catch (e) {
                throw new Error(`Pipe closed (Invalid JSON): ${line}`);
            }

            // Process event from host
            if (res.type === 'event') {
                let result = null;
                try {
                    // Call injected handler
                    result = this.onEvent(res);
                } catch (e) {
                    console.error("Callback Error:", e);
                }
                
                const reply = { type: 'reply', result: result };
                try {
                    fs.writeSync(this.fd, JSON.stringify(reply) + '\n');
                } catch {}
                continue; // Continue waiting for actual command response
            }

            if (res.type === 'error') throw new Error(`Host Error: ${res.message}`);
            
            if (res.type === 'exit') {
                this.exited = true;
                return res;
            }
            
            return res;
        }
    }
}