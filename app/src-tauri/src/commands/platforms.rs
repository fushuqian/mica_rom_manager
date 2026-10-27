use crate::db::platforms;
use crate::models::PlatformSetting;
use crate::state::AppState;
use crate::util::Res;
use tauri::State;

#[tauri::command]
pub fn platforms_list(st: State<'_, AppState>) -> Res<Vec<PlatformSetting>> {
    platforms::list_all(&st)
}

#[tauri::command]
pub fn platform_get(st: State<'_, AppState>, name: String) -> Res<Option<PlatformSetting>> {
    platforms::get(&st, &name)
}

#[tauri::command]
pub fn platform_save(st: State<'_, AppState>, setting: PlatformSetting) -> Res<()> {
    platforms::save(&st, setting)
}

#[tauri::command]
pub fn platform_delete(st: State<'_, AppState>, name: String) -> Res<()> {
    platforms::delete(&st, &name)
}
