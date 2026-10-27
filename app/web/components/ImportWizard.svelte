<script lang="ts">
  import { open } from "@tauri-apps/plugin-dialog";
  import { api, type ImportCandidate, type ImportOutcome } from "../lib/api";
  import { formatSize } from "../lib/format";
  import { store } from "../lib/state.svelte";

  let {
    initialSources = [],
    libraryRoot = "",
    onClose,
    onImported,
  }: {
    initialSources?: string[];
    libraryRoot?: string;
    onClose: () => void;
    onImported: (outcome: ImportOutcome) => void;
  } = $props();

  let sources = $state<string[]>([...initialSources]);
  let candidates = $state<ImportCandidate[]>([]);
  let platformOptions = $state<string[]>([]);
  let duplicates = $state(0);
  let excluded = $state(0);
  let formatSkipped = $state(0);
  let recursive = $state(true);
  let mode = $state<"Link" | "Copy" | "Move">("Link");
  let bulkPlatform = $state("");
  let busy = $state(false);
  let scanning = $state(false);
  let error = $state("");
  let dragging = $state(false);

  let hasLibrary = $derived(libraryRoot.trim().length > 0);
  let summary = $derived(
    candidates.length === 0
      ? sources.length === 0
        ? "拖放或添加 ROM 文件 / 文件夹"
        : "没有可导入的 ROM"
      : `共 ${candidates.length} 个文件，其中 ${candidates.filter((c) => c.needsSpecify).length} 个需要指定平台；已存在 ${duplicates} 个，忽略 ${excluded} 个`
  );

  let scanToken = 0;
  let debounce: ReturnType<typeof setTimeout> | undefined;

  async function runScan() {
    if (sources.length === 0) {
      candidates = [];
      duplicates = 0;
      excluded = 0;
      formatSkipped = 0;
      return;
    }
    const token = ++scanToken;
    scanning = true;
    busy = true;
    error = "";
    try {
      const result = await api.importScan(sources, recursive, null, null);
      if (token !== scanToken) return;
      candidates = result.candidates;
      duplicates = result.duplicateCount;
      excluded = result.excludedCount;
      formatSkipped = result.formatSkippedCount;
      for (const candidate of candidates) {
        if (candidate.platform && !platformOptions.includes(candidate.platform)) {
          platformOptions = [...platformOptions, candidate.platform];
        }
      }
    } catch (e) {
      if (token === scanToken) error = String(e);
    } finally {
      if (token === scanToken) {
        scanning = false;
        busy = false;
      }
    }
  }

  function scheduleScan() {
    if (debounce) clearTimeout(debounce);
    debounce = setTimeout(() => void runScan(), 150);
  }

  $effect(() => {
    sources.length;
    recursive;
    scheduleScan();
  });

  async function addFiles() {
    const extensions = await api.importSupportedExtensions();
    const picked = await open({
      title: "选择 ROM 文件",
      multiple: true,
      filters: [{ name: "ROM 文件", extensions }],
    });
    if (!picked) return;
    const list = Array.isArray(picked) ? picked : [picked];
    for (const path of list) addSource(path);
  }

  async function addFolder() {
    const picked = await open({ title: "选择 ROM 文件夹", directory: true, multiple: false });
    if (typeof picked === "string" && picked) addSource(picked);
  }

  function addSource(path: string) {
    if (!path || sources.includes(path)) return;
    sources = [...sources, path];
  }

  function removeSource(path: string) {
    sources = sources.filter((s) => s !== path);
  }

  function clearSources() {
    sources = [];
    candidates = [];
    duplicates = 0;
    excluded = 0;
    formatSkipped = 0;
  }

  function applyToAll() {
    const platform = bulkPlatform.trim();
    if (!platform) return;
    candidates = candidates.map((c) => ({ ...c, platform, needsSpecify: false }));
  }

  function onDragOver(event: DragEvent) {
    event.preventDefault();
    dragging = true;
  }

  function onDragLeave() {
    dragging = false;
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    dragging = false;
    const files = Array.from(event.dataTransfer?.files ?? []);
    for (const file of files) {
      const path = (file as File & { path?: string }).path;
      if (path) addSource(path);
    }
  }

  async function confirm() {
    if (candidates.length === 0) {
      error = "还没有可导入的 ROM，请先添加文件或文件夹。";
      return;
    }
    const missing = candidates.filter(
      (c) => !c.title.trim() || !c.platform.trim()
    ).length;
    if (missing > 0) {
      error = `还有 ${missing} 条记录的标题或平台为空，请补充后再导入。`;
      return;
    }
    if (mode !== "Link" && !hasLibrary) {
      error = "复制 / 移动需要先设置库目录（设置 → 常规）。";
      return;
    }
    if (scanning) {
      error = "正在扫描，请稍候再试。";
      return;
    }

    busy = true;
    error = "";
    try {
      const items = candidates.map((c) => ({
        filePath: c.filePath,
        title: c.title.trim(),
        platform: c.platform.trim(),
        romHash: c.romHash,
        fileSize: c.fileSize,
      }));
      const outcome = await api.importApply(items, mode);
      onImported(outcome);
    } catch (e) {
      error = String(e);
    } finally {
      busy = false;
    }
  }

  $effect(() => {
    void (async () => {
      if (store.platformNames.length) {
        platformOptions = [...store.platformNames];
      } else {
        try {
          platformOptions = await api.lookupsList("platform");
        } catch {
          platformOptions = [];
        }
      }
      try {
        const all = await api.settingsGetAll();
        const saved = all["library.import_mode"];
        const defaultMode = saved === "copy" ? "Copy" : saved === "move" ? "Move" : "Link";
        mode = hasLibrary ? defaultMode : "Link";
      } catch {
        mode = "Link";
      }
    })();
  });
</script>

<div class="modal-mask" role="presentation">
  <div
    class="modal wizard"
    role="dialog"
    aria-modal="true"
    ondragover={onDragOver}
    ondragleave={onDragLeave}
    ondrop={onDrop}
    class:dragging
  >
    <div class="modal-head">
      <span>导入向导</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>

    <div class="modal-body">
      {#if error}<div class="error">{error}</div>{/if}

      <div class="section-title">1. 添加要导入的 ROM 文件或文件夹</div>
      <div class="source-row">
        <div class="source-list">
          {#each sources as source (source)}
            <div class="source-item">
              <span class="source-path" title={source}>{source}</span>
              <button class="icon" onclick={() => removeSource(source)} title="移除">✕</button>
            </div>
          {/each}
          {#if sources.length === 0}
            <div class="source-empty">拖放文件到此处，或使用右侧按钮添加</div>
          {/if}
        </div>
        <div class="source-actions">
          <button class="outline" onclick={addFiles}>添加文件…</button>
          <button class="outline" onclick={addFolder}>添加文件夹…</button>
          <button class="outline" onclick={clearSources} disabled={sources.length === 0}>清空</button>
        </div>
      </div>

      <div class="summary">{summary}</div>

      <div class="candidate-wrap">
        <table class="candidates">
          <thead>
            <tr>
              <th style="width:240px">文件名</th>
              <th style="width:220px">标题</th>
              <th style="width:170px">平台</th>
              <th style="width:90px">大小</th>
            </tr>
          </thead>
          <tbody>
            {#each candidates as candidate, index (candidate.filePath)}
              <tr>
                <td class="name" title={candidate.filePath}>{candidate.fileName}</td>
                <td>
                  <input
                    type="text"
                    value={candidate.title}
                    oninput={(e) => {
                      candidates[index] = { ...candidate, title: (e.currentTarget as HTMLInputElement).value };
                    }}
                  />
                </td>
                <td class="platform-cell">
                  <input
                    type="text"
                    list="import-platforms"
                    class:needs={candidate.needsSpecify}
                    value={candidate.platform}
                    onchange={(e) => {
                      candidates[index] = {
                        ...candidate,
                        platform: (e.currentTarget as HTMLInputElement).value.trim(),
                        needsSpecify: false,
                      };
                    }}
                  />
                </td>
                <td class="size">{formatSize(candidate.fileSize)}</td>
              </tr>
            {/each}
            {#if candidates.length === 0}
              <tr>
                <td colspan="4" class="empty">
                  {sources.length === 0 ? "还没有添加任何来源" : scanning ? "正在扫描…" : "没有可导入的 ROM"}
                </td>
              </tr>
            {/if}
          </tbody>
        </table>
      </div>

      <div class="bulk-row">
        <span class="field-label">批量指定平台：</span>
        <input type="text" list="import-platforms" bind:value={bulkPlatform} placeholder="选择或输入平台" />
        <button class="outline" onclick={applyToAll} disabled={!bulkPlatform.trim()}>应用到全部</button>
      </div>

      <div class="section-title">2. 选择入库方式</div>
      <div class="mode-list">
        <label class="radio">
          <input type="radio" value="Link" bind:group={mode} name="import-mode" />
          仅链接：只记录路径，不移动也不复制文件
        </label>
        <label class="radio">
          <input type="radio" value="Copy" bind:group={mode} name="import-mode" disabled={!hasLibrary} />
          复制到库目录（原文件保留）
        </label>
        <label class="radio">
          <input type="radio" value="Move" bind:group={mode} name="import-mode" disabled={!hasLibrary} />
          移动到库目录（原位置文件会被移走）
        </label>
        <div class="hint">
          {hasLibrary ? `库目录：${libraryRoot}` : "尚未设置库目录：复制 / 移动不可用，请到「设置 → 常规」配置库目录。"}
        </div>
        <label class="check">
          <input type="checkbox" bind:checked={recursive} />
          包含子文件夹中的 ROM
        </label>
      </div>
    </div>

    <div class="modal-foot">
      <span class="busy-text">{busy ? (scanning ? "正在计算指纹…" : "正在导入…") : ""}</span>
      <button class="primary" onclick={confirm} disabled={busy}>开始导入</button>
      <button class="outline" onclick={onClose}>取消</button>
    </div>
  </div>
</div>

<datalist id="import-platforms">
  {#each platformOptions as option (option)}<option value={option}></option>{/each}
</datalist>

<style>
  .wizard {
    width: 820px;
    height: 660px;
  }

  .wizard.dragging {
    outline: 2px dashed var(--accent);
    outline-offset: -6px;
  }

  .error {
    margin-bottom: 8px;
    padding: 6px 10px;
    border: 1px solid #f0c7c0;
    background: #fdf2f0;
    color: var(--danger);
    border-radius: 4px;
  }

  .section-title {
    font-weight: 600;
    margin: 4px 0 6px;
    color: var(--text1);
  }

  .source-row {
    display: flex;
    gap: 8px;
  }

  .source-list {
    flex: 1;
    height: 96px;
    overflow: auto;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
    padding: 4px;
  }

  .source-item {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 2px 4px;
  }

  .source-item:hover {
    background: var(--hover);
  }

  .source-path {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 12px;
  }

  .source-empty {
    padding: 30px 8px;
    text-align: center;
    color: var(--text2);
  }

  .source-actions {
    display: flex;
    flex-direction: column;
    gap: 6px;
    width: 110px;
  }

  .summary {
    margin: 8px 0 6px;
    color: var(--text2);
    font-size: 12px;
  }

  .candidate-wrap {
    flex: 1;
    min-height: 0;
    overflow: auto;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
  }

  .candidates {
    width: 100%;
    border-collapse: collapse;
    table-layout: fixed;
  }

  .candidates th {
    position: sticky;
    top: 0;
    z-index: 1;
    height: 28px;
    padding: 0 6px;
    text-align: left;
    font-weight: 600;
    background: var(--header-bg);
    border-bottom: 1px solid var(--border);
    border-right: 1px solid var(--grid-line);
  }

  .candidates td {
    height: 26px;
    padding: 0 6px;
    border-bottom: 1px solid var(--grid-line);
    border-right: 1px solid var(--grid-line);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .candidates input {
    width: 100%;
    height: 22px;
    border: 1px solid transparent;
    background: transparent;
    padding: 0 4px;
    font-size: 12px;
  }

  .candidates input:hover,
  .candidates input:focus {
    border-color: var(--border);
    background: #fff;
  }

  .candidates input.needs {
    color: var(--danger);
  }

  .name,
  .size {
    color: var(--text2);
    font-size: 12px;
  }

  .empty {
    text-align: center;
    color: var(--text2);
    padding: 24px 0;
  }

  .bulk-row {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 10px 0;
  }

  .bulk-row input {
    width: 180px;
  }

  .field-label {
    color: var(--text2);
  }

  .mode-list {
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding-bottom: 4px;
  }

  .radio,
  .check {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .hint {
    margin-left: 20px;
    color: var(--text2);
    font-size: 12px;
  }

  .busy-text {
    flex: 1;
    color: var(--text2);
    font-size: 12px;
  }

  .modal-foot {
    align-items: center;
  }

  .icon {
    width: 18px;
    height: 18px;
    padding: 0;
    line-height: 1;
    border: 1px solid transparent;
    background: transparent;
    color: var(--text2);
  }

  .icon:hover {
    color: var(--danger);
    border-color: var(--border);
  }
</style>
