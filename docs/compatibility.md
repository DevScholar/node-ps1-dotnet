# node-ps1-dotnet 与 node-api-dotnet 兼容性说明

本文档描述 `node-ps1-dotnet` 与 Microsoft 官方 [`node-api-dotnet`](https://github.com/microsoft/node-api-dotnet) 的 API 兼容情况。

---

## 设计目标

`node-ps1-dotnet` 尽量与 `node-api-dotnet` 保持相同的公共 API 风格，使代码可在两者之间低成本迁移。两者的根本差异在于实现机制：

| 特性 | node-api-dotnet | node-ps1-dotnet |
|------|----------------|-----------------|
| 实现方式 | Node.js N-API 原生插件（.NET 进程内） | 进程外（PowerShell 子进程 + Named Pipe IPC） |
| 平台支持 | Windows / macOS / Linux | **仅 Windows**（依赖 WinForms/WPF） |
| 运行时要求 | .NET 6+ 或 .NET Framework 4.7.2+ | .NET Framework 4.x（通过 PowerShell 5.1） |
| 性能 | 高（同进程直接调用） | 中（每次调用有 IPC 往返开销） |
| 事件支持 | 尚未实现 | **已实现**（0/1/2 参数委托） |
| 无需预编译 | 需要 `.nupkg` 或 `.NET SDK` | **不需要**（`Add-Type` 运行时编译） |

---

## 兼容的 API

### 加载程序集

```typescript
// node-api-dotnet
import dotnet from 'node-api-dotnet';
dotnet.load('System.Windows.Forms');          // 按名称加载
dotnet.load('./MyLib.dll');                   // 按路径加载

// node-ps1-dotnet —— 完全相同
import dotnet from 'node-ps1-dotnet';
dotnet.load('System.Windows.Forms');
dotnet.load('./MyLib.dll');
```

`load(nameOrPath)` 自动区分程序集名称（不含路径分隔符和 `.dll`/`.exe` 扩展名）和文件路径。

显式路径加载也提供别名：

```typescript
dotnet.loadFrom('./MyLib.dll');   // 等价于 dotnet.load('./MyLib.dll')
```

### 访问类型

```typescript
// 两者相同：通过命名空间树访问类型
const Button = dotnet.System.Windows.Forms.Button;
const form   = dotnet.System.Windows.Forms.Form;
```

加载程序集后，其命名空间自动合并到 `dotnet` 的属性树中，访问任意层级的名称时内部调用 `GetType`：若找到类型则返回类型引用，否则返回命名空间代理继续向下导航。

### 构造对象

```typescript
// 两者相同：使用 new 关键字
const btn = new dotnet.System.Windows.Forms.Button();
btn.Text = 'Click me';
```

### 调用方法与读写属性

```typescript
// 两者相同
form.Controls.Add(btn);          // 调用方法
btn.Width = 200;                  // 设置属性
const text = btn.Text;            // 读取属性
```

### 运行时信息

```typescript
// node-api-dotnet
console.log(dotnet.runtimeInfo.frameworkMoniker);  // 'net472'
console.log(dotnet.runtimeInfo.runtimeVersion);    // '4.0.30319.42000'

// node-ps1-dotnet —— 完全相同
console.log(dotnet.runtimeInfo.frameworkMoniker);
console.log(dotnet.runtimeInfo.runtimeVersion);

// 也支持直接属性访问（兼容旧代码）
console.log(dotnet.frameworkMoniker);
console.log(dotnet.runtimeVersion);
```

### 依赖解析钩子

```typescript
// node-api-dotnet
dotnet.addListener('resolving', (name: string) => {
    const dllName = name.split(',')[0];
    const p = path.join('./libs', dllName + '.dll');
    return fs.existsSync(p) ? p : null;
});

// node-ps1-dotnet —— 完全相同
dotnet.addListener('resolving', (name: string) => {
    const dllName = name.split(',')[0];
    const p = path.join('./libs', dllName + '.dll');
    return fs.existsSync(p) ? p : null;
});
```

回调接收完整的程序集标识字符串（如 `"MyLib, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"`），返回文件路径字符串（CLR 将从该路径加载）或 `null`（跳过，由 CLR 继续默认解析）。

> **注意**：当前实现只支持注册一个 `resolving` 监听器，后注册的会覆盖前一个。

---

## 本项目独有的 API（node-api-dotnet 中不存在）

### 事件订阅

`node-api-dotnet` 尚未实现事件支持。`node-ps1-dotnet` 通过 `add_<EventName>` 方法订阅 .NET 事件：

```typescript
// node-ps1-dotnet 专有
btn.add_Click((sender: any, e: any) => {
    console.log('Button clicked');
});

form.add_Load(() => {
    console.log('Form loaded');
});
```

支持 0、1、2 个参数的委托类型（覆盖绝大多数 WinForms/WPF 事件）。3 个及以上参数的委托静默忽略。

### WPF/WinForms 应用程序循环

```typescript
// node-ps1-dotnet 专有（与 node-with-window 配合使用）
dotnet.startApplication(app, window);   // 启动 WPF 消息循环（非阻塞）
dotnet.pollEvent();                     // 轮询一个待处理的 UI 事件
```

---

## node-api-dotnet 有但本项目未实现的特性

| 特性 | 说明 |
|------|------|
| `dotnet.load()` 返回值 | node-api-dotnet 返回 `void`（类型合并到命名空间树）；本项目行为相同，但内部有 `GetType` IPC 调用 |
| 多个 `resolving` 监听器 | node-api-dotnet 支持链式多监听器；本项目目前只支持一个 |
| `import` 静态类型（TypeScript） | node-api-dotnet 通过 `.nupkg` 生成 `.d.ts`；本项目使用动态 `Proxy`，无静态类型 |
| macOS / Linux | node-api-dotnet 跨平台；本项目仅 Windows |
| `Task<T>` 原生 async/await | 本项目通过 `AwaitTask` IPC 调用将 Task 包装为 `Promise`，语义相同但有额外往返开销 |

---

## 迁移注意事项

从 `node-ps1-dotnet` 迁移到 `node-api-dotnet` 时需要注意：

1. **事件 API**：`add_Click(cb)` 目前在 `node-api-dotnet` 中无对应实现，需等待官方支持。
2. **平台检测**：`node-ps1-dotnet` 仅 Windows；若需跨平台，切换到 `node-api-dotnet` 并确保 .NET 6+ 运行时存在。
3. **无 `loadFrom` 别名**：`node-api-dotnet` 的 `load()` 已统一处理路径，无需 `loadFrom()`。
4. **IPC 开销**：`node-ps1-dotnet` 的每次属性读取/方法调用都有 IPC 往返，性能敏感场景切换 `node-api-dotnet` 可获得数量级的性能提升。
