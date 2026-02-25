import type { ProtocolResponse, CommandRequest } from './types.ts';
export declare class IpcSync {
    private pipeName;
    private onEvent;
    fd: number;
    private exited;
    private readBuffer;
    private resultBuffer;
    private bufferOffset;
    private bufferLength;
    constructor(pipeName: string, onEvent: (msg: ProtocolResponse) => any);
    private readLineSync;
    private tryConnect;
    connect(): void;
    send(cmd: CommandRequest): ProtocolResponse;
    close(): void;
}
