
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('IO Tests', () => {
  let dotnet: any;
  let System: any;
  let IO: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
      IO = System.IO;
    } catch (e) {
      console.log('Skipping IO tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access Path class', () => {
    const Path = IO.Path;
    expect(Path).toBeDefined();
    expect(Path.Combine).toBeDefined();
  });

  it('should use Path.Combine', () => {
    const Path = IO.Path;
    const result = Path.Combine('folder', 'file.txt');
    expect(result).toBeDefined();
    expect(typeof result).toBe('string');
  });

  it('should access File class', () => {
    const File = IO.File;
    expect(File).toBeDefined();
    expect(File.Exists).toBeDefined();
  });

  it('should access Directory class', () => {
    const Directory = IO.Directory;
    expect(Directory).toBeDefined();
    expect(Directory.Exists).toBeDefined();
  });

  it('should get current directory', () => {
    const Directory = IO.Directory;
    const cwd = Directory.GetCurrentDirectory();
    expect(cwd).toBeDefined();
    expect(typeof cwd).toBe('string');
  });
});
