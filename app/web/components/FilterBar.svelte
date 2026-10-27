<script lang="ts">
  import { store } from "../lib/state.svelte";

  let {
    statusOptions,
    regionOptions,
    languageOptions,
    developerOptions,
    tagOptions,
    countText,
    onClear,
    onEditMode,
  }: {
    statusOptions: string[];
    regionOptions: string[];
    languageOptions: string[];
    developerOptions: string[];
    tagOptions: string[];
    countText: string;
    onClear: () => void;
    onEditMode: (on: boolean) => void;
  } = $props();

  let searchBox: HTMLInputElement;
  $effect(() => {
    if (searchBox) searchBox.value = store.search;
  });

  export function focusSearch() {
    searchBox?.focus();
    searchBox?.select();
  }
</script>

<div class="filter-bar">
  <input
    bind:this={searchBox}
    type="text"
    style="width:220px"
    placeholder="搜索…"
    title="搜索标题、文件名、路径、标签、开发商（Ctrl+F）"
    oninput={(e) => (store.search = (e.currentTarget as HTMLInputElement).value)}
  />
  <select bind:value={store.status} title="状态">
    {#each statusOptions as option (option)}<option>{option}</option>{/each}
  </select>
  <select bind:value={store.region} title="区域">
    {#each regionOptions as option (option)}<option>{option}</option>{/each}
  </select>
  <select bind:value={store.language} title="语言">
    {#each languageOptions as option (option)}<option>{option}</option>{/each}
  </select>
  <select bind:value={store.developer} title="开发商" style="max-width:150px">
    {#each developerOptions as option (option)}<option>{option}</option>{/each}
  </select>
  <select bind:value={store.tag} title="标签" style="max-width:130px">
    {#each tagOptions as option (option)}<option>{option}</option>{/each}
  </select>
  <label class="check" title="开启后可直接在列表里修改单元格内容，回车或点击其他格即保存">
    <input
      type="checkbox"
      checked={store.editMode}
      onchange={(e) => onEditMode((e.currentTarget as HTMLInputElement).checked)}
    />编辑模式
  </label>
  <label class="check"><input type="checkbox" bind:checked={store.onlyFavorites} />仅收藏</label>
  <label class="check"><input type="checkbox" bind:checked={store.onlyMissing} />仅缺失文件</label>
  <button onclick={onClear}>清除筛选</button>
  <div class="spacer"></div>
  <span class="muted">{countText}</span>
</div>

<style>
  .filter-bar {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px 8px;
    padding-bottom: 8px;
  }

  select {
    height: 26px;
    padding: 2px 4px;
  }

  input[type="text"] {
    height: 26px;
  }

  .check {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    cursor: default;
    white-space: nowrap;
  }
</style>
