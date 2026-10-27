<script lang="ts">
  import { listen } from "@tauri-apps/api/event";
  import { writeText } from "@tauri-apps/plugin-clipboard-manager";
  import { ask as askDialog, message as showMessage, open as openDialog } from "@tauri-apps/plugin-dialog";
  import { revealItemInDir } from "@tauri-apps/plugin-opener";
  import { onMount } from "svelte";
  import BatchEditDialog from "./components/BatchEditDialog.svelte";
  import DiscSelectDialog from "./components/DiscSelectDialog.svelte";
  import FilterBar from "./components/FilterBar.svelte";
  import GameEditDialog from "./components/GameEditDialog.svelte";
  import GameGrid from "./components/GameGrid.svelte";
  import ImportWizard from "./components/ImportWizard.svelte";
  import PlatformSidebar from "./components/PlatformSidebar.svelte";
  import SettingsDialog from "./components/SettingsDialog.svelte";
  import SharePackDialog from "./components/SharePackDialog.svelte";
  import Toolbar from "./components/Toolbar.svelte";
  import { api, type BatchChanges, type Game } from "./lib/api";
  import { discs } from "./lib/format";
  import { COLUMNS, compareGames } from "./lib/grid";
  import {
    ALL_DEVELOPERS,
    ALL_LANGUAGES,
    ALL_PLATFORMS,
    ALL_REGIONS,
    ALL_STATUSES,
    ALL_TAGS,
    STATUSES,
    restoreViewState,
    saveViewState,
    store,
  } from "./lib/state.svelte";

  let filterBar: { focusSearch: () => void } | undefined = $state();

  let editState = $state<{ game: Game; isNew: boolean } | null>(null);
  let discState = $state<{ game: Game; emulatorId: number | null } | null>(null);
  let importSources = $state<string[] | null>(null);
  let batchOpen = $state(false);
  let settingsOpen = $state(false);
  let shareOpen = $state(false);

  let menu = $state<{ x: number; y: number } | null>(null);
  let launchWithOpen = $state(false);

  let saveTimer: ReturnType<typeof setTimeout> | undefined;

  let sortedGames = $derived.by(() => {
    const list = store.filterGames();
    const column = COLUMNS.find((c) => c.header === store.sortColumn);
    if (!column?.sortKey || !store.sortDirection) return list;
    const direction = store.sortDirection;
    const key = column.sortKey;
    return [...list].sort((a, b) => compareGames(a, b, key, direction));
  });

  let platformItems = $derived(store.platformFilters);
  let countText = $derived(`共 ${store.games.length} 款 / 已筛选 ${sortedGames.length} 款`);
  let dataPathText = $derived(
    store.info ? `数据：${store.info.dbPath}${store.info.portable ? "（便携模式）" : ""}` : ""
  );

  function setStatus(text: string) {
    store.statusMessage = text;
  }

  function primary(): Game | null {
    return store.selectedGames[0] ?? null;
  }

  function syncFilteredIds() {
    store.filteredIds = sortedGames.map((g) => g.id);
  }

  function onSelect(id: number, mode: "single" | "toggle" | "range") {
    syncFilteredIds();
    store.select(id, mode);
  }

  function onSort(header: string) {
    const column = COLUMNS.find((c) => c.header === header);
    if (!column?.sortKey) return;
    if (store.sortColumn !== header) {
      store.sortColumn = header;
      store.sortDirection = "asc";
      return;
    }
    store.sortDirection = store.sortDirection === "asc" ? "desc" : store.sortDirection === "desc" ? "" : "asc";
    if (!store.sortDirection) store.sortColumn = "";
  }

  function onResize(header: string, width: number) {
    store.columnWidths = { ...store.columnWidths, [header]: width };
  }

  function onEditMode(on: boolean) {
    store.editMode = on;
  }

  function clearFilters() {
    store.search = "";
    store.platform = ALL_PLATFORMS;
    store.status = ALL_STATUSES;
    store.tag = ALL_TAGS;
    store.region = ALL_REGIONS;
    store.language = ALL_LANGUAGES;
    store.developer = ALL_DEVELOPERS;
    store.onlyFavorites = false;
    store.onlyMissing = false;
  }

  function openEditDialog(game: Game) {
    editState = { game: structuredClone(game), isNew: false };
  }

  function openNewDialog() {
    editState = {
      game: {
        id: 0,
        title: "新建游戏",
        platform: "",
        genre: "",
        region: "",
        language: "",
        releaseYear: null,
        publisher: "",
        developer: "",
        favorite: false,
        rating: 0,
        status: "未玩",
        romPath: "",
        fileSize: 0,
        coverPath: "",
        notes: "",
        tags: "",
        emulatorId: null,
        playCount: 0,
        playSeconds: 0,
        lastPlayedAt: null,
        createdAt: "",
        updatedAt: "",
        discPaths: "",
        romHash: "",
        missing: false,
      },
      isNew: true,
    };
  }

  async function afterSaved(game: Game) {
    const isNew = editState?.isNew;
    editState = null;
    await store.reloadGames();
    await store.reloadLookups();
    store.selectedIds = [game.id];
    store.anchorId = game.id;
    setStatus(isNew ? `已新建「${game.title}」` : `已保存「${game.title}」`);
  }

  async function commitCell(game: Game, header: string, value: string) {
    const updated = structuredClone(game);
    switch (header) {
      case "标题":
        updated.title = value;
        break;
      case "平台":
        updated.platform = value;
        break;
      case "区域":
        updated.region = value;
        break;
      case "语言":
        updated.language = value;
        break;
      case "类型":
        updated.genre = value;
        break;
      case "标签":
        updated.tags = value;
        break;
      case "年份": {
        const year = parseInt(value, 10);
        updated.releaseYear = Number.isFinite(year) && year > 0 ? year : null;
        break;
      }
      case "状态":
        updated.status = value;
        break;
      default:
        return;
    }
    try {
      const saved = await api.gamesSave(updated);
      store.replaceGame(saved);
      if (header === "平台") await store.reloadLookups();
      setStatus(`已保存「${saved.title}」`);
    } catch (e) {
      setStatus(`保存失败：${e}`);
    }
  }

  async function setRating(game: Game, star: number) {
    const next = game.rating === star ? 0 : star;
    try {
      const saved = await api.setGameRating(game.id, next);
      store.replaceGame(saved);
    } catch (e) {
      setStatus(`评分失败：${e}`);
    }
  }

  async function openBatchEdit() {
    if (store.selectedGames.length === 0) {
      setStatus("请先选择要批量编辑的条目");
      return;
    }
    batchOpen = true;
  }

  async function applyBatch(changes: BatchChanges) {
    const ids = store.selectedGames.map((g) => g.id);
    batchOpen = false;
    try {
      const updated = await api.gamesBatchUpdate(ids, changes);
      await store.reloadGames();
      await store.reloadLookups();
      setStatus(`已批量更新 ${updated} 条记录`);
    } catch (e) {
      setStatus(`批量更新失败：${e}`);
    }
  }

  async function mergeDiscs() {
    const ids = store.selectedGames.map((g) => g.id);
    if (ids.length < 2) {
      setStatus("请先选择至少 2 个条目（首行为主条目）");
      return;
    }
    const confirmed = await askDialog(
      `把选中的 ${ids.length} 个条目合并为一个多碟游戏？\n\n首行「${store.selectedGames[0].title}」为主条目，其余条目会被删除。`,
      { title: "合并多碟", kind: "warning" }
    );
    if (!confirmed) return;
    try {
      const saved = await api.gamesMergeDiscs(ids);
      await store.reloadGames();
      store.selectedIds = [saved.id];
      store.anchorId = saved.id;
      setStatus(`已合并为多碟游戏：${saved.title}（${discs(saved).length} 碟）`);
    } catch (e) {
      setStatus(`合并失败：${e}`);
    }
  }

  async function splitDiscs() {
    const game = primary();
    if (!game) return;
    if (discs(game).length < 2) {
      setStatus("该条目不是多碟游戏");
      return;
    }
    const confirmed = await askDialog(
      `把「${game.title}」拆分为 ${discs(game).length} 个独立条目？`,
      { title: "拆分多碟", kind: "warning" }
    );
    if (!confirmed) return;
    try {
      const created = await api.gamesSplitDiscs(game.id);
      await store.reloadGames();
      store.selectedIds = created.map((g) => g.id);
      setStatus(`已拆分为 ${created.length} 个条目`);
    } catch (e) {
      setStatus(`拆分失败：${e}`);
    }
  }

  async function launch(game: Game, emulatorId: number | null = null) {
    const list = discs(game);
    if (list.length > 1) {
      discState = { game, emulatorId };
      return;
    }
    await doLaunch(game, emulatorId, null);
  }

  async function doLaunch(game: Game, emulatorId: number | null, discIndex: number | null) {
    store.busy = true;
    try {
      const outcome = await api.launchGame(game.id, emulatorId, discIndex);
      setStatus(outcome.message);
      if (!outcome.launched) {
        await showMessage(outcome.message, { title: "无法启动", kind: "warning" });
      }
    } catch (e) {
      setStatus(`启动失败：${e}`);
    } finally {
      store.busy = false;
    }
  }

  function availableEmulators(game: Game | null) {
    if (!game) return [];
    return store.emulators.filter(
      (e) => e.enabled && e.platforms.some((p) => p.toLowerCase() === game.platform.toLowerCase())
    );
  }

  let menuEmulators = $derived(availableEmulators(primary()));

  async function launchWith(emulatorId: number) {
    const game = primary();
    menu = null;
    if (!game) return;
    await launch(game, emulatorId);
  }

  async function openFolder() {
    const game = primary();
    if (!game?.romPath) return;
    if (!(await api.fileExists(game.romPath))) {
      setStatus("ROM 文件不存在");
      return;
    }
    try {
      await revealItemInDir(game.romPath);
    } catch (e) {
      setStatus(`打开目录失败：${e}`);
    }
  }

  async function copyPaths() {
    const paths = store.selectedGames.map((g) => g.romPath).filter((p) => p.trim());
    if (paths.length === 0) return;
    try {
      await writeText(paths.join("\n"));
      setStatus(`已复制 ${paths.length} 个路径`);
    } catch (e) {
      setStatus(`复制路径失败：${e}`);
    }
  }

  async function toggleFavorites() {
    const list = store.selectedGames;
    if (list.length === 0) return;
    try {
      for (const game of list) {
        const saved = await api.toggleFavorite(game.id);
        store.replaceGame(saved);
      }
      setStatus(`已切换 ${list.length} 个收藏状态`);
    } catch (e) {
      setStatus(`收藏失败：${e}`);
    }
  }

  async function setCover() {
    const game = primary();
    if (!game) return;
    const path = await openDialog({
      title: "选择封面图片",
      multiple: false,
      filters: [{ name: "图片", extensions: ["png", "jpg", "jpeg", "bmp", "webp", "gif"] }],
    });
    if (typeof path !== "string" || !path) return;
    try {
      const saved = await api.setCover(game.id, path);
      store.replaceGame(saved);
      setStatus(`已设置封面：${saved.title}`);
    } catch (e) {
      setStatus(`设置封面失败：${e}`);
    }
  }

  async function refreshStatus() {
    await store.reloadGames();
    setStatus("已刷新文件状态");
  }

  async function removeSelected() {
    const ids = store.selectedGames.map((g) => g.id);
    if (ids.length === 0) return;
    const confirmed = await askDialog(
      `从库中移除 ${ids.length} 条记录？\n\n只删除数据库记录，不会动 ROM 文件。`,
      { title: "从库中移除", kind: "warning" }
    );
    if (!confirmed) return;
    try {
      await api.gamesDeleteMany(ids);
      store.removeGames(ids);
      setStatus(`已移除 ${ids.length} 条记录`);
    } catch (e) {
      setStatus(`移除失败：${e}`);
    }
  }

  async function recycleSelected() {
    const list = store.selectedGames;
    if (list.length === 0) return;
    const confirmed = await askDialog(
      `把 ${list.length} 个 ROM 文件移到回收站并移除记录？\n\n文件可在 Windows 回收站找回。`,
      { title: "移到回收站", kind: "warning" }
    );
    if (!confirmed) return;
    const ids = list.map((g) => g.id);
    try {
      const paths = list.map((g) => g.romPath).filter((p) => p.trim());
      if (paths.length) await api.recycleFiles(paths);
      await api.gamesDeleteMany(ids);
      store.removeGames(ids);
      setStatus(`已移到回收站 ${paths.length} 个文件，并移除 ${ids.length} 条记录`);
    } catch (e) {
      setStatus(`移到回收站失败：${e}`);
    }
  }

  function openMenu(event: MouseEvent, game: Game | null) {
    event.preventDefault();
    if (event.clientX <= 0 && event.clientY <= 0) return;
    menu = {
      x: Math.min(event.clientX, window.innerWidth - 280),
      y: Math.min(event.clientY, window.innerHeight - 420),
    };
    launchWithOpen = false;
    void game;
  }

  function closeMenu() {
    menu = null;
    launchWithOpen = false;
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.ctrlKey && event.key.toLowerCase() === "f") {
      event.preventDefault();
      filterBar?.focusSearch();
      return;
    }
    const target = event.target as HTMLElement | null;
    if (target?.closest("input, textarea, select")) return;
    if (store.editMode) return;

    if (event.key === "Enter" && primary()) {
      event.preventDefault();
      void launch(primary()!);
    } else if (event.key === "F2" && primary()) {
      event.preventDefault();
      openEditDialog(primary()!);
    } else if (event.key === "Delete" && primary()) {
      event.preventDefault();
      void removeSelected();
    }
  }

  function openShare() {
    if (store.selectedGames.length === 0) {
      setStatus("请先选择要分享的游戏");
      return;
    }
    shareOpen = true;
  }

  function notReady(name: string) {
    setStatus(`${name}功能还在开发中`);
  }

  function mmss(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  }

  $effect(() => {
    syncFilteredIds();
  });

  $effect(() => {
    store.search;
    store.platform;
    store.status;
    store.tag;
    store.region;
    store.language;
    store.developer;
    store.onlyFavorites;
    store.onlyMissing;
    store.sortColumn;
    store.sortDirection;
    store.columnWidths;
    if (!store.viewStateLoaded) return;
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => void saveViewState(), 500);
  });

  onMount(() => {
    void (async () => {
      await store.loadAll();
      await restoreViewState();
      store.viewStateLoaded = true;
      store.statusMessage = "就绪";
      filterBar?.focusSearch();
    })();

    const unlisten = listen<{ gameId: number; seconds: number }>("game://session-ended", (event) => {
      const game = store.games.find((g) => g.id === event.payload.gameId);
      if (!game) return;
      setStatus(`「${game.title}」已退出，本次 ${mmss(event.payload.seconds)}`);
      void store.reloadGames();
    });

    const beforeUnload = () => {
      void saveViewState();
    };
    window.addEventListener("beforeunload", beforeUnload);

    return () => {
      void unlisten.then((fn) => fn());
      window.removeEventListener("beforeunload", beforeUnload);
    };
  });
</script>

<svelte:window onmousedown={closeMenu} onkeydown={onKeyDown} />

<main class="app">
  <Toolbar
    hasSelection={store.selectedIds.length > 0}
    onImport={() => (importSources = [])}
    onNew={openNewDialog}
    onBatchEdit={() => void openBatchEdit()}
    onLaunch={() => {
      const game = primary();
      if (game) void launch(game);
    }}
    onShare={openShare}
    onOpenFolder={openFolder}
    onRefresh={refreshStatus}
    onSettings={() => (settingsOpen = true)}
  />

  <div class="body">
    <PlatformSidebar
      items={platformItems}
      total={store.games.length}
      selected={store.platform}
      onSelect={(name) => (store.platform = name)}
    />

    <section class="content">
      <FilterBar
        bind:this={filterBar}
        statusOptions={[ALL_STATUSES, ...STATUSES]}
        regionOptions={[ALL_REGIONS, ...store.regions]}
        languageOptions={[ALL_LANGUAGES, ...store.languages]}
        developerOptions={[ALL_DEVELOPERS, ...store.developers]}
        tagOptions={[ALL_TAGS, ...store.tags]}
        {countText}
        onClear={clearFilters}
        onEditMode={onEditMode}
      />

      <GameGrid
        games={sortedGames}
        selectedIds={store.selectedIds}
        editMode={store.editMode}
        sortColumn={store.sortColumn}
        sortDirection={store.sortDirection}
        columnWidths={store.columnWidths}
        platformOptions={store.platformNames}
        regionOptions={store.regions}
        languageOptions={store.languages}
        onSelect={onSelect}
        onLaunch={(game) => void launch(game)}
        onEdit={openEditDialog}
        onRating={(game, star) => void setRating(game, star)}
        onCellCommit={(game, header, value) => void commitCell(game, header, value)}
        onSort={onSort}
        onResize={onResize}
        onContextMenu={openMenu}
        onDropFiles={(paths) => {
          if (paths.length) importSources = paths;
        }}
      />
    </section>
  </div>

  <footer class="statusbar">
    <span class="path" title={dataPathText}>{dataPathText}</span>
    <span class="message">{store.statusMessage}</span>
    {#if store.busy}<span class="progress"></span>{/if}
    <span class="refresh">{store.lastRefresh}</span>
  </footer>
</main>

{#if menu}
  <div
    class="menu"
    style="left:{menu.x}px; top:{menu.y}px"
    role="menu"
    tabindex="-1"
    onmousedown={(e) => e.stopPropagation()}
  >
    <div class="menu-item" onclick={() => { menu = null; const g = primary(); if (g) void launch(g); }}>
      <span>启动游戏</span><span class="hint">Enter</span>
    </div>
    <div
      class="menu-item submenu-anchor"
      onmouseenter={() => (launchWithOpen = true)}
      onmouseleave={() => (launchWithOpen = false)}
    >
      <span>用指定模拟器启动</span><span class="hint">▸</span>
      {#if launchWithOpen}
        <div class="menu submenu" role="menu">
          {#each menuEmulators as emulator (emulator.id)}
            <div class="menu-item" onclick={() => void launchWith(emulator.id)}>
              <span>{emulator.name}</span>
            </div>
          {/each}
          {#if menuEmulators.length === 0}
            <div class="menu-item disabled"><span>（该平台暂无可用模拟器）</span></div>
          {/if}
        </div>
      {/if}
    </div>
    <div class="menu-item" onclick={() => { menu = null; const g = primary(); if (g) openEditDialog(g); }}>
      <span>编辑信息…</span><span class="hint">F2</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void openBatchEdit(); }}>
      <span>批量编辑…</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void mergeDiscs(); }}>
      <span>合并为多碟游戏（选中多行，首行为主条目）</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void splitDiscs(); }}>
      <span>拆分多碟…</span>
    </div>
    <div class="menu-sep"></div>
    <div class="menu-item" onclick={() => { menu = null; openShare(); }}>
      <span>分享打包…</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void openFolder(); }}>
      <span>打开所在文件夹</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void copyPaths(); }}>
      <span>复制文件路径</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void setCover(); }}>
      <span>设置封面…</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void toggleFavorites(); }}>
      <span>收藏 / 取消收藏</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void refreshStatus(); }}>
      <span>刷新文件状态</span>
    </div>
    <div class="menu-sep"></div>
    <div class="menu-item" onclick={() => { menu = null; void removeSelected(); }}>
      <span>从库中移除（不删文件）</span>
    </div>
    <div class="menu-item" onclick={() => { menu = null; void recycleSelected(); }}>
      <span>移到回收站并移除</span>
    </div>
  </div>
{/if}

{#if shareOpen}
  <SharePackDialog
    games={store.selectedGames}
    defaults={{
      exportDir: store.info?.exportsDir ?? "",
      saveDir: "",
      packageName: store.selectedGames.length === 1 ? store.selectedGames[0].title : "ROM分享包",
    }}
    onClose={() => (shareOpen = false)}
  />
{/if}

{#if settingsOpen}
  <SettingsDialog
    onClose={() => (settingsOpen = false)}
    onDataChanged={() => {
      void (async () => {
        await store.reloadGames();
        await store.reloadLookups();
      })();
    }}
  />
{/if}

{#if importSources}
  <ImportWizard
    initialSources={importSources}
    libraryRoot={store.info?.libraryRoot ?? ""}
    onClose={() => (importSources = null)}
    onImported={(outcome) => {
      importSources = null;
      void store.reloadGames();
      void store.reloadLookups();
      setStatus(
        outcome.failed > 0
          ? `导入完成：成功 ${outcome.imported} 个，失败 ${outcome.failed} 个`
          : `导入完成：成功 ${outcome.imported} 个`
      );
    }}
  />
{/if}

{#if batchOpen}
  <BatchEditDialog
    selectedCount={store.selectedIds.length}
    onClose={() => (batchOpen = false)}
    onApply={(changes) => void applyBatch(changes)}
  />
{/if}

{#if editState}
  <GameEditDialog
    initial={editState.game}
    isNew={editState.isNew}
    onClose={() => (editState = null)}
    onSaved={(game) => void afterSaved(game)}
  />
{/if}

{#if discState}
  <DiscSelectDialog
    game={discState.game}
    onPick={(index) => {
      const state = discState!;
      discState = null;
      void doLaunch(state.game, state.emulatorId, index);
    }}
    onClose={() => (discState = null)}
  />
{/if}

<style>
  .app {
    height: 100%;
    display: flex;
    flex-direction: column;
    background: var(--window-bg);
  }

  .body {
    flex: 1;
    min-height: 0;
    display: flex;
    gap: 12px;
    padding: 8px 12px 0;
  }

  .content {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  .statusbar {
    display: flex;
    align-items: center;
    gap: 16px;
    margin-top: 10px;
    padding: 8px 12px;
    background: var(--surface);
    border-top: 1px solid var(--border);
    color: var(--text2);
    font-size: 12px;
  }

  .path {
    max-width: 520px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .message {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .progress {
    width: 110px;
    height: 4px;
    border-radius: 2px;
    background: linear-gradient(90deg, var(--accent) 0%, #9bb7d2 50%, var(--accent) 100%);
    background-size: 200% 100%;
    animation: progress 1.2s linear infinite;
  }

  @keyframes progress {
    from {
      background-position: 100% 0;
    }
    to {
      background-position: -100% 0;
    }
  }

  .submenu-anchor {
    position: relative;
  }

  .submenu {
    position: absolute;
    left: 100%;
    top: -4px;
    min-width: 180px;
  }
</style>
