// src/ipc.ts
import * as fs from 'node:fs';
import type { ProtocolResponse, CommandRequest } from './types.ts';

declare const Deno: any;

const MAX_LINE_LENGTH = 1024 * 1024 * 2; // 2MB buffer per line
const isDeno = typeof Deno !== 'undefined';
// Pre-allocate buffer to prevent memory thrashing on high-frequency IPC
const readResultBuffer = isDeno ? new Uint8Array(MAX_LINE_LENGTH) : Buffer.alloc(MAX_LINE_LENGTH);
const singleByteBuffer = isDeno ? new Uint8Array(1) : Buffer.alloc(1);

export function readLineSync(fd: number): string | null {
    let offset = 0;
    
    while (true) {
        try {
            const r = fs.readSync(fd, singleByteBuffer, 0, 1, null);
            if (r === 0) {
                if (offset === 0) return null;
                break;
            }
            if (singleByteBuffer[0] === 10) break; // \n character
            
            readResultBuffer[offset++] = singleByteBuffer[0];
            
            if (offset >= MAX_LINE_LENGTH) {
                throw new Error("IPC Pipe line length exceeded max limit.");
            }
        } catch (e) {
            return null;
        }
    }

    if (offset === 0) return '';
    
    if (isDeno) {
        return new TextDecoder().decode(readResultBuffer.subarray(0, offset));
    } else {
        return (readResultBuffer as Buffer).toString('utf8', 0, offset);
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

    close() {
        this.exited = true;
        if (this.fd) {
            try {
                fs.closeSync(this.fd);
            } catch {}
            this.fd = 0;
        }
    }
}