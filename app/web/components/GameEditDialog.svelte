<script lang="ts">
  import { open } from "@tauri-apps/plugin-dialog";
  import { api, type Game } from "../lib/api";
  import { clone } from "../lib/format";
  import { STATUSES, store } from "../lib/state.svelte";

  let {
    initial,
    isNew,
    onClose,
    onSaved,
  }: {
    initial: Game;
    isNew: boolean;
    onClose: () => void;
    onSaved: (game: Game) => void;
  } = $props();

  let draft = $state<Game>(clone(initial));
  let error = $state("");
  let saving = $state(false);
  let pendingCover = $state("");
  let removeCoverRequested = $state(false);

  async function pickCover() {
    const path = await open({
      title: "选择封面图片",
      multiple: false,
      filters: [{ name: "图片", extensions: ["png", "jpg", "jpeg", "bmp", "webp", "gif"] }],
    });
    if (typeof path === "string" && path) {
      pendingCover = path;
      draft.coverPath = path;
      removeCoverRequested = false;
    }
  }

  function clearCover() {
    pendingCover = "";
    draft.coverPath = "";
    removeCoverRequested = true;
  }

  async function save() {
    if (saving) return;
    if (!draft.title.trim()) {
      error = "标题不能为空。";
      return;
    }
    saving = true;
    error = "";
    try {
      const payload = clone(draft);
      if (payload.romPath.trim()) {
        try {
          payload.fileSize = await api.fileSize(payload.romPath.trim());
        } catch {
          // 文件不存在时保留原大小
        }
      }
      let saved = await api.gamesSave(payload);
      if (pendingCover) {
        saved = await api.setCover(saved.id, pendingCover);
      } else if (removeCoverRequested && !isNew) {
        saved = await api.removeCover(saved.id);
      }
      onSaved(saved);
    } catch (e) {
      error = String(e);
    } finally {
      saving = false;
    }
  }
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:640px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>{isNew ? "新建条目" : "编辑信息"}</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      {#if error}
        <div class="error">{error}</div>
      {/if}

      <div class="field"><label>标题</label><input type="text" bind:value={draft.title} /></div>
      <div class="field">
        <label>平台</label>
        <input type="text" list="edit-platforms" bind:value={draft.platform} />
      </div>
      <div class="field"><label>类型</label><input type="text" list="edit-genres" bind:value={draft.genre} /></div>
      <div class="field"><label>区域</label><input type="text" list="edit-regions" bind:value={draft.region} /></div>
      <div class="field">
        <label>语言</label><input type="text" list="edit-languages" bind:value={draft.language} />
      </div>
      <div class="field">
        <label>年份</label>
        <input
          type="number"
          min="1950"
          max="2100"
          value={draft.releaseYear ?? ""}
          oninput={(e) => {
            const n = parseInt((e.currentTarget as HTMLInputElement).value, 10);
            draft.releaseYear = Number.isFinite(n) ? n : null;
          }}
        />
      </div>
      <div class="field"><label>开发商</label><input type="text" list="edit-developers" bind:value={draft.developer} /></div>
      <div class="field"><label>发行商</label><input type="text" list="edit-publishers" bind:value={draft.publisher} /></div>
      <div class="field">
        <label>状态</label>
        <select bind:value={draft.status}>
          {#each STATUSES as status (status)}<option>{status}</option>{/each}
        </select>
      </div>
      <div class="field">
        <label>评分</label>
        <div class="stars">
          {#each [1, 2, 3, 4, 5] as star (star)}
            <span
              class="star"
              class:filled={star <= draft.rating}
              role="button"
              tabindex="-1"
              onclick={() => (draft.rating = draft.rating === star ? 0 : star)}
            >{star <= draft.rating ? "★" : "☆"}</span>
          {/each}
        </div>
      </div>
      <div class="field">
        <label>封面</label>
        <div class="cover-row">
          <input type="text" readonly value={draft.coverPath} placeholder="（未设置）" />
          <button class="outline" onclick={pickCover}>选择…</button>
          {#if draft.coverPath}<button class="danger" onclick={clearCover}>清除</button>{/if}
        </div>
      </div>
      <div class="field"><label>ROM 路径</label><input type="text" bind:value={draft.romPath} /></div>
      <div class="field">
        <label>其余碟片</label>
        <textarea rows="3" bind:value={draft.discPaths} placeholder="每行一个路径"></textarea>
      </div>
      <div class="field"><label>标签</label><input type="text" bind:value={draft.tags} placeholder="用逗号分隔" /></div>
      <div class="field"><label>备注</label><textarea rows="3" bind:value={draft.notes}></textarea></div>
    </div>
    <div class="modal-foot">
      <button class="outline" onclick={onClose}>取消</button>
      <button class="primary" onclick={save} disabled={saving}>{saving ? "保存中…" : "保存"}</button>
    </div>
  </div>
</div>

<datalist id="edit-platforms">
  {#each store.platformNames as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="edit-genres">
  {#each store.genres as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="edit-regions">
  {#each store.regions as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="edit-languages">
  {#each store.languages as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="edit-developers">
  {#each store.developers as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="edit-publishers">
  {#each store.publishers as option (option)}<option value={option}></option>{/each}
</datalist>

<style>
  .error {
    margin-bottom: 10px;
    padding: 6px 10px;
    border: 1px solid #f0c7c0;
    background: #fdf2f0;
    color: var(--danger);
    border-radius: 4px;
  }

  .cover-row {
    display: flex;
    flex: 1;
    gap: 6px;
  }

  .cover-row input {
    flex: 1;
  }

  .stars {
    display: inline-flex;
    gap: 2px;
  }

  .star {
    cursor: pointer;
    color: #c9ced5;
    font-size: 16px;
    line-height: 1;
  }

  .star.filled {
    color: #e6a817;
  }
</style>
