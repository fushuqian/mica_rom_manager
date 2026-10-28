<script lang="ts">
  import { open as openDialog } from "@tauri-apps/plugin-dialog";
  import { api, type Emulator } from "../lib/api";
  import { clone } from "../lib/format";

  let {
    emulator,
    platformOptions,
    onClose,
    onSaved,
  }: {
    emulator: Emulator;
    platformOptions: string[];
    onClose: () => void;
    onSaved: (emulator: Emulator) => void;
  } = $props();

  let draft = $state<Emulator>(clone(emulator));
  let platformsText = $state(draft.platforms.join(","));
  let error = $state("");
  let saving = $state(false);
  let isNew = emulator.id === 0;

  async function pickExe() {
    const picked = await openDialog({
      title: "选择模拟器可执行文件",
      multiple: false,
      filters: [{ name: "可执行文件", extensions: ["exe", "bat", "cmd"] }],
    });
    if (typeof picked === "string" && picked) {
      draft.exePath = picked;
      if (!draft.installDir.trim()) {
        draft.installDir = picked.replace(/[\\/][^\\/]+$/, "");
      }
      if (!draft.name) {
        const file = picked.split(/[\\/]/).pop() ?? "";
        draft.name = file.replace(/\.(exe|bat|cmd)$/i, "");
      }
    }
  }

  async function pickInstallDir() {
    const picked = await openDialog({ title: "选择安装目录", directory: true, multiple: false });
    if (typeof picked === "string" && picked) draft.installDir = picked;
  }

  async function pickCore() {
    const picked = await openDialog({
      title: "选择核心文件",
      multiple: false,
      filters: [{ name: "核心", extensions: ["dll", "so"] }],
    });
    if (typeof picked === "string" && picked) draft.corePath = picked;
  }

  async function pickConfig() {
    const picked = await openDialog({ title: "选择配置文件", multiple: false });
    if (typeof picked === "string" && picked) draft.configPath = picked;
  }

  async function save() {
    if (!draft.name.trim()) {
      error = "模拟器名称不能为空。";
      return;
    }
    if (!draft.exePath.trim()) {
      error = "请选择模拟器可执行文件。";
      return;
    }
    saving = true;
    error = "";
    try {
      draft.platforms = platformsText
        .split(/[,，;；]/)
        .map((s) => s.trim())
        .filter(Boolean);
      if (draft.defaultPlatforms.length === 0) draft.defaultPlatforms = [...draft.platforms];
      const saved = await api.emulatorSave(clone(draft));
      onSaved(saved);
    } catch (e) {
      error = String(e);
    } finally {
      saving = false;
    }
  }
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:660px;height:640px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>{isNew ? "新增模拟器" : `编辑模拟器 — ${emulator.name}`}</span>
      <button onclick={onClose} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      {#if error}<div class="error">{error}</div>{/if}

      <div class="grid">
        <label>名称</label>
        <input type="text" bind:value={draft.name} />

        <label>安装目录</label>
        <div class="inline">
          <input type="text" bind:value={draft.installDir} placeholder="例如 E:\Emulators\RetroArch" />
          <button class="outline" onclick={pickInstallDir}>浏览…</button>
        </div>

        <label>可执行文件</label>
        <div class="inline">
          <input type="text" bind:value={draft.exePath} placeholder="例如 E:\Emulators\RetroArch\retroarch.exe" />
          <button class="outline" onclick={pickExe}>浏览…</button>
        </div>

        <label>启动参数</label>
        <input type="text" bind:value={draft.argsTemplate} placeholder={'例如 "{rom}" 或 -L "{core}" "{rom}"'} />

        <label>核心路径</label>
        <div class="inline">
          <input type="text" bind:value={draft.corePath} placeholder="（可选）" />
          <button class="outline" onclick={pickCore}>浏览…</button>
        </div>

        <label>配置文件</label>
        <div class="inline">
          <input type="text" bind:value={draft.configPath} placeholder="（可选）" />
          <button class="outline" onclick={pickConfig}>浏览…</button>
        </div>

        <label>支持格式</label>
        <input type="text" bind:value={draft.supportedExt} placeholder="逗号分隔，例如：nes,sfc,gb" />

        <label>绑定平台</label>
        <div class="stack">
          <input type="text" list="emulator-platforms" bind:value={platformsText} placeholder="逗号分隔，例如：NES,SNES" />
          <div class="hint">绑定后，该平台游戏默认用这个模拟器启动。</div>
        </div>

        <label>版本</label>
        <input type="text" bind:value={draft.version} placeholder="例如 1.19.0" />

        <label>GitHub 仓库</label>
        <input type="text" bind:value={draft.githubRepo} placeholder="owner/repo，例如 libretro/RetroArch" />

        <label>官网</label>
        <input type="text" bind:value={draft.websiteUrl} placeholder="https://" />

        <label>备注</label>
        <textarea rows="3" bind:value={draft.notes}></textarea>

        <label>启用</label>
        <label class="check"><input type="checkbox" bind:checked={draft.enabled} />在启动菜单中显示</label>
      </div>
    </div>
    <div class="modal-foot">
      <button class="outline" onclick={onClose}>取消</button>
      <button class="primary" onclick={save} disabled={saving}>{saving ? "保存中…" : "保存"}</button>
    </div>
  </div>
</div>

<datalist id="emulator-platforms">
  {#each platformOptions as option (option)}<option value={option}></option>{/each}
</datalist>

<style>
  .grid {
    display: grid;
    grid-template-columns: 96px 1fr;
    gap: 9px 12px;
    align-items: center;
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

  .stack {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .hint {
    color: var(--text2);
    font-size: 12px;
  }

  .check {
    display: flex;
    align-items: center;
    gap: 6px;
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
