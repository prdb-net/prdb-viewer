import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../Prdb.Viewer.Host/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:8080',
    },
  },
  test: {
    // Only the jsdom suite. The browser-borne suite beside it in `e2e` is Playwright's, and
    // matches the same *.spec.ts shape — collected here it fails on Playwright's own guard, which
    // reads as a broken unit suite rather than as the two runners overlapping.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    globals: true,
  },
})
