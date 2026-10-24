import { fileURLToPath, URL } from 'node:url'
import basicSsl from '@vitejs/plugin-basic-ssl'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [vue(), basicSsl({ name: 'yt-fetcher-local' })],
  server: {
    port: 5173,
    strictPort: true,
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
