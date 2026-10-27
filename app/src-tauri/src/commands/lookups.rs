use crate::db::lookups;
use crate::models::{LookupActionResult, LookupKind};
use crate::state::AppState;
use crate::util::Res;
use tauri::State;

#[tauri::command]
pub fn lookups_kinds() -> Vec<LookupKind> {
    vec![
        LookupKind {
            key: "region".into(),
            title: "区域".into(),
        },
        LookupKind {
            key: "language".into(),
            title: "语言".into(),
        },
        LookupKind {
            key: "platform".into(),
            title: "平台".into(),
        },
        LookupKind {
            key: "genre".into(),
            title: "类型".into(),
        },
        LookupKind {
            key: "publisher".into(),
            title: "发行商".into(),
        },
        LookupKind {
            key: "developer".into(),
            title: "开发商".into(),
        },
        LookupKind {
            key: "tag".into(),
            title: "标签".into(),
        },
    ]
}

#[tauri::command]
pub fn lookups_list(st: State<'_, AppState>, kind: String) -> Res<Vec<String>> {
    lookups::list(&st, &kind)
}

#[tauri::command]
pub fn lookups_add(
    st: State<'_, AppState>,
    kind: String,
    value: String,
) -> Res<LookupActionResult> {
    lookups::add(&st, &kind, &value)
}

#[tauri::command]
pub fn lookups_rename(
    st: State<'_, AppState>,
    kind: String,
    old_value: String,
    new_value: String,
) -> Res<LookupActionResult> {
    lookups::rename(&st, &kind, &old_value, &new_value)
}

#[tauri::command]
pub fn lookups_delete(
    st: State<'_, AppState>,
    kind: String,
    value: String,
) -> Res<LookupActionResult> {
    lookups::delete(&st, &kind, &value)
}

#[tauri::command]
pub fn lookups_count_usage(st: State<'_, AppState>, kind: String, value: String) -> Res<i64> {
    lookups::count_usage(&st, &kind, &value)
}
