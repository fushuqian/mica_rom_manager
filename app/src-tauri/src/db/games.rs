use crate::models::Game;
use crate::portable;
use crate::state::AppState;
use crate::util::{now_iso, Res};
use rusqlite::{params, Connection, Row};
use std::path::Path;

pub const COLUMNS: &str = "id, title, platform, genre, region, language, release_year, publisher, developer, favorite, status, rom_path, file_size, cover_path, notes, tags, emulator_id, play_count, play_seconds, last_played_at, created_at, updated_at, disc_paths, rom_hash, rating";

fn map_row(r: &Row) -> rusqlite::Result<Game> {
    Ok(Game {
        id: r.get(0)?,
        title: r.get(1)?,
        platform: r.get(2)?,
        genre: r.get::<_, Option<String>>(3)?.unwrap_or_default(),
        region: r.get::<_, Option<String>>(4)?.unwrap_or_default(),
        language: r.get::<_, Option<String>>(5)?.unwrap_or_default(),
        release_year: r.get(6)?,
        publisher: r.get::<_, Option<String>>(7)?.unwrap_or_default(),
        developer: r.get::<_, Option<String>>(8)?.unwrap_or_default(),
        favorite: r.get::<_, i64>(9)? != 0,
        status: r.get(10)?,
        rom_path: r.get(11)?,
        file_size: r.get(12)?,
        cover_path: r.get::<_, Option<String>>(13)?.unwrap_or_default(),
        notes: r.get::<_, Option<String>>(14)?.unwrap_or_default(),
        tags: r.get::<_, Option<String>>(15)?.unwrap_or_default(),
        emulator_id: r.get(16)?,
        play_count: r.get(17)?,
        play_seconds: r.get(18)?,
        last_played_at: r.get(19)?,
        created_at: r.get(20)?,
        updated_at: r.get(21)?,
        disc_paths: r.get::<_, Option<String>>(22)?.unwrap_or_default(),
        rom_hash: r.get::<_, Option<String>>(23)?.unwrap_or_default(),
        rating: r.get::<_, Option<i64>>(24)?.unwrap_or(0),
        missing: false,
    })
}

pub fn list_all(st: &AppState) -> Res<Vec<Game>> {
    let mut games = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare(&format!(
                "SELECT {COLUMNS} FROM games ORDER BY title COLLATE NOCASE"
            ))
            .map_err(|e| e.to_string())?;
        let rows = stmt.query_map([], map_row).map_err(|e| e.to_string())?;
        let mut list = Vec::new();
        for row in rows {
            list.push(row.map_err(|e| e.to_string())?);
        }
        list
    };

    for g in &mut games {
        g.rom_path = st.expand(&g.rom_path);
        g.cover_path = st.expand(&g.cover_path);
        g.disc_paths = portable::expand_multiline(&g.disc_paths, &st.data_dir, &st.exe_dir);
        g.missing = !g.rom_path.trim().is_empty() && !Path::new(&g.rom_path).exists();
    }
    Ok(games)
}

pub fn get(st: &AppState, id: i64) -> Res<Option<Game>> {
    let raw = {
        let conn = st.conn();
        let mut stmt = conn
            .prepare(&format!("SELECT {COLUMNS} FROM games WHERE id = ?1"))
            .map_err(|e| e.to_string())?;
        stmt.query_row(params![id], map_row).ok()
    };
    Ok(raw.map(|mut g| {
        g.rom_path = st.expand(&g.rom_path);
        g.cover_path = st.expand(&g.cover_path);
        g.disc_paths = portable::expand_multiline(&g.disc_paths, &st.data_dir, &st.exe_dir);
        g.missing = !g.rom_path.trim().is_empty() && !Path::new(&g.rom_path).exists();
        g
    }))
}

pub fn save(st: &AppState, game: Game) -> Res<Game> {
    let mut game = game;
    if game.title.trim().is_empty() {
        return Err("标题不能为空".into());
    }

    let stored_rom = st.to_stored(&game.rom_path);
    let stored_cover = st.to_stored(&game.cover_path);
    let stored_discs =
        portable::to_stored_multiline(&game.disc_paths, st.portable(), &st.data_dir, &st.exe_dir);
    let now = now_iso();

    {
        let conn = st.conn();
        if game.id == 0 {
            game.created_at = now.clone();
            game.updated_at = now.clone();
            conn.execute(
                "INSERT INTO games(title, platform, genre, region, language, release_year, publisher, developer, favorite, status, rom_path, file_size, cover_path, notes, tags, emulator_id, play_count, play_seconds, last_played_at, created_at, updated_at, disc_paths, rom_hash, rating)
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18,?19,?20,?21,?22,?23,?24)",
                params![
                    game.title,
                    game.platform,
                    game.genre,
                    game.region,
                    game.language,
                    game.release_year,
                    game.publisher,
                    game.developer,
                    game.favorite as i64,
                    game.status,
                    stored_rom,
                    game.file_size,
                    stored_cover,
                    game.notes,
                    game.tags,
                    game.emulator_id,
                    game.play_count,
                    game.play_seconds,
                    game.last_played_at,
                    game.created_at,
                    game.updated_at,
                    stored_discs,
                    game.rom_hash,
                    game.rating
                ],
            )
            .map_err(|e| e.to_string())?;
            game.id = conn.last_insert_rowid();
        } else {
            game.updated_at = now;
            conn.execute(
                "UPDATE games SET title=?2, platform=?3, genre=?4, region=?5, language=?6, release_year=?7, publisher=?8, developer=?9, favorite=?10, status=?11, rom_path=?12, file_size=?13, cover_path=?14, notes=?15, tags=?16, emulator_id=?17, play_count=?18, play_seconds=?19, last_played_at=?20, updated_at=?21, disc_paths=?22, rom_hash=?23, rating=?24 WHERE id=?1",
                params![
                    game.id,
                    game.title,
                    game.platform,
                    game.genre,
                    game.region,
                    game.language,
                    game.release_year,
                    game.publisher,
                    game.developer,
                    game.favorite as i64,
                    game.status,
                    stored_rom,
                    game.file_size,
                    stored_cover,
                    game.notes,
                    game.tags,
                    game.emulator_id,
                    game.play_count,
                    game.play_seconds,
                    game.last_played_at,
                    game.updated_at,
                    stored_discs,
                    game.rom_hash,
                    game.rating
                ],
            )
            .map_err(|e| e.to_string())?;
        }
    }

    // 回填展开后的路径，便于前端立即使用
    game.rom_path = st.expand(&st.to_stored(&game.rom_path));
    game.cover_path = st.expand(&st.to_stored(&game.cover_path));
    game.missing = !game.rom_path.trim().is_empty() && !Path::new(&game.rom_path).exists();
    Ok(game)
}

pub fn create(st: &AppState, title: &str) -> Res<Game> {
    let game = Game {
        title: title.to_string(),
        status: "未玩".to_string(),
        ..Default::default()
    };
    save(st, game)
}

pub fn delete(st: &AppState, id: i64) -> Res<()> {
    let conn = st.conn();
    conn.execute("DELETE FROM games WHERE id = ?1", params![id])
        .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn delete_many(st: &AppState, ids: &[i64]) -> Res<()> {
    if ids.is_empty() {
        return Ok(());
    }
    let conn = st.conn();
    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    {
        let mut stmt = tx
            .prepare("DELETE FROM games WHERE id = ?1")
            .map_err(|e| e.to_string())?;
        for id in ids {
            stmt.execute(params![id]).map_err(|e| e.to_string())?;
        }
    }
    tx.commit().map_err(|e| e.to_string())?;
    Ok(())
}

pub fn delete_all(st: &AppState) -> Res<()> {
    let conn = st.conn();
    conn.execute("DELETE FROM games", [])
        .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn list_hashes(st: &AppState) -> Res<Vec<String>> {
    let conn = st.conn();
    let mut stmt = conn
        .prepare("SELECT rom_hash FROM games WHERE rom_hash <> ''")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| r.get::<_, String>(0))
        .map_err(|e| e.to_string())?;
    let mut list = Vec::new();
    for row in rows {
        list.push(row.map_err(|e| e.to_string())?);
    }
    Ok(list)
}

pub fn set_hash(st: &AppState, id: i64, hash: &str) -> Res<()> {
    let conn = st.conn();
    conn.execute(
        "UPDATE games SET rom_hash = ?2, updated_at = ?3 WHERE id = ?1",
        params![id, hash, now_iso()],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn add_play_stats(st: &AppState, id: i64, seconds: i64) -> Res<()> {
    let seconds = seconds.max(0);
    let conn = st.conn();
    conn.execute(
        "UPDATE games SET play_count = play_count + 1,
             play_seconds = play_seconds + ?2,
             last_played_at = ?3,
             status = CASE WHEN status = '未玩' THEN '在玩' ELSE status END,
             updated_at = ?3
         WHERE id = ?1",
        params![id, seconds, now_iso()],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

/// 重定位库目录：把旧库目录下的 ROM/碟片/封面路径批量改到新库目录。
pub fn relocate_library_paths(conn: &Connection, old_root: &str, new_root: &str) -> Res<i64> {
    if old_root.trim().is_empty() || new_root.trim().is_empty() {
        return Ok(0);
    }
    let old_l = old_root.trim_end_matches(['\\', '/']).to_lowercase();
    let new_l = new_root.trim_end_matches(['\\', '/']).to_string();

    let mut affected = 0i64;
    let mut updates: Vec<(i64, String, String, String)> = Vec::new();
    {
        let mut stmt = conn
            .prepare("SELECT id, rom_path, disc_paths, cover_path FROM games")
            .map_err(|e| e.to_string())?;
        let rows = stmt
            .query_map([], |r| {
                Ok((
                    r.get::<_, i64>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, Option<String>>(2)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(3)?.unwrap_or_default(),
                ))
            })
            .map_err(|e| e.to_string())?;
        for row in rows {
            let (id, rom, discs, cover) = row.map_err(|e| e.to_string())?;
            let new_rom = replace_prefix(&rom, &old_l, &new_l);
            let new_discs = discs
                .lines()
                .map(|line| replace_prefix(line, &old_l, &new_l))
                .collect::<Vec<_>>()
                .join("\n");
            let new_cover = replace_prefix(&cover, &old_l, &new_l);
            if new_rom != rom || new_cover != cover || new_discs != discs {
                updates.push((id, new_rom, new_discs, new_cover));
            }
        }
    }

    for (id, rom, discs, cover) in updates {
        conn.execute(
            "UPDATE games SET rom_path=?2, disc_paths=?3, cover_path=?4 WHERE id=?1",
            params![id, rom, discs, cover],
        )
        .map_err(|e| e.to_string())?;
        affected += 1;
    }
    Ok(affected)
}

/// 切换便携模式后统一转换路径存储形式（相对 ↔ 绝对）。
pub fn normalize_paths(st: &AppState, portable: bool) -> Res<i64> {
    let data = st.data_dir.clone();
    let exe = st.exe_dir.clone();
    let mut updates: Vec<(i64, String, String, String)> = Vec::new();
    {
        let conn = st.conn();
        let mut stmt = conn
            .prepare("SELECT id, rom_path, disc_paths, cover_path FROM games")
            .map_err(|e| e.to_string())?;
        let rows = stmt
            .query_map([], |r| {
                Ok((
                    r.get::<_, i64>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, Option<String>>(2)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(3)?.unwrap_or_default(),
                ))
            })
            .map_err(|e| e.to_string())?;
        for row in rows {
            let (id, rom, discs, cover) = row.map_err(|e| e.to_string())?;
            let new_rom = convert(&rom, portable, &data, &exe);
            let new_discs = discs
                .lines()
                .map(|line| convert(line, portable, &data, &exe))
                .filter(|line| !line.is_empty())
                .collect::<Vec<_>>()
                .join("\n");
            let new_cover = convert(&cover, portable, &data, &exe);
            if new_rom != rom || new_discs != discs || new_cover != cover {
                updates.push((id, new_rom, new_discs, new_cover));
            }
        }
    }

    let conn = st.conn();
    let mut affected = 0i64;
    for (id, rom, discs, cover) in updates {
        conn.execute(
            "UPDATE games SET rom_path=?2, disc_paths=?3, cover_path=?4 WHERE id=?1",
            params![id, rom, discs, cover],
        )
        .map_err(|e| e.to_string())?;
        affected += 1;
    }
    Ok(affected)
}

fn convert(stored: &str, portable: bool, data: &std::path::Path, exe: &std::path::Path) -> String {
    let trimmed = stored.trim();
    if trimmed.is_empty() {
        return String::new();
    }
    let absolute = crate::portable::expand(trimmed, data, exe);
    crate::portable::to_stored(&absolute, portable, data, exe)
}

fn replace_prefix(path: &str, old_root_lower: &str, new_root: &str) -> String {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return path.to_string();
    }
    let lower = trimmed.to_lowercase();
    if lower.starts_with(old_root_lower) {
        let rest = &trimmed[old_root_lower.len()..];
        return format!("{}{}", new_root, rest);
    }
    path.to_string()
}
