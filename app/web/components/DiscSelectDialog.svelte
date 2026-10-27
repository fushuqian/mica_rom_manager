<script lang="ts">
  import type { Game } from "../lib/api";
  import { discs, fileName } from "../lib/format";

  let {
    game,
    onPick,
    onClose,
  }: {
    game: Game;
    onPick: (index: number) => void;
    onClose: () => void;
  } = $props();

  const list = discs(game);
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:520px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>选择要启动的碟片 — {game.title}</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      {#each list as path, index (path)}
        <button class="disc" onclick={() => onPick(index)} ondblclick={() => onPick(index)}>
          <span class="badge">D{index + 1}</span>
          <span class="name" title={path}>{fileName(path)}</span>
        </button>
      {/each}
    </div>
    <div class="modal-foot">
      <button class="outline" onclick={onClose}>取消</button>
    </div>
  </div>
</div>

<style>
  .disc {
    display: flex;
    width: 100%;
    align-items: center;
    gap: 10px;
    padding: 8px 10px;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
    margin-bottom: 6px;
    text-align: left;
  }

  .disc:hover {
    background: var(--hover);
  }

  .badge {
    flex: none;
    font-weight: 600;
    color: var(--accent);
  }

  .name {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
</style>
