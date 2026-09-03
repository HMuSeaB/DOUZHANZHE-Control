import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist', 'server/**/bin/**', 'server/**/wwwroot/**']),
  {
    files: ['**/*.{js,jsx}'],
    extends: [
      js.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    rules: {
      // 代码库沿用 ref-latest / effect 初始化等既有模式，
      // React Compiler 新严格规则与现状冲突，按项目实际关闭（2026-08-02 审计确认）
      'react-hooks/set-state-in-effect': 'off',
      'react-hooks/refs': 'off',
      'react-hooks/immutability': 'off',
      'react-hooks/preserve-manual-memoization': 'off',
    },
    languageOptions: {
      globals: {
        ...globals.browser,
        __APP_VERSION__: 'readonly',
        __APP_BUILD__: 'readonly',
      },
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
  },
  // 测试文件：暴露 vitest 全局（describe/it/expect/vi/beforeEach 等），
  // 并复用 node + browser 内置全局
  {
    files: ['src/**/*.{test,spec}.{js,jsx}'],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        __APP_VERSION__: 'readonly',
        __APP_BUILD__: 'readonly',
      },
    },
  },
])
