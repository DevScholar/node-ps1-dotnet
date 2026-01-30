// src/types.ts
export interface ProtocolResponse {
    type: string;
    value?: any;
    id?: string;
    message?: string;
    args?: any[];
    callbackId?: string;
    memberType?: 'property' | 'method';
    assemblyName?: string;
    assemblyVersion?: string;
    frameworkMoniker?: string;
    runtimeVersion?: string;
    resolvedPath?: string;
    props?: Record<string, any>;
}

export interface CommandRequest {
    action: string;
    [key: string]: any;
}
