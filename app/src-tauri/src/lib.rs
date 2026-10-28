mod commands;
mod db;
mod models;
mod paths;
mod portable;
mod progress;
mod services;
mod settings;
mod state;
#[cfg(test)]
mod tests;
mod util;

use state::AppState;
use tauri::Manager;

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .setup(|app| {
            let state = AppState::new().map_err(|e| format!("初始化失败：{e}"))?;
            app.manage(state);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::settings::app_info,
            commands::settings::settings_get_all,
            commands::settings::settings_set,
            commands::settings::settings_save_general,
            commands::games::games_list,
            commands::games::games_save,
            commands::games::games_create,
            commands::games::games_delete,
            commands::games::games_delete_many,
            commands::games::games_delete_all,
            commands::games::games_set_hash,
            commands::games::games_add_play_stats,
            commands::games::games_batch_update,
            commands::games::games_merge_discs,
            commands::games::games_split_discs,
            commands::platforms::platforms_list,
            commands::platforms::platform_get,
            commands::platforms::platform_save,
            commands::platforms::platform_delete,
            commands::lookups::lookups_kinds,
            commands::lookups::lookups_list,
            commands::lookups::lookups_add,
            commands::lookups::lookups_rename,
            commands::lookups::lookups_delete,
            commands::lookups::lookups_count_usage,
            commands::emulators::emulators_list,
            commands::emulators::emulator_save,
            commands::emulators::emulator_delete,
            commands::emulators::emulator_set_default,
            commands::emulators::emulators_normalize_paths,
            commands::emulators::emulator_install_from_folder,
            commands::emulators::emulator_install_from_archive,
            commands::emulators::emulator_apply_install,
            commands::emulators::emulator_check_update,
            commands::emulators::emulator_download_asset,
            commands::emulators::emulator_test_launch,
            commands::system::file_exists,
            commands::system::detect_platform,
            commands::system::network_test_github,
            commands::system::cover_data_url,
            commands::system::app_version,
            commands::files::launch_game,
            commands::files::set_cover,
            commands::files::remove_cover,
            commands::files::recycle_files,
            commands::files::file_size,
            commands::files::set_game_rating,
            commands::files::toggle_favorite,
            commands::import::import_scan,
            commands::import::import_apply,
            commands::import::cancel_task,
            commands::import::platform_scan,
            commands::import::platform_organize,
            commands::import::import_supported_extensions,
            commands::maintenance::maintenance_stats,
            commands::maintenance::maintenance_hash_missing,
            commands::maintenance::maintenance_clean_missing,
            commands::maintenance::maintenance_export_csv,
            commands::maintenance::maintenance_backup_db,
            commands::maintenance::maintenance_restore_db,
            commands::maintenance::maintenance_sync_tags,
            commands::share::share_rows,
            commands::share::share_estimate,
            commands::share::share_export,
        ])
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
