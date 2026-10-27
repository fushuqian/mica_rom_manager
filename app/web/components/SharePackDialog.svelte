<script lang="ts">
  import { open as openDialog } from "@tauri-apps/plugin-dialog";
  import { revealItemInDir } from "@tauri-apps/plugin-opener";
  import { api, type Game, type SharePackResult, type ShareRow } from "../lib/api";
  import { formatSize } from "../lib/format";

  let {
    games,
    defaults,
    onClose,
  }: {
    games: Game[];
    defaults: { exportDir: string; saveDir: string; packageName: string };
    onClose: () => void;
  } = $props();

  let rows = $state<ShareRow[]>([]);
  let packageName = $state(defaults.packageName);
  let exportDir = $state(defaults.exportDir);
  let saveDir = $state(defaults.saveDir);
  let includeEmulators = $state(true);
  let includeSaves = $state(false);
  let createZip = $state(false);
  let estimate = $state(0);
  let busy = $state(false);
  let error = $state("");
  let result = $state<SharePackResult | null>(null);

  const gameIds = games.map((g) => g.id);

  async function refreshEstimate() {
    try {
      const value = await api.shareEstimate(
        gameIds,
        includeEmulators,
        includeSaves && saveDir.trim() ? saveDir.trim() : null
      );
      estimate = value.bytes;
    } catch {
      estimate = 0;
    }
  }

  $effect(() => {
    includeEmulators;
    includeSaves;
    void refreshEstimate();
  });

  async function pickExportDir() {
    const picked = await openDialog({ title: "选择导出位置", directory: true, multiple: false });
    if (typeof picked === "string" && picked) exportDir = picked;
  }

  async function pickSaveDir() {
    const picked = await openDialog({ title: "选择存档目录", directory: true, multiple: false });
    if (typeof picked === "string" && picked) saveDir = picked;
  }

  async function start() {
    if (!exportDir.trim()) {
      error = "请先选择导出位置。";
      return;
    }
    if (!packageName.trim()) {
      error = "请填写分享包名称。";
      return;
    }
    busy = true;
    error = "";
    result = null;
    try {
      result = await api.shareExport({
        gameIds,
        exportDir: exportDir.trim(),
        packageName: packageName.trim(),
        includeEmulators,
        includeSaves,
        saveDir: includeSaves && saveDir.trim() ? saveDir.trim() : null,
        createZip,
      });
    } catch (e) {
      error = String(e);
    } finally {
      busy = false;
    }
  }

  $effect(() => {
    void (async () => {
      try {
        rows = await api.shareRows(gameIds);
      } catch (e) {
        error = String(e);
      }
    })();
  });
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:780px;height:700px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>分享打包（已选 {games.length} 个游戏）</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      {#if error}<div class="error">{error}</div>{/if}

      <div class="grid">
        <label>分享包名称</label>
        <input type="text" bind:value={packageName} />
        <label>导出位置</label>
        <div class="inline">
          <input type="text" bind:value={exportDir} placeholder="例如 E:\exports" />
          <button class="outline" onclick={pickExportDir}>浏览…</button>
        </div>
      </div>

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th style="width:240px">游戏</th>
              <th style="width:120px">平台</th>
              <th style="width:180px">模拟器</th>
              <th>说明</th>
            </tr>
          </thead>
          <tbody>
            {#each rows as row, index (index)}
              <tr>
                <td class="ellipsis" title={row.title}>{row.title}</td>
                <td>{row.platform}</td>
                <td>{row.emulatorName}</td>
                <td class:warn={row.note}>{row.note || "可打包"}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>

      <div class="options">
        <label class="check">
          <input type="checkbox" bind:checked={includeEmulators} />
          一并打包模拟器（含核心与配置，目标电脑无需自备）
        </label>
        <label class="check">
          <input type="checkbox" bind:checked={includeSaves} />
          一并打包存档
        </label>
        {#if includeSaves}
          <div class="inline indent">
            <input type="text" bind:value={saveDir} placeholder="存档目录，例如 E:\Saves" />
            <button class="outline" onclick={pickSaveDir}>浏览…</button>
          </div>
        {/if}
        <label class="check">
          <input type="checkbox" bind:checked={createZip} />
          打包完成后压缩为 zip
        </label>
      </div>

      <div class="summary">
        预计大小：{formatSize(estimate)}（打包时会复制 ROM、模拟器与存档，请预留空间）
      </div>

      {#if result}
        <div class="result">
          <div>
            已生成：<span class="mono">{result.packageDir}</span>
            （{formatSize(result.totalBytes)}，{result.launchers.length} 个启动器）
          </div>
          {#if result.zipPath}
            <div>压缩包：<span class="mono">{result.zipPath}</span></div>
          {/if}
          {#if result.warnings.length}
            <div class="warn block">警告：</div>
            {#each result.warnings as warning (warning)}
              <div class="warn">· {warning}</div>
            {/each}
          {/if}
          <button class="outline" onclick={() => void revealItemInDir(result!.packageDir)}>打开导出目录</button>
        </div>
      {/if}
    </div>
    <div class="modal-foot">
      <span class="note">{result ? "已完成" : "生成的内容可直接拷给朋友，双击 Play-*.bat 启动。"}</span>
      <button class="primary" onclick={start} disabled={busy}>{busy ? "正在打包…" : "开始打包"}</button>
      <button class="outline" onclick={onClose}>关闭</button>
    </div>
  </div>
</div>

<style>
  .grid {
    display: grid;
    grid-template-columns: 96px 1fr;
    gap: 9px 12px;
    align-items: center;
    margin-bottom: 10px;
  }

  .grid > label {
    color: var(--text2);
  }

  .inline {
    display: flex;
    gap: 8px;
  }

  .inline > input {
    flex: 1;
  }

  .inline.indent {
    margin-left: 20px;
  }

  .table-wrap {
    flex: 1;
    min-height: 120px;
    overflow: auto;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
  }

  table {
    width: 100%;
    border-collapse: collapse;
    table-layout: fixed;
  }

  th {
    position: sticky;
    top: 0;
    height: 28px;
    padding: 0 8px;
    text-align: left;
    font-weight: 600;
    background: var(--header-bg);
    border-bottom: 1px solid var(--border);
  }

  td {
    height: 26px;
    padding: 0 8px;
    border-bottom: 1px solid var(--grid-line);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .options {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-top: 10px;
  }

  .check {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .summary {
    margin-top: 8px;
    color: var(--text2);
    font-size: 12px;
  }

  .result {
    margin-top: 10px;
    padding: 8px 10px;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: #f7f9fb;
    display: flex;
    flex-direction: column;
    gap: 6px;
    font-size: 12px;
    align-items: flex-start;
  }

  .mono {
    font-family: Consolas, monospace;
  }

  .warn {
    color: #8a6d1d;
  }

  .warn.block {
    font-weight: 600;
  }

  .note {
    flex: 1;
    color: var(--text2);
    font-size: 12px;
  }

  .ellipsis {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .error {
    margin-bottom: 8px;
    padding: 6px 10px;
    border: 1px solid #f0c7c0;
    background: #fdf2f0;
    color: var(--danger);
    border-radius: 4px;
  }
</style>
