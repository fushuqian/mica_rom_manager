<script lang="ts">
  import type { Game } from "../lib/api";
  import { COLUMNS, cellText, type GridColumn } from "../lib/grid";
  import { ratingStars } from "../lib/format";

  let {
    games,
    selectedIds,
    editMode,
    sortColumn,
    sortDirection,
    columnWidths,
    platformOptions,
    regionOptions,
    languageOptions,
    onSelect,
    onLaunch,
    onEdit,
    onRating,
    onCellCommit,
    onSort,
    onResize,
    onContextMenu,
    onDropFiles,
  }: {
    games: Game[];
    selectedIds: number[];
    editMode: boolean;
    sortColumn: string;
    sortDirection: "" | "asc" | "desc";
    columnWidths: Record<string, number>;
    platformOptions: string[];
    regionOptions: string[];
    languageOptions: string[];
    onSelect: (id: number, mode: "single" | "toggle" | "range") => void;
    onLaunch: (game: Game) => void;
    onEdit: (game: Game) => void;
    onRating: (game: Game, star: number) => void;
    onCellCommit: (game: Game, header: string, value: string) => void;
    onSort: (header: string) => void;
    onResize: (header: string, width: number) => void;
    onContextMenu: (event: MouseEvent, game: Game | null) => void;
    onDropFiles: (paths: string[], target: Game | null) => void;
  } = $props();

  const ROW_HEIGHT = 28;
  const HEADER_HEIGHT = 32;
  const BUFFER = 8;

  let scroller: HTMLDivElement;
  let scrollTop = $state(0);
  let scrollLeft = $state(0);
  let viewportHeight = $state(600);
  let resizing = $state<{ header: string; startX: number; startWidth: number } | null>(null);
  let dragOver = $state(false);

  function widthOf(column: GridColumn): number {
    return columnWidths[column.header] ?? column.width;
  }

  let totalWidth = $derived(COLUMNS.reduce((sum, c) => sum + widthOf(c), 0));

  let startIndex = $derived(Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - BUFFER));
  let endIndex = $derived(
    Math.min(games.length, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + BUFFER)
  );
  let visible = $derived(games.slice(startIndex, endIndex));

  let selectedSet = $derived(new Set(selectedIds));

  function handleScroll() {
    scrollTop = scroller.scrollTop;
    scrollLeft = scroller.scrollLeft;
    viewportHeight = scroller.clientHeight;
  }

  $effect(() => {
    if (!scroller) return;
    const observer = new ResizeObserver(() => {
      viewportHeight = scroller.clientHeight;
    });
    observer.observe(scroller);
    viewportHeight = scroller.clientHeight;
    return () => observer.disconnect();
  });

  function handleRowClick(event: MouseEvent, game: Game) {
    const mode: "single" | "toggle" | "range" = event.shiftKey
      ? "range"
      : event.ctrlKey || event.metaKey
        ? "toggle"
        : "single";
    onSelect(game.id, mode);
  }

  function handleDoubleClick(event: MouseEvent, game: Game) {
    const target = event.target as HTMLElement;
    if (target.closest("[data-editor]")) return;
    if (target.closest("[data-stars]")) return;
    onLaunch(game);
  }

  function startResize(event: MouseEvent, column: GridColumn) {
    event.preventDefault();
    event.stopPropagation();
    resizing = { header: column.header, startX: event.clientX, startWidth: widthOf(column) };
  }

  function onWindowMove(event: MouseEvent) {
    if (!resizing) return;
    const delta = event.clientX - resizing.startX;
    onResize(resizing.header, Math.max(24, Math.round(resizing.startWidth + delta)));
  }

  function endResize() {
    resizing = null;
  }

  // ---- 行内编辑 ----
  type EditState = { id: number; header: string; value: string };
  let editing = $state<EditState | null>(null);

  function beginEdit(game: Game, column: GridColumn, event: MouseEvent) {
    if (!editMode) return;
    if (column.editable === "none" || column.editable === "rating") return;
    event.stopPropagation();
    const value = column.editable === "year" ? (game.releaseYear ? String(game.releaseYear) : "") : cellText(game, column.header);
    editing = { id: game.id, header: column.header, value };
    queueMicrotask(() => {
      const el = document.querySelector<HTMLInputElement>("[data-editor] input");
      el?.focus();
      el?.select();
    });
  }

  function commitEdit(shift = false) {
    if (!editing) return;
    const game = games.find((g) => g.id === editing!.id);
    const state = editing;
    editing = null;
    if (game) onCellCommit(game, state.header, state.value);
    if (shift) return;
  }

  function cancelEdit() {
    editing = null;
  }

  function isEditing(game: Game, header: string): boolean {
    return editing?.id === game.id && editing.header === header;
  }

  function starCount(game: Game): number {
    return Math.max(0, Math.min(5, game.rating || 0));
  }

  function handleDragOver(event: DragEvent) {
    event.preventDefault();
    dragOver = true;
  }

  function handleDrop(event: DragEvent) {
    event.preventDefault();
    dragOver = false;
    const files = Array.from(event.dataTransfer?.files ?? []);
    const paths = files.map((f) => (f as File & { path?: string }).path).filter(Boolean) as string[];
    onDropFiles(paths, null);
  }
</script>

<svelte:window onmousemove={onWindowMove} onmouseup={endResize} />

<div
  class="grid-wrap"
  class:drag-over={dragOver}
  ondragover={handleDragOver}
  ondragleave={() => (dragOver = false)}
  ondrop={handleDrop}
>
  <div class="header">
    <div class="header-inner" style="width:{totalWidth}px; transform: translateX(-{scrollLeft}px)">
    {#each COLUMNS as column (column.header)}
      <div
        class="header-cell"
        class:sortable={column.sortKey !== null}
        style="width:{widthOf(column)}px"
        role="columnheader"
        tabindex="-1"
        onclick={() => column.sortKey !== null && onSort(column.header)}
        onkeydown={(e) => e.key === "Enter" && column.sortKey !== null && onSort(column.header)}
        oncontextmenu={(e) => onContextMenu(e, null)}
      >
        <span class="title">{column.header}</span>
        {#if sortColumn === column.header}
          <span class="sort">{sortDirection === "asc" ? "▲" : sortDirection === "desc" ? "▼" : ""}</span>
        {/if}
        <span class="resize" onmousedown={(e) => startResize(e, column)} role="separator"></span>
      </div>
    {/each}
    </div>
  </div>

  <div class="scroller" bind:this={scroller} onscroll={handleScroll} oncontextmenu={(e) => onContextMenu(e, null)}>
    <div style="height:{games.length * ROW_HEIGHT}px; position:relative;">
      <div style="transform: translateY({startIndex * ROW_HEIGHT}px);">
        {#each visible as game, i (game.id)}
          <div
            class="row"
            class:alt={(startIndex + i) % 2 === 1}
            class:selected={selectedSet.has(game.id)}
            class:missing={game.missing}
            style="width:{totalWidth}px; height:{ROW_HEIGHT}px"
            role="row"
            tabindex="-1"
            onclick={(e) => handleRowClick(e, game)}
            ondblclick={(e) => handleDoubleClick(e, game)}
            oncontextmenu={(e) => {
              if (!selectedSet.has(game.id)) onSelect(game.id, 'single');
              onContextMenu(e, game);
            }}
          >
            {#each COLUMNS as column (column.header)}
              <div
                class="cell"
                class:center={column.align === "center"}
                class:right={column.align === "right"}
                style="width:{widthOf(column)}px"
                title={column.header === "指纹" ? game.romPath : cellText(game, column.header)}
                role="gridcell"
                tabindex="-1"
                onclick={(e) => beginEdit(game, column, e)}
                onkeydown={(e) => {
                  if (e.key === "Enter" && editMode) onEdit(game);
                }}
              >
                {#if column.editable === "rating"}
                  <span class="stars" data-stars>
                    {#each [1, 2, 3, 4, 5] as star (star)}
                      <span
                        class="star"
                        class:filled={star <= starCount(game)}
                        role="button"
                        tabindex="-1"
                        onclick={(e) => {
                          e.stopPropagation();
                          onRating(game, star);
                        }}
                        onkeydown={(e) => e.key === "Enter" && onRating(game, star)}
                        title={`${ratingStars(star)}`}
                      >{star <= starCount(game) ? "★" : "☆"}</span>
                    {/each}
                  </span>
                {:else if isEditing(game, column.header)}
                  <span class="editor" data-editor>
                    {#if column.editable === "platform"}
                      <input
                        list="platform-options"
                        value={editing!.value}
                        oninput={(e) => (editing!.value = (e.currentTarget as HTMLInputElement).value)}
                        onkeydown={(e) => {
                          if (e.key === "Enter") commitEdit();
                          if (e.key === "Escape") cancelEdit();
                        }}
                        onblur={() => commitEdit()}
                      />
                    {:else if column.editable === "region"}
                      <input
                        list="region-options"
                        value={editing!.value}
                        oninput={(e) => (editing!.value = (e.currentTarget as HTMLInputElement).value)}
                        onkeydown={(e) => {
                          if (e.key === "Enter") commitEdit();
                          if (e.key === "Escape") cancelEdit();
                        }}
                        onblur={() => commitEdit()}
                      />
                    {:else if column.editable === "language"}
                      <input
                        list="language-options"
                        value={editing!.value}
                        oninput={(e) => (editing!.value = (e.currentTarget as HTMLInputElement).value)}
                        onkeydown={(e) => {
                          if (e.key === "Enter") commitEdit();
                          if (e.key === "Escape") cancelEdit();
                        }}
                        onblur={() => commitEdit()}
                      />
                    {:else if column.editable === "status"}
                      <select
                        bind:value={editing!.value}
                        onchange={() => commitEdit()}
                        onblur={() => commitEdit()}
                      >
                        {#each ["未玩", "在玩", "已通关", "已放弃"] as status (status)}
                          <option>{status}</option>
                        {/each}
                      </select>
                    {:else}
                      <input
                        value={editing!.value}
                        inputmode={column.editable === "year" ? "numeric" : "text"}
                        oninput={(e) => (editing!.value = (e.currentTarget as HTMLInputElement).value)}
                        onkeydown={(e) => {
                          if (e.key === "Enter") commitEdit();
                          if (e.key === "Escape") cancelEdit();
                        }}
                        onblur={() => commitEdit()}
                      />
                    {/if}
                  </span>
                {:else}
                  <span class="text">{cellText(game, column.header)}</span>
                {/if}
              </div>
            {/each}
          </div>
        {/each}
      </div>
    </div>
    {#if games.length === 0}
      <div class="empty">没有符合条件的游戏。点「导入…」添加 ROM，或调整筛选条件。</div>
    {/if}
  </div>
</div>

<datalist id="platform-options">
  {#each platformOptions as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="region-options">
  {#each regionOptions as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="language-options">
  {#each languageOptions as option (option)}<option value={option}></option>{/each}
</datalist>

<style>
  .grid-wrap {
    flex: 1;
    min-height: 0;
    background: var(--surface);
    border: 1px solid var(--border);
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .grid-wrap.drag-over {
    border-color: var(--accent);
    box-shadow: inset 0 0 0 1px var(--accent);
  }

  .header {
    height: 32px;
    background: var(--header-bg);
    border-bottom: 1px solid var(--border);
    flex: none;
    overflow: hidden;
  }

  .header-inner {
    display: flex;
    height: 32px;
  }

  .header-cell {
    position: relative;
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 0 8px;
    border-right: 1px solid var(--border);
    color: var(--text2);
    font-weight: 600;
    font-size: 12px;
    flex: none;
    overflow: hidden;
    white-space: nowrap;
  }

  .header-cell.sortable {
    cursor: pointer;
  }

  .header-cell.sortable:hover {
    background: var(--hover);
  }

  .header-cell .title {
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .sort {
    font-size: 9px;
    color: var(--accent);
  }

  .resize {
    position: absolute;
    right: -2px;
    top: 0;
    bottom: 0;
    width: 6px;
    cursor: col-resize;
    z-index: 2;
  }

  .resize:hover {
    background: rgba(47, 93, 138, 0.18);
  }

  .scroller {
    flex: 1;
    overflow: auto;
    position: relative;
  }

  .row {
    display: flex;
    border-bottom: 1px solid var(--grid-line);
    background: var(--surface);
  }

  .row.alt {
    background: var(--row-alt);
  }

  .row:hover {
    background: var(--hover);
  }

  .row.selected {
    background: var(--selected);
  }

  .row.missing .text {
    color: var(--danger);
  }

  .cell {
    display: flex;
    align-items: center;
    gap: 2px;
    padding: 0 8px;
    flex: none;
    overflow: hidden;
    white-space: nowrap;
    border-right: 1px solid transparent;
  }

  .cell.center {
    justify-content: center;
  }

  .cell.right {
    justify-content: flex-end;
  }

  .text {
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .stars {
    display: inline-flex;
    gap: 1px;
  }

  .star {
    cursor: pointer;
    color: #c9ced5;
    font-size: 13px;
    line-height: 1;
  }

  .star.filled {
    color: #e6a817;
  }

  .text {
    font-size: 13px;
  }

  .editor {
    width: 100%;
  }

  .editor input,
  .editor select {
    width: 100%;
    height: 22px;
    padding: 0 4px;
    font-size: 12px;
  }

  .empty {
    position: sticky;
    left: 0;
    padding: 24px;
    color: var(--text2);
  }
</style>
