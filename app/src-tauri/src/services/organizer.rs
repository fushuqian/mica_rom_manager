use crate::db::games;
use crate::models::Game;
use crate::state::AppState;
use crate::util::{sanitize_file_name, Res};
use std::path::{Path, PathBuf};

pub const MAX_TITLE_LENGTH: usize = 150;

#[derive(Debug, Clone, serde::Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct OrganizeResult {
    pub renamed: i64,
    pub skipped: i64,
    pub failed: i64,
    pub failed_files: Vec<String>,
}

/// 生成目标文件名：多碟才带 Disc 号；区域/语言各自非空才带括号。
pub fn build_file_name(game: &Game, disc_index: usize, source_path: &str) -> String {
    let title = sanitize_title(&game.title);
    if title.is_empty() || source_path.trim().is_empty() {
        return String::new();
    }

    let mut name = title;
    let discs = game.discs();
    if discs.len() > 1 {
        name.push_str(&format!(" Disc-{}", disc_index + 1));
    }

    let region = join_values(&game.region);
    let language = join_values(&game.language);
    let mut suffix = String::new();
    if !region.is_empty() {
        suffix.push_str(&format!("({})", region));
    }
    if !language.is_empty() {
        suffix.push_str(&format!("({})", language));
    }
    if !suffix.is_empty() {
        name.push(' ');
        name.push_str(&suffix);
    }

    let extension = Path::new(source_path)
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy()))
        .unwrap_or_default();
    format!("{}{}", name, extension)
}

/// 删除所有碟片都不存在的条目（无 rom_path 的不动）。
pub fn remove_missing(st: &AppState, platform: &str) -> Res<i64> {
    let list = games::list_all(st)?;
    let mut removed = 0;
    for game in list {
        if !platform.is_empty() && !game.platform.eq_ignore_ascii_case(platform) {
            continue;
        }
        if game.rom_path.trim().is_empty() {
            continue;
        }
        let discs = game.discs();
        if !discs.is_empty() && discs.iter().all(|d| !Path::new(d).exists()) {
            games::delete(st, game.id)?;
            removed += 1;
        }
    }
    Ok(removed)
}

/// 只重命名位于 platform_dir 内的碟片文件；库外链接文件不动。
pub fn organize(st: &AppState, platform: &str, platform_dir: &str) -> Res<OrganizeResult> {
    let list = games::list_all(st)?;
    let mut result = OrganizeResult::default();

    for game in list {
        if !game.platform.eq_ignore_ascii_case(platform) {
            continue;
        }
        let discs = game.discs();
        if !discs.iter().any(|d| is_under_directory(d, platform_dir)) {
            continue;
        }
        if discs.is_empty() {
            result.skipped += 1;
            continue;
        }

        let mut new_paths = discs.clone();
        let mut changed = false;

        for (index, source) in discs.iter().enumerate() {
            if !Path::new(source).exists() || !is_under_directory(source, platform_dir) {
                continue;
            }
            let Some(directory) = Path::new(source).parent() else {
                continue;
            };
            let file_name = build_file_name(&game, index, source);
            if file_name.is_empty() {
                continue;
            }

            let target = directory.join(&file_name);
            if target.to_string_lossy().eq_ignore_ascii_case(source) {
                continue;
            }
            let target = resolve_unique_name(&target, source);
            if target.to_string_lossy().eq_ignore_ascii_case(source) {
                continue;
            }

            match std::fs::rename(source, &target) {
                Ok(_) => {
                    new_paths[index] = target.to_string_lossy().to_string();
                    changed = true;
                    result.renamed += 1;
                }
                Err(_) => {
                    result.failed += 1;
                    result.failed_files.push(
                        Path::new(source)
                            .file_name()
                            .map(|n| n.to_string_lossy().to_string())
                            .unwrap_or_default(),
                    );
                }
            }
        }

        if !changed {
            result.skipped += 1;
            continue;
        }

        let mut updated = game.clone();
        updated.rom_path = new_paths[0].clone();
        updated.disc_paths = new_paths
            .iter()
            .skip(1)
            .filter(|p| !p.trim().is_empty())
            .cloned()
            .collect::<Vec<_>>()
            .join("\n");
        if games::save(st, updated).is_err() {
            result.failed += 1;
            result.failed_files.push(
                Path::new(&new_paths[0])
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_default(),
            );
        }
    }

    Ok(result)
}

pub fn is_under_directory(path: &str, directory: &str) -> bool {
    if path.trim().is_empty() || directory.trim().is_empty() {
        return false;
    }
    let full = std::fs::canonicalize(path).unwrap_or_else(|_| PathBuf::from(path));
    let root = std::fs::canonicalize(directory).unwrap_or_else(|_| PathBuf::from(directory));
    let full_s = full.to_string_lossy().to_lowercase();
    let mut root_s = root.to_string_lossy().to_lowercase();
    if !root_s.ends_with('\\') && !root_s.ends_with('/') {
        root_s.push('\\');
    }
    full_s.starts_with(&root_s)
}

fn sanitize_title(raw: &str) -> String {
    let text = raw.trim();
    if text.is_empty() {
        return String::new();
    }
    let replaced = sanitize_file_name(text);
    let collapsed = replaced
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .trim_end_matches('.')
        .to_string();
    if collapsed.is_empty() {
        return String::new();
    }
    if collapsed.chars().count() > MAX_TITLE_LENGTH {
        collapsed
            .chars()
            .take(MAX_TITLE_LENGTH)
            .collect::<String>()
            .trim_end()
            .to_string()
    } else {
        collapsed
    }
}

fn join_values(raw: &str) -> String {
    if raw.trim().is_empty() {
        return String::new();
    }
    let mut parts: Vec<String> = Vec::new();
    for token in raw.split([',', '，', '、', ';', '；']) {
        let trimmed = token.trim();
        if trimmed.is_empty() {
            continue;
        }
        let cleaned = sanitize_file_name(trimmed);
        if cleaned.is_empty() {
            continue;
        }
        if !parts.iter().any(|p| p.eq_ignore_ascii_case(&cleaned)) {
            parts.push(cleaned);
        }
    }
    parts.join(",")
}

fn resolve_unique_name(target: &Path, source: &str) -> PathBuf {
    if !target.exists() {
        return target.to_path_buf();
    }
    let directory = target.parent().unwrap_or(Path::new("."));
    let stem = target
        .file_stem()
        .map(|s| s.to_string_lossy().to_string())
        .unwrap_or_default();
    let extension = target
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy()))
        .unwrap_or_default();
    for index in 2..1000 {
        let candidate = directory.join(format!("{} ({}){}", stem, index, extension));
        if !candidate.exists() || candidate.to_string_lossy().eq_ignore_ascii_case(source) {
            return candidate;
        }
    }
    target.to_path_buf()
}
