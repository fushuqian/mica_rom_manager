<script lang="ts">
  import { ALL_PLATFORMS, ALL_STATUSES, ALL_TAGS, store } from "../lib/state.svelte";

  let { platformCount, filteredCount, busy }: { platformCount: number; filteredCount: number; busy: boolean } =
    $props();
</script>

<footer class="status-bar">
  <span class="path" title={store.info?.dbPath ?? ""}>数据：{store.info?.dbPath ?? ""}</span>
  <span class="message">{store.statusMessage}</span>
  <span class="spacer"></span>
  {#if busy}<span class="spinner"></span>{/if}
  <span class="muted">{store.lastRefresh}</span>
</footer>

<style>
  .status-bar {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 8px 12px;
    background: var(--surface);
    border-top: 1px solid var(--border);
    font-size: 12px;
    color: var(--text2);
  }

  .path {
    max-width: 520px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .message {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .spinner {
    width: 14px;
    height: 14px;
    border: 2px solid var(--border);
    border-top-color: var(--accent);
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
</style>
