use std::path::{Path, PathBuf};

/// 数据目录：优先 exe 同级 data（便携），不可写回退 %LOCALAPPDATA%\MicaRomManager。
/// 环境变量 MICA_DATA_DIR 可强制覆盖（用于与 WPF 版共用同一份数据做验收）。
pub fn resolve_data_dir(exe_dir: &Path) -> PathBuf {
    if let Ok(raw) = std::env::var("MICA_DATA_DIR") {
        let trimmed = raw.trim();
        if !trimmed.is_empty() {
            return PathBuf::from(trimmed);
        }
    }

    let portable = exe_dir.join("data");
    if is_writable(&portable) {
        return portable;
    }

    let local = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| {
        std::env::var("USERPROFILE")
            .map(|p| format!("{}\\AppData\\Local", p))
            .unwrap_or_else(|_| ".".into())
    });
    PathBuf::from(local).join("MicaRomManager")
}

pub fn ensure_dirs(data_dir: &Path) -> Result<(), String> {
    let dirs = [
        data_dir.to_path_buf(),
        covers_dir(data_dir),
        emulators_dir(data_dir),
        exports_dir(data_dir),
        logs_dir(data_dir),
        downloads_dir(data_dir),
        backups_dir(data_dir),
    ];
    for dir in dirs {
        std::fs::create_dir_all(&dir)
            .map_err(|e| format!("创建目录失败 {}: {}", dir.display(), e))?;
    }
    Ok(())
}

pub fn covers_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("covers")
}

pub fn emulators_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("emulators")
}

pub fn exports_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("exports")
}

pub fn logs_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("logs")
}

pub fn downloads_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("downloads")
}

pub fn backups_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("emulators").join(".backup")
}

fn is_writable(dir: &Path) -> bool {
    if std::fs::create_dir_all(dir).is_err() {
        return false;
    }
    let probe = dir.join(".write-test");
    let ok = std::fs::write(&probe, "ok").is_ok();
    let _ = std::fs::remove_file(&probe);
    ok
}
