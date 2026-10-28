use crate::db::migrations::migrate;
use crate::models::AppInfo;
use crate::paths;
use crate::portable;
use crate::settings;
use crate::util::Res;
use rusqlite::Connection;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, MutexGuard};

pub struct AppState {
    pub data_dir: PathBuf,
    pub exe_dir: PathBuf,
    pub conn: Mutex<Connection>,
    portable: AtomicBool,
    cancel: AtomicBool,
}

impl AppState {
    pub fn new() -> Res<Self> {
        let exe_path = std::env::current_exe().map_err(|e| format!("无法获取程序路径：{e}"))?;
        let exe_dir = exe_path
            .parent()
            .map(|p| p.to_path_buf())
            .ok_or_else(|| "无法确定程序目录".to_string())?;
        let data_dir = paths::resolve_data_dir(&exe_dir);
        Self::open_in(data_dir, exe_dir)
    }

    /// 在指定数据目录初始化（测试与便携切换共用）。
    pub fn open_in(data_dir: PathBuf, exe_dir: PathBuf) -> Res<Self> {
        paths::ensure_dirs(&data_dir)?;

        let db_path = data_dir.join("mica.db");
        let conn = Connection::open(&db_path)
            .map_err(|e| format!("打开数据库失败 {}: {e}", db_path.display()))?;
        conn.execute_batch("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;")
            .map_err(|e| e.to_string())?;
        migrate(&conn)?;

        let portable = settings::get_bool(&conn, "library.portable");
        Ok(Self {
            data_dir,
            exe_dir,
            conn: Mutex::new(conn),
            portable: AtomicBool::new(portable),
            cancel: AtomicBool::new(false),
        })
    }

    pub fn conn(&self) -> MutexGuard<'_, Connection> {
        self.conn
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    pub fn portable(&self) -> bool {
        self.portable.load(Ordering::Relaxed)
    }

    /// 长任务开始时清掉上一次的取消请求。
    pub fn begin_task(&self) {
        self.cancel.store(false, Ordering::Relaxed);
    }

    pub fn request_cancel(&self) {
        self.cancel.store(true, Ordering::Relaxed);
    }

    pub fn cancelled(&self) -> bool {
        self.cancel.load(Ordering::Relaxed)
    }

    /// 任务结束，顺带清掉取消标记，避免下一次任务一开头就被取消。
    pub fn end_task(&self) {
        self.cancel.store(false, Ordering::Relaxed);
    }

    pub fn set_portable(&self, on: bool) {
        self.portable.store(on, Ordering::Relaxed);
    }

    pub fn expand(&self, stored: &str) -> String {
        portable::expand(stored, &self.data_dir, &self.exe_dir)
    }

    pub fn to_stored(&self, absolute: &str) -> String {
        portable::to_stored(absolute, self.portable(), &self.data_dir, &self.exe_dir)
    }

    /// 只处理 %DATA%，不把相对路径拼到程序目录（模拟器 exe/core 语义）。
    pub fn to_stored_data_only(&self, absolute: &str) -> String {
        portable::to_stored(absolute, false, &self.data_dir, &self.exe_dir)
    }

    pub fn expand_data_only(&self, stored: &str) -> String {
        if portable::strip_data_token(stored).is_some() {
            portable::expand(stored, &self.data_dir, &self.exe_dir)
        } else {
            stored.to_string()
        }
    }

    pub fn library_root(&self) -> String {
        let stored = {
            let conn = self.conn();
            settings::get(&conn, "library.root").unwrap_or_default()
        };
        self.expand(&stored)
    }

    pub fn app_info(&self) -> AppInfo {
        AppInfo {
            version: env!("CARGO_PKG_VERSION").to_string(),
            data_dir: self.data_dir.to_string_lossy().to_string(),
            exe_dir: self.exe_dir.to_string_lossy().to_string(),
            db_path: self.data_dir.join("mica.db").to_string_lossy().to_string(),
            library_root: self.library_root(),
            portable: self.portable(),
            covers_dir: paths::covers_dir(&self.data_dir)
                .to_string_lossy()
                .to_string(),
            emulators_dir: paths::emulators_dir(&self.data_dir)
                .to_string_lossy()
                .to_string(),
            exports_dir: paths::exports_dir(&self.data_dir)
                .to_string_lossy()
                .to_string(),
            downloads_dir: paths::downloads_dir(&self.data_dir)
                .to_string_lossy()
                .to_string(),
        }
    }
}
