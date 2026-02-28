
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('Console Tests', () => {
  let dotnet: any;
  let System: any;
  let Console: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
      Console = System.Console;
    } catch (e) {
      console.log('Skipping console tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should access Console class', () => {
    expect(Console).toBeDefined();
    expect(Console.WriteLine).toBeDefined();
  });
});
