# Testing
Run all tests:

```bash
npm test
```

Run specific test files:

```bash
npm test -- --testPathPatterns=winforms
npm test -- --testPathPatterns=wpf
npm test -- --testPathPatterns=dotnet
```

Test files are located in the `__tests__` directory:

- `basic.test.ts` - Basic module functionality tests
- `ipc.test.ts` - IPC communication tests
- `winforms.test.ts` - WinForms GUI component tests
- `wpf.test.ts` - WPF GUI component tests
- `gui-events.test.ts` - GUI event handling tests
- `dotnet-enums.test.ts` - .NET enum tests
- `dotnet-console.test.ts` - Console class tests
- `dotnet-io.test.ts` - System.IO tests
- `dotnet-task.test.ts` - Task asynchronous tests
- `dotnet-misc.test.ts` - String, Array, Environment tests
- `dotnet-runtime.test.ts` - Runtime info and type loading tests

Note: GUI tests will not display windows - they only create objects in memory for verification.
