use crate::models::PlatformSetting;
use crate::services::formats::{default_formats_text, normalize};
use crate::services::PLATFORM_CATALOG;
use crate::state::AppState;
use crate::util::{now_iso, Res};
use rusqlite::{params, Connection, Row};

fn map_row(r: &Row) -> rusqlite::Result<PlatformSetting> {
    let name: String = r.get(0)?;
    Ok(PlatformSetting {
        is_custom: !PLATFORM_CATALOG
            .iter()
            .any(|p| p.eq_ignore_ascii_case(&name)),
        name,
        emulator_id: r.get(1)?,
        folder: r.get::<_, Option<String>>(2)?.unwrap_or_default(),
        formats: r.get::<_, Option<String>>(3)?.unwrap_or_default(),
        core_path: r.get::<_, Option<String>>(4)?.unwrap_or_default(),
        args_template: r.get::<_, Option<String>>(5)?.unwrap_or_default(),
    })
}

pub fn list_all(st: &AppState) -> Res<Vec<PlatformSetting>> {
    let names: Vec<String> = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare("SELECT value FROM lookup_items WHERE kind='platform' ORDER BY value")
            .map_err(|e| e.to_string())?;
        let rows = stmt
            .query_map([], |r| r.get::<_, String>(0))
            .map_err(|e| e.to_string())?;
        rows.map(|r| r.map_err(|e| e.to_string()))
            .collect::<Result<_, _>>()?
    };

    {
        let conn = st.conn();
        for name in &names {
            ensure_exists(&conn, name)?;
        }
    }

    let mut map: Vec<PlatformSetting> = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare("SELECT name, emulator_id, folder, formats, core_path, args_template FROM platforms")
            .map_err(|e| e.to_string())?;
        let rows = stmt.query_map([], map_row).map_err(|e| e.to_string())?;
        rows.map(|r| r.map_err(|e| e.to_string()))
            .collect::<Result<_, _>>()?
    };

    let mut list: Vec<PlatformSetting> = Vec::new();
    for name in &names {
        if let Some(pos) = map.iter().position(|p| p.name.eq_ignore_ascii_case(name)) {
            list.push(map.remove(pos));
        }
    }
    if list.is_empty() {
        map.sort_by_key(|p| p.name.to_lowercase());
        list.extend(map);
    }
    Ok(list)
}

pub fn get(st: &AppState, platform: &str) -> Res<Option<PlatformSetting>> {
    if platform.trim().is_empty() {
        return Ok(None);
    }
    let conn = st.conn();
    let mut stmt = conn
        .prepare("SELECT name, emulator_id, folder, formats, core_path, args_template FROM platforms WHERE name=?1")
        .map_err(|e| e.to_string())?;
    let found = stmt.query_row(params![platform], map_row).ok();
    Ok(found)
}

pub fn save(st: &AppState, setting: PlatformSetting) -> Res<()> {
    if setting.name.trim().is_empty() {
        return Ok(());
    }
    let conn = st.conn();
    conn.execute(
        "INSERT INTO platforms (name, emulator_id, folder, formats, core_path, args_template, updated_at)
         VALUES (?1,?2,?3,?4,?5,?6,?7)
         ON CONFLICT(name) DO UPDATE SET
             emulator_id=?2, folder=?3, formats=?4, core_path=?5, args_template=?6, updated_at=?7",
        params![
            setting.name.trim(),
            setting.emulator_id,
            setting.folder.trim(),
            normalize(&setting.formats),
            setting.core_path.trim(),
            setting.args_template.trim(),
            now_iso()
        ],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn delete(st: &AppState, platform: &str) -> Res<()> {
    if platform.trim().is_empty() {
        return Ok(());
    }
    let conn = st.conn();
    conn.execute("DELETE FROM platforms WHERE name=?1", params![platform])
        .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn ensure_exists(conn: &Connection, platform: &str) -> Res<()> {
    if platform.trim().is_empty() {
        return Ok(());
    }
    conn.execute(
        "INSERT OR IGNORE INTO platforms (name, folder, formats, args_template, updated_at)
         VALUES (?1, ?1, ?2, '', ?3)",
        params![platform, default_formats_text(platform), now_iso()],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}
