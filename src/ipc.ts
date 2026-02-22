// src/ipc.ts
import * as fs from 'node:fs';
import type { ProtocolResponse, CommandRequest } from './types.ts';

declare const Deno: any;

const MAX_LINE_LENGTH = 1024 * 1024 * 2; // 2MB buffer per line
const CHUNK_SIZE = 16 * 1024; // 16KB chunk size for buffering
const isDeno = typeof Deno !== 'undefined';

const readBuffer = isDeno ? new Uint8Array(CHUNK_SIZE) : Buffer.alloc(CHUNK_SIZE);
const resultBuffer = isDeno ? new Uint8Array(MAX_LINE_LENGTH) : Buffer.alloc(MAX_LINE_LENGTH);
let bufferOffset = 0;
let bufferLength = 0;

export function readLineSync(fd: number): string | null {
    let resultOffset = 0;
    
    while (true) {
        if (bufferOffset >= bufferLength) {
            try {
                const bytesRead = fs.readSync(fd, readBuffer, 0, CHUNK_SIZE, null);
                if (bytesRead === 0) {
                    if (resultOffset === 0) return null;
                    break;
                }
                bufferOffset = 0;
                bufferLength = bytesRead;
            } catch (e) {
                return null;
            }
        }
        
        let lineEnd = -1;
        for (let i = bufferOffset; i < bufferLength; i++) {
            if (readBuffer[i] === 10) {
                lineEnd = i;
                break;
            }
        }
        
        if (lineEnd !== -1) {
            const lineLength = lineEnd - bufferOffset;
            if (resultOffset + lineLength > MAX_LINE_LENGTH) {
                throw new Error("IPC Pipe line length exceeded max limit.");
            }
            if (isDeno) {
                resultBuffer.set(readBuffer.subarray(bufferOffset, lineEnd), resultOffset);
            } else {
                (resultBuffer as Buffer).copy(readBuffer, resultOffset, bufferOffset, lineEnd);
            }
            resultOffset += lineLength;
            bufferOffset = lineEnd + 1;
            break;
        }
        
        const availableLength = bufferLength - bufferOffset;
        if (resultOffset + availableLength > MAX_LINE_LENGTH) {
            throw new Error("IPC Pipe line length exceeded max limit.");
        }
        if (isDeno) {
            resultBuffer.set(readBuffer.subarray(bufferOffset, bufferLength), resultOffset);
        } else {
            (resultBuffer as Buffer).copy(readBuffer, resultOffset, bufferOffset, bufferLength);
        }
        resultOffset += availableLength;
        bufferOffset = bufferLength;
    }

    if (resultOffset === 0) return '';
    
    if (isDeno) {
        return new TextDecoder().decode(resultBuffer.subarray(0, resultOffset));
    } else {
        return (resultBuffer as Buffer).toString('utf8', 0, resultOffset);
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