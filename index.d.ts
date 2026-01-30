declare module 'node-ps51' {
    interface DotnetProxy {
        (): void;
        new (...args: any[]): any;
        [key: string]: any;
        __ref?: string;
    }

    interface DotnetAPI {
        load(typeName: string): DotnetProxy;
        release(id: string): void;
        createProxy(meta: any): DotnetProxy;
        close(): void;
        getAssembly(assemblyName: string): DotnetProxy;
        readonly frameworkMoniker: string;
        readonly runtimeVersion: string;
        addListener(
            event: "resolving",
            listener: (assemblyName: string, assemblyVersion: string, resolve: (resolvedPath: string) => void) => void
        ): void;
        removeListener(
            event: "resolving",
            listener: (assemblyName: string, assemblyVersion: string) => void
        ): void;
        load(assemblyNameOrFilePath: string): void;
        require(dotnetAssemblyFilePath: string): any;
    }

    const dotnet: DotnetAPI;
    export default dotnet;
}

declare module 'node-ps51/src/index' {
    export * from 'node-ps51';
}

declare module 'node-ps51/src/dotnet' {
    export const Dotnet: {
        load(typeName: string): any;
        release(id: string): void;
        createProxy(meta: any): any;
        close(): void;
        getAssembly(assemblyName: string): any;
        readonly frameworkMoniker: string;
        readonly runtimeVersion: string;
        addListener(
            event: "resolving",
            listener: (assemblyName: string, assemblyVersion: string, resolve: (resolvedPath: string) => void) => void
        ): void;
        removeListener(
            event: "resolving",
            listener: (assemblyName: string, assemblyVersion: string) => void
        ): void;
        load(assemblyNameOrFilePath: string): void;
        require(dotnetAssemblyFilePath: string): any;
    };
    export default Dotnet;
}
