import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Vite 仅用于生产构建 (npm run build)，前端请求直连 C# API (localhost:3100)
export default defineConfig({
  plugins: [react()],
})
