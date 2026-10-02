import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

// The panel is served by the WildCat host under /panel, so every build uses that base. In
// development the dev server proxies /api to the host; set WILDCAT_API_URL to point it elsewhere.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const apiTarget = env.WILDCAT_API_URL || 'http://localhost:5000'

  return {
    base: '/panel/',
    plugins: [react()],
    server: {
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
      },
    },
    test: {
      include: ['src/**/*.test.ts'],
      environment: 'node',
    },
  }
})
