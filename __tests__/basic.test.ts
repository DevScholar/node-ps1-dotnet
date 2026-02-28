
describe('node-ps1-dotnet', () => {
  describe('utils', () => {
    it('should export getPowerShellPath', async () => {
      const { getPowerShellPath } = await import('../src/utils.js');
      expect(typeof getPowerShellPath).toBe('function');
    });
  });

  describe('ipc', () => {
    it('should export IpcSync class', async () => {
      const { IpcSync } = await import('../src/ipc.js');
      expect(IpcSync).toBeDefined();
    });
  });

  describe('types', () => {
    it('should export ProtocolResponse type', async () => {
      const types = await import('../src/types.js');
      expect(types).toBeDefined();
    });
  });
});
