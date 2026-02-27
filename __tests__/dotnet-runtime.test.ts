import { jest } from '@jest/globals';

const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('Runtime Info Tests', () => {
  let dotnet: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
    } catch (e) {
      console.log('Skipping runtime info tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should get runtime version', () => {
    const version = dotnet.default.runtimeVersion;
    expect(version).toBeDefined();
    expect(typeof version).toBe('string');
  });

  it('should get framework moniker', () => {
    const moniker = dotnet.default.frameworkMoniker;
    expect(moniker).toBeDefined();
    expect(typeof moniker).toBe('string');
  });

  it('should access _getRuntimeInfo', () => {
    const info = dotnet.node_ps1_dotnet._getRuntimeInfo();
    expect(info).toBeDefined();
    expect(info.frameworkMoniker).toBeDefined();
    expect(info.runtimeVersion).toBeDefined();
  });
});

(isWindows ? describe : describe.skip)('Type Loading Tests', () => {
  let dotnet: any;
  let System: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
    } catch (e) {
      console.log('Skipping type loading tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should load System namespace', () => {
    expect(System).toBeDefined();
  });

  it('should access basic types', () => {
    expect(System.Int32).toBeDefined();
    expect(System.String).toBeDefined();
    expect(System.Boolean).toBeDefined();
    expect(System.Object).toBeDefined();
  });
});

(isWindows ? describe : describe.skip)('Assembly Tests', () => {
  let dotnet: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
    } catch (e) {
      console.log('Skipping assembly tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should have default export with load method', () => {
    expect(dotnet.default).toBeDefined();
    expect(typeof dotnet.default.load).toBe('function');
  });

  it('should have node_ps1_dotnet object', () => {
    expect(dotnet.node_ps1_dotnet).toBeDefined();
    expect(typeof dotnet.node_ps1_dotnet._load).toBe('function');
  });
});
