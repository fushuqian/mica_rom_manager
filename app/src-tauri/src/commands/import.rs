use crate::db::platforms;
use crate::models::{ImportItem, ImportOutcome, ScanResult};
use crate::services::importer;
use crate::state::AppState;
use crate::util::Res;
use tauri::State;

#[tauri::command]
pub fn import_scan(
    st: State<'_, AppState>,
    inputs: Vec<String>,
    recursive: bool,
    allowed_extensions: Option<Vec<String>>,
    platform_override: Option<String>,
) -> Res<ScanResult> {
    let allowed = allowed_extensions.filter(|v| !v.is_empty());
    importer::scan(
        &st,
        &inputs,
        recursive,
        allowed.as_deref(),
        platform_override.as_deref(),
    )
}

#[tauri::command]
pub fn import_apply(
    st: State<'_, AppState>,
    items: Vec<ImportItem>,
    mode: String,
) -> Res<ImportOutcome> {
    importer::apply(&st, &items, &mode)
}

#[derive(Debug, Clone, serde::Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PlatformScanOutcome {
    pub candidates: Vec<crate::models::ImportCandidate>,
    pub duplicate_count: i64,
    pub excluded_count: i64,
    pub format_skipped_count: i64,
    pub removed: i64,
    pub dir: String,
}

/// 扫描平台子库：先清理失效记录，再按格式白名单补充新文件。
#[tauri::command]
pub fn platform_scan(
    st: State<'_, AppState>,
    name: String,
    formats: Option<String>,
) -> Res<PlatformScanOutcome> {
    let library_root = st.library_root();
    if library_root.trim().is_empty() {
        return Err("尚未配置库目录，请先到「常规」里设置库目录。".into());
    }
    let setting = platforms::get(&st, &name)?;
    let folder_name = setting
        .as_ref()
        .map(|s| {
            if s.folder.trim().is_empty() {
                s.name.clone()
            } else {
                s.folder.clone()
            }
        })
        .unwrap_or_else(|| name.clone());

    let dir =
        std::path::Path::new(&library_root).join(importer::sanitize_folder_name(&folder_name));
    if !dir.exists() {
        return Err(format!("该平台库目录不存在：{}", dir.display()));
    }

    let removed = crate::services::organizer::remove_missing(&st, &name)?;

    let allowed_text = formats.unwrap_or_else(|| {
        setting
            .as_ref()
            .map(|s| s.formats.clone())
            .unwrap_or_default()
    });
    let allowed = crate::services::formats::parse(&allowed_text);
    let scan = importer::scan(
        &st,
        &[dir.to_string_lossy().to_string()],
        true,
        if allowed.is_empty() {
            None
        } else {
            Some(&allowed)
        },
        Some(&name),
    )?;

    Ok(PlatformScanOutcome {
        candidates: scan.candidates,
        duplicate_count: scan.duplicate_count,
        excluded_count: scan.excluded_count,
        format_skipped_count: scan.format_skipped_count,
        removed,
        dir: dir.to_string_lossy().to_string(),
    })
}

#[derive(Debug, Clone, serde::Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct OrganizeOutcome {
    pub renamed: i64,
    pub skipped: i64,
    pub failed: i64,
    pub failed_files: Vec<String>,
    pub dir: String,
}

#[tauri::command]
pub fn platform_organize(st: State<'_, AppState>, name: String) -> Res<OrganizeOutcome> {
    let library_root = st.library_root();
    if library_root.trim().is_empty() {
        return Err("尚未配置库目录，请先到「常规」里设置库目录。".into());
    }
    let setting = platforms::get(&st, &name)?;
    let folder_name = setting
        .as_ref()
        .map(|s| {
            if s.folder.trim().is_empty() {
                s.name.clone()
            } else {
                s.folder.clone()
            }
        })
        .unwrap_or_else(|| name.clone());
    let dir =
        std::path::Path::new(&library_root).join(importer::sanitize_folder_name(&folder_name));
    if !dir.exists() {
        return Err(format!("该平台库目录不存在：{}", dir.display()));
    }

    let result = crate::services::organizer::organize(&st, &name, &dir.to_string_lossy())?;
    Ok(OrganizeOutcome {
        renamed: result.renamed,
        skipped: result.skipped,
        failed: result.failed,
        failed_files: result.failed_files,
        dir: dir.to_string_lossy().to_string(),
    })
}

/// 扫描整个导入目录（向导内部用）。
#[tauri::command]
pub fn import_supported_extensions() -> Vec<String> {
    crate::services::extension_map::ExtensionMap::extensions()
        .iter()
        .map(|s| s.to_string())
        .collect()
}
