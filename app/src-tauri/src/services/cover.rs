use crate::state::AppState;
use crate::util::Res;
use std::fs;
use std::path::Path;
use uuid::Uuid;

/// 把图片复制进 data\covers\{guid}{ext}，返回新路径。
pub fn store(st: &AppState, source: &str) -> Res<String> {
    let src = Path::new(source);
    if !src.is_file() {
        return Err("封面文件不存在。".into());
    }
    let covers = crate::paths::covers_dir(&st.data_dir);
    fs::create_dir_all(&covers).map_err(|e| format!("创建封面目录失败：{e}"))?;

    let ext = src
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
        .unwrap_or_else(|| ".png".to_string());
    let target = covers.join(format!("{}{}", Uuid::new_v4(), ext));
    fs::copy(src, &target).map_err(|e| format!("复制封面失败：{e}"))?;
    Ok(target.to_string_lossy().to_string())
}

/// 删除封面文件（仅限 covers 目录内，失败静默）。
pub fn remove(st: &AppState, cover_path: &str) {
    let expanded = st.expand(cover_path);
    if expanded.trim().is_empty() {
        return;
    }
    let covers = crate::paths::covers_dir(&st.data_dir);
    if crate::util::is_under(&expanded, &covers) {
        let _ = fs::remove_file(&expanded);
    }
}
