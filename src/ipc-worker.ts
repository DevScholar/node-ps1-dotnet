// src/ipc-worker.ts
// Worker thread that owns the named-pipe connection and dispatches messages.
// Runs in a separate thread so the main thread is never blocked by pipe I/O.
import * as net from 'node:net';
import * as readline from 'node:readline';
import { parentPort } from 'node:worker_threads';
import type { MessagePort } from 'node:worker_threads';

let responsePort: MessagePort | null = null;
let socket: net.Socket | null = null;
let exited = false;

parentPort!.on('message', (msg: any) => {
    if (msg.type === 'init') {
        responsePort = msg.responsePort as MessagePort;
        connectWithRetry(msg.pipeName, Date.now());
    } else if (msg.type === 'cmd') {
        if (socket && !exited) {
            try {
                socket.write(JSON.stringify(msg.cmd) + '\n');
            } catch {
                responsePort!.postMessage({ type: 'response', data: { type: 'error', message: 'Pipe write failed' } });
            }
        }
    } else if (msg.type === 'resolving_reply') {
        if (socket && !exited) {
            try {
                socket.write(JSON.stringify({ type: 'reply', result: msg.result }) + '\n');
            } catch {}
        }
    }
});

function connectWithRetry(pipeName: string, startTime: number): void {
    const pipePath = `\\\\.\\pipe\\${pipeName}`;
    const s = net.createConnection(pipePath);

    s.once('connect', () => {
        socket = s;
        const rl = readline.createInterface({ input: s, crlfDelay: Infinity });

        rl.on('line', (line: string) => {
            if (!line.trim()) return;
            let msg: any;
            try {
                msg = JSON.parse(line);
            } catch {
                return;
            }

            if (msg.type === 'event') {
                if (msg.callbackId === '__resolving__') {
                    // Must be handled synchronously — route through response channel
                    // so main thread can process it inline inside send()
                    responsePort!.postMessage({ type: 'resolving', event: msg });
                } else {
                    // Fire-and-forget: dispatch to main event loop via parentPort
                    parentPort!.postMessage({ type: 'event', event: msg });
                }
            } else {
                // Command response
                responsePort!.postMessage({ type: 'response', data: msg });
            }
        });

        s.on('close', () => {
            if (!exited) {
                exited = true;
                responsePort!.postMessage({ type: 'response', data: { type: 'exit', message: '' } });
            }
        });

        responsePort!.postMessage({ type: 'connected' });
    });

    s.once('error', () => {
        s.destroy();
        if (Date.now() - startTime > 5000) {
            responsePort!.postMessage({ type: 'connect_error', message: `Timeout connecting to pipe: ${pipePath}` });
            return;
        }
        setTimeout(() => connectWithRetry(pipeName, startTime), 100);
    });
}
