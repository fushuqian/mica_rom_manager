use crate::state::AppState;
use crate::util::Res;
use std::path::Path;
use tauri::State;

#[tauri::command]
pub fn file_exists(path: String) -> bool {
    !path.trim().is_empty() && Path::new(&path).exists()
}

/// 读取封面并转成 data URL（避免暴露任意本地文件给前端）。
#[tauri::command]
pub fn cover_data_url(st: State<'_, AppState>, path: String) -> Res<String> {
    use base64::Engine;
    let expanded = st.expand(&path);
    if expanded.trim().is_empty() || !Path::new(&expanded).is_file() {
        return Ok(String::new());
    }
    let bytes = std::fs::read(&expanded).map_err(|e| format!("读取封面失败：{e}"))?;
    let ext = Path::new(&expanded)
        .extension()
        .map(|e| e.to_string_lossy().to_lowercase())
        .unwrap_or_default();
    let mime = match ext.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        _ => "application/octet-stream",
    };
    let encoded = base64::engine::general_purpose::STANDARD.encode(bytes);
    Ok(format!("data:{};base64,{}", mime, encoded))
}

#[tauri::command]
pub fn app_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}
