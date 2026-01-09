<template>
    <Teleport to="body">
        <div v-if="show" class="file-picker-overlay" @click="handleOverlayClick">
            <div ref="pickerEl" class="file-picker" :style="pickerStyle" @click.stop>
                <div class="search-box">
                    <input
                        ref="searchInput"
                        v-model="searchQuery"
                        type="text"
                        placeholder="搜索文件..."
                        class="search-input"
                        @keydown="handleKeyDown"
                    />
                </div>
                <div class="file-list">
                    <div
                        v-for="(file, idx) in filteredFiles"
                        :key="file.path"
                        class="file-item"
                        :class="{ selected: idx === selectedIndex }"
                        @click="selectFile(file)"
                        @mouseenter="selectedIndex = idx"
                    >
                        <div class="file-info">
                            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" class="file-icon">
                                <path d="M13.71 4.29l-3-3L10 1H4c-.55 0-1 .45-1 1v12c0 .55.45 1 1 1h8c.55 0 1-.45 1-1V5l-.29-.71zM13 14H4V2h5v3h4v9z"/>
                            </svg>
                            <span class="file-name">{{ file.name }}</span>
                        </div>
                        <span class="file-path">{{ file.directory }}</span>
                    </div>
                    <div v-if="filteredFiles.length === 0" class="no-results">
                        没有找到匹配的文件
                    </div>
                </div>
            </div>
        </div>
    </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'

type FileItem = {
    name: string
    path: string
    directory: string
}

const props = defineProps<{
    show: boolean
    files: FileItem[]
}>()

const emit = defineEmits<{
    select: [file: FileItem]
    close: []
}>()

const searchInput = ref<HTMLInputElement | null>(null)
const pickerEl = ref<HTMLDivElement | null>(null)
const searchQuery = ref('')
const selectedIndex = ref(0)
const position = ref({ top: 0, left: 0, width: 300 })

const filteredFiles = computed(() => {
    if (!searchQuery.value.trim()) {
        return props.files
    }
    
    const query = searchQuery.value.toLowerCase()
    return props.files.filter(file => 
        file.name.toLowerCase().includes(query) || 
        file.path.toLowerCase().includes(query)
    )
})

const pickerStyle = computed(() => {
    return {
        top: `${position.value.top}px`,
        left: `${position.value.left}px`,
        width: `${position.value.width}px`
    }
})

function updatePosition() {
    // 查找 composer-container 元素来计算位置
    const composerContainer = document.querySelector('.composer-container')
    if (!composerContainer) return
    
    const rect = composerContainer.getBoundingClientRect()
    const pickerHeight = pickerEl.value?.offsetHeight || 300
    const gap = 8
    
    position.value = {
        top: rect.top - pickerHeight - gap,
        left: rect.left,
        width: rect.width
    }
}

function handleKeyDown(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') {
        e.preventDefault()
        selectedIndex.value = Math.min(selectedIndex.value + 1, filteredFiles.value.length - 1)
    } else if (e.key === 'ArrowUp') {
        e.preventDefault()
        selectedIndex.value = Math.max(selectedIndex.value - 1, 0)
    } else if (e.key === 'Enter') {
        e.preventDefault()
        if (filteredFiles.value[selectedIndex.value]) {
            selectFile(filteredFiles.value[selectedIndex.value])
        }
    } else if (e.key === 'Escape') {
        emit('close')
    }
}

function selectFile(file: FileItem) {
    emit('select', file)
}

function handleOverlayClick() {
    emit('close')
}

let resizeObserver: ResizeObserver | null = null

watch(() => props.show, async (show) => {
    if (show) {
        await nextTick()
        updatePosition()
        searchInput.value?.focus()
        searchQuery.value = ''
        selectedIndex.value = 0
        
        // 监听窗口大小变化
        window.addEventListener('resize', updatePosition)
        
        // 监听 composer-container 大小变化
        const composerContainer = document.querySelector('.composer-container')
        if (composerContainer) {
            resizeObserver = new ResizeObserver(() => {
                updatePosition()
            })
            resizeObserver.observe(composerContainer)
        }
    } else {
        // 清理监听器
        window.removeEventListener('resize', updatePosition)
        if (resizeObserver) {
            resizeObserver.disconnect()
            resizeObserver = null
        }
    }
})

// 监听文件列表变化重新计算位置（因为高度可能变化）
watch(() => props.files, async () => {
    if (props.show) {
        await nextTick()
        updatePosition()
    }
})

watch(filteredFiles, async () => {
    if (props.show) {
        await nextTick()
        updatePosition()
    }
})

watch(searchQuery, () => {
    selectedIndex.value = 0
})

onUnmounted(() => {
    window.removeEventListener('resize', updatePosition)
    if (resizeObserver) {
        resizeObserver.disconnect()
        resizeObserver = null
    }
})
</script>

<style scoped>
.file-picker-overlay {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: 9999;
    background: transparent;
}

.file-picker {
    position: fixed;
    min-width: 300px;
    max-height: 400px;
    background: var(--vscode-quickInput-background, #252526);
    border: 1px solid var(--vscode-widget-border, #454545);
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.5);
    display: flex;
    flex-direction: column;
    overflow: hidden;
}

.search-box {
    padding: 8px;
    border-bottom: 1px solid var(--vscode-widget-border, #454545);
}

.search-input {
    width: 100%;
    background: var(--vscode-input-background, #3c3c3c);
    border: 1px solid var(--vscode-input-border, #3c3c3c);
    outline: none;
    color: var(--vscode-input-foreground, #cccccc);
    font-size: 13px;
    font-family: inherit;
    padding: 6px 8px;
}

.search-input:focus {
    border-color: var(--vscode-focusBorder, #007acc);
}

.search-input::placeholder {
    color: var(--vscode-input-placeholderForeground, #888888);
}

.file-list {
    flex: 1;
    overflow-y: auto;
}

.file-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 5px 10px;
    cursor: pointer;
    transition: background 0.1s;
    min-height: 28px;
}

.file-item:hover {
    background: var(--vscode-list-hoverBackground, rgba(90, 93, 94, 0.31));
}

.file-item.selected {
    background: var(--vscode-list-activeSelectionBackground, #04395e);
    color: var(--vscode-list-activeSelectionForeground, #ffffff);
}

.file-info {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: 1;
    min-width: 0;
}

.file-icon {
    color: var(--vscode-icon-foreground, #c5c5c5);
    flex-shrink: 0;
}

.file-item.selected .file-icon {
    color: var(--vscode-list-activeSelectionForeground, #ffffff);
}

.file-name {
    font-size: 13px;
    color: var(--vscode-foreground, #cccccc);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.file-item.selected .file-name {
    color: var(--vscode-list-activeSelectionForeground, #ffffff);
}

.file-path {
    font-size: 11px;
    color: var(--vscode-descriptionForeground, #888888);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 200px;
    text-align: right;
    margin-left: 16px;
}

.file-item.selected .file-path {
    color: rgba(255, 255, 255, 0.8);
}

.no-results {
    padding: 20px;
    text-align: center;
    color: var(--vscode-descriptionForeground, #888888);
    font-size: 13px;
}

/* 滚动条样式 */
.file-list::-webkit-scrollbar {
    width: 10px;
}

.file-list::-webkit-scrollbar-track {
    background: transparent;
}

.file-list::-webkit-scrollbar-thumb {
    background: var(--vscode-scrollbarSlider-background, rgba(121, 121, 121, 0.4));
    border-radius: 5px;
}

.file-list::-webkit-scrollbar-thumb:hover {
    background: var(--vscode-scrollbarSlider-hoverBackground, rgba(100, 100, 100, 0.7));
}

.file-list::-webkit-scrollbar-thumb:active {
    background: var(--vscode-scrollbarSlider-activeBackground, rgba(191, 191, 191, 0.4));
}
</style>
