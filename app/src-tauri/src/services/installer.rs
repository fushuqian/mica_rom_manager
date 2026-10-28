use crate::db::emulators;
use crate::settings;
use crate::state::AppState;
use crate::util::Res;
use serde::Serialize;
use std::path::{Path, PathBuf};

pub(crate) fn client(st: &AppState) -> Res<reqwest::Client> {
    let conn = st.conn();
    let mode = settings::get(&conn, "network.proxy.mode").unwrap_or_else(|| "system".into());
    let address = settings::get(&conn, "network.proxy.address").unwrap_or_default();
    drop(conn);

    let mut builder = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(30))
        .user_agent("MicaRomManager-Lite");
    builder = match mode.as_str() {
        "none" => builder.no_proxy(),
        "manual" if !address.trim().is_empty() => {
            let url = if address.starts_with("http") {
                address.trim().to_string()
            } else {
                format!("http://{}", address.trim())
            };
            builder.proxy(reqwest::Proxy::all(&url).map_err(|e| format!("代理地址无效：{e}"))?)
        }
        _ => builder,
    };
    builder
        .build()
        .map_err(|e| format!("创建网络客户端失败：{e}"))
}

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseAsset {
    pub name: String,
    pub download_url: String,
    pub size: i64,
}

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ReleaseInfo {
    pub tag: String,
    pub name: String,
    pub published_at: String,
    pub notes: String,
    pub assets: Vec<ReleaseAsset>,
}

pub async fn latest_release(st: &AppState, repo: &str) -> Res<ReleaseInfo> {
    let repo = repo.trim().trim_matches('/');
    if !repo.contains('/') {
        return Err("请填写 GitHub 仓库，格式为 owner/repo。".into());
    }
    let url = format!("https://api.github.com/repos/{repo}/releases/latest");
    let client = client(st)?;
    let response = client
        .get(&url)
        .header("Accept", "application/vnd.github+json")
        .send()
        .await
        .map_err(|e| format!("请求 GitHub 失败：{e}"))?;

    if response.status() == reqwest::StatusCode::NOT_FOUND {
        return Err(format!("仓库 {repo} 没有发布版本。"));
    }
    if !response.status().is_success() {
        return Err(format!("GitHub 返回状态：{}", response.status()));
    }

    let json: serde_json::Value = response
        .json()
        .await
        .map_err(|e| format!("解析 GitHub 响应失败：{e}"))?;

    let assets = json["assets"]
        .as_array()
        .map(|items| {
            items
                .iter()
                .map(|asset| ReleaseAsset {
                    name: asset["name"].as_str().unwrap_or_default().to_string(),
                    download_url: asset["browser_download_url"]
                        .as_str()
                        .unwrap_or_default()
                        .to_string(),
                    size: asset["size"].as_i64().unwrap_or(0),
                })
                .collect()
        })
        .unwrap_or_default();

    Ok(ReleaseInfo {
        tag: json["tag_name"].as_str().unwrap_or_default().to_string(),
        name: json["name"].as_str().unwrap_or_default().to_string(),
        published_at: json["published_at"]
            .as_str()
            .unwrap_or_default()
            .to_string(),
        notes: json["body"].as_str().unwrap_or_default().to_string(),
        assets,
    })
}

pub async fn download_asset(st: &AppState, url: &str, file_name: &str) -> Res<String> {
    let client = client(st)?;
    let response = client
        .get(url)
        .send()
        .await
        .map_err(|e| format!("下载失败：{e}"))?;
    if !response.status().is_success() {
        return Err(format!("下载失败，状态：{}", response.status()));
    }

    let safe = crate::util::sanitize_file_name(file_name);
    let downloads = crate::paths::downloads_dir(&st.data_dir);
    let target = downloads.join(if safe.is_empty() {
        "download.bin".to_string()
    } else {
        safe
    });
    std::fs::create_dir_all(&downloads).ok();

    let bytes = response
        .bytes()
        .await
        .map_err(|e| format!("读取下载内容失败：{e}"))?;
    std::fs::write(&target, &bytes).map_err(|e| format!("保存下载文件失败：{e}"))?;
    Ok(target.to_string_lossy().to_string())
}

pub struct InstallResult {
    pub install_dir: String,
    pub exe_candidates: Vec<String>,
}

const PENALIZED: [&str; 10] = [
    "uninstall",
    "setup",
    "install",
    "update",
    "updater",
    "crash",
    "report",
    "vcredist",
    "helper",
    "launcher",
];

pub fn install_from_folder(st: &AppState, source: &str, emulator_name: &str) -> Res<InstallResult> {
    let target = prepare_target(st, emulator_name)?;
    copy_directory(Path::new(source), &target)?;
    Ok(finish(&target, emulator_name))
}

pub fn install_from_archive(
    st: &AppState,
    archive: &str,
    emulator_name: &str,
) -> Res<InstallResult> {
    let temp =
        std::env::temp_dir().join(format!("MicaRomManager-{}", uuid::Uuid::new_v4().simple()));
    std::fs::create_dir_all(&temp).map_err(|e| format!("创建临时目录失败：{e}"))?;

    let result = (|| -> Res<InstallResult> {
        extract_archive(Path::new(archive), &temp)?;
        let source = find_extracted_root(&temp);
        let target = prepare_target(st, emulator_name)?;
        copy_directory(&source, &target)?;
        Ok(finish(&target, emulator_name))
    })();

    let _ = std::fs::remove_dir_all(&temp);
    result
}

fn extract_archive(archive: &Path, temp: &Path) -> Res<()> {
    let name = archive
        .file_name()
        .map(|s| s.to_string_lossy().to_lowercase())
        .unwrap_or_default();

    if name.ends_with(".zip") {
        let file = std::fs::File::open(archive).map_err(|e| format!("打开压缩包失败：{e}"))?;
        let mut zip = zip::ZipArchive::new(file).map_err(|e| format!("读取 zip 失败：{e}"))?;
        for index in 0..zip.len() {
            let mut entry = zip.by_index(index).map_err(|e| e.to_string())?;
            let Some(relative) = entry.enclosed_name() else {
                continue;
            };
            let target = temp.join(relative);
            if entry.is_dir() {
                std::fs::create_dir_all(&target).ok();
                continue;
            }
            if let Some(parent) = target.parent() {
                std::fs::create_dir_all(parent).ok();
            }
            let mut out =
                std::fs::File::create(&target).map_err(|e| format!("解压写入失败：{e}"))?;
            std::io::copy(&mut entry, &mut out).map_err(|e| format!("解压失败：{e}"))?;
        }
        return Ok(());
    }

    if name.ends_with(".tar.gz") || name.ends_with(".tgz") {
        let file = std::fs::File::open(archive).map_err(|e| format!("打开压缩包失败：{e}"))?;
        let decoder = flate2::read::GzDecoder::new(file);
        let mut tar = tar::Archive::new(decoder);
        tar.unpack(temp)
            .map_err(|e| format!("解压 tar.gz 失败：{e}"))?;
        return Ok(());
    }

    if name.ends_with(".7z") {
        let file = std::fs::File::open(archive).map_err(|e| format!("打开压缩包失败：{e}"))?;
        sevenz_rust::decompress(file, temp).map_err(|e| format!("解压 7z 失败：{e}"))?;
        return Ok(());
    }

    if name.ends_with(".rar") {
        return extract_rar(temp, archive);
    }

    Err("仅支持 zip / 7z / rar / tar.gz 压缩包。".into())
}

/// rar 优先使用内置 unrar-rs 解压，失败时回退到外部 7-Zip / WinRAR。
fn extract_rar(temp: &Path, archive: &Path) -> Res<()> {
    match extract_rar_builtin(temp, archive) {
        Ok(()) => Ok(()),
        Err(builtin) => extract_rar_external(temp, archive)
            .map_err(|external| format!("内置解压失败：{builtin}；外部工具：{external}")),
    }
}

fn extract_rar_builtin(temp: &Path, archive: &Path) -> Res<()> {
    let file = std::fs::File::open(archive).map_err(|e| format!("打开压缩包失败：{e}"))?;
    let mut rar = unrar_rs::RarArchive::open(file).map_err(|e| format!("读取 rar 失败：{e}"))?;
    for index in 0..rar.len() {
        let entry = rar
            .by_index(index)
            .map_err(|e| format!("读取 rar 条目失败：{e}"))?;
        entry
            .unpack_in(temp)
            .map_err(|e| format!("解压 rar 失败：{e}"))?;
    }
    Ok(())
}

/// 外部兜底：优先 7-Zip，其次 WinRAR。
fn extract_rar_external(temp: &Path, archive: &Path) -> Res<()> {
    let seven_zip = [
        r"C:\Program Files\7-Zip\7z.exe",
        r"C:\Program Files (x86)\7-Zip\7z.exe",
    ]
    .iter()
    .find(|path| Path::new(path).is_file());

    if let Some(exe) = seven_zip {
        let status = std::process::Command::new(exe)
            .arg("x")
            .arg("-y")
            .arg(format!("-o{}", temp.display()))
            .arg(archive)
            .status()
            .map_err(|e| format!("调用 7-Zip 失败：{e}"))?;
        if status.success() {
            return Ok(());
        }
        return Err("7-Zip 解压失败。".into());
    }

    let winrar = [
        r"C:\Program Files\WinRAR\UnRAR.exe",
        r"C:\Program Files (x86)\WinRAR\UnRAR.exe",
    ]
    .iter()
    .find(|path| Path::new(path).is_file());
    if let Some(exe) = winrar {
        let status = std::process::Command::new(exe)
            .arg("x")
            .arg("-y")
            .arg(archive)
            .arg(format!("{}\\", temp.display()))
            .status()
            .map_err(|e| format!("调用 WinRAR 失败：{e}"))?;
        if status.success() {
            return Ok(());
        }
        return Err("WinRAR 解压失败。".into());
    }

    Err("解压 RAR 需要已安装 7-Zip 或 WinRAR；也可以先手动解压，再用「从文件夹安装」。".into())
}

fn find_extracted_root(temp: &Path) -> PathBuf {
    let entries: Vec<PathBuf> = std::fs::read_dir(temp)
        .map(|iter| iter.filter_map(|e| e.ok()).map(|e| e.path()).collect())
        .unwrap_or_default();
    if entries.len() == 1 && entries[0].is_dir() {
        return entries[0].clone();
    }
    temp.to_path_buf()
}

/// 同名旧安装目录整体移到 data\emulators\.backup，不直接删除。
fn prepare_target(st: &AppState, emulator_name: &str) -> Res<PathBuf> {
    let safe_name = crate::services::importer::sanitize_folder_name(emulator_name);
    let emulators_root = crate::paths::emulators_dir(&st.data_dir);
    let target = emulators_root.join(&safe_name);
    if target.is_dir() {
        let backup_root = emulators_root.join(".backup");
        std::fs::create_dir_all(&backup_root).ok();
        let backup = backup_root.join(format!(
            "{}-{}",
            safe_name,
            chrono::Local::now().format("%Y%m%d%H%M%S")
        ));
        std::fs::rename(&target, &backup).map_err(|e| format!("备份旧安装失败：{e}"))?;
    }
    std::fs::create_dir_all(&target).map_err(|e| format!("创建安装目录失败：{e}"))?;
    Ok(target)
}

fn finish(target: &Path, emulator_name: &str) -> InstallResult {
    InstallResult {
        install_dir: target.to_string_lossy().to_string(),
        exe_candidates: find_exe_candidates(target, emulator_name),
    }
}

fn find_exe_candidates(directory: &Path, emulator_name: &str) -> Vec<String> {
    let mut candidates: Vec<(String, i32)> = walkdir::WalkDir::new(directory)
        .max_depth(4)
        .into_iter()
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.file_type().is_file())
        .filter(|entry| {
            entry
                .path()
                .extension()
                .map(|ext| ext.eq_ignore_ascii_case("exe"))
                .unwrap_or(false)
        })
        .map(|entry| {
            let path = entry.path().to_string_lossy().to_string();
            let score = score_exe(entry.path(), directory, emulator_name);
            (path, score)
        })
        .collect();
    candidates.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
    candidates.into_iter().map(|(path, _)| path).collect()
}

fn score_exe(path: &Path, root: &Path, emulator_name: &str) -> i32 {
    let stem = path
        .file_stem()
        .map(|s| s.to_string_lossy().to_lowercase())
        .unwrap_or_default();
    let target: String = emulator_name
        .chars()
        .filter(|c| c.is_alphanumeric())
        .collect::<String>()
        .to_lowercase();

    let mut score = 0;
    if !target.is_empty() {
        let compact = stem.replace([' ', '-', '_'], "");
        if compact == target {
            score += 100;
        } else if stem.contains(&target) {
            score += 40;
        }
    }
    if PENALIZED.iter().any(|keyword| stem.contains(keyword)) {
        score -= 200;
    }
    if let Ok(relative) = path.strip_prefix(root) {
        let depth = relative.components().count() as i32 - 1;
        score += (20 - depth * 5).max(0);
    }
    score
}

fn copy_directory(source: &Path, target: &Path) -> Res<()> {
    std::fs::create_dir_all(target).map_err(|e| format!("创建目录失败：{e}"))?;
    for entry in std::fs::read_dir(source).map_err(|e| format!("读取目录失败：{e}"))? {
        let entry = entry.map_err(|e| e.to_string())?;
        let name = entry.file_name();
        if name.to_string_lossy().eq_ignore_ascii_case(".git") {
            continue;
        }
        let from = entry.path();
        let to = target.join(&name);
        if from.is_dir() {
            copy_directory(&from, &to)?;
        } else {
            std::fs::copy(&from, &to).map_err(|e| format!("复制文件失败：{e}"))?;
        }
    }
    Ok(())
}

/// 安装完成后自动识别可执行文件并写回模拟器配置。
pub fn apply_install(st: &AppState, emulator_id: i64, result: &InstallResult) -> Res<()> {
    let mut emulator = emulators::list_all(st)?
        .into_iter()
        .find(|e| e.id == emulator_id)
        .ok_or_else(|| "模拟器不存在".to_string())?;

    emulator.install_dir = result.install_dir.clone();
    if let Some(exe) = result.exe_candidates.first() {
        if let Ok(relative) = Path::new(exe).strip_prefix(Path::new(&result.install_dir)) {
            emulator.exe_path = relative.to_string_lossy().to_string();
        } else {
            emulator.exe_path = exe.clone();
        }
    }
    emulators::save(st, emulator)?;
    emulators::normalize_paths(st)?;
    Ok(())
}
