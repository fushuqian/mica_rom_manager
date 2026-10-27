use crate::db::{games, platforms};
use crate::models::{Emulator, Game};
use crate::services::{args, launcher};
use crate::state::AppState;
use crate::util::Res;
use serde::Serialize;
use std::collections::HashSet;
use std::io::Write;
use std::path::{Path, PathBuf};
use walkdir::WalkDir;

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ShareRow {
    pub title: String,
    pub platform: String,
    pub emulator_name: String,
    pub note: String,
    pub has_emulator: bool,
    pub rom_exists: bool,
}

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct SharePackResult {
    pub package_dir: String,
    pub zip_path: Option<String>,
    pub total_bytes: i64,
    pub warnings: Vec<String>,
    pub launchers: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ShareEstimate {
    pub bytes: i64,
    pub items: usize,
}

/// 打包预览：逐条解析模拟器与 ROM 状态。
pub fn rows(st: &AppState, game_ids: &[i64]) -> Res<Vec<ShareRow>> {
    let mut list = Vec::new();
    for id in game_ids {
        let Some(game) = games::get(st, *id)? else {
            continue;
        };
        let emulator = launcher::resolve_emulator(st, &game, None)?;
        let rom_path = st.expand(&game.rom_path);
        let rom_exists = !rom_path.trim().is_empty() && Path::new(&rom_path).is_file();
        let mut notes: Vec<&str> = Vec::new();
        if emulator.is_none() {
            notes.push("未配置模拟器");
        }
        if !rom_exists {
            notes.push("ROM 缺失");
        }
        list.push(ShareRow {
            title: game.title.clone(),
            platform: game.platform.clone(),
            emulator_name: emulator
                .as_ref()
                .map(|e| e.name.clone())
                .unwrap_or_else(|| "—".into()),
            note: notes.join("；"),
            has_emulator: emulator.is_some(),
            rom_exists,
        });
    }
    Ok(list)
}

pub fn estimate(
    st: &AppState,
    game_ids: &[i64],
    include_emulators: bool,
    save_dir: Option<&str>,
) -> Res<ShareEstimate> {
    let mut bytes = 0i64;
    let mut items = 0usize;
    let mut seen_emulators: HashSet<i64> = HashSet::new();

    for id in game_ids {
        let Some(game) = games::get(st, *id)? else {
            continue;
        };
        let Some(emulator) = launcher::resolve_emulator(st, &game, None)? else {
            continue;
        };
        for disc in game.discs() {
            let path = st.expand(&disc);
            if let Ok(meta) = std::fs::metadata(&path) {
                bytes += meta.len() as i64;
            }
        }
        items += 1;

        if include_emulators && seen_emulators.insert(emulator.id) {
            if let Some(dir) = emulator_source_dir(&emulator) {
                bytes += directory_size(&dir);
            }
        }
    }

    if let Some(dir) = save_dir {
        let expanded = st.expand(dir);
        if Path::new(&expanded).is_dir() {
            bytes += directory_size(Path::new(&expanded));
        }
    }

    Ok(ShareEstimate { bytes, items })
}

#[allow(clippy::too_many_arguments)]
pub fn export(
    st: &AppState,
    game_ids: &[i64],
    export_root: &str,
    package_name: &str,
    include_emulators: bool,
    include_saves: bool,
    save_dir: Option<&str>,
    create_zip: bool,
) -> Res<SharePackResult> {
    if game_ids.is_empty() {
        return Err("没有可打包的游戏。".into());
    }

    let root = PathBuf::from(export_root.trim());
    if root.as_os_str().is_empty() {
        return Err("请先选择导出位置。".into());
    }
    std::fs::create_dir_all(&root).map_err(|e| format!("创建导出目录失败：{e}"))?;

    let mut folder = sanitize_name(package_name);
    if folder.is_empty() {
        folder = "ROM分享包".into();
    }
    let mut package_dir = root.join(&folder);
    if package_dir.exists() {
        let stamp = chrono::Local::now().format("%Y%m%d%H%M%S");
        package_dir = root.join(format!("{folder}-{stamp}"));
    }
    std::fs::create_dir_all(&package_dir).map_err(|e| format!("创建分享包目录失败：{e}"))?;

    let mut result = SharePackResult {
        package_dir: package_dir.to_string_lossy().to_string(),
        ..Default::default()
    };

    // (emulator id, platform) -> (exe 表达式, 核心表达式)
    let mut layouts: std::collections::HashMap<(i64, String), (String, String)> =
        std::collections::HashMap::new();
    let mut prepared: HashSet<i64> = HashSet::new();

    let mut items: Vec<(Game, Emulator)> = Vec::new();
    for id in game_ids {
        let Some(game) = games::get(st, *id)? else {
            continue;
        };
        match launcher::resolve_emulator(st, &game, None)? {
            Some(emulator) => items.push((game, emulator)),
            None => {
                if let Some(game) = games::get(st, *id)? {
                    result
                        .warnings
                        .push(format!("「{}」未配置模拟器，无法生成启动器。", game.title));
                }
            }
        }
    }

    if include_emulators {
        for (game, emulator) in &items {
            let Some(source_dir) = emulator_source_dir(emulator) else {
                if prepared.insert(emulator.id) {
                    result.warnings.push(format!(
                        "模拟器「{}」找不到安装目录，未打包；启动器将使用原路径。",
                        emulator.name
                    ));
                }
                continue;
            };

            let mut emulator_folder = sanitize_name(&emulator.name);
            if emulator_folder.is_empty() {
                emulator_folder = "emulator".into();
            }
            let package_emulator_dir = package_dir.join("emulators").join(&emulator_folder);

            if prepared.insert(emulator.id) {
                if let Err(e) = copy_directory(&source_dir, &package_emulator_dir) {
                    result
                        .warnings
                        .push(format!("复制模拟器「{}」失败：{e}", emulator.name));
                    continue;
                }
            }

            let exe_abs = resolve_inside(&emulator.exe_path, &emulator.install_dir);
            let setting = platforms::get(st, &game.platform)?;
            let platform_core = setting
                .as_ref()
                .map(|s| s.core_path.clone())
                .unwrap_or_default();
            let core_abs = resolve_inside(&platform_core, &emulator.install_dir)
                .or_else(|| resolve_inside(&emulator.core_path, &emulator.install_dir));

            layouts.insert(
                (emulator.id, game.platform.clone()),
                (
                    relative_inside(exe_abs.as_deref(), &source_dir, &emulator_folder),
                    relative_inside(core_abs.as_deref(), &source_dir, &emulator_folder),
                ),
            );
        }
    }

    if include_saves {
        if let Some(dir) = save_dir {
            let expanded = st.expand(dir);
            if Path::new(&expanded).is_dir() {
                if let Err(e) = copy_directory(Path::new(&expanded), &package_dir.join("saves")) {
                    result.warnings.push(format!("复制存档失败：{e}"));
                }
            }
        }
    }

    let mut used_names: HashSet<String> = HashSet::new();
    for (game, emulator) in &items {
        let discs = game.discs();
        if discs.is_empty() {
            result
                .warnings
                .push(format!("游戏「{}」的 ROM 文件不存在，已跳过。", game.title));
            continue;
        }
        let setting = platforms::get(st, &game.platform)?;
        let template = args::resolve_template(
            setting.as_ref().map(|s| s.args_template.as_str()),
            Some(&emulator.name),
            Some(&emulator.args_template),
        );

        let (layout_exe, layout_core) = layouts
            .get(&(emulator.id, game.platform.clone()))
            .cloned()
            .unwrap_or_default();
        let exe_expression = if !layout_exe.is_empty() {
            layout_exe
        } else {
            resolve_inside(&emulator.exe_path, &emulator.install_dir)
                .map(|p| p.to_string_lossy().to_string())
                .unwrap_or_else(|| emulator.exe_path.clone())
        };
        let core_expression = if !layout_core.is_empty() {
            layout_core
        } else {
            let platform_core = setting
                .as_ref()
                .map(|s| s.core_path.clone())
                .unwrap_or_default();
            resolve_inside(&platform_core, &emulator.install_dir)
                .or_else(|| resolve_inside(&emulator.core_path, &emulator.install_dir))
                .map(|p| p.to_string_lossy().to_string())
                .unwrap_or_else(|| emulator.core_path.clone())
        };

        if args::requires_core(&template) && core_expression.trim().is_empty() {
            result.warnings.push(format!(
                "游戏「{}」的平台「{}」缺少核心文件，未生成启动器。",
                game.title, game.platform
            ));
            continue;
        }

        let mut title_folder = sanitize_name(&game.title);
        if title_folder.is_empty() {
            title_folder = Path::new(&discs[0])
                .file_stem()
                .map(|s| s.to_string_lossy().to_string())
                .unwrap_or_else(|| "game".into());
        }

        // 多碟游戏每张碟各生成一个启动器。
        for (index, disc) in discs.iter().enumerate() {
            let source = st.expand(disc);
            if !Path::new(&source).is_file() {
                result
                    .warnings
                    .push(format!("游戏「{}」的 ROM 文件不存在，已跳过。", game.title));
                continue;
            }
            let Some(file_name) = Path::new(&source).file_name() else {
                continue;
            };
            let rom_relative = Path::new("roms")
                .join(&title_folder)
                .join(file_name)
                .to_string_lossy()
                .to_string();
            let rom_target = package_dir.join(&rom_relative);
            if let Some(parent) = rom_target.parent() {
                std::fs::create_dir_all(parent).map_err(|e| format!("创建 ROM 目录失败：{e}"))?;
            }
            std::fs::copy(&source, &rom_target).map_err(|e| format!("复制 ROM 失败：{e}"))?;

            let arguments = args::build(
                &template,
                &rom_relative,
                Some(core_expression.as_str()).filter(|s| !s.is_empty()),
                None,
            );

            let base = if discs.len() > 1 {
                format!("Play-{title_folder}-{}", index + 1)
            } else {
                format!("Play-{title_folder}")
            };
            let launcher_name = format!("{}.bat", unique_name(&base, &mut used_names));
            let launcher_path = package_dir.join(&launcher_name);
            write_launcher(&launcher_path, &exe_expression, &arguments)?;
            result.launchers.push(launcher_name);
        }
    }

    write_manifest(&package_dir, &items)?;
    write_readme(&package_dir, &items, &result.launchers)?;
    result.total_bytes = directory_size(&package_dir);

    if create_zip {
        let zip_path = package_dir.with_extension("zip");
        match zip_directory(&package_dir, &zip_path) {
            Ok(()) => result.zip_path = Some(zip_path.to_string_lossy().to_string()),
            Err(e) => result.warnings.push(format!("压缩 zip 失败：{e}")),
        }
    }

    Ok(result)
}

fn resolve_inside(value: &str, install_dir: &str) -> Option<PathBuf> {
    if value.trim().is_empty() {
        return None;
    }
    let path = Path::new(value);
    if path.is_absolute() {
        return Some(path.to_path_buf());
    }
    if install_dir.trim().is_empty() {
        return Some(PathBuf::from(value));
    }
    Some(Path::new(install_dir).join(path))
}

fn emulator_source_dir(emulator: &Emulator) -> Option<PathBuf> {
    if !emulator.install_dir.trim().is_empty() {
        let dir = Path::new(&emulator.install_dir);
        if dir.is_dir() {
            return Some(dir.to_path_buf());
        }
    }
    let exe = resolve_inside(&emulator.exe_path, &emulator.install_dir)?;
    let parent = exe.parent()?;
    if parent.is_dir() {
        Some(parent.to_path_buf())
    } else {
        None
    }
}

fn relative_inside(path: Option<&Path>, source_dir: &Path, emulator_folder: &str) -> String {
    let Some(path) = path else {
        return String::new();
    };
    let Ok(relative) = path.strip_prefix(source_dir) else {
        return String::new();
    };
    Path::new("emulators")
        .join(emulator_folder)
        .join(relative)
        .to_string_lossy()
        .to_string()
}

pub fn sanitize_name(name: &str) -> String {
    let mut cleaned: String = name
        .chars()
        .map(|c| match c {
            '\\' | '/' | ':' | '*' | '?' | '"' | '<' | '>' | '|' => '_',
            _ => c,
        })
        .collect();
    cleaned = cleaned.trim().trim_end_matches('.').to_string();
    if cleaned.chars().count() > 80 {
        cleaned = cleaned.chars().take(80).collect::<String>();
        cleaned = cleaned.trim_end().to_string();
    }
    cleaned
}

fn unique_name(base: &str, used: &mut HashSet<String>) -> String {
    let mut name = base.to_string();
    let mut index = 2;
    while !used.insert(name.to_lowercase()) {
        name = format!("{base} ({index})");
        index += 1;
    }
    name
}

fn write_launcher(path: &Path, executable: &str, arguments: &str) -> Res<()> {
    let script = format!(
        "@echo off\r\nchcp 65001 >nul\r\ncd /d \"%~dp0\"\r\nstart \"\" \"{executable}\" {arguments}\r\n"
    );
    std::fs::write(path, script).map_err(|e| format!("生成启动器失败：{e}"))
}

fn copy_directory(source: &Path, target: &Path) -> std::io::Result<()> {
    std::fs::create_dir_all(target)?;
    for entry in std::fs::read_dir(source)? {
        let entry = entry?;
        let name = entry.file_name();
        let name_text = name.to_string_lossy();
        if name_text.eq_ignore_ascii_case(".git") {
            continue;
        }
        let from = entry.path();
        let to = target.join(&name);
        if from.is_dir() {
            copy_directory(&from, &to)?;
        } else {
            std::fs::copy(&from, &to)?;
        }
    }
    Ok(())
}

pub fn directory_size(dir: &Path) -> i64 {
    WalkDir::new(dir)
        .into_iter()
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.file_type().is_file())
        .filter_map(|entry| entry.metadata().ok())
        .map(|meta| meta.len() as i64)
        .sum()
}

fn write_manifest(package_dir: &Path, items: &[(Game, Emulator)]) -> Res<()> {
    let games_json: Vec<serde_json::Value> = items
        .iter()
        .map(|(game, emulator)| {
            serde_json::json!({
                "title": game.title,
                "platform": game.platform,
                "rom": Path::new("roms").join(sanitize_name(&game.title)).join(
                    Path::new(&game.rom_path).file_name().map(|s| s.to_string_lossy().to_string()).unwrap_or_default()
                ).to_string_lossy().to_string(),
                "emulator": emulator.name,
            })
        })
        .collect();

    let mut seen: HashSet<i64> = HashSet::new();
    let emulators_json: Vec<serde_json::Value> = items
        .iter()
        .filter(|(_, emulator)| seen.insert(emulator.id))
        .map(|(_, emulator)| {
            serde_json::json!({
                "name": emulator.name,
                "version": emulator.version,
                "argsTemplate": emulator.args_template,
            })
        })
        .collect();

    let manifest = serde_json::json!({
        "tool": "MicaRomManager",
        "version": env!("CARGO_PKG_VERSION"),
        "createdAt": crate::util::now_iso(),
        "games": games_json,
        "emulators": emulators_json,
    });
    let text = serde_json::to_string_pretty(&manifest).map_err(|e| e.to_string())?;
    std::fs::write(package_dir.join("manifest.json"), text)
        .map_err(|e| format!("写入 manifest.json 失败：{e}"))
}

fn write_readme(package_dir: &Path, items: &[(Game, Emulator)], launchers: &[String]) -> Res<()> {
    let mut text = String::from(
        "Mica ROM Manager 分享包\r\n======================\r\n\r\n\
         玩法：双击本目录下的 Play-*.bat 即可启动对应游戏。\r\n目录说明：\r\n\
         \x20 emulators\\  打包的模拟器（若打包时未勾选则需目标电脑自备）\r\n\
         \x20 roms\\       游戏 ROM\r\n\
         \x20 config\\     模拟器配置\r\n\
         \x20 saves\\      存档（若勾选）\r\n\
         \x20 manifest.json 本包内容清单\r\n\r\n游戏列表：\r\n",
    );
    for (game, emulator) in items {
        text.push_str(&format!(
            "  - {}（{}） -> {}\r\n",
            game.title, game.platform, emulator.name
        ));
    }
    if !launchers.is_empty() {
        text.push_str("\r\n启动器：\r\n");
        for launcher in launchers {
            text.push_str(&format!("  - {launcher}\r\n"));
        }
    }
    text.push_str(&format!(
        "\r\n生成时间：{}\r\n",
        chrono::Local::now().format("%Y-%m-%d %H:%M:%S")
    ));
    std::fs::write(package_dir.join("README.txt"), text)
        .map_err(|e| format!("写入 README.txt 失败：{e}"))
}

fn zip_directory(source: &Path, target: &Path) -> Res<()> {
    let file = std::fs::File::create(target).map_err(|e| format!("创建 zip 失败：{e}"))?;
    let mut writer = zip::ZipWriter::new(file);
    let options = zip::write::SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated);

    let base = source.to_path_buf();
    for entry in WalkDir::new(source).into_iter().filter_map(|e| e.ok()) {
        let path = entry.path();
        if path == source {
            continue;
        }
        let relative = path.strip_prefix(&base).map_err(|e| e.to_string())?;
        let name = relative.to_string_lossy().replace('\\', "/");
        if entry.file_type().is_dir() {
            writer
                .add_directory(format!("{name}/"), options)
                .map_err(|e| e.to_string())?;
        } else {
            writer
                .start_file(name, options)
                .map_err(|e| e.to_string())?;
            let mut input = std::fs::File::open(path).map_err(|e| format!("读取文件失败：{e}"))?;
            std::io::copy(&mut input, &mut writer).map_err(|e| format!("写入 zip 失败：{e}"))?;
            writer.flush().ok();
        }
    }
    writer.finish().map_err(|e| format!("完成 zip 失败：{e}"))?;
    Ok(())
}
