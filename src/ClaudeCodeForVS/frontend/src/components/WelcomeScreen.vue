<template>
    <div class="welcome-container">
        <div class="logo-section">
            <img :src="claudeLogo" alt="Claude Logo" class="claude-logo" />
        </div>

        <div class="text-section">
            <h2>Tired of repeating yourself?</h2>
            <p>Claude can help you write code, debug issues, and explain complex topics.</p>
        </div>
    </div>
</template>

<script setup lang="ts">
import claudeLogo from '../resources/claude-logo.svg'
</script>

<style scoped>
.welcome-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    color: var(--vscode-foreground);
    text-align: center;
    padding: 20px;
    opacity: 0.9;
}

.logo-section {
    margin-bottom: 30px;
}

.claude-logo {
    width: 48px;
    height: 48px;
}

.art-section {
    margin-bottom: 30px;
    width: 100%;
    display: flex;
    justify-content: center;
}

.welcome-art {
    max-width: 80%;
    height: auto;
    opacity: 0.8;
}

.text-section h2 {
    font-size: 1.2em;
    font-weight: 600;
    margin-bottom: 10px;
    color: var(--vscode-foreground);
}

.text-section p {
    font-size: 0.9em;
    color: var(--vscode-descriptionForeground);
    max-width: 320px;
    margin: 0 auto;
    line-height: 1.5;
}
</style>
