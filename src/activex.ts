/**
 * COM / ActiveX object support for node-ps1-dotnet.
 *
 * Creates COM objects by ProgID, mimicking the WSH/IE `ActiveXObject` constructor.
 * This is an alternative to the `winax` npm package for environments where
 * node-gyp is unavailable.
 *
 * Usage:
 *
 *   import { ActiveXObject } from '@devscholar/node-ps1-dotnet/activex';
 *   const shell = new ActiveXObject('WScript.Shell');
 *   shell.Run('notepad.exe');
 *
 * @module
 */

import { ActiveXObject as _ActiveXObject } from './index.js';
import { getIpc } from './state.js';
import { createProxy } from './proxy.js';

/**
 * Creates a COM object by ProgID, mimicking the WSH/IE `ActiveXObject` constructor.
 * Works as both a regular function call and a `new` expression.
 */
export const ActiveXObject: {
    (progId: string): any;
    new (progId: string): any;
} = _ActiveXObject as any;

/**
 * Materializes a COM collection into a JS array, enabling iteration.
 * Use this when a COM object exposes `_NewEnum` / `IEnumVARIANT` for enumeration.
 *
 * @example
 * import { ActiveXObject, Enumerator } from '@devscholar/node-ps1-dotnet/activex';
 * const fso = new ActiveXObject('Scripting.FileSystemObject');
 * const files = fso.GetFolder('.').Files;
 * for (const file of Enumerator(files)) {
 *     console.log(file.Name);
 * }
 */
export function Enumerator(collection: any): any[] {
    const ipc = getIpc();
    if (!ipc) throw new Error('IPC not initialized');
    const targetId = collection?.__ref;
    if (!targetId) throw new Error('Enumerator: expected a .NET/COM object proxy');
    const res = ipc.send({ action: 'MaterializeEnum', targetId } as any) as any;
    if (res.type === 'array') return (res.value as any[]).map((item: any) => createProxy(item));
    return [];
}
