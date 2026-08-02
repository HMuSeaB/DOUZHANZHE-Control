import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import fs from 'node:fs'

const pkg = JSON.parse(fs.readFileSync(new URL('./package.json', import.meta.url), 'utf-8'))
let buildInfo = { commit: '' }
try {
  buildInfo = JSON.parse(fs.readFileSync(new URL('./build-info.json', import.meta.url), 'utf-8'))
} catch { /* build-info.json 由打包脚本生成，缺失时仅不显示 Build */ }

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
    __APP_BUILD__: JSON.stringify(buildInfo.commit || ''),
  },
})
