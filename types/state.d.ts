import { IpcSync } from './ipc.js';
import * as cp from 'node:child_process';
export declare function getIpc(): IpcSync;
export declare function getProc(): cp.ChildProcess;
export declare function getInitialized(): boolean;
export declare function getCachedRuntimeInfo(): {
    frameworkMoniker: string;
    runtimeVersion: string;
};
export declare function setIpc(val: IpcSync | null): void;
export declare function setProc(val: cp.ChildProcess | null): void;
export declare function setInitialized(val: boolean): void;
export declare function setCachedRuntimeInfo(val: {
    frameworkMoniker: string;
    runtimeVersion: string;
} | null): void;
