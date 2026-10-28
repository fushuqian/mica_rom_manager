use crate::db::{games, platforms};
use crate::models::{Game, ImportCandidate, ImportItem, ImportOutcome, ScanResult};
use crate::progress::TaskReporter;
use crate::services::extension_map::ExtensionMap;
use crate::services::formats;
use crate::services::hasher;
use crate::services::UNSPECIFIED_FOLDER;
use crate::state::AppState;
use crate::util::{sanitize_file_name, Res};
use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};

pub fn sanitize_folder_name(name: &str) -> String {
    let cleaned = sanitize_file_name(name)
        .trim()
        .trim_end_matches('.')
        .to_string();
    if cleaned.trim().is_empty() {
        UNSPECIFIED_FOLDER.to_string()
    } else {
        cleaned
    }
}

/// 扫描导入来源（文件或目录），去重、识别平台、计算指纹。
pub fn scan(
    st: &AppState,
    inputs: &[String],
    recursive: bool,
    allowed_ext: Option<&[String]>,
    platform_override: Option<&str>,
    reporter: &mut dyn TaskReporter,
) -> Res<ScanResult> {
    let existing_paths: HashSet<String> = {
        let list = games::list_all(st)?;
        list.into_iter()
            .map(|g| g.rom_path.to_lowercase())
            .filter(|p| !p.is_empty())
            .collect()
    };
    let existing_hashes: HashSet<String> = games::list_hashes(st)?
        .into_iter()
        .map(|h| h.to_lowercase())
        .collect();

    let platform_formats: HashMap<String, Vec<String>> = {
        let settings = platforms::list_all(st)?;
        settings
            .into_iter()
            .filter(|p| !p.formats.trim().is_empty())
            .map(|p| (p.name, formats::parse(&p.formats)))
            .collect()
    };

    let allowed: Option<HashSet<String>> = allowed_ext.map(|list| {
        list.iter()
            .filter(|e| !e.trim().is_empty())
            .map(|e| {
                let trimmed = e.trim().to_lowercase();
                if trimmed.starts_with('.') {
                    trimmed
                } else {
                    format!(".{}", trimmed)
                }
            })
            .collect()
    });

    let mut seen: HashSet<String> = HashSet::new();
    let mut seen_hashes: HashSet<String> = HashSet::new();
    let mut result = ScanResult::default();
    let mut visited = 0usize;

    for input in inputs {
        let path = Path::new(input);
        if path.is_file() {
            add_file(
                st,
                path,
                true,
                &existing_paths,
                &existing_hashes,
                allowed.as_ref(),
                &platform_formats,
                platform_override,
                &mut seen,
                &mut seen_hashes,
                &mut result,
            );
            visited += 1;
        } else if path.is_dir() {
            let walker = walkdir::WalkDir::new(path)
                .max_depth(if recursive { usize::MAX } else { 1 })
                .follow_links(false)
                .into_iter()
                .filter_map(|e| e.ok());
            for entry in walker {
                if !entry.file_type().is_file() {
                    continue;
                }
                add_file(
                    st,
                    entry.path(),
                    false,
                    &existing_paths,
                    &existing_hashes,
                    allowed.as_ref(),
                    &platform_formats,
                    platform_override,
                    &mut seen,
                    &mut seen_hashes,
                    &mut result,
                );
                visited += 1;
            }
        }
        let message = format!("已扫描 {} 个文件，候选 {} 个", visited, result.candidates.len());
        if reporter.tick(visited, 0, &message) {
            result.cancelled = true;
            break;
        }
    }

    Ok(result)
}

#[allow(clippy::too_many_arguments)]
fn add_file(
    _st: &AppState,
    file: &Path,
    explicit_selection: bool,
    existing_paths: &HashSet<String>,
    existing_hashes: &HashSet<String>,
    allowed: Option<&HashSet<String>>,
    platform_formats: &HashMap<String, Vec<String>>,
    platform_override: Option<&str>,
    seen: &mut HashSet<String>,
    seen_hashes: &mut HashSet<String>,
    result: &mut ScanResult,
) {
    let file_str = file.to_string_lossy().to_string();
    let extension = file
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
        .unwrap_or_default();

    if let Some(list) = allowed {
        if !list.is_empty() && !list.contains(&extension) {
            if explicit_selection {
                result.excluded_count += 1;
            } else {
                result.format_skipped_count += 1;
            }
            return;
        }
    } else if !ExtensionMap::is_known_or_archive(&file_str) {
        if !explicit_selection {
            return;
        }
        result.excluded_count += 1;
        return;
    }

    let full = std::fs::canonicalize(file)
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or(file_str);
    let lower = full.to_lowercase();
    if existing_paths.contains(&lower) || !seen.insert(lower) {
        result.duplicate_count += 1;
        return;
    }

    let info = ExtensionMap::detect(&full);
    let size = std::fs::metadata(&full)
        .map(|m| m.len() as i64)
        .unwrap_or(0);
    let hash = hasher::compute(&full);
    if !hash.is_empty() {
        let hash_lower = hash.to_lowercase();
        if existing_hashes.contains(&hash_lower) || !seen_hashes.insert(hash_lower) {
            result.duplicate_count += 1;
            return;
        }
    }

    let (platform, needs_specify) = match platform_override {
        Some(name) if !name.trim().is_empty() => (name.to_string(), false),
        _ => formats::resolve(
            &full,
            &info,
            if platform_formats.is_empty() {
                None
            } else {
                Some(platform_formats)
            },
        ),
    };

    let file_name = file
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default();
    let title = Path::new(&file_name)
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_else(|| file_name.clone());

    result.candidates.push(ImportCandidate {
        file_path: full,
        file_name,
        file_size: size,
        title,
        platform,
        rom_hash: hash,
        needs_specify,
    });
}

/// 计算库内目标路径（同名自动追加序号）。
pub fn resolve_target_path(
    source_file: &str,
    library_root: &str,
    platform: &str,
    folder: Option<&str>,
) -> String {
    let folder_name = folder
        .filter(|f| !f.trim().is_empty())
        .map(|f| f.to_string())
        .unwrap_or_else(|| {
            if platform.trim().is_empty() {
                UNSPECIFIED_FOLDER.to_string()
            } else {
                platform.to_string()
            }
        });
    let directory = Path::new(library_root).join(sanitize_folder_name(&folder_name));
    let _ = std::fs::create_dir_all(&directory);

    let name = Path::new(source_file)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default();
    let mut target = directory.join(&name);

    let same_file = target.to_string_lossy().eq_ignore_ascii_case(source_file);
    if target.exists() || same_file {
        let stem = Path::new(source_file)
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_default();
        let extension = Path::new(source_file)
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy()))
            .unwrap_or_default();
        let mut index = 1;
        loop {
            target = directory.join(format!("{} ({}){}", stem, index, extension));
            if !target.exists() {
                break;
            }
            index += 1;
        }
    }

    target.to_string_lossy().to_string()
}

pub fn copy_to_library(
    source_file: &str,
    library_root: &str,
    platform: &str,
    folder: Option<&str>,
) -> Res<String> {
    let target = resolve_target_path(source_file, library_root, platform, folder);
    std::fs::create_dir_all(Path::new(&target).parent().unwrap_or(Path::new(".")))
        .map_err(|e| e.to_string())?;
    std::fs::copy(source_file, &target).map_err(|e| format!("复制失败：{e}"))?;
    Ok(target)
}

pub fn move_to_library(
    source_file: &str,
    library_root: &str,
    platform: &str,
    folder: Option<&str>,
) -> Res<String> {
    let target = resolve_target_path(source_file, library_root, platform, folder);
    std::fs::create_dir_all(Path::new(&target).parent().unwrap_or(Path::new(".")))
        .map_err(|e| e.to_string())?;
    if std::fs::rename(source_file, &target).is_err() {
        std::fs::copy(source_file, &target).map_err(|e| format!("复制失败：{e}"))?;
        std::fs::remove_file(source_file).map_err(|e| format!("删除源文件失败：{e}"))?;
    }
    Ok(target)
}

/// 按选定模式入库并写入数据库。
pub fn apply(
    st: &AppState,
    items: &[ImportItem],
    mode: &str,
    reporter: &mut dyn TaskReporter,
) -> Res<ImportOutcome> {
    let library_root = st.library_root();
    let mut outcome = ImportOutcome::default();
    let total = items.len();

    // 平台目录映射
    let mut folders: HashMap<String, String> = HashMap::new();
    for setting in platforms::list_all(st)? {
        folders.insert(setting.name.to_lowercase(), setting.folder);
    }

    for (index, item) in items.iter().enumerate() {
        let folder = folders
            .get(&item.platform.to_lowercase())
            .cloned()
            .filter(|f| !f.trim().is_empty());

        if !item.platform.trim().is_empty() {
            let conn = st.conn();
            let _ = platforms::ensure_exists(&conn, &item.platform);
        }

        let rom_path = match mode {
            "Copy" if !library_root.trim().is_empty() => {
                match copy_to_library(
                    &item.file_path,
                    &library_root,
                    &item.platform,
                    folder.as_deref(),
                ) {
                    Ok(p) => p,
                    Err(err) => {
                        outcome.failed += 1;
                        outcome
                            .messages
                            .push(format!("{}：{}", file_name(item), err));
                        continue;
                    }
                }
            }
            "Move" if !library_root.trim().is_empty() => {
                match move_to_library(
                    &item.file_path,
                    &library_root,
                    &item.platform,
                    folder.as_deref(),
                ) {
                    Ok(p) => p,
                    Err(err) => {
                        outcome.failed += 1;
                        outcome
                            .messages
                            .push(format!("{}：{}", file_name(item), err));
                        continue;
                    }
                }
            }
            _ => item.file_path.clone(),
        };

        let file_size = std::fs::metadata(&rom_path)
            .map(|m| m.len() as i64)
            .unwrap_or(item.file_size);
        let hash = if item.rom_hash.trim().is_empty() {
            hasher::compute(&rom_path)
        } else {
            item.rom_hash.clone()
        };

        let game = Game {
            title: item.title.trim().to_string(),
            platform: item.platform.clone(),
            rom_path,
            rom_hash: hash,
            file_size,
            status: "未玩".to_string(),
            ..Default::default()
        };

        match games::save(st, game) {
            Ok(_) => outcome.imported += 1,
            Err(err) => {
                outcome.failed += 1;
                outcome
                    .messages
                    .push(format!("{}：{}", file_name(item), err));
            }
        }

        let message = format!("导入中 {}/{}：{}", index + 1, total, file_name(item));
        if reporter.tick(index + 1, total, &message) {
            outcome.cancelled = true;
            break;
        }
    }

    Ok(outcome)
}

fn file_name(item: &ImportItem) -> String {
    PathBuf::from(&item.file_path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_default()
}
