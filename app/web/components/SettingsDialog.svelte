<script lang="ts">
  import { ask as askDialog, open as openDialog, save as saveDialog } from "@tauri-apps/plugin-dialog";
  import { openPath as openLocalPath, openUrl, revealItemInDir } from "@tauri-apps/plugin-opener";
  import { onMount } from "svelte";
  import EmulatorEditDialog from "./EmulatorEditDialog.svelte";
  import {
    api,
    type AppInfo,
    type Emulator,
    type InstallOutcome,
    type MaintenanceStats,
    type PlatformSetting,
    type ReleaseAsset,
    type ReleaseInfo,
  } from "../lib/api";
  import { formatSize } from "../lib/format";
  import { store } from "../lib/state.svelte";

  let { onClose, onDataChanged }: { onClose: () => void; onDataChanged: () => void } = $props();

  type TabKey = "general" | "emulators" | "lookups" | "platforms" | "maintenance" | "about";
  let tab = $state<TabKey>("general");
  let busy = $state(false);
  let messages = $state<Record<string, string>>({});

  function note(section: string, text: string) {
    messages = { ...messages, [section]: text };
  }

  // ---------- 常规 ----------
  let info = $state<AppInfo | null>(null);
  let libraryRoot = $state("");
  let importMode = $state("link");
  let portable = $state(false);
  let proxyMode = $state("system");
  let proxyAddress = $state("127.0.0.1:7890");

  async function loadGeneral() {
    info = await api.appInfo();
    libraryRoot = info.libraryRoot;
    portable = info.portable;
    const all = await api.settingsGetAll();
    importMode = all["library.import_mode"] || "link";
    proxyMode = all["network.proxy.mode"] || "system";
    proxyAddress = all["network.proxy.address"] || "127.0.0.1:7890";
  }

  async function browseLibraryRoot() {
    const picked = await openDialog({ title: "选择 ROM 库目录", directory: true, multiple: false });
    if (typeof picked === "string" && picked) libraryRoot = picked;
  }

  async function saveGeneral() {
    if (busy) return;
    busy = true;
    try {
      info = await api.settingsSaveGeneral({
        libraryRoot: libraryRoot.trim(),
        portable,
        importMode,
        proxyMode,
        proxyAddress: proxyAddress.trim(),
      });
      libraryRoot = info.libraryRoot;
      note("general", "常规设置已保存。");
      onDataChanged();
    } catch (e) {
      note("general", String(e));
    } finally {
      busy = false;
    }
  }

  async function openDataDir() {
    if (!info) return;
    try {
      await revealItemInDir(info.dbPath);
    } catch (e) {
      note("general", `打开数据目录失败：${e}`);
    }
  }

  // ---------- 模拟器 ----------
  let emulators = $state<Emulator[]>([]);
  let editingEmulator = $state<Emulator | null>(null);
  let selectedEmulatorId = $state(0);

  async function loadEmulators(selectId?: number) {
    emulators = await api.emulatorsList();
    if (selectId) selectedEmulatorId = selectId;
  }

  async function refreshEmulatorList(selectId?: number) {
    await loadEmulators(selectId);
    await store.reloadLookups();
    onDataChanged();
  }

  function newEmulator() {
    editingEmulator = {
      id: 0,
      name: "",
      exePath: "",
      argsTemplate: "{rom}",
      corePath: "",
      supportedExt: "",
      version: "",
      installDir: "",
      configPath: "",
      githubRepo: "",
      websiteUrl: "",
      notes: "",
      enabled: true,
      platforms: [],
      defaultPlatforms: [],
      available: false,
    };
  }

  async function deleteEmulator(emulator: Emulator) {
    if (!(await confirmDelete(emulator.name))) return;
    try {
      await api.emulatorDelete(emulator.id);
      await refreshEmulatorList();
      note("emulators", `已删除模拟器「${emulator.name}」`);
    } catch (e) {
      note("emulators", String(e));
    }
  }

  async function confirmDelete(name: string): Promise<boolean> {
    return askDialog(`删除模拟器「${name}」？\n\n只删除配置，不会删除已安装的程序文件。`, {
      title: "删除模拟器",
      kind: "warning",
    });
  }

  async function setDefaultEmulator(emulator: Emulator) {
    try {
      await api.emulatorSetDefault(emulator.id);
      await refreshEmulatorList(emulator.id);
      note("emulators", `已把「${emulator.name}」设为绑定平台的默认模拟器`);
    } catch (e) {
      note("emulators", String(e));
    }
  }

  async function normalizeEmulatorPaths() {
    try {
      const count = await api.emulatorsNormalizePaths();
      await loadEmulators();
      note("emulators", `已转换 ${count} 个模拟器路径`);
    } catch (e) {
      note("emulators", String(e));
    }
  }

  function currentEmulator(): Emulator | undefined {
    return emulators.find((e) => e.id === selectedEmulatorId);
  }

  async function testLaunch() {
    const emulator = currentEmulator();
    if (!emulator) return;
    try {
      note("emulators", await api.emulatorTestLaunch(emulator.id));
    } catch (e) {
      note("emulators", String(e));
    }
  }

  async function openInstallDir() {
    const emulator = currentEmulator();
    if (!emulator?.installDir) {
      note("emulators", "该模拟器还没有安装目录。");
      return;
    }
    try {
      await openLocalPath(emulator.installDir);
    } catch (e) {
      note("emulators", `打开安装目录失败：${e}`);
    }
  }

  async function openWebsite() {
    const emulator = currentEmulator();
    const url =
      emulator?.websiteUrl?.trim() ||
      (emulator?.githubRepo?.trim() ? `https://github.com/${emulator.githubRepo.trim()}` : "");
    if (!url) {
      note("emulators", "该模拟器还没有配置官网或 GitHub 仓库。");
      return;
    }
    try {
      await openUrl(url);
    } catch (e) {
      note("emulators", `打开网页失败：${e}`);
    }
  }

  async function finishInstall(emulator: Emulator, outcome: InstallOutcome) {
    const exe = outcome.exeCandidates[0] ?? "";
    await api.emulatorApplyInstall(emulator.id, outcome.installDir, exe);
    await refreshEmulatorList(emulator.id);
    note(
      "emulators",
      exe
        ? `已安装「${emulator.name}」：${exe}`
        : `已解压「${emulator.name}」到 ${outcome.installDir}，未找到 exe，请手动指定。`
    );
  }

  async function installFromFolder() {
    const emulator = currentEmulator();
    if (!emulator) {
      note("emulators", "请先在左侧选择一个模拟器。");
      return;
    }
    const picked = await openDialog({ title: "选择模拟器文件夹", directory: true, multiple: false });
    if (typeof picked !== "string" || !picked) return;
    busy = true;
    note("emulators", "正在安装…");
    try {
      const outcome = await api.emulatorInstallFromFolder(picked, emulator.name);
      await finishInstall(emulator, outcome);
    } catch (e) {
      note("emulators", String(e));
    } finally {
      busy = false;
    }
  }

  async function installFromArchive() {
    const emulator = currentEmulator();
    if (!emulator) {
      note("emulators", "请先在左侧选择一个模拟器。");
      return;
    }
    const picked = await openDialog({
      title: "选择模拟器压缩包",
      multiple: false,
      filters: [{ name: "压缩包", extensions: ["zip", "7z", "rar", "gz", "tgz"] }],
    });
    if (typeof picked !== "string" || !picked) return;
    busy = true;
    note("emulators", "正在解压安装…");
    try {
      const outcome = await api.emulatorInstallFromArchive(picked, emulator.name);
      await finishInstall(emulator, outcome);
    } catch (e) {
      note("emulators", String(e));
    } finally {
      busy = false;
    }
  }

  let updateInfo = $state<ReleaseInfo | null>(null);

  async function checkUpdate() {
    const emulator = currentEmulator();
    if (!emulator) return;
    if (!emulator.githubRepo.trim()) {
      note("emulators", "该模拟器还没有填写 GitHub 仓库（owner/repo）。");
      return;
    }
    busy = true;
    note("emulators", "正在检查更新…");
    try {
      updateInfo = await api.emulatorCheckUpdate(emulator.githubRepo.trim());
      note(
        "emulators",
        updateInfo.assets.length
          ? `最新版本 ${updateInfo.tag}，共 ${updateInfo.assets.length} 个下载文件。`
          : `最新版本 ${updateInfo.tag}，但没有可下载的附件。`
      );
    } catch (e) {
      note("emulators", String(e));
    } finally {
      busy = false;
    }
  }

  async function installReleaseAsset(asset: ReleaseAsset) {
    const emulator = currentEmulator();
    if (!emulator || !updateInfo) return;
    busy = true;
    note("emulators", `正在下载 ${asset.name}…`);
    try {
      const downloaded = await api.emulatorDownloadAsset(asset.downloadUrl, asset.name);
      note("emulators", "下载完成，正在解压安装…");
      const outcome = await api.emulatorInstallFromArchive(downloaded, emulator.name);
      emulator.version = updateInfo.tag.replace(/^v/i, "");
      await api.emulatorSave(emulator);
      updateInfo = null;
      await finishInstall(emulator, outcome);
    } catch (e) {
      note("emulators", String(e));
    } finally {
      busy = false;
    }
  }

  // ---------- 字典库 ----------
  let kinds = $state<{ key: string; title: string }[]>([]);
  let currentKind = $state("platform");
  let lookupValues = $state<string[]>([]);
  let selectedValue = $state("");
  let lookupInput = $state("");

  async function loadKinds() {
    kinds = await api.lookupsKinds();
    await loadValues();
  }

  async function loadValues() {
    lookupValues = await api.lookupsList(currentKind);
    selectedValue = "";
  }

  async function selectKind(key: string) {
    currentKind = key;
    await loadValues();
  }

  async function addValue() {
    try {
      const result = await api.lookupsAdd(currentKind, lookupInput);
      note("lookups", result.message);
      if (!result.success) return;
      lookupInput = "";
      await loadValues();
      await afterLookupChange();
    } catch (e) {
      note("lookups", String(e));
    }
  }

  async function renameValue() {
    try {
      const result = await api.lookupsRename(currentKind, selectedValue, lookupInput);
      note("lookups", result.message);
      if (!result.success) return;
      lookupInput = "";
      await loadValues();
      await afterLookupChange();
    } catch (e) {
      note("lookups", String(e));
    }
  }

  async function deleteValue() {
    if (!selectedValue) {
      note("lookups", "请先在右侧列表选中要删除的值。");
      return;
    }
    try {
      const result = await api.lookupsDelete(currentKind, selectedValue);
      note("lookups", result.message);
      if (!result.success) return;
      await loadValues();
      await afterLookupChange();
    } catch (e) {
      note("lookups", String(e));
    }
  }

  async function afterLookupChange() {
    await store.reloadLookups();
    if (tab === "platforms") await loadPlatforms();
    if (tab === "emulators") await loadEmulators();
    onDataChanged();
  }

  // ---------- 平台 ----------
  let platforms = $state<PlatformSetting[]>([]);
  let selectedPlatform = $state("");
  let platformDraft = $state<PlatformSetting | null>(null);

  async function loadPlatforms(select?: string) {
    platforms = await api.platformsList();
    const name = select ?? selectedPlatform;
    const match = platforms.find((p) => p.name === name) ?? platforms[0];
    selectedPlatform = match?.name ?? "";
    platformDraft = match ? structuredClone(match) : null;
  }

  function selectPlatform(setting: PlatformSetting) {
    selectedPlatform = setting.name;
    platformDraft = structuredClone(setting);
  }

  async function savePlatform() {
    if (!platformDraft) return;
    try {
      await api.platformSave(platformDraft);
      note("platforms", `已保存平台「${platformDraft.name}」的设置`);
      await loadPlatforms(platformDraft.name);
      onDataChanged();
    } catch (e) {
      note("platforms", String(e));
    }
  }

  async function scanPlatform() {
    if (!platformDraft) return;
    busy = true;
    note("platforms", "正在扫描平台库…");
    try {
      const result = await api.platformScan(platformDraft.name, platformDraft.formats);
      if (result.candidates.length > 0) {
        const items = result.candidates.map((c) => ({
          filePath: c.filePath,
          title: c.title,
          platform: platformDraft!.name,
          romHash: c.romHash,
          fileSize: c.fileSize,
        }));
        const confirmed = await askDialog(
          `发现 ${items.length} 个未登记 ROM，补充入库？\n\n目录：${result.dir}\n已存在 ${result.duplicateCount} 个，忽略 ${result.excludedCount} 个，格式不匹配 ${result.formatSkippedCount} 个，清理失效 ${result.removed} 条。`,
          { title: "扫描平台库", kind: "info" }
        );
        if (confirmed) {
          const outcome = await api.importApply(items, "Link");
          note("platforms", `已补充 ${outcome.imported} 条，失败 ${outcome.failed} 条，清理失效 ${result.removed} 条。`);
          await store.reloadGames();
        } else {
          note("platforms", `已取消；清理失效 ${result.removed} 条。`);
        }
      } else {
        note(
          "platforms",
          `没有需要补充的 ROM（目录：${result.dir}）。已存在 ${result.duplicateCount} 个，忽略 ${result.excludedCount} 个，格式不匹配 ${result.formatSkippedCount} 个，清理失效 ${result.removed} 条。`
        );
      }
      onDataChanged();
    } catch (e) {
      note("platforms", String(e));
    } finally {
      busy = false;
    }
  }

  async function organizePlatform() {
    if (!platformDraft) return;
    const confirmed = await askDialog(
      `按「标题 (区域)(语言)」重命名「${platformDraft.name}」子库内的文件？\n\n只处理库目录内的文件，链接入库的文件不会动。`,
      { title: "整理平台库", kind: "warning" }
    );
    if (!confirmed) return;
    busy = true;
    note("platforms", "正在整理平台库…");
    try {
      const result = await api.platformOrganize(platformDraft.name);
      note(
        "platforms",
        `整理完成：重命名 ${result.renamed} 个，跳过 ${result.skipped} 条，失败 ${result.failed} 个。`
      );
      await store.reloadGames();
      onDataChanged();
    } catch (e) {
      note("platforms", String(e));
    } finally {
      busy = false;
    }
  }

  // ---------- 数据维护 ----------
  let stats = $state<MaintenanceStats | null>(null);

  async function loadStats() {
    try {
      stats = await api.maintenanceStats();
    } catch (e) {
      note("maintenance", String(e));
    }
  }

  async function backupDb() {
    const path = await saveDialog({
      title: "备份数据库",
      defaultPath: `mica-backup-${new Date().toISOString().slice(0, 10)}.db`,
      filters: [{ name: "SQLite 数据库", extensions: ["db"] }],
    });
    if (typeof path !== "string" || !path) return;
    try {
      await api.maintenanceBackupDb(path);
      note("maintenance", `已备份到：${path}`);
    } catch (e) {
      note("maintenance", String(e));
    }
  }

  async function restoreDb() {
    const path = await openDialog({
      title: "选择备份数据库",
      multiple: false,
      filters: [{ name: "SQLite 数据库", extensions: ["db"] }],
    });
    if (typeof path !== "string" || !path) return;
    const confirmed = await askDialog(
      "用所选备份覆盖当前数据库？\n\n建议先备份当前数据库，恢复后建议重启程序。",
      { title: "恢复数据库", kind: "warning" }
    );
    if (!confirmed) return;
    try {
      await api.maintenanceRestoreDb(path);
      note("maintenance", "数据库已恢复，建议重启程序生效。");
      await loadStats();
      onDataChanged();
    } catch (e) {
      note("maintenance", String(e));
    }
  }

  async function exportCsv() {
    const path = await saveDialog({
      title: "导出 CSV",
      defaultPath: `rom-library-${new Date().toISOString().slice(0, 10)}.csv`,
      filters: [{ name: "CSV 文件", extensions: ["csv"] }],
    });
    if (typeof path !== "string" || !path) return;
    try {
      const count = await api.maintenanceExportCsv(path);
      note("maintenance", `已导出 ${count} 条记录：${path}`);
    } catch (e) {
      note("maintenance", String(e));
    }
  }

  async function cleanMissing() {
    if (!stats) return;
    const confirmed = await askDialog(
      `发现 ${stats.missing} 条记录的 ROM 文件不存在，移除这些记录？\n\n只删数据库记录，不动文件。`,
      { title: "清理失效记录", kind: "question" }
    );
    if (!confirmed) return;
    try {
      const removed = await api.maintenanceCleanMissing();
      note("maintenance", `已清理 ${removed} 条失效记录。`);
      await loadStats();
      onDataChanged();
    } catch (e) {
      note("maintenance", String(e));
    }
  }

  async function hashMissing() {
    busy = true;
    note("maintenance", "正在补算指纹…");
    try {
      const [computed, skipped] = await api.maintenanceHashMissing();
      note("maintenance", `指纹补算完成：${computed} 条，跳过 ${skipped} 条。`);
      await loadStats();
      onDataChanged();
    } catch (e) {
      note("maintenance", String(e));
    } finally {
      busy = false;
    }
  }

  onMount(() => {
    void (async () => {
      try {
        await loadGeneral();
        await loadEmulators();
        await loadKinds();
        await loadPlatforms();
        await loadStats();
      } catch (e) {
        note("general", String(e));
      }
    })();
  });
</script>

<div class="modal-mask" role="presentation">
  <div class="modal settings" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>设置</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>

    <div class="tabs" role="tablist">
      {#each [
        ["general", "常规"],
        ["emulators", "模拟器管理"],
        ["lookups", "字典库"],
        ["platforms", "平台"],
        ["maintenance", "数据维护"],
        ["about", "关于"],
      ] as [key, label] (key)}
        <button class="tab" class:active={tab === key} role="tab" onclick={() => (tab = key as TabKey)}>
          {label}
        </button>
      {/each}
    </div>

    <div class="pane">
      {#if tab === "general"}
        <div class="grid">
          <label>数据目录</label>
          <div class="inline">
            <span class="mono ellipsis" title={info?.dataDir ?? ""}>{info?.dataDir ?? ""}</span>
            <button class="outline" onclick={openDataDir}>打开</button>
          </div>

          <label>导入策略</label>
          <select bind:value={importMode}>
            <option value="link">仅链接（只记录路径）</option>
            <option value="copy">复制到库目录</option>
            <option value="move">移动到库目录</option>
          </select>

          <label>库目录</label>
          <div class="inline">
            <input type="text" bind:value={libraryRoot} placeholder="例如 E:\game_library" />
            <button class="outline" onclick={browseLibraryRoot}>浏览…</button>
          </div>

          <label>便携模式</label>
          <div class="stack">
            <label class="check">
              <input type="checkbox" bind:checked={portable} />
              启用便携模式
            </label>
            <div class="hint">
              程序 exe 与库文件夹放在同一盘时，库目录、ROM / 碟片 / 封面、模拟器路径都存为相对路径，换电脑盘符变化仍可用。
            </div>
          </div>

          <label>网络代理</label>
          <select bind:value={proxyMode}>
            <option value="system">使用系统代理</option>
            <option value="none">不使用代理</option>
            <option value="manual">手动指定</option>
          </select>

          <label>代理地址</label>
          <input type="text" bind:value={proxyAddress} disabled={proxyMode !== "manual"} placeholder="127.0.0.1:7890" />
        </div>
        <div class="hint block">
          数据目录内含 mica.db、covers、emulators、downloads、exports、logs。代理设置同时作用于 GitHub 检查更新与安装包下载。
        </div>
        <div class="pane-foot">
          <span class="note">{messages.general ?? ""}</span>
          <button class="primary" onclick={saveGeneral} disabled={busy}>保存常规设置</button>
        </div>
      {/if}

      {#if tab === "emulators"}
        <div class="split">
          <div class="listbox">
            {#each emulators as emulator (emulator.id)}
              <button
                class="list-item"
                class:active={selectedEmulatorId === emulator.id}
                onclick={() => (selectedEmulatorId = emulator.id)}
                ondblclick={() => (editingEmulator = structuredClone(emulator))}
              >
                <span class="ellipsis">{emulator.name}</span>
                <span class="badge" class:off={!emulator.available}>{emulator.available ? "可用" : "未找到"}</span>
              </button>
            {/each}
            {#if emulators.length === 0}
              <div class="empty">还没有配置模拟器</div>
            {/if}
          </div>
          <div class="list-actions">
            <button class="outline" onclick={newEmulator}>新增…</button>
            <button
              class="outline"
              disabled={!selectedEmulatorId}
              onclick={() => {
                const emulator = emulators.find((e) => e.id === selectedEmulatorId);
                if (emulator) editingEmulator = structuredClone(emulator);
              }}
            >编辑…</button>
            <button
              class="outline"
              disabled={!selectedEmulatorId}
              onclick={() => {
                const emulator = emulators.find((e) => e.id === selectedEmulatorId);
                if (emulator) void setDefaultEmulator(emulator);
              }}
            >设为默认</button>
            <button
              class="outline"
              disabled={!selectedEmulatorId}
              onclick={() => {
                const emulator = emulators.find((e) => e.id === selectedEmulatorId);
                if (emulator) void deleteEmulator(emulator);
              }}
            >删除…</button>
            <button class="outline" onclick={normalizeEmulatorPaths}>转换路径</button>
            <button class="outline" disabled={!selectedEmulatorId} onclick={testLaunch}>运行调试</button>
            <button class="outline" disabled={!selectedEmulatorId} onclick={openInstallDir}>安装目录</button>
            <button class="outline" disabled={!selectedEmulatorId} onclick={openWebsite}>打开官网</button>
            <button class="outline" disabled={!selectedEmulatorId || busy} onclick={installFromFolder}>从文件夹安装…</button>
            <button class="outline" disabled={!selectedEmulatorId || busy} onclick={installFromArchive}>从压缩包安装…</button>
            <button class="outline" disabled={!selectedEmulatorId || busy} onclick={checkUpdate}>检查更新…</button>
          </div>
        </div>
        {#if updateInfo}
          <div class="update-panel">
            <div class="update-head">
              <span class="ellipsis">最新版本 {updateInfo.tag}{updateInfo.name ? ` · ${updateInfo.name}` : ""}</span>
              <button class="outline" onclick={() => (updateInfo = null)}>收起</button>
            </div>
            {#each updateInfo.assets as asset (asset.name)}
              <div class="update-row">
                <span class="ellipsis">{asset.name}</span>
                <span class="size">{formatSize(asset.size)}</span>
                <button class="outline" disabled={busy} onclick={() => installReleaseAsset(asset)}>下载并安装</button>
              </div>
            {/each}
            {#if updateInfo.assets.length === 0}
              <div class="empty">该版本没有可下载的附件</div>
            {/if}
          </div>
        {/if}
        <div class="hint block">
          双击条目可直接编辑；「转换路径」会把模拟器路径按当前便携模式统一存为相对或绝对路径。安装会把同名旧目录移到 data\emulators\.backup。
        </div>
        <div class="pane-foot">
          <span class="note">{messages.emulators ?? ""}</span>
        </div>
      {/if}

      {#if tab === "lookups"}
        <div class="split">
          <div class="listbox narrow">
            {#each kinds as kind (kind.key)}
              <button class="list-item" class:active={currentKind === kind.key} onclick={() => void selectKind(kind.key)}>
                <span class="ellipsis">{kind.title}</span>
              </button>
            {/each}
          </div>
          <div class="listbox grow">
            {#each lookupValues as value (value)}
              <button class="list-item" class:active={selectedValue === value} onclick={() => (selectedValue = value)}>
                <span class="ellipsis">{value}</span>
              </button>
            {/each}
            {#if lookupValues.length === 0}
              <div class="empty">该字段还没有值</div>
            {/if}
          </div>
        </div>
        <div class="inline lookups-input">
          <input type="text" bind:value={lookupInput} placeholder="输入新值或改名后的值" />
          <button class="outline" onclick={addValue}>新增</button>
          <button class="outline" onclick={renameValue} disabled={!selectedValue}>改名</button>
          <button class="outline" onclick={deleteValue} disabled={!selectedValue}>删除</button>
        </div>
        <div class="hint block">
          改名会同步更新已有游戏记录；仍有记录使用的值不能删除。平台名称也只在这里维护。
        </div>
        <div class="pane-foot">
          <span class="note">{messages.lookups ?? ""}</span>
        </div>
      {/if}

      {#if tab === "platforms"}
        <div class="split">
          <div class="listbox narrow">
            {#each platforms as setting (setting.name)}
              <button
                class="list-item"
                class:active={selectedPlatform === setting.name}
                onclick={() => selectPlatform(setting)}
              >
                <span class="ellipsis">{setting.name}</span>
                {#if setting.isCustom}<span class="badge">自定义</span>{/if}
              </button>
            {/each}
          </div>
          <div class="form-col">
            {#if platformDraft}
              <div class="grid tight">
                <label>默认模拟器</label>
                <select
                  value={platformDraft.emulatorId === null ? "0" : String(platformDraft.emulatorId)}
                  onchange={(e) => {
                    const id = Number((e.currentTarget as HTMLSelectElement).value);
                    platformDraft!.emulatorId = id > 0 ? id : null;
                  }}
                >
                  <option value="0">（不指定）</option>
                  {#each emulators.filter((e) => e.enabled) as emulator (emulator.id)}
                    <option value={String(emulator.id)}>{emulator.name}</option>
                  {/each}
                </select>

                <label>库内子文件夹</label>
                <input type="text" bind:value={platformDraft.folder} placeholder={platformDraft.name} />

                <label>支持的 ROM 格式</label>
                <input type="text" bind:value={platformDraft.formats} placeholder="例如：iso cue bin chd" />

                <label>核心路径</label>
                <div class="inline">
                  <input type="text" bind:value={platformDraft.corePath} placeholder="（使用模拟器自带核心）" />
                  <button
                    class="outline"
                    onclick={async () => {
                      const picked = await openDialog({
                        title: "选择核心文件",
                        multiple: false,
                        filters: [{ name: "核心", extensions: ["dll", "so"] }],
                      });
                      if (typeof picked === "string" && picked) platformDraft!.corePath = picked;
                    }}>浏览…</button>
                </div>

                <label>启动参数</label>
                <input type="text" bind:value={platformDraft.argsTemplate} placeholder='例如：-L "{core}" "{rom}"' />
              </div>
              <div class="hint block">平台名称请在「字典库 → 平台」里维护；启动时使用这里的默认模拟器、核心与参数。</div>
              <div class="pane-foot split-foot">
                <span class="note">{messages.platforms ?? ""}</span>
                <button class="outline" onclick={scanPlatform} disabled={busy}>扫描平台库</button>
                <button class="outline" onclick={organizePlatform} disabled={busy}>整理平台库</button>
                <button class="primary" onclick={savePlatform} disabled={busy}>保存平台设置</button>
              </div>
            {:else}
              <div class="empty">请先在左侧选择一个平台</div>
            {/if}
          </div>
        </div>
      {/if}

      {#if tab === "maintenance"}
        <div class="section-title">数据库与数据</div>
        <div class="hint block">备份会复制当前数据库文件；恢复会用备份覆盖当前数据库，恢复后建议重启程序。</div>
        <div class="rows">
          <div class="row"><span>共 {stats?.total ?? 0} 条记录</span></div>
          <div class="row"><span>文件缺失 {stats?.missing ?? 0} 条</span></div>
          <div class="row"><span>待补指纹 {stats?.hashMissing ?? 0} 条</span></div>
        </div>
        <div class="pane-foot wrap">
          <button class="outline" onclick={backupDb}>备份数据库…</button>
          <button class="outline" onclick={restoreDb}>从备份恢复…</button>
          <button class="outline" onclick={exportCsv}>导出 CSV…</button>
          <button class="outline" onclick={cleanMissing}>清理失效路径记录</button>
          <button class="outline" onclick={hashMissing} disabled={busy}>补算缺失指纹</button>
        </div>
        <div class="pane-foot">
          <span class="note">{messages.maintenance ?? ""}</span>
        </div>
      {/if}

      {#if tab === "about"}
        <div class="section-title">Mica ROM Manager</div>
        <div class="rows">
          <div class="row">版本 {info?.version ?? ""}</div>
          <div class="row wrap-text">Windows 桌面端 ROM 集中管理工具。支持导入、编辑、模拟器启动、分享打包（ROM + 模拟器 + 一键启动脚本）。</div>
          <div class="row wrap-text">规则：ROM 删除一律走回收站；「从库中移除」不会删除文件。</div>
          <div class="row mono wrap-text">数据：{info?.dataDir ?? ""}</div>
          <div class="row mono wrap-text">导出：{info?.exportsDir ?? ""}</div>
        </div>
      {/if}
    </div>
  </div>
</div>

{#if editingEmulator}
  <EmulatorEditDialog
    emulator={editingEmulator}
    platformOptions={store.platformNames}
    onClose={() => (editingEmulator = null)}
    onSaved={async (saved) => {
      editingEmulator = null;
      await refreshEmulatorList(saved.id);
      note("emulators", `已保存模拟器「${saved.name}」`);
    }}
  />
{/if}

<style>
  .settings {
    width: 900px;
    height: 720px;
    display: flex;
    flex-direction: column;
  }

  .tabs {
    display: flex;
    gap: 4px;
    padding: 0 12px;
    border-bottom: 1px solid var(--border);
    background: var(--surface);
  }

  .tab {
    height: 32px;
    padding: 0 14px;
    border: none;
    border-bottom: 2px solid transparent;
    background: transparent;
    color: var(--text2);
  }

  .tab:hover {
    color: var(--text1);
  }

  .tab.active {
    color: var(--accent);
    border-bottom-color: var(--accent);
    font-weight: 600;
  }

  .pane {
    flex: 1;
    min-height: 0;
    overflow: auto;
    padding: 16px;
    display: flex;
    flex-direction: column;
  }

  .grid {
    display: grid;
    grid-template-columns: 88px 1fr;
    gap: 10px 12px;
    align-items: center;
    max-width: 640px;
  }

  .grid.tight {
    gap: 8px 10px;
  }

  .grid > label {
    color: var(--text2);
  }

  .inline {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .inline > input[type="text"] {
    flex: 1;
  }

  .stack {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .check {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .hint {
    color: var(--text2);
    font-size: 12px;
  }

  .hint.block {
    margin-top: 10px;
    line-height: 1.6;
  }

  .mono {
    font-family: Consolas, monospace;
    font-size: 12px;
  }

  .ellipsis {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .pane-foot {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: auto;
    padding-top: 12px;
  }

  .pane-foot.wrap {
    flex-wrap: wrap;
    margin-top: 12px;
  }

  .split-foot {
    margin-left: 0;
  }

  .note {
    flex: 1;
    color: var(--text2);
    font-size: 12px;
  }

  .split {
    display: flex;
    gap: 12px;
    flex: 1;
    min-height: 240px;
  }

  .listbox {
    width: 240px;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
    overflow: auto;
    padding: 4px;
  }

  .listbox.narrow {
    width: 150px;
  }

  .listbox.grow {
    flex: 1;
    width: auto;
  }

  .list-item {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 100%;
    height: 26px;
    padding: 0 8px;
    border: none;
    border-radius: 3px;
    background: transparent;
    text-align: left;
  }

  .list-item:hover {
    background: var(--hover);
  }

  .list-item.active {
    background: var(--selected);
  }

  .badge {
    flex: none;
    font-size: 11px;
    padding: 0 5px;
    border-radius: 8px;
    background: #e4f1e4;
    color: #2e7d32;
  }

  .badge.off {
    background: #fbe9e7;
    color: var(--danger);
  }

  .list-actions {
    display: flex;
    flex-direction: column;
    gap: 6px;
    width: 110px;
  }

  .update-panel {
    margin-top: 10px;
    border: 1px solid var(--border);
    border-radius: 4px;
    padding: 8px;
    background: var(--panel);
  }

  .update-head {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 6px;
  }

  .update-head > .ellipsis {
    flex: 1;
    font-weight: 600;
  }

  .update-row {
    display: flex;
    align-items: center;
    gap: 8px;
    height: 28px;
  }

  .update-row > .ellipsis {
    flex: 1;
  }

  .update-row > .size {
    color: var(--text2);
    font-variant-numeric: tabular-nums;
  }

  .empty {
    padding: 24px 8px;
    text-align: center;
    color: var(--text2);
  }

  .form-col {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  .lookups-input {
    margin-top: 10px;
  }

  .lookups-input > input {
    flex: 1;
  }

  .section-title {
    font-weight: 600;
    margin-bottom: 8px;
  }

  .rows {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-top: 10px;
  }

  .row {
    color: var(--text2);
  }

  .wrap-text {
    white-space: normal;
    line-height: 1.6;
  }
</style>
