<script lang="ts">
  let {
    hasSelection,
    onImport,
    onNew,
    onBatchEdit,
    onLaunch,
    onShare,
    onOpenFolder,
    onRefresh,
    onSettings,
  }: {
    hasSelection: boolean;
    onImport: () => void;
    onNew: () => void;
    onBatchEdit: () => void;
    onLaunch: () => void;
    onShare: () => void;
    onOpenFolder: () => void;
    onRefresh: () => void;
    onSettings: () => void;
  } = $props();
</script>

<div class="toolbar">
  <span class="brand" style="margin-right:14px">MICA ROM MANAGER</span>
  <button onclick={onImport} title="导入 ROM 文件或文件夹"><span class="icon">&#xE8E5;</span>导入…</button>
  <button onclick={onNew} title="手动新建一个条目"><span class="icon">&#xE710;</span>新建条目</button>
  <button onclick={onBatchEdit} disabled={!hasSelection} title="批量修改选中的游戏">
    <span class="icon">&#xE70F;</span>批量编辑
  </button>
  <button onclick={onLaunch} disabled={!hasSelection} title="启动选中的游戏（Enter）">
    <span class="icon">&#xE768;</span>启动游戏
  </button>
  <button onclick={onShare} disabled={!hasSelection} title="把选中游戏打包成可分享的目录">
    <span class="icon">&#xE72D;</span>分享打包
  </button>
  <button onclick={onOpenFolder} disabled={!hasSelection} title="打开 ROM 所在文件夹">
    <span class="icon">&#xE8B7;</span>打开目录
  </button>
  <button onclick={onRefresh} title="重新读取文件状态与数据">
    <span class="icon">&#xE72C;</span>刷新
  </button>
  <div class="spacer"></div>
  <button onclick={onSettings} title="设置"><span class="icon">&#xE713;</span>设置</button>
</div>
