use chrono::Local;

pub type Res<T> = Result<T, String>;

/// 与 .NET DateTime "O" 等价的本地时间字符串（7 位小数 + 时区偏移）。
pub fn now_iso() -> String {
    let now = Local::now();
    let fraction = now.timestamp_subsec_nanos() / 100;
    format!(
        "{}.{:07}{}",
        now.format("%Y-%m-%dT%H:%M:%S"),
        fraction,
        now.format("%:z")
    )
}

/// 文件名非法字符替换为下划线（Windows）。
pub fn sanitize_file_name(raw: &str) -> String {
    const INVALID: [char; 9] = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    raw.chars()
        .map(|c| {
            if INVALID.contains(&c) || (c as u32) < 32 {
                '_'
            } else {
                c
            }
        })
        .collect()
}

/// 与 portable.rs 共用的路径前缀判断（大小写不敏感，按目录边界）。
pub fn is_under(path: &str, root: &std::path::Path) -> bool {
    if path.is_empty() {
        return false;
    }
    let full = std::fs::canonicalize(path).unwrap_or_else(|_| std::path::PathBuf::from(path));
    let root_full = std::fs::canonicalize(root).unwrap_or_else(|_| root.to_path_buf());
    let full_s = full.to_string_lossy().to_lowercase();
    let mut root_s = root_full.to_string_lossy().to_lowercase();
    if !root_s.ends_with('\\') && !root_s.ends_with('/') {
        root_s.push('\\');
    }
    full_s.starts_with(&root_s)
}
