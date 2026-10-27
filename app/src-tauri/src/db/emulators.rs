use crate::models::Emulator;
use crate::state::AppState;
use crate::util::{now_iso, Res};
use rusqlite::{params, Connection, Row};
use std::path::Path;

const COLUMNS: &str = "id,name,exe_path,args_template,core_path,supported_ext,version,install_dir,config_path,github_repo,notes,enabled,website_url";

fn map_row(r: &Row) -> rusqlite::Result<Emulator> {
    Ok(Emulator {
        id: r.get(0)?,
        name: r.get(1)?,
        exe_path: r.get::<_, Option<String>>(2)?.unwrap_or_default(),
        args_template: r.get::<_, Option<String>>(3)?.unwrap_or_default(),
        core_path: r.get::<_, Option<String>>(4)?.unwrap_or_default(),
        supported_ext: r.get::<_, Option<String>>(5)?.unwrap_or_default(),
        version: r.get::<_, Option<String>>(6)?.unwrap_or_default(),
        install_dir: r.get::<_, Option<String>>(7)?.unwrap_or_default(),
        config_path: r.get::<_, Option<String>>(8)?.unwrap_or_default(),
        github_repo: r.get::<_, Option<String>>(9)?.unwrap_or_default(),
        notes: r.get::<_, Option<String>>(10)?.unwrap_or_default(),
        enabled: r.get::<_, i64>(11)? != 0,
        website_url: r.get::<_, Option<String>>(12)?.unwrap_or_default(),
        platforms: Vec::new(),
        default_platforms: Vec::new(),
        available: false,
    })
}

pub fn list_all(st: &AppState) -> Res<Vec<Emulator>> {
    let mut list = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare(&format!(
                "SELECT {COLUMNS} FROM emulators ORDER BY name COLLATE NOCASE"
            ))
            .map_err(|e| e.to_string())?;
        let rows = stmt.query_map([], map_row).map_err(|e| e.to_string())?;
        let mut list = Vec::new();
        for row in rows {
            list.push(row.map_err(|e| e.to_string())?);
        }

        let mut stmt2 = conn
            .prepare("SELECT emulator_id, platform, is_default FROM emulator_platforms ORDER BY platform COLLATE NOCASE")
            .map_err(|e| e.to_string())?;
        let rows2 = stmt2
            .query_map([], |r| {
                Ok((
                    r.get::<_, i64>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, i64>(2)? != 0,
                ))
            })
            .map_err(|e| e.to_string())?;
        let bindings: Vec<(i64, String, bool)> = rows2
            .map(|r| r.map_err(|e| e.to_string()))
            .collect::<Result<_, _>>()?;
        for (emulator_id, platform, is_default) in bindings {
            if let Some(emulator) = list.iter_mut().find(|e| e.id == emulator_id) {
                emulator.platforms.push(platform.clone());
                if is_default {
                    emulator.default_platforms.push(platform);
                }
            }
        }
        list
    };

    for e in &mut list {
        e.install_dir = st.expand(&e.install_dir);
        e.exe_path = st.expand_data_only(&e.exe_path);
        e.core_path = st.expand_data_only(&e.core_path);
        e.config_path = st.expand_data_only(&e.config_path);
        let exe_full = resolve_exe_path(e);
        e.available = !exe_full.trim().is_empty() && Path::new(&exe_full).exists();
    }
    Ok(list)
}

/// exe_path 为单段文件名时相对安装目录，否则原样使用。
pub fn resolve_exe_path(emulator: &Emulator) -> String {
    let exe = emulator.exe_path.trim();
    if exe.is_empty() {
        return String::new();
    }
    let path = Path::new(exe);
    if path.is_absolute() {
        return exe.to_string();
    }
    let parent = Path::new(&emulator.install_dir);
    if parent.as_os_str().is_empty() {
        return exe.to_string();
    }
    parent.join(path).to_string_lossy().to_string()
}

pub fn save(st: &AppState, emulator: Emulator) -> Res<Emulator> {
    let mut emulator = emulator;
    if emulator.name.trim().is_empty() {
        return Err("模拟器名称不能为空".into());
    }

    let stored_exe = st.to_stored_data_only(&emulator.exe_path);
    let stored_core = st.to_stored_data_only(&emulator.core_path);
    let stored_config = st.to_stored_data_only(&emulator.config_path);
    let stored_install = st.to_stored(&emulator.install_dir);
    let now = now_iso();

    let conn = st.conn();
    if emulator.id == 0 {
        conn.execute(
            "INSERT INTO emulators(name,exe_path,args_template,core_path,supported_ext,version,install_dir,config_path,github_repo,website_url,notes,enabled,created_at,updated_at)
             VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?13)",
            params![
                emulator.name,
                stored_exe,
                emulator.args_template,
                stored_core,
                emulator.supported_ext,
                emulator.version,
                stored_install,
                stored_config,
                emulator.github_repo,
                emulator.website_url,
                emulator.notes,
                emulator.enabled as i64,
                now
            ],
        )
        .map_err(|e| e.to_string())?;
        emulator.id = conn.last_insert_rowid();
    } else {
        conn.execute(
            "UPDATE emulators SET name=?2, exe_path=?3, args_template=?4, core_path=?5, supported_ext=?6,
                 version=?7, install_dir=?8, config_path=?9, github_repo=?10, website_url=?11, notes=?12,
                 enabled=?13, updated_at=?14
             WHERE id=?1",
            params![
                emulator.id,
                emulator.name,
                stored_exe,
                emulator.args_template,
                stored_core,
                emulator.supported_ext,
                emulator.version,
                stored_install,
                stored_config,
                emulator.github_repo,
                emulator.website_url,
                emulator.notes,
                emulator.enabled as i64,
                now
            ],
        )
        .map_err(|e| e.to_string())?;
    }

    save_platforms(
        &conn,
        emulator.id,
        &emulator.platforms,
        &emulator.default_platforms,
    )?;
    Ok(emulator)
}

pub fn save_platforms(
    conn: &Connection,
    emulator_id: i64,
    platforms: &[String],
    defaults: &[String],
) -> Res<()> {
    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    tx.execute(
        "DELETE FROM emulator_platforms WHERE emulator_id=?1",
        params![emulator_id],
    )
    .map_err(|e| e.to_string())?;
    let mut seen: Vec<String> = Vec::new();
    for platform in platforms {
        if seen.iter().any(|p| p.eq_ignore_ascii_case(platform)) {
            continue;
        }
        seen.push(platform.clone());
        let is_default = defaults.iter().any(|d| d.eq_ignore_ascii_case(platform));
        tx.execute(
            "INSERT INTO emulator_platforms(emulator_id, platform, is_default) VALUES(?1,?2,?3)",
            params![emulator_id, platform, is_default as i64],
        )
        .map_err(|e| e.to_string())?;
    }
    tx.commit().map_err(|e| e.to_string())?;
    Ok(())
}

pub fn set_as_default_for_bound_platforms(st: &AppState, emulator_id: i64) -> Res<()> {
    let conn = st.conn();
    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    tx.execute(
        "UPDATE emulator_platforms SET is_default=0
         WHERE platform IN (SELECT platform FROM emulator_platforms WHERE emulator_id=?1)",
        params![emulator_id],
    )
    .map_err(|e| e.to_string())?;
    tx.execute(
        "UPDATE emulator_platforms SET is_default=1 WHERE emulator_id=?1",
        params![emulator_id],
    )
    .map_err(|e| e.to_string())?;
    tx.commit().map_err(|e| e.to_string())?;
    Ok(())
}

pub fn delete(st: &AppState, emulator_id: i64) -> Res<()> {
    let conn = st.conn();
    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    tx.execute(
        "DELETE FROM emulator_platforms WHERE emulator_id=?1",
        params![emulator_id],
    )
    .map_err(|e| e.to_string())?;
    tx.execute("DELETE FROM emulators WHERE id=?1", params![emulator_id])
        .map_err(|e| e.to_string())?;
    tx.commit().map_err(|e| e.to_string())?;
    Ok(())
}

/// 数据目录下的绝对路径改写为 %DATA%（幂等），与 EmulatorRepository.NormalizePaths 一致。
pub fn normalize_paths(st: &AppState) -> Res<i64> {
    let rows: Vec<(i64, String, String, String, String)> = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare("SELECT id, exe_path, core_path, config_path, install_dir FROM emulators")
            .map_err(|e| e.to_string())?;
        let mapped = stmt
            .query_map([], |r| {
                Ok((
                    r.get::<_, i64>(0)?,
                    r.get::<_, Option<String>>(1)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(2)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(3)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(4)?.unwrap_or_default(),
                ))
            })
            .map_err(|e| e.to_string())?;
        mapped
            .map(|r| r.map_err(|e| e.to_string()))
            .collect::<Result<_, _>>()?
    };

    let mut changed = 0i64;
    let conn = st.conn();
    for (id, exe, core, config, install) in rows {
        let new_exe = st.to_stored_data_only(&exe);
        let new_core = st.to_stored_data_only(&core);
        let new_config = st.to_stored_data_only(&config);
        let new_install = st.to_stored(&install);
        if new_exe == exe && new_core == core && new_config == config && new_install == install {
            continue;
        }
        conn.execute(
            "UPDATE emulators SET exe_path=?2, core_path=?3, config_path=?4, install_dir=?5 WHERE id=?1",
            params![id, new_exe, new_core, new_config, new_install],
        )
        .map_err(|e| e.to_string())?;
        changed += 1;
    }
    Ok(changed)
}
