import MarkdownIt from 'markdown-it'
import hljs from 'highlight.js'

/**
 * 共享的 MarkdownIt 实例配置
 * 用于统一处理 Markdown 渲染，避免重复创建实例
 */

// 创建单例实例
const md = new MarkdownIt({
    html: false,
    linkify: true,
    breaks: true,
    highlight: function (str, lang) {
        if (lang && hljs.getLanguage(lang)) {
            try {
                return hljs.highlight(str, { language: lang }).value
            } catch (__) { /* ignore */ }
        }
        return '' // 使用外部默认转义
    }
})

// 保存原始的 fence 渲染器
const defaultFence = md.renderer.rules.fence || function (tokens, idx, options, env, self) {
    return self.renderToken(tokens, idx, options)
}

// 自定义 fence 渲染器，添加复制按钮
md.renderer.rules.fence = function (tokens, idx, options, env, self) {
    const token = tokens[idx]
    const code = token.content.trim()
    const rawHtml = defaultFence(tokens, idx, options, env, self)

    return `<div class="code-block-wrapper">
        <button class="copy-btn" data-code="${encodeURIComponent(code)}" title="Copy code">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
                <path fill-rule="evenodd" clip-rule="evenodd" d="M4 4l1-1h5.414L14 6.586V14l-1 1H5l-1-1V4zm9 3l-3-3H5v10h8V7z"/>
                <path fill-rule="evenodd" clip-rule="evenodd" d="M3 1L2 2v10l1 1V2h6.414l-1-1H3z"/>
            </svg>
        </button>
        ${rawHtml}
    </div>`
}

/**
 * 渲染 Markdown 文本为 HTML
 */
export function renderMarkdown(text: string): string {
    return md.render(text || '')
}

/**
 * 安全解析 JSON，失败返回 null
 */
export function safeJsonParse<T = unknown>(text: string): T | null {
    if (!text || typeof text !== 'string') return null
    try {
        return JSON.parse(text) as T
    } catch {
        return null
    }
}

/**
 * 格式化 JSON 字符串（用于显示）
 */
export function formatJson(input: string): string {
    const parsed = safeJsonParse(input)
    if (parsed !== null) {
        return JSON.stringify(parsed, null, 2)
    }
    return input
}

/**
 * 将 JSON 渲染为带语法高亮的代码块
 */
export function renderJsonAsCodeBlock(input: string): string {
    const formatted = formatJson(input)
    return md.render('```json\n' + formatted + '\n```')
}

/**
 * 获取 MarkdownIt 实例（用于需要直接访问的场景）
 */
export function getMarkdownInstance(): MarkdownIt {
    return md
}

export default md
