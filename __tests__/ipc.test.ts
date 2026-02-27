import { jest } from '@jest/globals';

describe('IpcSync', () => {
  let IpcSync: any;
  let mockFs: any;

  beforeAll(async () => {
    const ipcModule = await import('../src/ipc.js');
    IpcSync = ipcModule.IpcSync;
  });

  beforeEach(() => {
    mockFs = {
      openSync: jest.fn(),
      readSync: jest.fn(),
      closeSync: jest.fn(),
    };
  });

  it('should create an instance with pipe name and callback', () => {
    const onEvent = jest.fn();
    const ipc = new IpcSync('test_pipe', onEvent);
    
    expect(ipc).toBeDefined();
    expect(ipc).toHaveProperty('pipeName', 'test_pipe');
  });

  it('should have connect, send, and close methods', () => {
    const onEvent = jest.fn();
    const ipc = new IpcSync('test_pipe', onEvent);
    
    expect(typeof ipc.connect).toBe('function');
    expect(typeof ipc.send).toBe('function');
    expect(typeof ipc.close).toBe('function');
  });

  it('should throw error when connecting to nonexistent pipe', () => {
    const onEvent = jest.fn();
    const ipc = new IpcSync('nonexistent_pipe_12345', onEvent);
    
    expect(() => ipc.connect()).toThrow();
  });
});
