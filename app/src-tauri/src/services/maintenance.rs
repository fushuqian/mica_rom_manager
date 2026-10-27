use crate::db::{games, lookups};
use crate::models::Game;
use crate::services::hasher;
use crate::state::AppState;
use crate::util::Res;
use serde::Serialize;
use std::path::Path;

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MaintenanceStats {
    pub total: i64,
    pub missing: i64,
    pub hash_missing: i64,
}

fn is_missing(st: &AppState, game: &Game) -> bool {
    let discs = game.discs();
    if discs.is_empty() {
        return false;
    }
    discs
        .iter()
        .all(|path| !Path::new(&st.expand(path)).is_file())
}

pub fn stats(st: &AppState) -> Res<MaintenanceStats> {
    let list = games::list_all(st)?;
    let mut result = MaintenanceStats {
        total: list.len() as i64,
        ..Default::default()
    };
    for game in &list {
        if is_missing(st, game) {
            result.missing += 1;
        }
        if game.rom_hash.trim().is_empty()
            && !game.rom_path.trim().is_empty()
            && Path::new(&st.expand(&game.rom_path)).is_file()
        {
            result.hash_missing += 1;
        }
    }
    Ok(result)
}

/// 给所有缺失指纹且文件存在的记录补算指纹。
pub fn hash_missing(st: &AppState) -> Res<(i64, i64)> {
    let list = games::list_all(st)?;
    let mut computed = 0i64;
    let mut skipped = 0i64;
    for game in list {
        if !game.rom_hash.trim().is_empty() || game.rom_path.trim().is_empty() {
            continue;
        }
        let path = st.expand(&game.rom_path);
        if !Path::new(&path).is_file() {
            continue;
        }
        let hash = hasher::compute(&path);
        if hash.is_empty() {
            skipped += 1;
            continue;
        }
        games::set_hash(st, game.id, &hash)?;
        computed += 1;
    }
    Ok((computed, skipped))
}

/// 移除所有碟片都不存在的记录（只删数据库记录）。
pub fn clean_missing(st: &AppState) -> Res<i64> {
    let list = games::list_all(st)?;
    let mut removed = 0i64;
    for game in &list {
        if is_missing(st, game) {
            games::delete(st, game.id)?;
            removed += 1;
        }
    }
    Ok(removed)
}

fn quote(value: &str) -> String {
    format!("\"{}\"", value.replace('"', "\"\""))
}

/// 导出 CSV（UTF-8 BOM，列与 WPF 版一致）。
pub fn export_csv(st: &AppState, path: &str) -> Res<i64> {
    let list = games::list_all(st)?;
    let mut text = String::from(
        "标题,平台,类型,区域,语言,年份,状态,收藏,游玩次数,游玩时长(秒),最近游玩,文件大小,标签,封面,ROM路径,备注\r\n",
    );
    for game in &list {
        let last_played = game
            .last_played_at
            .as_deref()
            .map(display_time)
            .unwrap_or_default();
        let columns = [
            quote(&game.title),
            quote(&game.platform),
            quote(&game.genre),
            quote(&game.region),
            quote(&game.language),
            game.release_year.map(|y| y.to_string()).unwrap_or_default(),
            quote(&game.status),
            if game.favorite { "是" } else { "否" }.to_string(),
            game.play_count.to_string(),
            game.play_seconds.to_string(),
            quote(&last_played),
            game.file_size.to_string(),
            quote(&game.tags),
            quote(&game.cover_path),
            quote(&game.rom_path),
            quote(&game.notes),
        ];
        text.push_str(&columns.join(","));
        text.push_str("\r\n");
    }
    if let Some(parent) = Path::new(path).parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    let mut bytes = Vec::with_capacity(text.len() + 3);
    bytes.extend_from_slice(&[0xEF, 0xBB, 0xBF]);
    bytes.extend_from_slice(text.as_bytes());
    std::fs::write(path, bytes).map_err(|e| format!("导出失败：{e}"))?;
    Ok(list.len() as i64)
}

/// 把库内标签写入字典库，保持与 WPF 版一致的下拉数据来源。
pub fn sync_lookup_tags(st: &AppState) -> Res<()> {
    let mut tags: Vec<String> = Vec::new();
    for game in games::list_all(st)? {
        for tag in lookups::split_tags(&game.tags) {
            if !tags.iter().any(|t| t.eq_ignore_ascii_case(&tag)) {
                tags.push(tag);
            }
        }
    }
    for tag in tags {
        lookups::add(st, lookups::TAG, &tag)?;
    }
    Ok(())
}

fn display_time(raw: &str) -> String {
    let text = raw.trim();
    if text.len() >= 16 {
        text[..16].replace('T', " ")
    } else {
        text.to_string()
    }
}

pub fn backup_db(st: &AppState, path: &str) -> Res<()> {
    {
        let conn = st.conn();
        conn.execute_batch("PRAGMA wal_checkpoint(TRUNCATE);")
            .map_err(|e| format!("备份失败：{e}"))?;
    }
    let db = st.data_dir.join("mica.db");
    if let Some(parent) = Path::new(path).parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    std::fs::copy(&db, path).map_err(|e| format!("备份失败：{e}"))?;
    Ok(())
}

/// 用备份覆盖当前库（逐表替换，无需关闭连接；恢复后建议重启）。
pub fn restore_db(st: &AppState, path: &str) -> Res<()> {
    if !Path::new(path).is_file() {
        return Err("备份文件不存在".into());
    }
    let conn = st.conn();
    let version: i64 = conn
        .query_row("PRAGMA user_version;", [], |r| r.get(0))
        .map_err(|e| e.to_string())?;

    conn.execute("ATTACH DATABASE ?1 AS bk", rusqlite::params![path])
        .map_err(|e| format!("无法读取备份：{e}"))?;

    let backup_version: i64 = conn
        .query_row("PRAGMA bk.user_version;", [], |r| r.get(0))
        .map_err(|e| {
            let _ = conn.execute_batch("DETACH DATABASE bk");
            format!("备份版本读取失败：{e}")
        })?;

    if backup_version != version {
        let _ = conn.execute_batch("DETACH DATABASE bk");
        return Err(format!(
            "备份数据库版本（v{backup_version}）与当前（v{version}）不一致，请先升级备份或使用同版本程序。"
        ));
    }

    conn.execute_batch("PRAGMA foreign_keys = OFF;")
        .map_err(|e| e.to_string())?;

    let tables = [
        "games",
        "emulator_platforms",
        "emulators",
        "platforms",
        "lookup_items",
        "settings",
    ];
    let result = (|| -> Res<()> {
        conn.execute_batch("BEGIN;").map_err(|e| e.to_string())?;
        for table in tables {
            conn.execute(&format!("DELETE FROM main.{table};"), [])
                .map_err(|e| e.to_string())?;
            conn.execute(
                &format!("INSERT INTO main.{table} SELECT * FROM bk.{table};"),
                [],
            )
            .map_err(|e| e.to_string())?;
        }
        conn.execute_batch("COMMIT;").map_err(|e| e.to_string())?;
        Ok(())
    })();

    let _ = conn.execute_batch("DETACH DATABASE bk; PRAGMA foreign_keys = ON;");
    result.map_err(|e| format!("恢复失败：{e}"))
}
