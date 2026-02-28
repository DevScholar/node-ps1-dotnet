import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    setupFiles: ['./vitest.setup.js'],
    onUnhandledError: (unhandledErr, afterNext) => {
      if (unhandledErr.message?.includes('process.exit unexpectedly called')) {
        afterNext();
        return;
      }
      console.error('Unhandled error:', unhandledErr);
    },
    include: [
      '__tests__/**/*.ts',
      '**/?(*.)+(spec|test).ts',
    ],
    exclude: [
      '**/node_modules/**',
      '**/dist/**',
    ],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov', 'html'],
      include: ['src/**/*.ts'],
      exclude: ['src/**/*.d.ts'],
    },
    testTimeout: 30000,
  },
});
