/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// In development the site calls /v1 on its own origin and Vite forwards it to the API, so no CORS
// is needed. By default that's the deployed API; set VITE_API_PROXY=http://localhost:5199 to use a
// local one. In production, vercel.json rewrites /v1 to the API the same way.
const deployedApi = 'https://transporttracker-api-brerdhdygwcua7fq.australiaeast-01.azurewebsites.net'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd())
  const proxy = { '/v1': { target: env.VITE_API_PROXY || deployedApi, changeOrigin: true } }

  return {
    plugins: [react()],
    server: { proxy },
    preview: { proxy },
    test: { environment: 'node' },
  }
})
