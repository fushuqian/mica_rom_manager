# lite 功能追平主版本（WPF）实施计划

> 依据：`origin/main`（WPF v0.13.1）与 `lite` 分支逐项功能对比（详见 `.trae/documents/lite与主版本差异分析.md`）。
> 本轮范围（老板确认）：**仅界面功能缺口 + 内置 rar 解压**；不补长任务进度事件，不补新增单测。
> 不改动 `src/`、`tests/`（与 main 保持一致）；不改数据库 schema。

## 1. 摘要

lite 主流程（导入、列表、启动、分享、平台、字典、维护）已与 WPF 对齐，剩余缺口集中在 5 处：

1. 游戏编辑弹窗缺 `收藏`、`模拟器`、`ROM 路径浏览`、`文件大小`、封面缩略图，碟片只能手填多行文本；
2. 模拟器编辑弹窗缺 `安装目录` 及其浏览；
3. 模拟器安装完成后不能像 WPF 那样从多个 exe 候选中选择；
4. 设置「常规」缺「测试 GitHub 连接」；
5. `光碟数`、`格式` 两列不可排序，`格式` 用 `romPath` 排序语义不对；
6. rar 依赖本机 7-Zip/WinRAR，WPF 内置 SharpCompress。

本计划逐项补齐，rar 采用 `unrar-rs`（纯 Rust）内置解压，保留外部 7-Zip/WinRAR 作兜底。

## 2. 现状与缺口对照

| # | 缺口 | lite 现状 | WPF 行为 | 处理 |
| --- | --- | --- | --- | --- |
| 1 | 游戏编辑字段 | `GameEditDialog.svelte` 无收藏/模拟器/大小/预览，碟片为 textarea | `GameEditWindow.xaml` 有收藏、模拟器下拉、ROM 浏览、大小只读、封面 96×96 预览、碟片列表 4 按钮 | 补齐 |
| 2 | 模拟器安装目录 | `EmulatorEditDialog.svelte` 无安装目录字段 | `EmulatorEditWindow.xaml` 名称下一行：安装目录 + 浏览 | 补齐 |
| 3 | 多 exe 选择 | `SettingsDialog.svelte:202` 直接取 `exeCandidates[0]` | 0 个提示手填；1 个直接用；多个弹 `ExePickWindow` | 新增 `ExePickDialog.svelte` |
| 4 | 网络测试 | 常规页只有代理模式/地址 | `SettingsWindow` 有「测试 GitHub 连接」 | 新增命令 + 按钮 |
| 5 | 排序 | `grid.ts:32-33`：光碟数 `sortKey:null`，格式 `sortKey:"romPath"` | WPF 两列均可排序（DiscCount / Format） | 增加自定义排序值 |
| 6 | rar | `installer.rs:224-275` 调外部 7-Zip/WinRAR | 内置 SharpCompress | 引入 `unrar-rs` 内置，外部兜底 |

## 3. 变更清单（逐文件）

### 3.1 `app/web/components/GameEditDialog.svelte`

目标：字段与 `GameEditWindow.xaml` 对齐，保存仍走 `api.gamesSave`（后端已支持 `favorite/emulatorId/discPaths/fileSize`，无需改 Rust）。

- **收藏**：标题行右侧加 `<label class="check"><input type="checkbox" bind:checked={draft.favorite} />收藏</label>`。
- **模拟器**：状态下方新增下拉：
  ```svelte
  <select value={draft.emulatorId ?? ""} onchange={(e) => (draft.emulatorId = e.currentTarget.value ? Number(e.currentTarget.value) : null)}>
    <option value="">（不指定）</option>
    {#each store.emulators as emulator (emulator.id)}<option value={emulator.id}>{emulator.name}</option>{/each}
  </select>
  ```
  数据源用 `store.emulators`（启动时已加载，BatchEditDialog 也在用）。
- **ROM 路径 + 浏览…**：
  - 保留可编辑输入框，右侧加按钮。
  - `pickRom()` 用 `open({title:"选择 ROM 文件", filters:[{name:"ROM 文件", extensions: await api.importSupportedExtensions()}]})`（扩展名一次性缓存）。
  - 选中后：`setDisc(0, path)`；若标题为空或为「新建游戏」→ 取文件名去扩展名；若平台为空或「未指定」→ `api.detectPlatform(path)` 自动填；`draft.fileSize = await api.fileSize(path)`。
- **文件大小**：ROM 行下新增只读行 `<input readonly value={formatSize(draft.fileSize)} placeholder="（文件缺失或未计算）" />`。
- **封面缩略图**：封面控制行下方新增 96×96 预览：
  ```svelte
  let coverUrl = $state("");
  $effect(() => { const p = draft.coverPath; coverUrl = ""; if (p) api.coverDataUrl(p).then(u => coverUrl = u).catch(() => {}); });
  ```
  有图显示 `<img>`，无图显示占位边框；`clearCover()` 同步清空 `coverUrl`。
- **碟片管理**（替换现有 textarea）：
  - 初始化：`let discs = $state<string[]>(discsOf(draft))`（用 `format.ts` 已有的 `discs()`，第一项即 `romPath`）。
  - 列表逐条渲染：`碟 {i+1}：{fileName(path)}{exists ? "" : "（文件缺失）"}`；`exists` 通过 `api.fileExists` 批量刷新（打开弹窗时对每个碟片调用一次）。
  - 按钮：`添加碟片…`（多选、大小写不敏感去重）、`移除`（选中且数量 > 1）、`上移`、`下移`。
  - 保存前 `syncDiscs()`：`draft.romPath = discs[0] ?? ""`，`draft.discPaths = discs.slice(1).join("\n")`；再按现有逻辑刷新 `fileSize`。
  - 选中索引用 `selectedDisc = $state(0)`；移除后 `selectedDisc = Math.min(removed, discs.length - 1)`。

### 3.2 `app/web/components/EmulatorEditDialog.svelte`

- 在「名称」与「可执行文件」之间插入：
  ```svelte
  <label>安装目录</label>
  <div class="inline">
    <input type="text" bind:value={draft.installDir} placeholder="例如 E:\Emulators\RetroArch" />
    <button class="outline" onclick={pickInstallDir}>浏览…</button>
  </div>
  ```
- `pickInstallDir()`：`openDialog({title:"选择安装目录", directory:true, multiple:false})`，选中写 `draft.installDir`。
- 保存校验不变（仍要求名称 + exe），后端 `emulator_save` 已持久化 `install_dir`。

### 3.3 新增 `app/web/components/ExePickDialog.svelte`

Props：`candidates: string[]`、`installDir: string`、`onPick: (exe: string) => void`、`onCancel: () => void`。

- 标题：「在安装目录中找到多个 exe，请选择启动用的可执行文件：」。
- 列表项显示相对 `installDir` 的路径（`path.replace(installDir + "\\", "")` 兼容 `/`）；默认选中第 0 项（后端候选已按匹配度排序）。
- 按钮：确定（未选中时提示）、取消。
- 样式沿用现有 `.modal-mask/.modal` 体系，宽度 560px、列表高 260px。

### 3.4 `app/web/components/SettingsDialog.svelte`

- 引入 `ExePickDialog` 与 `fileName` 工具。
- `finishInstall` 改为：
  ```ts
  async function finishInstall(emulator, outcome) {
    const list = outcome.exeCandidates;
    if (list.length === 0) { await applyInstall(emulator, outcome, ""); note("emulators", `已解压到 ${outcome.installDir}，未找到 exe，请用「编辑…」手动指定。`); return; }
    if (list.length === 1) { await applyInstall(emulator, outcome, list[0]); return; }
    exePick = { emulator, outcome, candidates: list };
  }
  async function applyInstall(emulator, outcome, exe) { await api.emulatorApplyInstall(emulator.id, outcome.installDir, exe); await refreshEmulatorList(emulator.id); note(...); }
  ```
  新增 `let exePick = $state<{emulator; outcome; candidates} | null>(null)`；弹窗确定→`applyInstall` 后清空；取消→`applyInstall(..., "")` 并在 note 说明「已解压，未指定 exe」。
- 在文件末尾与 `EmulatorEditDialog` 同级渲染 `ExePickDialog`。
- 常规页代理地址行下方新增：
  ```svelte
  <label>网络测试</label>
  <div class="inline">
    <button class="outline" onclick={testGithub} disabled={busy}>测试 GitHub 连接</button>
    <span class="note">{networkNote}</span>
  </div>
  ```
  `testGithub()` 调 `api.networkTestGithub()`，成功/失败都写入 `networkNote`；`busy` 期间禁用。

### 3.5 `app/web/lib/api.ts`

新增：
```ts
networkTestGithub: () => invoke<string>("network_test_github"),
detectPlatform: (path: string) => invoke<string | null>("detect_platform", { path }),
```

### 3.6 排序：`app/web/lib/grid.ts`、`App.svelte`、`GameGrid.svelte`

- `GridColumn` 增加 `sortValue?: (game: Game) => string | number`。
- 两列改为：
  ```ts
  { header: "光碟数", width: 60, sortKey: null, sortValue: (g) => discCount(g), ... }
  { header: "格式", width: 60, sortKey: null, sortValue: (g) => formatExt(g), ... }
  ```
  （`discCount`、`formatExt` 已在文件顶部导入）
- `compareGames` 签名改为 `(a, b, column: GridColumn, dir)`：取值优先 `column.sortValue?.(g)`，否则 `g[column.sortKey!]`；通用比较/空值/降序逻辑复用现有实现。
- `App.svelte`：`sortedGames`（约 49-53 行）改用 `compareGames(a, b, column, direction)`；`toggleSort`（约 81 行）与列头可排序判断改为 `column.sortKey !== null || !!column.sortValue`。
- `GameGrid.svelte`（约 185/189/190 行）把 `column.sortKey !== null` 同步改为 `canSort(column)`（从 grid 导出一个 `canSort` 辅助函数，避免重复表达式）。

### 3.7 rar 内置解压：`app/src-tauri/Cargo.toml`、`services/installer.rs`

- 依赖：
  ```toml
  unrar-rs = { version = "0.14", default-features = false, features = ["crypto-rust"] }
  ```
  说明：`crypto-rust` 走纯 Rust 加密后端，避免 aws-lc 的 C 工具链；当前只解未加密压缩包，行为与 WPF 一致。
- `installer.rs` 将 `extract_rar` 改为「内置优先、外部兜底」：
  ```rust
  fn extract_rar(temp: &Path, archive: &Path) -> Res<()> {
      match extract_rar_builtin(temp, archive) {
          Ok(()) => Ok(()),
          Err(builtin) => extract_rar_external(temp, archive)
              .map_err(|external| format!("内置解压失败：{builtin}；外部工具：{external}")),
      }
  }

  fn extract_rar_builtin(temp: &Path, archive: &Path) -> Res<()> {
      let file = std::fs::File::open(archive).map_err(|e| format!("打开压缩包失败：{e}"))?;
      let mut rar = unrar_rs::RarArchive::open(file).map_err(|e| format!("读取 rar 失败：{e}"))?;
      for index in 0..rar.len() {
          let entry = rar.by_index(index).map_err(|e| format!("读取 rar 条目失败：{e}"))?;
          entry.unpack_in(temp).map_err(|e| format!("解压 rar 失败：{e}"))?;
      }
      Ok(())
  }
  ```
  原 `extract_rar` 内 7-Zip/WinRAR 查找逻辑原样保留，改名为 `extract_rar_external`。
- 若内置解压中途失败，外部兜底会向同一 `temp` 继续解压（可能重复写文件，无害）；最终 `find_extracted_root` 行为不变。

### 3.8 命令：`commands/system.rs`、`commands/emulators` 依赖、`lib.rs`

- `services/installer.rs`：`fn client` 改为 `pub(crate) fn client`（供网络测试复用代理/超时配置）。
- `commands/system.rs` 新增：
  ```rust
  #[tauri::command]
  pub async fn network_test_github(st: State<'_, AppState>) -> Res<String> {
      let client = crate::services::installer::client(&st)?;
      let response = client
          .get("https://api.github.com/rate_limit")
          .timeout(std::time::Duration::from_secs(10))
          .header("Accept", "application/vnd.github+json")
          .send()
          .await
          .map_err(|e| format!("连接 GitHub 失败：{e}"))?;
      if response.status().is_success() {
          Ok(format!("连接成功（HTTP {}）", response.status()))
      } else {
          Err(format!("GitHub 返回状态：{}", response.status()))
      }
  }

  #[tauri::command]
  pub fn detect_platform(path: String) -> Option<String> {
      crate::services::extension_map::ExtensionMap::detect(&path).platform
  }
  ```
- `lib.rs` 的 `generate_handler!` 注册 `commands::system::network_test_github`、`commands::system::detect_platform`。

### 3.9 文档与规则同步（收尾）

- `QUEST.md`：新增一条需求记录（描述、状态=已完成、优先级、时间）。
- `CHANGELOG.md`：新增「功能追平主版本」条目，列 6 项变更。
- `MEMORIES.md`：记录 `unrar-rs` 可纯 Rust 解 rar（关闭 aws-lc 默认特性）。
- `IMPORTANT.md`：无新增密钥，不写。
- 关于页 `SettingsDialog.svelte` 增加一行「RAR 解压：unrar-rs（RARLAB UnRAR 许可，仅解压）」，作为许可提示。

## 4. 关键行为与边界

| 场景 | 规则 |
| --- | --- |
| 安装候选 0 个 | 仍写回 `installDir`，exe 保持原值，提示手动指定（与 WPF 一致） |
| 安装候选 1 个 | 直接写回，不弹窗 |
| 安装候选多个 | 弹 `ExePickDialog`，默认第 0 项；取消也写回 `installDir` |
| 单碟片 | 「移除」禁用；清空所有碟片后 `romPath`/`discPaths` 置空（沿用后端能力） |
| 添加碟片 | 大小写不敏感去重；缺文件显示「（文件缺失）」 |
| 排序 | 光碟数按实际碟片数、格式按扩展名；空值始终排最后；再次点击取消排序 |
| 网络测试 | 走当前代理设置；10 秒超时；成功/失败都显示在按钮右侧 |
| rar | 内置失败才调外部；两者都失败时错误信息同时包含两条原因 |

## 5. 假设与决策

1. 主版本 = `origin/main`（WPF v0.13.1）；lite 的 `src/`/`tests/` 与 main 相同，保持不动。
2. rar 内置方案由老板确认；外部 7-Zip/WinRAR 兜底保留。
3. 本轮不做进度事件、不做新增单测；现有 9 个 Rust 测试必须保持通过。
4. 游戏编辑仍允许空 ROM 路径（lite 现有行为），只补字段与交互，不引入 WPF 的「ROM 必填」校验，避免影响现有新建流程。
5. 模拟器编辑保留 lite 多出的启动参数/核心/配置/支持格式/绑定平台/GitHub 字段（信息更全），仅补安装目录。
6. 不删除 `maintenance_sync_tags` 死命令（非本轮范围）。

## 6. 验证步骤

```powershell
# 后端
cd app\src-tauri
cargo fmt --check
cargo check
cargo test --lib

# 前端
cd app
npm run check
npm run build
```

人工冒烟（老板或执行者）：

1. 设置 → 模拟器管理 → 从压缩包安装一个含多个 exe 的模拟器 → 应弹出候选选择；取消后安装目录已写入、exe 未变。
2. 用一个 `.rar` 包安装（机器上临时改名 7-Zip/WinRAR 目录不可用）→ 应成功解压（内置）。
3. 编辑游戏：改收藏、指定模拟器、浏览 ROM（标题/平台自动带出）、查看大小与封面、添加/移除/上移/下移碟片 → 保存后列表与再次打开一致。
4. 设置 → 常规 → 测试 GitHub 连接（分别切系统代理/不使用/手动）→ 显示成功或明确错误。
5. 列表点击「光碟数」「格式」表头 → 升/降序正确，第三次点击取消排序。
6. 设置 → 模拟器管理 → 编辑 → 修改安装目录并保存 → 重复打开仍保留。

## 7. 风险

- `unrar-rs` 首次编译会拉取新依赖；若 Windows/MSVC 构建异常，回退方案是「只保留外部 7-Zip/WinRAR + 更清晰提示」，并回报老板。
- 碟片改造涉及保存语义（第一碟=romPath），需重点回归合并/拆分多碟。
- 排序函数签名变化只影响 `App.svelte` 一处调用，`GameGrid` 只改可排序判断，风险低。
