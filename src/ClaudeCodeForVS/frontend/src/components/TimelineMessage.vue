<template>
  <div class="timeline-container">
    <div v-for="(item, index) in items" :key="item.uniqueId" class="timeline-item">
      
      <!-- Marker / Axis -->
      <div class="timeline-marker">
        <div class="timeline-line" :class="{ 'is-last': index === items.length - 1 }"></div>
        <div class="timeline-dot" :class="getDotClass(item)"></div>
      </div>

      <!-- Content -->
      <div class="timeline-content">
        
        <!-- Text Content -->
        <div v-if="item.type === 'text'" class="markdown-body" v-html="renderMarkdown(item.content || '')"></div>
        
        <!-- Tool Use -->
        <div v-else-if="item.type === 'tool_use'" class="tool-container">
            
            <!-- Compact Tool View (Default) -->
            <div class="tool-compact" @click="toggleExpand(item.uniqueId)" :class="{ 'is-expanded': isExpanded(item.uniqueId) }">
                <span class="tool-label">{{ getToolLabel(item) }}:</span>
                <span class="tool-value" :title="getToolSummary(item)">{{ getToolSummary(item) }}</span>
            </div>

            <!-- Expanded Details -->
            <div class="tool-details" v-if="isExpanded(item.uniqueId)">
                <div class="markdown-body" v-html="renderToolInput(item.input || '')"></div>
            </div>
        </div>

        <!-- Error -->
        <div v-else-if="item.type === 'error'" class="error-message">
            <span class="error-icon">
                <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
                    <path fill-rule="evenodd" clip-rule="evenodd" d="M7.56 1h.88l6.54 12.26-.44.74H1.44L1 13.26 7.56 1zM8 2.28L2.28 13H13.7L8 2.28zM8.625 12v-1h-1.25v1h1.25zm-1.25-2V6h1.25v4h-1.25z"/>
                </svg>
            </span>
            {{ item.content }}
        </div>

      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { renderMarkdown, safeJsonParse, renderJsonAsCodeBlock } from '../utils/markdown'

const props = defineProps<{
  content: string
}>()

interface TimelineItem {
  type: 'text' | 'tool_use' | 'error'
  content?: string
  name?: string
  input?: string
  id?: string
  uniqueId: string // For v-for key
}

// State for expanded items
const expandedItems = reactive(new Set<string>())

const toggleExpand = (id: string) => {
    if (expandedItems.has(id)) {
        expandedItems.delete(id)
    } else {
        expandedItems.add(id)
    }
}

const isExpanded = (id: string) => expandedItems.has(id)

// 缓存已解析的行数，用于增量解析
const parsedLineCount = ref(0)
const cachedItems = ref<TimelineItem[]>([])

const items = computed(() => {
  const lines = props.content.split('\n').filter(l => l.trim())
  
  // 增量解析：只处理新增的行
  if (lines.length === parsedLineCount.value) {
      return cachedItems.value
  }

  const result: TimelineItem[] = []
  let currentText = ''
  let currentTool: TimelineItem | null = null
  
  // Helper to push text
  const pushText = () => {
      if (currentText) {
          result.push({ 
              type: 'text', 
              content: currentText, 
              uniqueId: `text-${result.length}` 
          })
          currentText = ''
      }
  }

  for (const line of lines) {
    // 使用共享的 safeJsonParse
    const json = safeJsonParse<any>(line)
    
    if (!json) {
        currentText += (currentText ? '\n' : '') + line
        continue
    }
      
    if (json.type === 'stream_event') {
        const event = json.event
        if (!event) continue
        
        if (event.type === 'content_block_start') {
            if (event.content_block?.type === 'tool_use') {
                pushText()
                currentTool = {
                    type: 'tool_use',
                    name: event.content_block.name,
                    input: '',
                    id: event.content_block.id,
                    uniqueId: event.content_block.id || `tool-${result.length}`
                }
                result.push(currentTool)
            }
        } else if (event.type === 'content_block_delta') {
            if (event.delta?.type === 'text_delta') {
                currentText += event.delta.text
            } else if (event.delta?.type === 'input_json_delta') {
                if (currentTool) {
                    currentTool.input = (currentTool.input || '') + event.delta.partial_json
                }
            }
        } else if (event.type === 'content_block_stop') {
            currentTool = null
        }
    } 
    else if (json.type === 'error') {
        pushText()
        result.push({ 
            type: 'error', 
            content: json.error?.message || 'Unknown error',
            uniqueId: `error-${result.length}`
        })
    }
  }
  
  pushText()
  
  // 更新缓存
  parsedLineCount.value = lines.length
  cachedItems.value = result
  
  return result
})

const isReadTool = (item: TimelineItem) => {
    return item.name === 'Read' || item.name === 'ReadFile'
}

const getToolLabel = (item: TimelineItem) => {
    if (isReadTool(item)) return 'Read'
    if (item.name === 'Bash') return 'Exec'
    return item.name
}

const getToolSummary = (item: TimelineItem) => {
    if (!item.input) return ''
    const json = safeJsonParse<Record<string, unknown>>(item.input)
    
    if (!json) {
        // Fallback for partial JSON
        if (isReadTool(item)) {
            const match = item.input?.match(/"(?:file_)?path"\s*:\s*"([^"]+)"/)
            return match ? match[1] : '...'
        }
        return '...'
    }

    if (isReadTool(item)) {
        return (json.file_path || json.path || 'Unknown file') as string
    }

    if (item.name === 'Bash') {
        return (json.command || '...') as string
    }

    // Default summary: first key-value or truncated string
    const keys = Object.keys(json)
    if (keys.length > 0) {
        const val = json[keys[0]]
        return typeof val === 'string' ? val : JSON.stringify(val)
    }
    return '...'
}

const renderToolInput = (input: string) => {
    return renderJsonAsCodeBlock(input)
}

const getDotClass = (item: TimelineItem) => {
    if (item.type === 'tool_use') {
        if (isReadTool(item)) return 'dot-read'
        return 'dot-tool'
    }
    if (item.type === 'error') return 'dot-error'
    return 'dot-text'
}

</script>

<style scoped>
.timeline-container {
    position: relative;
    padding-left: 0;
    font-size: 0.9em;
    min-width: 0;
    width: 100%;
}

.timeline-item {
    position: relative;
    padding-left: 12px;
    padding-bottom: 4px;
    min-width: 0;
}

/* Marker / Axis Styles */
.timeline-marker {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 6px;
    pointer-events: none;
}

.timeline-line {
    position: absolute;
    top: 0.45em;
    bottom: -4px;
    width: 2px; /* Thicker line for visibility */
    background-color: var(--vscode-editorGuide-background);
    left: 3px;
    opacity: 0.6;
}

.timeline-item:last-child .timeline-line {
    display: none;
}

.timeline-dot {
    position: absolute;
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background-color: var(--vscode-editor-foreground);
    left: 0;
    top: 0;
    transform: translateY(0.45em);
    box-shadow: 0 0 0 3px var(--vscode-editor-background); /* Gap effect */
}

.dot-read { background-color: #4EC9B0; }
.dot-tool { background-color: #DCDCAA; }
.dot-error { background-color: #F48771; }
.dot-text {
    background-color: var(--vscode-descriptionForeground);
    opacity: 0.7;
}

/* Content Styles */
.timeline-content {
    flex: 1;
    min-width: 0; /* Allow shrinking */
}

/* Tool Styles */
.tool-container {
    margin-top: 0;
}

.tool-compact {
    display: flex;
    align-items: center;
    gap: 8px;
    font-family: var(--vscode-editor-font-family);
    font-size: 0.85em;
    padding: 2px 0;
    background: transparent;
    border-radius: 0;
    width: 100%;
    cursor: pointer;
    user-select: none;
}

.tool-compact:hover {
    opacity: 0.8;
}

.tool-label {
    font-weight: 600;
    color: var(--vscode-symbolIcon-functionForeground);
    flex-shrink: 0;
    font-size: 0.9em;
}

.tool-value {
    color: var(--vscode-textLink-foreground);
    overflow-wrap: anywhere;
    word-break: break-all;
    font-family: var(--vscode-editor-font-family);
    opacity: 0.9;
    font-size: 0.9em;
    min-width: 0;
}

.tool-details {
    margin-top: 6px;
    min-width: 0;
    width: 100%;
}

.error-message {
    color: var(--vscode-errorForeground);
    display: flex;
    gap: 8px;
    align-items: flex-start;
    margin-top: 4px;
}

.error-icon {
    flex-shrink: 0;
    margin-top: 2px;
}

/* Markdown Overrides */
.markdown-body {
    font-size: 0.9em;
    line-height: 1.4;
    overflow-wrap: anywhere;
    word-wrap: break-word;
    word-break: break-word;
    min-width: 0;
}

.markdown-body p {
    margin-top: 0;
    margin-bottom: 0.3em;
    overflow-wrap: anywhere;
    word-wrap: break-word;
    word-break: break-word;
}

.markdown-body p:last-child {
    margin-bottom: 0;
}

.markdown-body code {
    overflow-wrap: anywhere;
    word-wrap: break-word;
    word-break: break-all;
    white-space: pre-wrap;
}

.markdown-body pre {
    background-color: var(--vscode-textBlockQuote-background);
    border-radius: 4px;
    padding: 6px;
    margin: 0;
    white-space: pre-wrap;
    word-wrap: break-word;
    word-break: break-all;
    overflow-wrap: anywhere;
}

.markdown-body pre code {
    white-space: pre-wrap;
    word-break: break-all;
    overflow-wrap: anywhere;
}
</style>