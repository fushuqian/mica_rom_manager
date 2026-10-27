use crate::models::LookupActionResult;
use crate::services::formats::default_formats_text;
use crate::state::AppState;
use crate::util::{now_iso, Res};
use rusqlite::{params, Connection};

pub const REGION: &str = "region";
pub const LANGUAGE: &str = "language";
pub const PLATFORM: &str = "platform";
pub const GENRE: &str = "genre";
pub const PUBLISHER: &str = "publisher";
pub const DEVELOPER: &str = "developer";
pub const TAG: &str = "tag";

pub fn list(st: &AppState, kind: &str) -> Res<Vec<String>> {
    let conn = st.conn();
    let mut stmt = conn
        .prepare("SELECT value FROM lookup_items WHERE kind=?1 ORDER BY value")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map(params![kind], |r| r.get::<_, String>(0))
        .map_err(|e| e.to_string())?;
    rows.map(|r| r.map_err(|e| e.to_string()))
        .collect::<Result<_, _>>()
}

pub fn add(st: &AppState, kind: &str, value: &str) -> Res<LookupActionResult> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return Ok(LookupActionResult {
            success: false,
            message: "值不能为空。".into(),
            affected: 0,
        });
    }

    let conn = st.conn();
    let changed = conn
        .execute(
            "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES (?1, ?2)",
            params![kind, trimmed],
        )
        .map_err(|e| e.to_string())?;
    if changed == 0 {
        return Ok(LookupActionResult {
            success: false,
            message: "该值已存在。".into(),
            affected: 0,
        });
    }

    if kind == PLATFORM {
        conn.execute(
            "INSERT OR IGNORE INTO platforms (name, folder, formats, updated_at) VALUES (?1, ?1, ?2, ?3)",
            params![trimmed, default_formats_text(trimmed), now_iso()],
        )
        .map_err(|e| e.to_string())?;
    }

    Ok(LookupActionResult {
        success: true,
        message: format!("已添加「{}」。", trimmed),
        affected: 0,
    })
}

pub fn rename(
    st: &AppState,
    kind: &str,
    old_value: &str,
    new_value: &str,
) -> Res<LookupActionResult> {
    let new_name = new_value.trim();
    if new_name.is_empty() {
        return Ok(LookupActionResult {
            success: false,
            message: "新值不能为空。".into(),
            affected: 0,
        });
    }
    if old_value == new_name {
        return Ok(LookupActionResult {
            success: true,
            message: "值未变化。".into(),
            affected: 0,
        });
    }

    let conn = st.conn();
    if exists(&conn, kind, new_name)? {
        return Ok(LookupActionResult {
            success: false,
            message: "该值已存在。".into(),
            affected: 0,
        });
    }

    if kind == PLATFORM {
        let platform_exists: bool = conn
            .query_row(
                "SELECT 1 FROM platforms WHERE name=?1 LIMIT 1",
                params![new_name],
                |_| Ok(true),
            )
            .unwrap_or(false);
        if platform_exists {
            return Ok(LookupActionResult {
                success: false,
                message: format!("平台「{}」已存在。", new_name),
                affected: 0,
            });
        }
    }

    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    let affected = match kind {
        TAG => rename_tags(&tx, old_value, new_name)?,
        PLATFORM => rename_platform(&tx, old_value, new_name)?,
        _ => rename_column(&tx, kind, old_value, new_name)?,
    };
    tx.execute(
        "UPDATE lookup_items SET value=?1 WHERE kind=?2 AND value=?3",
        params![new_name, kind, old_value],
    )
    .map_err(|e| e.to_string())?;
    tx.commit().map_err(|e| e.to_string())?;

    Ok(LookupActionResult {
        success: true,
        message: format!(
            "已把「{}」改为「{}」，更新 {} 条记录。",
            old_value, new_name, affected
        ),
        affected,
    })
}

pub fn delete(st: &AppState, kind: &str, value: &str) -> Res<LookupActionResult> {
    let usage = count_usage(st, kind, value)?;
    if usage > 0 {
        return Ok(LookupActionResult {
            success: false,
            message: format!("还有 {} 条记录在使用「{}」，不能删除。", usage, value),
            affected: 0,
        });
    }

    let conn = st.conn();
    let tx = conn.unchecked_transaction().map_err(|e| e.to_string())?;
    tx.execute(
        "DELETE FROM lookup_items WHERE kind=?1 AND value=?2",
        params![kind, value],
    )
    .map_err(|e| e.to_string())?;
    if kind == PLATFORM {
        tx.execute("DELETE FROM platforms WHERE name=?1", params![value])
            .map_err(|e| e.to_string())?;
        tx.execute(
            "DELETE FROM emulator_platforms WHERE platform=?1",
            params![value],
        )
        .map_err(|e| e.to_string())?;
    }
    tx.commit().map_err(|e| e.to_string())?;

    Ok(LookupActionResult {
        success: true,
        message: format!("已删除「{}」。", value),
        affected: 0,
    })
}

pub fn count_usage(st: &AppState, kind: &str, value: &str) -> Res<i64> {
    if value.trim().is_empty() {
        return Ok(0);
    }
    let conn = st.conn();
    if kind == TAG {
        return count_tag_usage(&conn, value);
    }
    let column = match kind {
        REGION => "region",
        LANGUAGE => "language",
        GENRE => "genre",
        PUBLISHER => "publisher",
        DEVELOPER => "developer",
        PLATFORM => "platform",
        _ => return Ok(0),
    };
    let count: i64 = conn
        .query_row(
            &format!("SELECT COUNT(*) FROM games WHERE {column}=?1"),
            params![value],
            |r| r.get(0),
        )
        .map_err(|e| e.to_string())?;
    Ok(count)
}

pub fn split_tags(raw: &str) -> Vec<String> {
    raw.split([',', '，', ';', '；'])
        .map(|s| s.trim())
        .filter(|s| !s.is_empty())
        .map(|s| s.to_string())
        .collect()
}

fn exists(conn: &Connection, kind: &str, value: &str) -> Res<bool> {
    let found: bool = conn
        .query_row(
            "SELECT 1 FROM lookup_items WHERE kind=?1 AND value=?2 LIMIT 1",
            params![kind, value],
            |_| Ok(true),
        )
        .unwrap_or(false);
    Ok(found)
}

fn rename_column(conn: &Connection, kind: &str, old_value: &str, new_name: &str) -> Res<i64> {
    let column = match kind {
        REGION => "region",
        LANGUAGE => "language",
        GENRE => "genre",
        PUBLISHER => "publisher",
        DEVELOPER => "developer",
        _ => return Ok(0),
    };
    conn.execute(
        &format!("UPDATE games SET {column}=?1 WHERE {column}=?2"),
        params![new_name, old_value],
    )
    .map(|n| n as i64)
    .map_err(|e| e.to_string())
}

fn rename_platform(conn: &Connection, old_value: &str, new_name: &str) -> Res<i64> {
    conn.execute(
        "UPDATE platforms SET name=?1 WHERE name=?2",
        params![new_name, old_value],
    )
    .map_err(|e| e.to_string())?;
    conn.execute(
        "UPDATE emulator_platforms SET platform=?1 WHERE platform=?2",
        params![new_name, old_value],
    )
    .map_err(|e| e.to_string())?;
    conn.execute(
        "UPDATE games SET platform=?1 WHERE platform=?2",
        params![new_name, old_value],
    )
    .map(|n| n as i64)
    .map_err(|e| e.to_string())
}

fn rename_tags(conn: &Connection, old_value: &str, new_name: &str) -> Res<i64> {
    let rows: Vec<(i64, String)> = {
        let mut stmt = conn
            .prepare("SELECT id, tags FROM games WHERE tags<>''")
            .map_err(|e| e.to_string())?;
        let mapped = stmt
            .query_map([], |r| Ok((r.get::<_, i64>(0)?, r.get::<_, String>(1)?)))
            .map_err(|e| e.to_string())?;
        mapped
            .map(|r| r.map_err(|e| e.to_string()))
            .collect::<Result<_, _>>()?
    };

    let mut changed = 0i64;
    for (id, raw) in rows {
        let tags = split_tags(&raw);
        if !tags.iter().any(|t| t.eq_ignore_ascii_case(old_value)) {
            continue;
        }
        let mut updated: Vec<String> = Vec::new();
        for tag in tags {
            let value = if tag.eq_ignore_ascii_case(old_value) {
                new_name.to_string()
            } else {
                tag
            };
            if !updated.iter().any(|t| t.eq_ignore_ascii_case(&value)) {
                updated.push(value);
            }
        }
        conn.execute(
            "UPDATE games SET tags=?1 WHERE id=?2",
            params![updated.join(","), id],
        )
        .map_err(|e| e.to_string())?;
        changed += 1;
    }
    Ok(changed)
}

fn count_tag_usage(conn: &Connection, value: &str) -> Res<i64> {
    let mut stmt = conn
        .prepare("SELECT tags FROM games WHERE tags<>''")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| r.get::<_, String>(0))
        .map_err(|e| e.to_string())?;
    let mut count = 0i64;
    for row in rows {
        let raw = row.map_err(|e| e.to_string())?;
        if split_tags(&raw)
            .iter()
            .any(|t| t.eq_ignore_ascii_case(value))
        {
            count += 1;
        }
    }
    Ok(count)
}
