use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct Game {
    pub id: i64,
    pub title: String,
    pub platform: String,
    pub genre: String,
    pub region: String,
    pub language: String,
    pub release_year: Option<i64>,
    pub publisher: String,
    pub developer: String,
    pub favorite: bool,
    pub rating: i64,
    pub status: String,
    pub rom_path: String,
    pub file_size: i64,
    pub cover_path: String,
    pub notes: String,
    pub tags: String,
    pub emulator_id: Option<i64>,
    pub play_count: i64,
    pub play_seconds: i64,
    pub last_played_at: Option<String>,
    pub created_at: String,
    pub updated_at: String,
    pub disc_paths: String,
    pub rom_hash: String,
    /// 只读：ROM 文件是否缺失（列表计算，不入库）。
    #[serde(default)]
    pub missing: bool,
}

impl Game {
    /// 全部碟片路径（第一碟 + 附加碟，去重）。
    pub fn discs(&self) -> Vec<String> {
        let mut list: Vec<String> = Vec::new();
        if !self.rom_path.trim().is_empty() {
            list.push(self.rom_path.clone());
        }
        for path in self
            .disc_paths
            .split('\n')
            .map(|line| line.trim())
            .filter(|line| !line.is_empty())
        {
            if !list.iter().any(|p| p.eq_ignore_ascii_case(path)) {
                list.push(path.to_string());
            }
        }
        list
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct Emulator {
    pub id: i64,
    pub name: String,
    pub exe_path: String,
    pub args_template: String,
    pub core_path: String,
    pub supported_ext: String,
    pub version: String,
    pub install_dir: String,
    pub config_path: String,
    pub github_repo: String,
    pub website_url: String,
    pub notes: String,
    pub enabled: bool,
    pub platforms: Vec<String>,
    pub default_platforms: Vec<String>,
    /// 只读：安装目录/可执行文件是否可用。
    #[serde(default)]
    pub available: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct PlatformSetting {
    pub name: String,
    pub emulator_id: Option<i64>,
    pub folder: String,
    pub formats: String,
    pub core_path: String,
    pub args_template: String,
    pub is_custom: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppInfo {
    pub version: String,
    pub data_dir: String,
    pub exe_dir: String,
    pub db_path: String,
    pub library_root: String,
    pub portable: bool,
    pub covers_dir: String,
    pub emulators_dir: String,
    pub exports_dir: String,
    pub downloads_dir: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct GeneralSettings {
    pub library_root: String,
    pub portable: bool,
    pub import_mode: String,
    pub proxy_mode: String,
    pub proxy_address: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LookupKind {
    pub key: String,
    pub title: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LookupActionResult {
    pub success: bool,
    pub message: String,
    pub affected: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ImportCandidate {
    pub file_path: String,
    pub file_name: String,
    pub file_size: i64,
    pub title: String,
    pub platform: String,
    pub rom_hash: String,
    pub needs_specify: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ScanResult {
    pub candidates: Vec<ImportCandidate>,
    pub duplicate_count: i64,
    pub excluded_count: i64,
    pub format_skipped_count: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ImportItem {
    pub file_path: String,
    pub title: String,
    pub platform: String,
    pub rom_hash: String,
    pub file_size: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct ImportOutcome {
    pub imported: i64,
    pub failed: i64,
    pub messages: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BatchChanges {
    pub title: Option<String>,
    pub platform: Option<String>,
    pub genre: Option<String>,
    pub region: Option<String>,
    pub language: Option<String>,
    pub release_year: Option<i64>,
    /// 勾选年份但留空时，表示把年份清空。
    #[serde(default)]
    pub clear_release_year: bool,
    pub publisher: Option<String>,
    pub developer: Option<String>,
    pub status: Option<String>,
    pub emulator_id: Option<i64>,
    /// 勾选模拟器但选择「不指定」时，表示清空绑定。
    #[serde(default)]
    pub clear_emulator: bool,
    pub tags_add: Option<Vec<String>>,
    pub tags_remove: Option<Vec<String>>,
    /// "append" 追加到原备注末尾；其他值覆盖。
    pub notes_mode: Option<String>,
    pub notes: Option<String>,
}
