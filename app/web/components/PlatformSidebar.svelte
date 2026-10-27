<script lang="ts">
  import { ALL_PLATFORMS } from "../lib/state.svelte";

  let {
    items,
    total,
    selected,
    onSelect,
  }: {
    items: { name: string; count: number }[];
    total: number;
    selected: string;
    onSelect: (name: string) => void;
  } = $props();
</script>

<aside class="sidebar">
  <div class="section-title">平台</div>
  <div class="list">
    <button
      class="row"
      class:active={selected === ALL_PLATFORMS}
      onclick={() => onSelect(ALL_PLATFORMS)}
    >
      <span class="name">{ALL_PLATFORMS}</span>
      <span class="count">{total}</span>
    </button>
    {#each items as item (item.name)}
      <button class="row" class:active={selected === item.name} onclick={() => onSelect(item.name)}>
        <span class="name" title={item.name}>{item.name}</span>
        <span class="count">{item.count}</span>
      </button>
    {/each}
  </div>
</aside>

<style>
  .sidebar {
    width: 210px;
    flex: none;
    background: var(--surface);
    border: 1px solid var(--border);
    padding: 8px;
    display: flex;
    flex-direction: column;
    min-height: 0;
  }

  .section-title {
    padding: 2px 4px 8px;
  }

  .list {
    overflow: auto;
    flex: 1;
  }

  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    width: 100%;
    padding: 4px 8px;
    margin: 0;
    border-radius: 3px;
    min-height: 24px;
    gap: 8px;
  }

  .row.active {
    background: var(--selected);
  }

  .row:hover {
    background: var(--hover);
  }

  .name {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .count {
    color: var(--text2);
    flex: none;
  }
</style>
