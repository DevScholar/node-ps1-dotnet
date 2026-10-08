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

import { ActiveXObject as _ActiveXObject, GetObject as _GetObject } from './index.js';
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
 * JScript-compatible Enumerator for COM collections that expose `_NewEnum` /
 * `IEnumVARIANT`. Mirrors the Microsoft JScript host object of the same name, so
 * legacy JScript cursor patterns keep working unchanged:
 *
 *   var e = new Enumerator(coll);
 *   e.moveFirst();
 *   while (!e.atEnd()) {
 *       WScript.Echo(e.item());
 *       e.moveNext();
 *   }
 *
 * The instance is also ES6-iterable (`Symbol.iterator`), so `for...of` and spread
 * work on it without giving up the classic cursor API.
 *
 * @example
 * import { ActiveXObject, Enumerator } from '@devscholar/node-ps1-dotnet/activex';
 * const fso = new ActiveXObject('Scripting.FileSystemObject');
 * const files = fso.GetFolder('.').Files;
 * for (const file of new Enumerator(files)) {
 *     console.log(file.Name);
 * }
 */
export class Enumerator implements Iterable<any> {
    private readonly _items: any[];
    private _pos: number;

    constructor(collection: any) {
        const ipc = getIpc();
        if (!ipc) throw new Error('IPC not initialized');
        const targetId = collection?.__ref;
        if (!targetId) throw new Error('Enumerator: expected a .NET/COM object proxy');
        const res = ipc.send({ action: 'MaterializeEnum', targetId } as any) as any;
        this._items =
            res.type === 'array' ? (res.value as any[]).map((item: any) => createProxy(item)) : [];
        // A fresh JScript Enumerator points before the first item, so atEnd() is true
        // until moveFirst() (or moveNext()) lands on a real item.
        this._pos = -1;
    }

    /** True when positioned before the first item or after the last item. */
    atEnd(): boolean {
        return this._pos < 0 || this._pos >= this._items.length;
    }

    /** Returns the current item; throws when positioned at the beginning or end. */
    item(): any {
        if (this.atEnd()) {
            throw new Error('Enumerator is positioned before the first item or after the last item');
        }
        return this._items[this._pos];
    }

    /** Moves to the first item (past-the-end position if the collection is empty). */
    moveFirst(): void {
        this._pos = 0;
    }

    /** Advances to the next item, or to the past-the-end position. */
    moveNext(): void {
        if (this._pos < this._items.length) {
            this._pos++;
        }
    }

    /** ES6 iteration over the materialized items, independent of the cursor. */
    [Symbol.iterator](): Iterator<any> {
        let i = 0;
        const items = this._items;
        return {
            next(): IteratorResult<any> {
                if (i < items.length) {
                    return { value: items[i++], done: false };
                }
                return { value: undefined, done: true };
            },
        };
    }
}

/**
 * Gets a reference to a COM object from a running instance or a file/moniker path,
 * mimicking VBScript's `GetObject`.
 *
 * - `GetObject(undefined, "Excel.Application")` — get running instance (Marshal.GetActiveObject)
 * - `GetObject("winmgmts:")` — bind to WMI moniker string
 * - `GetObject("C:\\file.xls")` — bind to file moniker
 */
export const GetObject: (pathname?: string, cls?: string) => any = _GetObject;
