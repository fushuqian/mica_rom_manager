use crate::db::emulators;
use crate::models::Emulator;
use crate::progress::{TaskGuard, TaskReporter};
use crate::services::installer::{self, ReleaseInfo};
use crate::state::AppState;
use crate::util::Res;
use serde::Serialize;
use tauri::{AppHandle, State};

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallOutcome {
    pub install_dir: String,
    pub exe_candidates: Vec<String>,
}

#[tauri::command]
pub fn emulators_list(st: State<'_, AppState>) -> Res<Vec<Emulator>> {
    emulators::list_all(&st)
}

#[tauri::command]
pub fn emulator_save(st: State<'_, AppState>, emulator: Emulator) -> Res<Emulator> {
    emulators::save(&st, emulator)
}

#[tauri::command]
pub fn emulator_delete(st: State<'_, AppState>, id: i64) -> Res<()> {
    emulators::delete(&st, id)
}

#[tauri::command]
pub fn emulator_set_default(st: State<'_, AppState>, id: i64) -> Res<()> {
    emulators::set_as_default_for_bound_platforms(&st, id)
}

#[tauri::command]
pub fn emulators_normalize_paths(st: State<'_, AppState>) -> Res<i64> {
    emulators::normalize_paths(&st)
}

#[tauri::command]
pub fn emulator_install_from_folder(
    st: State<'_, AppState>,
    source: String,
    name: String,
) -> Res<InstallOutcome> {
    let result = installer::install_from_folder(&st, &source, &name)?;
    Ok(InstallOutcome {
        install_dir: result.install_dir,
        exe_candidates: result.exe_candidates,
    })
}

#[tauri::command]
pub fn emulator_install_from_archive(
    st: State<'_, AppState>,
    archive: String,
    name: String,
) -> Res<InstallOutcome> {
    let result = installer::install_from_archive(&st, &archive, &name)?;
    Ok(InstallOutcome {
        install_dir: result.install_dir,
        exe_candidates: result.exe_candidates,
    })
}

/// 安装完成后把安装目录与可执行文件写回模拟器配置。
#[tauri::command]
pub fn emulator_apply_install(
    st: State<'_, AppState>,
    id: i64,
    install_dir: String,
    exe: String,
) -> Res<Emulator> {
    let result = installer::InstallResult {
        install_dir,
        exe_candidates: if exe.trim().is_empty() {
            Vec::new()
        } else {
            vec![exe]
        },
    };
    installer::apply_install(&st, id, &result)?;
    emulators::list_all(&st)?
        .into_iter()
        .find(|e| e.id == id)
        .ok_or_else(|| "模拟器不存在".to_string())
}

#[tauri::command]
pub async fn emulator_check_update(st: State<'_, AppState>, repo: String) -> Res<ReleaseInfo> {
    installer::latest_release(&st, &repo).await
}

/// 直接启动模拟器可执行文件（不带 ROM），用于安装后调试。
#[tauri::command]
pub fn emulator_test_launch(st: State<'_, AppState>, id: i64) -> Res<String> {
    let emulator = emulators::list_all(&st)?
        .into_iter()
        .find(|e| e.id == id)
        .ok_or_else(|| "模拟器不存在".to_string())?;
    let exe = emulators::resolve_exe_path(&emulator);
    if exe.trim().is_empty() || !std::path::Path::new(&exe).exists() {
        return Err("可执行文件不存在，请先安装或编辑配置。".into());
    }
    let working_dir = std::path::Path::new(&exe)
        .parent()
        .map(|p| p.to_path_buf())
        .unwrap_or_default();
    std::process::Command::new(&exe)
        .current_dir(working_dir)
        .spawn()
        .map_err(|e| format!("启动失败：{e}"))?;
    Ok(format!(
        "已启动 {}",
        std::path::Path::new(&exe)
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_default()
    ))
}

#[tauri::command]
pub async fn emulator_download_asset(
    app: AppHandle,
    st: State<'_, AppState>,
    url: String,
    file_name: String,
) -> Res<String> {
    use std::io::Write;

    let task = TaskGuard::new(&app, &st, "download");
    let mut reporter = task.reporter();
    let (mut response, target) = installer::start_download(&st, &url, &file_name).await?;
    let total = response.content_length().unwrap_or(0) as usize;
    let mut file = std::fs::File::create(&target).map_err(|e| format!("保存下载文件失败：{e}"))?;
    let mut done = 0usize;
    loop {
        let chunk = match response.chunk().await {
            Ok(Some(chunk)) => chunk,
            Ok(None) => break,
            Err(e) => {
                drop(file);
                let _ = std::fs::remove_file(&target);
                drop(task);
                return Err(format!("读取下载内容失败：{e}"));
            }
        };
        file.write_all(&chunk)
            .map_err(|e| format!("保存下载文件失败：{e}"))?;
        done += chunk.len();
        let message = format!(
            "下载中 {} / {}",
            format_size(done as i64),
            if total > 0 {
                format_size(total as i64)
            } else {
                "未知大小".to_string()
            }
        );
        if reporter.tick(done, total, &message) {
            drop(file);
            let _ = std::fs::remove_file(&target);
            drop(task);
            return Err("已取消下载".into());
        }
    }
    reporter.tick(done, if total == 0 { done } else { total }, "下载完成");
    drop(task);
    Ok(target.to_string_lossy().to_string())
}

fn format_size(bytes: i64) -> String {
    const UNITS: [&str; 4] = ["B", "KB", "MB", "GB"];
    let mut value = bytes as f64;
    let mut unit = 0;
    while value >= 1024.0 && unit < UNITS.len() - 1 {
        value /= 1024.0;
        unit += 1;
    }
    if unit == 0 {
        format!("{} {}", bytes, UNITS[0])
    } else {
        format!("{:.1} {}", value, UNITS[unit])
    }
}
