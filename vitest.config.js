import react from '@vitejs/plugin-react'

// 测试环境配置：仅用于 `vitest`（`npm test` / `npm run test:watch`）。
// 与 vite.config.js 分开，避免把 test 专属配置混入生产构建。
// 业务测试跑在 jsdom 环境，从而能真实挂载 React Hook（useControlState）。
export default {
  plugins: [react()],
  test: {
    environment: 'jsdom',
    // 仅收集 src 下的业务测试（node_modules 里无大量无关测试）
    include: ['src/**/*.{test,spec}.{js,jsx,ts,tsx}'],
    globals: false,
    mockReset: false,
    restoreMocks: false,
    clearMocks: true,
  },
}
