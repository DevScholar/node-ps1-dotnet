
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('String Tests', () => {
  let dotnet: any;
  let System: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
    } catch (e) {
      console.log('Skipping string tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access String class', () => {
    const String = System.String;
    expect(String).toBeDefined();
  });
});

(isWindows ? describe : describe.skip)('Array Tests', () => {
  let dotnet: any;
  let System: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
    } catch (e) {
      console.log('Skipping array tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access Array class', () => {
    const Array = System.Array;
    expect(Array).toBeDefined();
    expect(Array.Reverse).toBeDefined();
  });
});

(isWindows ? describe : describe.skip)('Environment Tests', () => {
  let dotnet: any;
  let System: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
    } catch (e) {
      console.log('Skipping environment tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access Environment class', () => {
    const Environment = System.Environment;
    expect(Environment).toBeDefined();
    expect(Environment.ProcessorCount).toBeDefined();
    expect(Environment.CurrentDirectory).toBeDefined();
  });

  it('should get processor count', () => {
    const Environment = System.Environment;
    const processorCount = Environment.ProcessorCount;
    expect(processorCount).toBeDefined();
    expect(processorCount).toBeGreaterThan(0);
  });

  it('should get current directory', () => {
    const Environment = System.Environment;
    const cwd = Environment.CurrentDirectory;
    expect(cwd).toBeDefined();
    expect(typeof cwd).toBe('string');
  });
});
