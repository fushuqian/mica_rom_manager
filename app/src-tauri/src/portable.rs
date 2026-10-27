use std::path::{Path, PathBuf};

pub const DATA_TOKEN: &str = "%DATA%";

/// 展开存储路径：先展开 %DATA%，相对路径再按 exe 目录拼接（与 WPF PortablePath.Expand 一致）。
pub fn expand(stored: &str, data_dir: &Path, exe_dir: &Path) -> String {
    if stored.trim().is_empty() {
        return String::new();
    }

    if let Some(rest) = strip_data_token(stored) {
        if rest.is_empty() {
            return data_dir.to_string_lossy().to_string();
        }
        return data_dir.join(rest).to_string_lossy().to_string();
    }

    let path = Path::new(stored);
    if path.is_absolute() {
        return stored.to_string();
    }
    if exe_dir.as_os_str().is_empty() {
        return stored.to_string();
    }
    exe_dir.join(path).to_string_lossy().to_string()
}

/// 存入数据库前：数据目录内一律存 %DATA%；便携模式下程序目录内路径存相对路径。
pub fn to_stored(absolute: &str, portable: bool, data_dir: &Path, exe_dir: &Path) -> String {
    if absolute.trim().is_empty() {
        return String::new();
    }
    let abs = PathBuf::from(absolute);
    if !abs.is_absolute() {
        return absolute.to_string();
    }

    if let Ok(rel) = abs.strip_prefix(data_dir) {
        let rel_str = rel.to_string_lossy();
        if rel_str.is_empty() {
            return DATA_TOKEN.to_string();
        }
        return format!("{}\\{}", DATA_TOKEN, rel_str);
    }

    if portable {
        if let Ok(rel) = abs.strip_prefix(exe_dir) {
            return rel.to_string_lossy().to_string();
        }
    }

    absolute.to_string()
}

/// 多行路径（多碟）逐行展开。
pub fn expand_multiline(stored: &str, data_dir: &Path, exe_dir: &Path) -> String {
    stored
        .lines()
        .map(|line| line.trim())
        .filter(|line| !line.is_empty())
        .map(|line| expand(line, data_dir, exe_dir))
        .collect::<Vec<_>>()
        .join("\n")
}

pub fn to_stored_multiline(
    stored: &str,
    portable: bool,
    data_dir: &Path,
    exe_dir: &Path,
) -> String {
    stored
        .lines()
        .map(|line| line.trim())
        .filter(|line| !line.is_empty())
        .map(|line| to_stored(line, portable, data_dir, exe_dir))
        .collect::<Vec<_>>()
        .join("\n")
}

pub fn strip_data_token(stored: &str) -> Option<String> {
    let lower = stored.to_lowercase();
    if !lower.starts_with("%data%") {
        return None;
    }
    let rest = &stored[DATA_TOKEN.len()..];
    Some(rest.trim_start_matches(['\\', '/']).to_string())
}
