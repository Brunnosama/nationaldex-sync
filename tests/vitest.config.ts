import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    projects: [{ test: { name: 'data', include: ['data/**/*.test.ts'] } }],
  },
});
