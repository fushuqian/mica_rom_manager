use crate::services::formats::default_formats_text;
use crate::services::PLATFORM_CATALOG;
use crate::util::Res;
use rusqlite::{params, Connection};

/// 与 WPF Database.Initialize 完全一致的 v1–v10 迁移。
pub fn migrate(conn: &Connection) -> Res<()> {
    let version: i64 = conn
        .query_row("PRAGMA user_version;", [], |r| r.get(0))
        .map_err(|e| e.to_string())?;

    if version < 1 {
        conn.execute_batch(&format!("{} PRAGMA user_version = 1;", SCHEMA_V1))
            .map_err(|e| e.to_string())?;
    }
    if version < 2 {
        conn.execute_batch(SCHEMA_V2).map_err(|e| e.to_string())?;
    }
    if version < 3 {
        conn.execute_batch(SCHEMA_V3).map_err(|e| e.to_string())?;
    }
    if version < 4 {
        conn.execute_batch(SCHEMA_V4).map_err(|e| e.to_string())?;
    }
    if version < 5 {
        conn.execute_batch(SCHEMA_V5).map_err(|e| e.to_string())?;
        for platform in PLATFORM_CATALOG {
            conn.execute(
                "INSERT OR IGNORE INTO platforms(name, folder) VALUES (?1, ?1)",
                params![platform],
            )
            .map_err(|e| e.to_string())?;
        }
    }
    if version < 6 {
        conn.execute_batch(SCHEMA_V6).map_err(|e| e.to_string())?;
    }
    if version < 7 {
        conn.execute_batch(SCHEMA_V7).map_err(|e| e.to_string())?;
        for platform in PLATFORM_CATALOG {
            let formats = default_formats_text(platform);
            if formats.is_empty() {
                continue;
            }
            conn.execute(
                "UPDATE platforms SET formats=?1 WHERE name=?2 AND (formats IS NULL OR formats='')",
                params![formats, platform],
            )
            .map_err(|e| e.to_string())?;
        }
    }
    if version < 8 {
        conn.execute_batch(SCHEMA_V8).map_err(|e| e.to_string())?;
    }
    if version < 9 {
        conn.execute_batch(SCHEMA_V9).map_err(|e| e.to_string())?;
        conn.execute_batch(
            "UPDATE platforms SET args_template=(
                 SELECT e.args_template FROM emulators e WHERE e.id=platforms.emulator_id
             ) WHERE args_template='' AND emulator_id IS NOT NULL;
             UPDATE emulators SET website_url='https://github.com/'||github_repo||'/releases'
             WHERE website_url='' AND github_repo<>'';
             INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'platform', name FROM platforms;
             INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'genre', genre FROM games WHERE genre<>'';
             INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'publisher', publisher FROM games WHERE publisher<>'';
             INSERT OR IGNORE INTO lookup_items(kind, value) SELECT 'developer', developer FROM games WHERE developer<>'';",
        )
        .map_err(|e| e.to_string())?;
        seed_tags(conn)?;
    }
    if version < 10 {
        conn.execute_batch(SCHEMA_V10).map_err(|e| e.to_string())?;
    }

    Ok(())
}

/// 把库里已有的标签拆成独立词条写入字典。
fn seed_tags(conn: &Connection) -> Res<()> {
    let mut tags: Vec<String> = Vec::new();
    let mut stmt = conn
        .prepare("SELECT tags FROM games WHERE tags<>''")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| r.get::<_, String>(0))
        .map_err(|e| e.to_string())?;
    for row in rows {
        let raw = row.map_err(|e| e.to_string())?;
        for part in raw.split([',', '，', ';', '；']) {
            let trimmed = part.trim();
            if trimmed.is_empty() {
                continue;
            }
            if !tags.iter().any(|t| t.eq_ignore_ascii_case(trimmed)) {
                tags.push(trimmed.to_string());
            }
        }
    }

    for tag in tags {
        conn.execute(
            "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ('tag', ?1)",
            params![tag],
        )
        .map_err(|e| e.to_string())?;
    }
    Ok(())
}

const SCHEMA_V1: &str = r#"
CREATE TABLE IF NOT EXISTS games (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    platform TEXT NOT NULL DEFAULT '',
    genre TEXT DEFAULT '',
    region TEXT DEFAULT '',
    release_year INTEGER,
    publisher TEXT DEFAULT '',
    developer TEXT DEFAULT '',
    favorite INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT '未玩',
    rom_path TEXT NOT NULL UNIQUE,
    file_size INTEGER NOT NULL DEFAULT 0,
    cover_path TEXT DEFAULT '',
    notes TEXT DEFAULT '',
    tags TEXT DEFAULT '',
    emulator_id INTEGER,
    play_count INTEGER NOT NULL DEFAULT 0,
    play_seconds INTEGER NOT NULL DEFAULT 0,
    last_played_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS emulators (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    exe_path TEXT NOT NULL DEFAULT '',
    args_template TEXT NOT NULL DEFAULT '"{rom}"',
    core_path TEXT DEFAULT '',
    supported_ext TEXT DEFAULT '',
    version TEXT DEFAULT '',
    install_dir TEXT DEFAULT '',
    config_path TEXT DEFAULT '',
    github_repo TEXT DEFAULT '',
    notes TEXT DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS emulator_platforms (
    emulator_id INTEGER NOT NULL,
    platform TEXT NOT NULL,
    is_default INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (emulator_id, platform)
);

CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT
);
"#;

const SCHEMA_V2: &str = r#"
ALTER TABLE games ADD COLUMN language TEXT DEFAULT '';
PRAGMA user_version = 2;
"#;

const SCHEMA_V3: &str = r#"
CREATE TABLE IF NOT EXISTS lookup_items (
    kind TEXT NOT NULL,
    value TEXT NOT NULL,
    PRIMARY KEY (kind, value)
);

INSERT OR IGNORE INTO lookup_items(kind, value) VALUES
    ('region', '日版'), ('region', '美版'), ('region', '欧版'), ('region', '国行'),
    ('region', '港版'), ('region', '台版'), ('region', '韩版'), ('region', '汉化'),
    ('region', '世界版'),
    ('language', '简体中文'), ('language', '繁体中文'), ('language', '英语'),
    ('language', '日语'), ('language', '韩语'), ('language', '法语'),
    ('language', '德语'), ('language', '西班牙语'), ('language', '意大利语'),
    ('language', '俄语'), ('language', '葡萄牙语'), ('language', '多语言');

PRAGMA user_version = 3;
"#;

const SCHEMA_V4: &str = r#"
ALTER TABLE games ADD COLUMN disc_paths TEXT DEFAULT '';
PRAGMA user_version = 4;
"#;

const SCHEMA_V5: &str = r#"
CREATE TABLE IF NOT EXISTS platforms (
    name TEXT PRIMARY KEY,
    emulator_id INTEGER,
    folder TEXT NOT NULL DEFAULT '',
    updated_at TEXT NOT NULL DEFAULT ''
);
PRAGMA user_version = 5;
"#;

const SCHEMA_V6: &str = r#"
ALTER TABLE games ADD COLUMN rom_hash TEXT DEFAULT '';
PRAGMA user_version = 6;
"#;

const SCHEMA_V7: &str = r#"
ALTER TABLE platforms ADD COLUMN formats TEXT DEFAULT '';
PRAGMA user_version = 7;
"#;

const SCHEMA_V8: &str = r#"
ALTER TABLE platforms ADD COLUMN core_path TEXT DEFAULT '';
PRAGMA user_version = 8;
"#;

const SCHEMA_V9: &str = r#"
ALTER TABLE platforms ADD COLUMN args_template TEXT DEFAULT '';
ALTER TABLE emulators ADD COLUMN website_url TEXT DEFAULT '';
PRAGMA user_version = 9;
"#;

const SCHEMA_V10: &str = r#"
ALTER TABLE games ADD COLUMN rating INTEGER NOT NULL DEFAULT 0;
UPDATE games SET status = '在玩' WHERE play_count > 0 AND status = '未玩';
PRAGMA user_version = 10;
"#;
