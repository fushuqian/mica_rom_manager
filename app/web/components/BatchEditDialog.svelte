<script lang="ts">
  import type { BatchChanges } from "../lib/api";
  import { STATUSES, store } from "../lib/state.svelte";

  let {
    selectedCount,
    onClose,
    onApply,
  }: {
    selectedCount: number;
    onClose: () => void;
    onApply: (changes: BatchChanges) => void;
  } = $props();

  let applyPlatform = $state(false);
  let platformValue = $state("");
  let applyGenre = $state(false);
  let genreValue = $state("");
  let applyRegion = $state(false);
  let regionValue = $state("");
  let applyLanguage = $state(false);
  let languageValue = $state("");
  let applyReleaseYear = $state(false);
  let releaseYearText = $state("");
  let applyPublisher = $state(false);
  let publisherValue = $state("");
  let applyDeveloper = $state(false);
  let developerValue = $state("");
  let applyStatus = $state(false);
  let statusValue = $state("未玩");
  let applyEmulator = $state(false);
  let emulatorId = $state<string>("0");
  let applyTagsAdd = $state(false);
  let tagsAddValue = $state("");
  let applyTagsRemove = $state(false);
  let tagsRemoveValue = $state("");
  let applyNotes = $state(false);
  let notesMode = $state("append");
  let notesValue = $state("");
  let error = $state("");

  function splitTags(text: string): string[] {
    return text
      .split(/[,，;；]/)
      .map((s) => s.trim())
      .filter(Boolean);
  }

  function submit() {
    error = "";
    const changes: BatchChanges = {};

    if (applyPlatform) {
      const platform = platformValue.trim();
      if (!platform) {
        error = "平台不能为空";
        return;
      }
      changes.platform = platform;
    }
    if (applyGenre) changes.genre = genreValue.trim();
    if (applyRegion) changes.region = regionValue.trim();
    if (applyLanguage) changes.language = languageValue.trim();
    if (applyReleaseYear) {
      const text = releaseYearText.trim();
      if (!text) {
        changes.clearReleaseYear = true;
      } else {
        const year = Number(text);
        if (!Number.isInteger(year) || year < 1950 || year > 2100) {
          error = "年份格式不正确（1950–2100 的整数，留空表示清空）";
          return;
        }
        changes.releaseYear = year;
      }
    }
    if (applyPublisher) changes.publisher = publisherValue.trim();
    if (applyDeveloper) changes.developer = developerValue.trim();
    if (applyStatus) changes.status = statusValue;
    if (applyEmulator) {
      const id = Number(emulatorId);
      if (id > 0) changes.emulatorId = id;
      else changes.clearEmulator = true;
    }
    if (applyTagsAdd) changes.tagsAdd = splitTags(tagsAddValue);
    if (applyTagsRemove) changes.tagsRemove = splitTags(tagsRemoveValue);
    if (applyNotes) {
      changes.notesMode = notesMode;
      changes.notes = notesValue;
    }

    if (Object.keys(changes).length === 0) {
      error = "请至少勾选一个要修改的字段";
      return;
    }
    onApply(changes);
  }
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:640px;height:720px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>批量编辑（已选 {selectedCount} 条）</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      <div class="lead">只修改勾选的字段，未勾选的保持原值。</div>

      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyPlatform} />平台</label>
        <input type="text" list="batch-platforms" bind:value={platformValue} disabled={!applyPlatform} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyGenre} />类型</label>
        <input type="text" list="batch-genres" bind:value={genreValue} disabled={!applyGenre} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyRegion} />区域</label>
        <input type="text" list="batch-regions" bind:value={regionValue} disabled={!applyRegion} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyLanguage} />语言</label>
        <input type="text" list="batch-languages" bind:value={languageValue} disabled={!applyLanguage} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyReleaseYear} />年份</label>
        <input
          type="text"
          bind:value={releaseYearText}
          disabled={!applyReleaseYear}
          placeholder="1950–2100；留空表示清空"
        />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyPublisher} />发行商</label>
        <input type="text" list="batch-publishers" bind:value={publisherValue} disabled={!applyPublisher} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyDeveloper} />开发商</label>
        <input type="text" list="batch-developers" bind:value={developerValue} disabled={!applyDeveloper} />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyStatus} />状态</label>
        <select bind:value={statusValue} disabled={!applyStatus}>
          {#each STATUSES as status (status)}<option>{status}</option>{/each}
        </select>
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyEmulator} />模拟器</label>
        <select bind:value={emulatorId} disabled={!applyEmulator}>
          <option value="0">（不指定）</option>
          {#each store.emulators.filter((e) => e.enabled) as emulator (emulator.id)}
            <option value={String(emulator.id)}>{emulator.name}</option>
          {/each}
        </select>
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyTagsAdd} />追加标签</label>
        <input type="text" bind:value={tagsAddValue} disabled={!applyTagsAdd} placeholder="多个标签用逗号分隔" />
      </div>
      <div class="row">
        <label class="check"><input type="checkbox" bind:checked={applyTagsRemove} />移除标签</label>
        <input
          type="text"
          bind:value={tagsRemoveValue}
          disabled={!applyTagsRemove}
          placeholder="多个标签用逗号分隔"
        />
      </div>
      <div class="row notes-row">
        <label class="check"><input type="checkbox" bind:checked={applyNotes} />备注</label>
        <div class="notes">
          <select bind:value={notesMode} disabled={!applyNotes}>
            <option value="append">追加到原备注末尾</option>
            <option value="replace">覆盖原备注</option>
          </select>
          <textarea rows="4" bind:value={notesValue} disabled={!applyNotes}></textarea>
        </div>
      </div>

      {#if error}<div class="error">{error}</div>{/if}
    </div>
    <div class="modal-foot">
      <button class="outline" onclick={onClose}>取消</button>
      <button class="primary" onclick={submit}>应用</button>
    </div>
  </div>
</div>

<datalist id="batch-platforms">
  {#each store.platformNames as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="batch-genres">
  {#each store.genres as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="batch-regions">
  {#each store.regions as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="batch-languages">
  {#each store.languages as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="batch-publishers">
  {#each store.publishers as option (option)}<option value={option}></option>{/each}
</datalist>
<datalist id="batch-developers">
  {#each store.developers as option (option)}<option value={option}></option>{/each}
</datalist>

<style>
  .lead {
    margin-bottom: 10px;
    color: var(--text2);
    font-size: 12px;
  }

  .row {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 9px;
  }

  .row > :global(input[type="text"]),
  .row > select {
    flex: 1;
  }

  .check {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 104px;
    flex: none;
  }

  .notes-row {
    align-items: flex-start;
  }

  .notes {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .notes textarea {
    resize: vertical;
  }

  .error {
    margin-top: 4px;
    color: var(--danger);
    font-size: 12px;
  }
</style>
