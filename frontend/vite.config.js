import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': resolve(__dirname, 'src') }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: '152.42.232.18:5000',
        changeOrigin: true
      },
      '/avatars': {
        target: '152.42.232.18:5000',
        changeOrigin: true
      }
    }
  }
})