<script lang="ts">
  import { open } from "@tauri-apps/plugin-dialog";
  import { api, type Game } from "../lib/api";
  import { clone, discs as discsOf, fileName, formatSize } from "../lib/format";
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
  let coverUrl = $state("");
  let discList = $state<string[]>(discsOf(initial));
  let selectedDisc = $state(discList.length > 0 ? 0 : -1);
  let discMissing = $state<boolean[]>([]);
  let romExtensions = $state<string[] | null>(null);

  $effect(() => {
    const path = draft.coverPath;
    coverUrl = "";
    if (!path) return;
    let cancelled = false;
    api
      .coverDataUrl(path)
      .then((url) => {
        if (!cancelled) coverUrl = url;
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  });

  $effect(() => {
    const paths = [...discList];
    let cancelled = false;
    Promise.all(
      paths.map((path) => (path.trim() ? api.fileExists(path).catch(() => false) : Promise.resolve(false)))
    ).then((flags) => {
      if (!cancelled) discMissing = flags;
    });
    return () => {
      cancelled = true;
    };
  });

  async function supportedExtensions(): Promise<string[]> {
    if (!romExtensions) {
      try {
        romExtensions = await api.importSupportedExtensions();
      } catch {
        romExtensions = [];
      }
    }
    return romExtensions;
  }

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
    coverUrl = "";
  }

  function setDisc(index: number, path: string) {
    if (index < discList.length) discList[index] = path;
    else discList.push(path);
  }

  async function pickRom() {
    const extensions = await supportedExtensions();
    const picked = await open({
      title: "选择 ROM 文件",
      multiple: false,
      filters: extensions.length > 0 ? [{ name: "ROM 文件", extensions }] : undefined,
    });
    if (typeof picked !== "string" || !picked) return;
    setDisc(0, picked);
    draft.romPath = picked;
    if (!draft.title.trim() || draft.title.trim() === "新建游戏") {
      draft.title = fileName(picked).replace(/\.[^.]+$/, "");
    }
    if (!draft.platform.trim() || draft.platform === "未指定") {
      try {
        const platform = await api.detectPlatform(picked);
        if (platform) draft.platform = platform;
      } catch {
        // 识别失败时保留原平台
      }
    }
    try {
      draft.fileSize = await api.fileSize(picked);
    } catch {
      // 文件不存在时保留原大小
    }
  }

  async function addDiscs() {
    const extensions = await supportedExtensions();
    const picked = await open({
      title: "添加碟片",
      multiple: true,
      filters: extensions.length > 0 ? [{ name: "ROM 文件", extensions }] : undefined,
    });
    const files = Array.isArray(picked) ? picked : typeof picked === "string" ? [picked] : [];
    for (const file of files) {
      if (!discList.some((path) => path.toLowerCase() === file.toLowerCase())) discList.push(file);
    }
  }

  function removeDisc() {
    if (selectedDisc < 0 || selectedDisc >= discList.length || discList.length <= 1) return;
    const removed = selectedDisc;
    discList.splice(removed, 1);
    selectedDisc = Math.min(removed, discList.length - 1);
  }

  function moveDiscUp() {
    if (selectedDisc <= 0) return;
    const index = selectedDisc;
    [discList[index - 1], discList[index]] = [discList[index], discList[index - 1]];
    selectedDisc = index - 1;
  }

  function moveDiscDown() {
    if (selectedDisc < 0 || selectedDisc >= discList.length - 1) return;
    const index = selectedDisc;
    [discList[index + 1], discList[index]] = [discList[index], discList[index + 1]];
    selectedDisc = index + 1;
  }

  function syncDiscs() {
    draft.romPath = discList[0] ?? "";
    draft.discPaths = discList.slice(1).join("\n");
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
      syncDiscs();
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

      <div class="field">
        <label>标题</label>
        <div class="title-row">
          <input type="text" bind:value={draft.title} />
          <label class="check"><input type="checkbox" bind:checked={draft.favorite} />收藏</label>
        </div>
      </div>
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
        <label>模拟器</label>
        <select
          value={draft.emulatorId ?? ""}
          onchange={(e) => (draft.emulatorId = e.currentTarget.value ? Number(e.currentTarget.value) : null)}
        >
          <option value="">（不指定）</option>
          {#each store.emulators as emulator (emulator.id)}
            <option value={emulator.id}>{emulator.name}</option>
          {/each}
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
      <div class="field">
        <label>封面预览</label>
        {#if coverUrl}
          <img class="cover-preview" src={coverUrl} alt="封面预览" />
        {:else}
          <div class="cover-preview placeholder">无封面</div>
        {/if}
      </div>
      <div class="field">
        <label>ROM 路径</label>
        <div class="cover-row">
          <input
            type="text"
            value={draft.romPath}
            oninput={(e) => {
              draft.romPath = e.currentTarget.value;
              if (discList.length > 0) discList[0] = draft.romPath;
              else if (draft.romPath.trim()) discList.push(draft.romPath);
            }}
          />
          <button class="outline" onclick={pickRom}>浏览…</button>
        </div>
      </div>
      <div class="field">
        <label>文件大小</label>
        <input type="text" readonly value={formatSize(draft.fileSize)} placeholder="（文件缺失或未计算）" />
      </div>
      <div class="field">
        <label>碟片</label>
        <div class="discs">
          <div class="disc-actions">
            <button class="outline" onclick={addDiscs}>添加碟片…</button>
            <button class="outline" onclick={removeDisc} disabled={selectedDisc < 0 || discList.length <= 1}>移除</button>
            <button class="outline" onclick={moveDiscUp} disabled={selectedDisc <= 0}>上移</button>
            <button class="outline" onclick={moveDiscDown} disabled={selectedDisc < 0 || selectedDisc >= discList.length - 1}>
              下移
            </button>
          </div>
          <ul class="disc-list">
            {#each discList as path, index (index)}
              <li
                class:active={selectedDisc === index}
                role="button"
                tabindex="-1"
                onclick={() => (selectedDisc = index)}
              >
                <span class="ellipsis">碟 {index + 1}：{fileName(path)}</span>
                {#if discMissing[index] === false}<span class="missing">（文件缺失）</span>{/if}
              </li>
            {/each}
            {#if discList.length === 0}
              <li class="empty">还没有碟片</li>
            {/if}
          </ul>
        </div>
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

  .title-row {
    display: flex;
    flex: 1;
    gap: 10px;
    align-items: center;
  }

  .title-row > input {
    flex: 1;
  }

  .check {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    white-space: nowrap;
  }

  .cover-preview {
    width: 96px;
    height: 96px;
    object-fit: cover;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
  }

  .cover-preview.placeholder {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text2);
    font-size: 12px;
  }

  .discs {
    display: flex;
    flex-direction: column;
    flex: 1;
    gap: 6px;
  }

  .disc-actions {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
  }

  .disc-list {
    height: 120px;
    margin: 0;
    padding: 0;
    overflow-y: auto;
    list-style: none;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--surface);
  }

  .disc-list li {
    display: flex;
    gap: 6px;
    padding: 4px 8px;
    font-size: 12px;
    cursor: default;
    user-select: none;
  }

  .disc-list li.active {
    background: var(--accent-weak, #e8f0fe);
  }

  .disc-list li.empty {
    color: var(--text2);
    cursor: default;
  }

  .missing {
    color: var(--danger);
  }

  .ellipsis {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
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
