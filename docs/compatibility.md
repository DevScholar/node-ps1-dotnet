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
| Event Support | Not yet implemented | **Implemented** (0/1/2 parameter delegates) |
| No Pre-compilation Required | Requires `.nupkg` or `.NET SDK` | **Not required** (`Add-Type` runtime compilation) |

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

After loading an assembly, its namespace is automatically merged into the `dotnet` property tree. Accessing any level of the name internally calls `GetType`: if a type is found, it returns a type reference; otherwise, it returns a namespace proxy to continue navigation downward.

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
// node-api-dotnet
console.log(dotnet.frameworkMoniker);  // 'net472'
console.log(dotnet.runtimeVersion);   // '4.0.30319.42000'

// node-ps1-dotnet — identical
console.log(dotnet.frameworkMoniker);
console.log(dotnet.runtimeVersion);
```

### Dependency Resolution Hooks

```typescript
// node-api-dotnet
dotnet.addListener('resolving', (name: string) => {
    const dllName = name.split(',')[0];
    const p = path.join('./libs', dllName + '.dll');
    return fs.existsSync(p) ? p : null;
});

// node-ps1-dotnet — identical
dotnet.addListener('resolving', (name: string) => {
    const dllName = name.split(',')[0];
    const p = path.join('./libs', dllName + '.dll');
    return fs.existsSync(p) ? p : null;
});
```

The callback receives the full assembly identity string (e.g., `"MyLib, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"`), and returns a file path string (from which the CLR will load) or `null` (skip, let CLR continue with default resolution).

> **Note**: The current implementation only supports registering one `resolving` listener; later registrations will override previous ones.

---

## APIs Unique to This Project (Not in node-api-dotnet)

### Event Subscription

`node-api-dotnet` has not yet implemented event support. `node-ps1-dotnet` subscribes to .NET events via `add_<EventName>` methods:

```typescript
// node-ps1-dotnet specific
btn.add_Click((sender: any, e: any) => {
    console.log('Button clicked');
});

form.add_Load(() => {
    console.log('Form loaded');
});
```

Supports delegate types with 0, 1, or 2 parameters (covering the vast majority of WinForms/WPF events). Delegates with 3 or more parameters are silently ignored.

### WPF/WinForms Application Loop

```typescript
// node-ps1-dotnet specific (used with node-with-window)
dotnet.startApplication(app, window);   // Start WPF message loop (non-blocking)
dotnet.pollEvent();                     // Poll one pending UI event
```

---

## Features in node-api-dotnet but Not Implemented in This Project

| Feature | Description |
|---------|-------------|
| `dotnet.load()` return value | node-api-dotnet returns `void` (types merged into namespace tree); this project behaves the same, but internally has `GetType` IPC calls |
| Multiple `resolving` listeners | node-api-dotnet supports chained multiple listeners; this project currently only supports one |
| `import` static types (TypeScript) | node-api-dotnet generates `.d.ts` via `.nupkg`; this project uses dynamic `Proxy`, no static types |
| macOS / Linux | node-api-dotnet is cross-platform; this project is Windows only |
| `Task<T>` native async/await | This project wraps Task as `Promise` via `AwaitTask` IPC call, same semantics but with additional round-trip overhead |

---

## Migration Notes

---

## Type Conversion Modes

`node-ps1-dotnet` supports two collection marshalling strategies, selectable at runtime via `dotnet.typeConversionBehavior`:

```typescript
dotnet.typeConversionBehavior = 'node-api-dotnet'; // default
dotnet.typeConversionBehavior = 'pythonnet';
```

The setting takes effect immediately for all subsequent IPC calls; it does not need to be set before `dotnet.load()`.

### `'node-api-dotnet'` (default)

Mirrors Microsoft's official `node-api-dotnet` type mappings:

| .NET type | JS value | Mutable from JS? |
|-----------|----------|-----------------|
| `IList<T>` / `List<T>` | `T[]` (copy) | No — mutations are lost |
| `IDictionary<K,V>` | `Map<K,V>` (copy) | No — mutations are lost |

This is the default because it gives the most natural JS experience for read-only data.

### `'pythonnet'` mode

Inspired by [pythonnet](https://pythonnet.github.io/pythonnet/python.html)'s rule that reference types stay as references:

| .NET type | JS value | Mutable from JS? |
|-----------|----------|-----------------|
| `IList<T>` / `List<T>` | ref proxy + JS array interface | **Yes** — `Add`, `Remove`, `Clear` apply to the original .NET object |
| `IDictionary<K,V>` | `Map<K,V>` (copy, unchanged) | No (not yet implemented) |

#### List proxy: JS array interface

When a `IList<T>` is returned as a ref proxy it also exposes the full JS array reading API, so existing code that reads the collection does not need to change:

```typescript
dotnet.typeConversionBehavior = 'pythonnet';

const origins = nwwReg.AllowedOrigins; // ref proxy, not a copy

// ── Write operations: applied to the real .NET IList<string> ──
origins.Add('*');           // ✅
origins.Remove('old');      // ✅
origins.Clear();            // ✅

// ── Read operations: JS array interface (lowercase names) ──
origins.length;                      // ✅  → Count
for (const o of origins) { ... }     // ✅  → Symbol.iterator
origins.map(o => o.toUpperCase());   // ✅  → materialises array, then maps
origins.filter(o => o !== '*');      // ✅
origins.forEach(o => console.log(o));// ✅
origins.includes('*');               // ✅
origins.indexOf('*');                // ✅
[...origins];                        // ✅  → spread via Symbol.iterator
origins.at(-1);                      // ✅  → last element

// ── .NET PascalCase methods still work as normal ──
origins.Contains('*');    // ✅
origins.Count;            // ✅
origins.IndexOf('*');     // ✅
```

> **Why no name clash?**  .NET standard members use `PascalCase` (`Add`, `Count`, `Contains`, …) while the added JS interface uses `camelCase` (`map`, `filter`, `length`, …) — these two conventions never overlap.

#### Comparison with pythonnet

```python
# pythonnet: IList<T> is always a live reference
origins.Add('*')    # works — reference type stays as reference
```

```typescript
// node-ps1-dotnet default ('node-api-dotnet'):
origins.Add('*');   // ❌ — origins is a copied JS array, not the original IList

// node-ps1-dotnet pythonnet mode:
dotnet.typeConversionBehavior = 'pythonnet';
origins.Add('*');   // ✅ — ref proxy, mutation goes through
```

#### Known limitations

- `IDictionary<K,V>` is not yet affected by pythonnet mode (always returned as `Map`). This will be addressed in a future update.
- The `length` getter, `Symbol.iterator`, and all read methods make synchronous IPC round-trips each time they are called. For large collections accessed in a tight loop, materialise once with `[...list]` and work on the JS array.


1. **Event API**: `add_Click(cb)` currently has no corresponding implementation in `node-api-dotnet`; wait for official support.
2. **Platform Detection**: `node-ps1-dotnet` is Windows only; if cross-platform is needed, switch to `node-api-dotnet` and ensure .NET 6+ runtime is present.
3. **IPC Overhead**: Every property read/method call in `node-ps1-dotnet` has IPC round-trip overhead; for performance-sensitive scenarios, switching to `node-api-dotnet` can provide orders-of-magnitude performance improvements.