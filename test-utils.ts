import { jest } from '@jest/globals';

let cachedDotnet: any = null;
let isInitialized = false;

export async function setupDotnet(): Promise<any> {
  if (cachedDotnet && isInitialized) {
    return cachedDotnet;
  }

  try {
    cachedDotnet = await import('../src/index.js');
    await cachedDotnet.node_ps1_dotnet._load('System');
    isInitialized = true;
    return cachedDotnet;
  } catch (e) {
    console.log('Failed to setup dotnet:', e);
    return null;
  }
}

export function teardownDotnet(dotnet: any): void {
  try {
    dotnet?.node_ps1_dotnet?._close();
  } catch {}
  isInitialized = false;
  cachedDotnet = null;
}

export function skipIfNotWindows(): void {
  if (process.platform !== 'win32') {
    throw new Error('Skipped: Windows only test');
  }
}

export function createDotnetTests(name: string, testFn: (dotnet: any, System: any) => void): void {
  describe(name, () => {
    let dotnet: any;
    let System: any;

    beforeAll(async () => {
      dotnet = await setupDotnet();
      if (dotnet) {
        System = dotnet.System;
      }
    });

    afterAll(() => {
      teardownDotnet(dotnet);
    });

    if (process.platform !== 'win32') {
      it('should skip on non-Windows', () => {
        expect(true).toBe(true);
      });
    } else {
      testFn(dotnet, System);
    }
  });
}

export function isWindows(): boolean {
  return process.platform === 'win32';
}
