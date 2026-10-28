<script lang="ts">
  let {
    emulatorName,
    installDir,
    candidates,
    onPick,
    onCancel,
  }: {
    emulatorName: string;
    installDir: string;
    candidates: string[];
    onPick: (exe: string) => void;
    onCancel: () => void;
  } = $props();

  let selected = $state(candidates[0] ?? "");

  function relative(path: string): string {
    const dir = installDir.replace(/[\\/]+$/, "");
    if (!dir) return path;
    if (path.toLowerCase().startsWith(dir.toLowerCase())) {
      return path.slice(dir.length).replace(/^[\\/]+/, "");
    }
    return path;
  }
</script>

<div class="modal-mask" role="presentation">
  <div class="modal" style="width:560px" role="dialog" aria-modal="true">
    <div class="modal-head">
      <span>选择可执行文件 — {emulatorName}</span>
      <button onclick={onCancel} title="关闭">✕</button>
    </div>
    <div class="modal-body">
      <div class="hint">在安装目录中找到多个 exe，请选择启动用的可执行文件：</div>
      <ul class="exe-list">
        {#each candidates as path (path)}
          <li class:active={selected === path} role="button" tabindex="-1" onclick={() => (selected = path)}>
            {relative(path)}
          </li>
        {/each}
      </ul>
    </div>
    <div class="modal-foot">
      <button class="outline" onclick={onCancel}>取消</button>
      <button class="primary" onclick={() => (selected ? onPick(selected) : null)} disabled={!selected}>确定</button>
    </div>
  </div>
</div>

<style>
  .hint {
    margin-bottom: 8px;
    color: var(--text2);
    font-size: 12px;
  }

  .exe-list {
    height: 260px;
    margin: 0;
    padding: 0;
    overflow-y: auto;
    list-style: none;
    border: 1px solid var(--border);
    border-radius: 4px;
  }

  .exe-list li {
    padding: 6px 10px;
    cursor: default;
    user-select: none;
  }

  .exe-list li.active {
    background: var(--accent-weak, #e8f0fe);
  }
</style>
