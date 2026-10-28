use crate::progress::TaskGuard;
use crate::services::maintenance;
use crate::state::AppState;
use crate::util::Res;
use tauri::{AppHandle, State};

#[tauri::command]
pub fn maintenance_stats(st: State<'_, AppState>) -> Res<maintenance::MaintenanceStats> {
    maintenance::stats(&st)
}

#[tauri::command]
pub fn maintenance_hash_missing(
    app: AppHandle,
    st: State<'_, AppState>,
) -> Res<maintenance::HashOutcome> {
    let task = TaskGuard::new(&app, &st, "hash-missing");
    let mut reporter = task.reporter();
    let result = maintenance::hash_missing(&st, &mut reporter);
    drop(task);
    result
}

#[tauri::command]
pub fn maintenance_clean_missing(st: State<'_, AppState>) -> Res<i64> {
    maintenance::clean_missing(&st)
}

#[tauri::command]
pub fn maintenance_export_csv(st: State<'_, AppState>, path: String) -> Res<i64> {
    maintenance::export_csv(&st, &path)
}

#[tauri::command]
pub fn maintenance_backup_db(st: State<'_, AppState>, path: String) -> Res<()> {
    maintenance::backup_db(&st, &path)
}

#[tauri::command]
pub fn maintenance_restore_db(st: State<'_, AppState>, path: String) -> Res<()> {
    maintenance::restore_db(&st, &path)
}

#[tauri::command]
pub fn maintenance_sync_tags(st: State<'_, AppState>) -> Res<()> {
    maintenance::sync_lookup_tags(&st)
}
