# node-ps1-dotnet and node-api-dotnet Compatibility Notes

This document describes the API compatibility between `node-ps1-dotnet` and Microsoft's official [`node-api-dotnet`](https://github.com/microsoft/node-api-dotnet).

---

## Design Goals

`node-ps1-dotnet` aims to maintain the same public API style as `node-api-dotnet`, enabling low-cost code migration between the two. The fundamental difference lies in their implementation mechanisms:

| Feature | node-api-dotnet | node-ps1-dotnet |
|---------|-----------------|-----------------|
| Implementation | Node.js N-API native addon (.NET in-process) | Out-of-process (PowerShell child process + Named Pipe IPC) |
| Platform Support | Windows / macOS / Linux | **Windows only** (depends on WinForms/WPF) |
| Runtime Requirements | .NET 6+ or .NET Framework 4.7.2+ | .NET Framework 4.x (via PowerShell 5.1) |
| Performance | High (direct in-process calls) | Medium (IPC round-trip overhead per call) |
| Event Support | Not yet implemented | **Implemented** |
| No Pre-compilation Required | Requires `.nupkg` or `.NET SDK` | **Not required** (`Add-Type` runtime compilation) |

---

## Type Conversion Reference

The table below shows how each .NET type is marshalled to JavaScript.

| .NET type | JS value | Notes |
|-----------|----------|-------|
| `string` | `string` | |
| `bool` | `boolean` | |
| `byte` `short` `int` `long` `float` `double` | `number` | NaN / Infinity → `null` |
| `null` | `null` | |
| `Enum` | `number` | integer value |
| `Guid` | `string` | UUID format |
| `DateTime` / `DateTimeOffset` | `Date` | UTC ISO 8601 |
| `TimeSpan` | `number` | total milliseconds |
| `BigInteger` | `bigint` | via `BigInt(decimalString)` |
| `Tuple<A,B>` / `ValueTuple<A,B>` | `[A, B]` (array) | up to 8 elements |
| `T[]` | `T[]` | value copy |
| `IList<T>` / `List<T>` | `T[]` copy **or** ref proxy | see [Type Conversion Modes](#type-conversion-modes) |
| `IEnumerable<T>` (not IList) | ref proxy + `Symbol.iterator` | lazy iterable; see below |
| `IDictionary<K,V>` | `Map<K,V>` copy **or** ref proxy | see [Type Conversion Modes](#type-conversion-modes) |
| `System.IO.Stream` | `stream.Duplex` | see [Stream Support](#stream-support) |
| `Task<T>` | `Promise<T>` | via `await` / `dotnet.awaitTask()` |
| ref/out params | `{ result, paramA, paramB }` | node-api-dotnet style |
| class / interface instance | ref proxy | live .NET reference |
| struct | ref proxy | boxed .NET reference |

---

## Compatible APIs

### Loading Assemblies

```typescript
// node-api-dotnet
import dotnet from 'node-api-dotnet';
dotnet.load('System.Windows.Forms');          // Load by name
dotnet.load('./MyLib.dll');                   // Load by path

// node-ps1-dotnet — identical
import dotnet from 'node-ps1-dotnet';
dotnet.load('System.Windows.Forms');
dotnet.load('./MyLib.dll');
```

`load(nameOrPath)` automatically distinguishes between assembly names (without path separators and `.dll`/`.exe` extensions) and file paths.

### Accessing Types

```typescript
// Both are the same: access types through namespace tree
const Button = dotnet.System.Windows.Forms.Button;
const form   = dotnet.System.Windows.Forms.Form;
```

### Constructing Objects

```typescript
// Both are the same: use the new keyword
const btn = new dotnet.System.Windows.Forms.Button();
btn.Text = 'Click me';
```

### Calling Methods and Reading/Writing Properties

```typescript
// Both are the same
form.Controls.Add(btn);          // Call method
btn.Width = 200;                  // Set property
const text = btn.Text;            // Read property
```

### Runtime Information

```typescript
console.log(dotnet.frameworkMoniker);  // e.g. 'net472'
console.log(dotnet.runtimeVersion);   // e.g. '4.0.30319.42000'
```

### Dependency Resolution Hooks

```typescript
dotnet.addListener('resolving', (name: string) => {
    const dllName = name.split(',')[0];
    const p = path.join('./libs', dllName + '.dll');
    return fs.existsSync(p) ? p : null;
});
```

> **Note**: The current implementation only supports one `resolving` listener; later registrations override previous ones.

---

## APIs Unique to This Project (Not in node-api-dotnet)

### Event Subscription

```typescript
btn.add_Click((sender: any, e: any) => {
    console.log('Button clicked');
});

// Return value is forwarded back to .NET — works for all events
form.add_Closing((s, e) => { e.Cancel = true; });

// Unsubscribe
btn.remove_Click(handler);
```

All `add_*` handlers are synchronous: the .NET event handler thread blocks until the JS callback returns. The return value is forwarded back to .NET, so patterns like `e.Cancel = true` or returning a response object work without any special API variant. Nested IPC calls inside the callback are also supported.

### WPF/WinForms Application Loop

```typescript
dotnet.startApplication(app, window);   // Start WPF message loop (non-blocking)
```

---

## Features in node-api-dotnet but Not Implemented in This Project

| Feature | Description |
|---------|-------------|
| Multiple `resolving` listeners | node-api-dotnet supports chained listeners; this project supports one |
| `import` static types (TypeScript) | node-api-dotnet generates `.d.ts` via `.nupkg`; this project uses dynamic `Proxy` |
| macOS / Linux | node-api-dotnet is cross-platform; this project is Windows only |
| `Task<T>` native async/await | Wrapped as `Promise` via poll-based IPC; same semantics, additional round-trip overhead |

---

## Type Conversion Modes

`node-ps1-dotnet` supports two collection marshalling strategies, switchable at runtime:

```typescript
dotnet.typeConversionBehavior = 'node-api-dotnet'; // default
dotnet.typeConversionBehavior = 'pythonnet';
```

The setting takes effect immediately for all subsequent IPC calls.

### `'node-api-dotnet'` (default)

| .NET type | JS value | Mutable from JS? |
|-----------|----------|-----------------|
| `IList<T>` / `List<T>` | `T[]` (value copy) | No — mutations are lost |
| `IDictionary<K,V>` | `Map<K,V>` (value copy) | No — mutations are lost |
| `IEnumerable<T>` (not IList) | ref proxy + `Symbol.iterator` | Read-only iteration |

This is the default because it gives the most natural JS experience for read-only data.

### `'pythonnet'` mode

Inspired by [pythonnet](https://pythonnet.github.io/pythonnet/python.html)'s rule that reference types stay as references:

| .NET type | JS value | Mutable from JS? |
|-----------|----------|-----------------|
| `IList<T>` / `List<T>` | ref proxy + JS array interface | **Yes** — `Add`, `Remove`, `Clear` apply to the original object |
| `IDictionary<K,V>` | ref proxy + JS Map interface | **Yes** — `set`, `delete`, `clear` apply to the original object |
| `IEnumerable<T>` (not IList) | ref proxy + `Symbol.iterator` | Read-only (same as default mode) |

#### IList proxy: JS array interface

```typescript
dotnet.typeConversionBehavior = 'pythonnet';

const origins = nwwReg.AllowedOrigins; // ref proxy, not a copy

// ── Mutation (applied to the real .NET IList<string>) ──
origins.Add('*');           // ✅
origins.Remove('old');      // ✅
origins.Clear();            // ✅

// ── JS array read interface (lowercase — never clashes with .NET PascalCase) ──
origins.length;                      // ✅  → Count
for (const o of origins) { ... }     // ✅  → Symbol.iterator
[...origins];                        // ✅  → spread
origins.map(o => o.toUpperCase());   // ✅
origins.filter(o => o !== '*');      // ✅
origins.find(o => o === '*');        // ✅
origins.includes('*');               // ✅
origins.indexOf('*');                // ✅
origins.at(-1);                      // ✅  → last element
origins.slice(0, 2);                 // ✅

// ── .NET PascalCase members still work ──
origins.Contains('*');    // ✅
origins.Count;            // ✅
```

#### IDictionary proxy: JS Map interface

```typescript
dotnet.typeConversionBehavior = 'pythonnet';

const headers = response.Headers; // ref proxy to Dictionary<string,string>

// ── Mutation ──
headers.set('X-Custom', 'value');   // ✅  → set_Item
headers.delete('X-Unused');         // ✅  → Remove
headers.clear();                    // ✅  → Clear

// ── Read (JS Map interface) ──
headers.get('Content-Type');        // ✅  → get_Item
headers.has('Authorization');       // ✅  → ContainsKey
headers.size;                       // ✅  → Count
for (const [k, v] of headers) { }  // ✅  → Symbol.iterator (materialises snapshot)
headers.keys();                     // ✅
headers.values();                   // ✅
headers.entries();                  // ✅
headers.forEach((v, k) => { });     // ✅

// ── .NET PascalCase members still work ──
headers.Count;                  // ✅
headers.ContainsKey('Accept');  // ✅
```

#### IEnumerable\<T\> proxy (both modes)

Types that implement `IEnumerable<T>` but not `IList<T>` (e.g. `HashSet<T>`, `Queue<T>`, LINQ results) are always returned as ref proxies with a JS iterable interface:

```typescript
const set = obj.UniqueNames; // HashSet<string> → ref proxy

for (const name of set) { ... }        // ✅  → Symbol.iterator
[...set];                              // ✅
set.map(n => n.toUpperCase());         // ✅  (materialises then maps)
set.filter(n => n.length > 3);        // ✅
set.includes('Alice');                 // ✅
```

> **Why no name clash?** .NET standard members use `PascalCase` (`Add`, `Count`, `Contains`, …) while the JS interfaces use `camelCase` (`map`, `filter`, `length`, `get`, `set`, …) — these two naming conventions never overlap.

#### Performance note

JS read methods (`map`, `filter`, `forEach`, etc.) on list/enumerable proxies materialise all items via IPC each time they are called. For large collections accessed in a tight loop, materialise once:

```typescript
const arr = [...list]; // one IPC snapshot
arr.map(...)
arr.filter(...)
```

---

## Stream Support

`System.IO.Stream` subclasses are automatically marshalled as Node.js `stream.Duplex` objects.

```typescript
const ms = new dotnet.System.IO.MemoryStream();

// Write to the .NET stream via Node.js writable interface
import { pipeline } from 'node:stream/promises';
await pipeline(nodeReadable, ms);

// Read from the .NET stream
ms.seek(0);               // seek to beginning (available when canSeek=true)
const chunks: Buffer[] = [];
for await (const chunk of ms) chunks.push(chunk);
const data = Buffer.concat(chunks);

// Access .NET Stream properties directly via __ref
console.log(ms.Length);  // ✅ — .NET property via ref proxy
console.log(ms.Position);
```

**Properties on the returned Duplex:**

| Property / method | Description |
|-------------------|-------------|
| `duplex.__ref` | The .NET object ref — use for direct .NET property/method access |
| `duplex.seek(offset, origin?)` | Calls `Stream.Seek(offset, origin)` — only present when `canSeek=true` |

**`origin` values for `seek`:** `0` = Begin (default), `1` = Current, `2` = End.

> **Warning**: The Duplex `_read` implementation calls `Stream.Read()` synchronously over the IPC channel. This is fine for in-memory or local file streams. Avoid wrapping slow network streams — they will block the Node.js event loop until data arrives.

---

## Migration Notes

1. **Event API**: `add_Click(cb)` / `remove_Click(cb)` have no equivalent in `node-api-dotnet` yet; wait for official support.
2. **Platform**: `node-ps1-dotnet` is Windows only. For cross-platform use, switch to `node-api-dotnet` with .NET 6+.
3. **IPC Overhead**: Every property read / method call in `node-ps1-dotnet` has an IPC round-trip. For performance-sensitive code, batching reads or switching to `node-api-dotnet` can provide orders-of-magnitude improvement.
