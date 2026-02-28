
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('Enum Tests', () => {
  let dotnet: any;
  let System: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      System = dotnet.System;
    } catch (e) {
      console.log('Skipping enum tests - load failed:', e);
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

  it('should access StringComparison enum', () => {
    const StringComparison = System.StringComparison;
    expect(StringComparison).toBeDefined();
    expect(StringComparison.Ordinal).toBeDefined();
    expect(StringComparison.OrdinalIgnoreCase).toBeDefined();
  });

  it('should access DateTimeKind enum', () => {
    const DateTimeKind = System.DateTimeKind;
    expect(DateTimeKind).toBeDefined();
    expect(DateTimeKind.Unspecified).toBeDefined();
    expect(DateTimeKind.Utc).toBeDefined();
    expect(DateTimeKind.Local).toBeDefined();
  });

  it('should access DayOfWeek enum', () => {
    const DayOfWeek = System.DayOfWeek;
    expect(DayOfWeek).toBeDefined();
    expect(DayOfWeek.Sunday).toBeDefined();
    expect(DayOfWeek.Monday).toBeDefined();
  });

  it('should access StringComparison enum values', () => {
    const StringComparison = System.StringComparison;
    expect(StringComparison.Ordinal).toBeDefined();
    expect(StringComparison.OrdinalIgnoreCase).toBeDefined();
  });
});
