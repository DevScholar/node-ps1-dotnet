import { jest } from '@jest/globals';

const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('Task Tests', () => {
  let dotnet: any;
  let System: any;
  let Task: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
      Task = System.Threading.Tasks.Task;
    } catch (e) {
      console.log('Skipping task tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access Task class', () => {
    expect(Task).toBeDefined();
    expect(Task.Delay).toBeDefined();
  });

  it('should access Task.CompletedTask', () => {
    const completedTask = Task.CompletedTask;
    expect(completedTask).toBeDefined();
  });
});
