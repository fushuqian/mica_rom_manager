use crate::db::{emulators, games, platforms};
use crate::models::{Emulator, Game};
use crate::services::args;
use crate::state::AppState;
use crate::util::Res;
#[cfg(windows)]
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LaunchOutcome {
    pub launched: bool,
    pub message: String,
    pub emulator_name: String,
    pub path: String,
    pub available_emulators: Vec<Emulator>,
}

/// 启动结果 + 待回收的子进程（统计由命令层在进程退出后写入）。
pub struct LaunchHandle {
    pub outcome: LaunchOutcome,
    pub child: Option<std::process::Child>,
}

fn fail(message: String, emulator_name: String, path: String) -> Res<LaunchHandle> {
    Ok(LaunchHandle {
        outcome: LaunchOutcome {
            launched: false,
            message,
            emulator_name,
            path,
            available_emulators: Vec::new(),
        },
        child: None,
    })
}

fn resolve_path(value: &str, install_dir: &str, must_exist: bool) -> Option<PathBuf> {
    if value.trim().is_empty() {
        return None;
    }
    let path = Path::new(value);
    let combined = if path.is_absolute() {
        path.to_path_buf()
    } else if install_dir.trim().is_empty() {
        PathBuf::from(value)
    } else {
        Path::new(install_dir).join(path)
    };
    if must_exist && !combined.exists() {
        return None;
    }
    Some(combined)
}

/// 与 WPF EmulatorResolver 一致：指定模拟器 > 游戏绑定 > 平台默认模拟器。
pub fn resolve_emulator(
    st: &AppState,
    game: &Game,
    override_id: Option<i64>,
) -> Res<Option<Emulator>> {
    let all = emulators::list_all(st)?;
    if let Some(id) = override_id {
        if let Some(found) = all.iter().find(|e| e.id == id && e.enabled) {
            return Ok(Some(found.clone()));
        }
    }
    if let Some(id) = game.emulator_id {
        if let Some(found) = all.iter().find(|e| e.id == id && e.enabled) {
            return Ok(Some(found.clone()));
        }
    }
    if let Some(setting) = platforms::get(st, &game.platform)? {
        if let Some(id) = setting.emulator_id {
            if let Some(found) = all.iter().find(|e| e.id == id && e.enabled) {
                return Ok(Some(found.clone()));
            }
        }
    }
    Ok(None)
}

pub fn launch(
    st: &AppState,
    game_id: i64,
    emulator_id: Option<i64>,
    disc_index: Option<usize>,
) -> Res<LaunchHandle> {
    let game = games::get(st, game_id)?.ok_or_else(|| "找不到该游戏记录".to_string())?;

    let discs = game.discs();
    let rom_path = match disc_index {
        Some(index) if index < discs.len() => discs[index].clone(),
        _ => game.rom_path.clone(),
    };

    if rom_path.trim().is_empty() || !Path::new(&rom_path).exists() {
        return fail(
            "ROM 文件不存在，请检查路径或刷新文件状态。".into(),
            String::new(),
            rom_path,
        );
    }

    let emulator = resolve_emulator(st, &game, emulator_id)?;
    let Some(emulator) = emulator else {
        let available = emulators::list_all(st)?
            .into_iter()
            .filter(|e| {
                e.enabled
                    && e.platforms
                        .iter()
                        .any(|p| p.eq_ignore_ascii_case(&game.platform))
            })
            .collect();
        return Ok(LaunchHandle {
            outcome: LaunchOutcome {
                launched: false,
                message: format!(
                    "还没有为平台「{}」配置模拟器。请到设置 → 模拟器管理中安装或绑定。",
                    game.platform
                ),
                emulator_name: String::new(),
                path: rom_path,
                available_emulators: available,
            },
            child: None,
        });
    };

    let install_dir = emulator.install_dir.clone();
    let Some(exe_path) = resolve_path(&emulator.exe_path, &install_dir, true) else {
        return fail(
            format!(
                "模拟器「{}」的可执行文件不存在，请在模拟器管理中检查配置。",
                emulator.name
            ),
            emulator.name.clone(),
            rom_path,
        );
    };

    let setting = platforms::get(st, &game.platform)?;
    let template = args::resolve_template(
        setting.as_ref().map(|s| s.args_template.as_str()),
        Some(&emulator.name),
        Some(&emulator.args_template),
    );

    let platform_core = setting
        .as_ref()
        .and_then(|s| resolve_path(&s.core_path, &install_dir, true));
    let core_path = platform_core.or_else(|| resolve_path(&emulator.core_path, &install_dir, true));

    if args::requires_core(&template) && core_path.is_none() {
        return fail(
            format!(
                "平台「{}」的启动参数需要核心文件，请到设置 → 平台里选择 RetroArch 核心。",
                game.platform
            ),
            emulator.name.clone(),
            rom_path,
        );
    }

    let core_arg = core_path
        .as_ref()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    let arguments = args::build(
        &template,
        &rom_path,
        Some(core_arg.as_str()).filter(|s| !s.is_empty()),
        None,
    );

    let working_dir = exe_path
        .parent()
        .map(|p| p.to_path_buf())
        .unwrap_or_else(|| PathBuf::from("."));

    let mut command = std::process::Command::new(&exe_path);
    if !arguments.trim().is_empty() {
        match split_arguments(&arguments) {
            Some(argv) => command.args(argv),
            None => command.raw_arg(&arguments),
        };
    }
    command.current_dir(&working_dir);

    match command.spawn() {
        Ok(child) => Ok(LaunchHandle {
            outcome: LaunchOutcome {
                launched: true,
                message: format!("已启动「{}」", game.title),
                emulator_name: emulator.name,
                path: rom_path,
                available_emulators: Vec::new(),
            },
            child: Some(child),
        }),
        Err(e) => fail(format!("启动失败：{e}"), emulator.name, rom_path),
    }
}

/// 简单的 Windows 命令行拆解（支持双引号包裹）。
fn split_arguments(raw: &str) -> Option<Vec<String>> {
    let mut args = Vec::new();
    let mut current = String::new();
    let mut in_quotes = false;
    let mut has_token = false;
    for ch in raw.chars() {
        match ch {
            '"' => {
                in_quotes = !in_quotes;
                has_token = true;
            }
            ' ' | '\t' if !in_quotes => {
                if has_token || !current.is_empty() {
                    args.push(std::mem::take(&mut current));
                    has_token = false;
                }
            }
            _ => {
                current.push(ch);
                has_token = true;
            }
        }
    }
    if in_quotes {
        return None;
    }
    if has_token || !current.is_empty() {
        args.push(current);
    }
    Some(args)
}
