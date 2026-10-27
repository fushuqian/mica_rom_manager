use crate::services::sharer::{self, ShareEstimate, SharePackResult, ShareRow};
use crate::state::AppState;
use crate::util::Res;
use tauri::State;

#[tauri::command]
pub fn share_rows(st: State<'_, AppState>, game_ids: Vec<i64>) -> Res<Vec<ShareRow>> {
    sharer::rows(&st, &game_ids)
}

#[tauri::command]
pub fn share_estimate(
    st: State<'_, AppState>,
    game_ids: Vec<i64>,
    include_emulators: bool,
    save_dir: Option<String>,
) -> Res<ShareEstimate> {
    sharer::estimate(&st, &game_ids, include_emulators, save_dir.as_deref())
}

#[tauri::command]
#[allow(clippy::too_many_arguments)]
pub fn share_export(
    st: State<'_, AppState>,
    game_ids: Vec<i64>,
    export_dir: String,
    package_name: String,
    include_emulators: bool,
    include_saves: bool,
    save_dir: Option<String>,
    create_zip: bool,
) -> Res<SharePackResult> {
    sharer::export(
        &st,
        &game_ids,
        &export_dir,
        &package_name,
        include_emulators,
        include_saves,
        save_dir.as_deref(),
        create_zip,
    )
}
