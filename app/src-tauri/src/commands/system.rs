use crate::state::AppState;
use crate::util::Res;
use std::path::Path;
use std::time::Duration;
use tauri::State;

#[tauri::command]
pub fn file_exists(st: State<'_, AppState>, path: String) -> bool {
    if path.trim().is_empty() {
        return false;
    }
    Path::new(&st.expand(&path)).exists()
}

/// 按扩展名推断平台（用于浏览 ROM 时自动带出平台）。
#[tauri::command]
pub fn detect_platform(path: String) -> Option<String> {
    crate::services::extension_map::ExtensionMap::detect(&path).platform
}

/// 测试 GitHub 连通性，复用当前代理设置。
#[tauri::command]
pub async fn network_test_github(st: State<'_, AppState>) -> Res<String> {
    let client = crate::services::installer::client(&st)?;
    let response = client
        .get("https://api.github.com/rate_limit")
        .timeout(Duration::from_secs(10))
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
