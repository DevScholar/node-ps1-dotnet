/**
 * P/Invoke decorator support for node-ps1-dotnet.
 *
 * Declare Win32 P/Invoke bindings in TypeScript using TC39 Stage 3 decorators.
 * No tsconfig flags required.
 *
 * Usage:
 *
 *   import { Struct, Field, DllImport, compilePInvoke } from '@devscholar/node-ps1-dotnet/pinvoke';
 *
 *   @Struct()
 *   class FLASHWINFO {
 *       @Field('uint')   cbSize  = 0;
 *       @Field('IntPtr') hwnd    = 0n;
 *       @Field('uint')   dwFlags = 0;
 *       @Field('uint')   uCount  = 0;
 *       @Field('uint')   dwTimeout = 0;
 *   }
 *
 *   class User32 {
 *       @DllImport('user32.dll', { setLastError: true, returns: 'bool', params: ['ref FLASHWINFO'] })
 *       static FlashWindowEx(pwfi: FLASHWINFO): boolean { return false; }
 *
 *       @DllImport('user32.dll', { setLastError: true, returns: 'int', params: ['IntPtr', 'int'] })
 *       static GetWindowLong(hWnd: bigint, nIndex: number): number { return 0; }
 *   }
 *
 *   compilePInvoke([FLASHWINFO, User32]);
 *
 * After compilePInvoke(), static methods are live P/Invoke calls.
 *
 * Notes on ref struct parameters:
 *   When a method takes `ref StructType`, the struct's fields are flattened into
 *   individual C# arguments in a generated wrapper. The JS caller passes a plain
 *   object with the struct's field names. Mutations made by the P/Invoke to the
 *   struct are NOT reflected back to the JS object (input-only semantics).
 */

import dotnet from './index.js';

// ── Metadata types ─────────────────────────────────────────────────────────────

interface StructMeta {
    csharpName: string;
    layout:     'Sequential' | 'Explicit' | 'Auto';
    charset?:   'Auto' | 'Unicode' | 'Ansi';
    fields:     Array<{ name: string; csType: string }>;
}

interface ParamMeta {
    name:   string;
    csType: string;
    byRef:  boolean;
}

interface MethodMeta {
    methodName:   string;
    dll:          string;
    entryPoint?:  string;
    charSet?:     'Auto' | 'Unicode' | 'Ansi';
    setLastError?: boolean;
    preserveSig?:  boolean;
    returnCsType: string;
    params:       ParamMeta[];
    targetClass:  any; // class constructor
}

// ── Storage ────────────────────────────────────────────────────────────────────

const _structMeta = new WeakMap<object, StructMeta>();
const _methodMeta = new WeakMap<object, MethodMeta[]>();
const _compiled   = new WeakSet<object>();

// Buffer for @Field decorators — drained by the @Struct class decorator.
// Safe: JS class evaluation is synchronous, so all @Field runs finish
// before @Struct runs on the same class.
const _pendingFields: Array<{ name: string; csType: string }> = [];

// ── @Struct ────────────────────────────────────────────────────────────────────

export interface StructOptions {
    /** StructLayout kind. Default: Sequential. */
    layout?:  'Sequential' | 'Explicit' | 'Auto';
    /** CharSet for the struct. Optional. */
    charset?: 'Auto' | 'Unicode' | 'Ansi';
    /** Override the C# struct name. Default: class name. */
    name?:    string;
}

export function Struct(options: StructOptions = {}) {
    return function(
        target:  abstract new (...args: any[]) => any,
        context: ClassDecoratorContext,
    ): void {
        const fields = _pendingFields.splice(0); // drain all pending @Field entries
        _structMeta.set(target, {
            csharpName: options.name ?? String(context.name ?? (target as any).name),
            layout:     options.layout  ?? 'Sequential',
            charset:    options.charset,
            fields,
        });
    };
}

// ── @Field ─────────────────────────────────────────────────────────────────────

/**
 * Declares an instance field of a @Struct class with its C# type.
 * Must be applied to every field that appears in the struct layout.
 *
 * @param csType C# type name, e.g. 'uint', 'IntPtr', 'bool', 'ushort'.
 */
export function Field(csType: string) {
    return function(_target: undefined, context: ClassFieldDecoratorContext): void {
        _pendingFields.push({ name: String(context.name), csType });
    };
}

// ── @DllImport ─────────────────────────────────────────────────────────────────

export interface DllImportOptions {
    /** Override the native entry-point name. */
    entryPoint?:   string;
    /** CharSet for string marshaling. */
    charSet?:      'Auto' | 'Unicode' | 'Ansi';
    /** Sets SetLastError = true on the DllImport attribute. */
    setLastError?: boolean;
    /** Sets PreserveSig = false on the DllImport attribute. */
    preserveSig?:  boolean;
    /** C# return type, e.g. 'int', 'bool', 'void', 'IntPtr'. */
    returns:       string;
    /**
     * C# parameter types in declaration order.
     * Prefix with 'ref ' for by-reference parameters (structs or primitives).
     * Example: ['IntPtr', 'int', 'ref FLASHWINFO']
     */
    params?:       string[];
}

/**
 * Declares a static method as a P/Invoke binding.
 * Must only be applied to static methods.
 * Call compilePInvoke() before the method is invoked.
 */
export function DllImport(dll: string, options: DllImportOptions) {
    return function<T extends (...args: any[]) => any>(
        _target:  T,
        context: ClassMethodDecoratorContext,
    ): T {
        if (!context.static) {
            throw new Error(`@DllImport can only decorate static methods (got '${String(context.name)}')`);
        }

        const methodName = String(context.name);
        const rawParams  = options.params ?? [];
        const params: ParamMeta[] = rawParams.map((p, i) => {
            const byRef  = p.startsWith('ref ') || p.startsWith('out ');
            const csType = byRef ? p.replace(/^(?:ref|out)\s+/, '') : p;
            return { name: `p${i}`, csType, byRef };
        });

        // addInitializer runs after the class is fully set up.
        // For static context, `this` is the class constructor.
        context.addInitializer(function(this: any) {
            const ctor    = this;
            const metas   = (_methodMeta.get(ctor) as MethodMeta[] | undefined) ?? [];
            metas.push({
                methodName,
                dll,
                entryPoint:   options.entryPoint,
                charSet:      options.charSet,
                setLastError: options.setLastError,
                preserveSig:  options.preserveSig,
                returnCsType: options.returns,
                params,
                targetClass:  ctor,
            });
            _methodMeta.set(ctor, metas);
        });

        // Replaced by compilePInvoke() after compilation.
        return function stub(..._args: any[]): any {
            throw new Error(
                `[node-ps1-dotnet] P/Invoke '${methodName}' not compiled — call compilePInvoke() first.`,
            );
        } as unknown as T;
    };
}

// ── C# code generation ─────────────────────────────────────────────────────────

function genStructCs(sm: StructMeta): string {
    const charAttr = sm.charset ? `, CharSet = CharSet.${sm.charset}` : '';
    return [
        `[StructLayout(LayoutKind.${sm.layout}${charAttr})]`,
        `internal struct ${sm.csharpName} {`,
        ...sm.fields.map(f => `    public ${f.csType} ${f.name};`),
        '}',
    ].join('\n');
}

function genMethodCs(m: MethodMeta, structMap: Map<string, StructMeta>): string {
    // Build DllImport attribute extras
    const extras: string[] = [];
    if (m.setLastError)          extras.push('SetLastError = true');
    // Always set EntryPoint: the extern is named _extern_X to avoid colliding
    // with the public wrapper, so DllImport must know the real entry-point name.
    extras.push(`EntryPoint = "${m.entryPoint || m.methodName}"`);
    if (m.charSet)               extras.push(`CharSet = CharSet.${m.charSet}`);
    if (m.preserveSig === false)  extras.push('PreserveSig = false');
    const extraStr = extras.length ? ', ' + extras.join(', ') : '';

    // Private extern declaration (direct P/Invoke signature)
    const externParams = m.params.map(p =>
        `${p.byRef ? 'ref ' : ''}${p.csType} ${p.name}`
    ).join(', ');

    // Public wrapper: ref-struct params are flattened into individual primitive args
    // to avoid the complexity of marshaling C# structs from JS objects.
    const wrapperParts: string[] = [];
    const callParts:    string[] = [];
    const structInits:  string[] = [];

    for (const p of m.params) {
        const sm = p.byRef ? structMap.get(p.csType) : undefined;
        if (sm) {
            // Flatten each struct field into a separate parameter
            for (const f of sm.fields) {
                wrapperParts.push(`${f.csType} _${p.name}_${f.name}`);
            }
            // Rebuild the struct locally before calling the extern
            structInits.push(`        ${p.csType} ${p.name};`);
            for (const f of sm.fields) {
                structInits.push(`        ${p.name}.${f.name} = _${p.name}_${f.name};`);
            }
            callParts.push(`ref ${p.name}`);
        } else {
            wrapperParts.push(`${p.byRef ? 'ref ' : ''}${p.csType} ${p.name}`);
            callParts.push(`${p.byRef ? 'ref ' : ''}${p.name}`);
        }
    }

    const retKw = m.returnCsType === 'void' ? '' : 'return ';
    return [
        `    [DllImport("${m.dll}"${extraStr})]`,
        `    private static extern ${m.returnCsType} _extern_${m.methodName}(${externParams});`,
        `    public static ${m.returnCsType} ${m.methodName}(${wrapperParts.join(', ')}) {`,
        ...structInits,
        `        ${retKw}_extern_${m.methodName}(${callParts.join(', ')});`,
        `    }`,
    ].join('\n');
}

// ── compilePInvoke ─────────────────────────────────────────────────────────────

let _counter = 0;

/**
 * Compiles all @Struct and @DllImport declarations in `targets` into a single
 * Add-Type call, then patches the static methods to route through the compiled
 * C# class.
 *
 * Call this once after all decorated classes are defined and before any P/Invoke
 * method is invoked. Subsequent calls with the same targets are no-ops.
 *
 * @param targets Array of @Struct-decorated and/or @DllImport-decorated classes.
 */
export function compilePInvoke(targets: (abstract new (...args: any[]) => any)[]): void {
    const toCompile = targets.filter(t => !_compiled.has(t));
    if (toCompile.length === 0) return;

    // Build struct-name → StructMeta map for cross-referencing ref params
    const structMap = new Map<string, StructMeta>();
    for (const t of toCompile) {
        const sm = _structMeta.get(t) as StructMeta | undefined;
        if (sm) structMap.set(sm.csharpName, sm);
    }

    const className = `__PInvoke${_counter++}__`;
    const lines: string[] = ['using System;', 'using System.Runtime.InteropServices;', ''];

    // Emit struct declarations
    for (const t of toCompile) {
        const sm = _structMeta.get(t) as StructMeta | undefined;
        if (sm) { lines.push(genStructCs(sm), ''); }
    }

    // Collect all P/Invoke methods across all targets
    const allMethods: MethodMeta[] = [];
    for (const t of toCompile) {
        const metas = _methodMeta.get(t) as MethodMeta[] | undefined;
        if (metas) allMethods.push(...metas);
    }

    if (allMethods.length > 0) {
        lines.push(`public static class ${className} {`);
        for (const m of allMethods) {
            lines.push(genMethodCs(m, structMap), '');
        }
        lines.push('}');
    }

    // Compile via Add-Type (C# 5.0 compatible — no auto-property initializers etc.)
    (dotnet as any).addType(lines.join('\n'));

    // Obtain a node-ps1-dotnet proxy for the compiled static class
    const cls = (dotnet as any)[className];

    // Patch each decorated static method with the live P/Invoke implementation
    for (const m of allMethods) {
        const hasStructRef = m.params.some(p => p.byRef && structMap.has(p.csType));

        if (hasStructRef) {
            // Unpack JS struct objects into flat args that match the C# wrapper signature
            m.targetClass[m.methodName] = (...jsArgs: any[]) => {
                const flat: any[] = [];
                let ai = 0;
                for (const p of m.params) {
                    const sm = p.byRef ? structMap.get(p.csType) : undefined;
                    if (sm) {
                        const obj = jsArgs[ai++] ?? {};
                        for (const f of sm.fields) flat.push(obj[f.name] ?? 0);
                    } else {
                        flat.push(jsArgs[ai++]);
                    }
                }
                return cls[m.methodName](...flat);
            };
        } else {
            // No struct flattening needed — direct passthrough
            m.targetClass[m.methodName] = (...args: any[]) => cls[m.methodName](...args);
        }
    }

    for (const t of toCompile) _compiled.add(t);
}
