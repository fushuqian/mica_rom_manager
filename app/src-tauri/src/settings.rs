use rusqlite::{params, Connection};

pub fn get(conn: &Connection, key: &str) -> Option<String> {
    conn.query_row(
        "SELECT value FROM settings WHERE key = ?1",
        params![key],
        |r| r.get::<_, Option<String>>(0),
    )
    .ok()
    .flatten()
}

pub fn get_bool(conn: &Connection, key: &str) -> bool {
    get(conn, key)
        .map(|v| v.eq_ignore_ascii_case("true"))
        .unwrap_or(false)
}

pub fn set(conn: &Connection, key: &str, value: &str) -> Result<(), String> {
    conn.execute(
        "INSERT INTO settings(key, value) VALUES(?1, ?2) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
        params![key, value],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

pub fn get_all(conn: &Connection) -> Result<std::collections::HashMap<String, String>, String> {
    let mut stmt = conn
        .prepare("SELECT key, value FROM settings")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, String>(0)?,
                r.get::<_, Option<String>>(1)?.unwrap_or_default(),
            ))
        })
        .map_err(|e| e.to_string())?;
    let mut map = std::collections::HashMap::new();
    for row in rows {
        let (k, v) = row.map_err(|e| e.to_string())?;
        map.insert(k, v);
    }
    Ok(map)
}
