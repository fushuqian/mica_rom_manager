use crate::db::games;
use crate::models::Game;
use crate::services::{cover, launcher};
use crate::state::AppState;
use crate::util::Res;
use std::path::Path;
use std::time::Instant;
use tauri::{AppHandle, Emitter, Manager, State};

#[tauri::command]
pub fn launch_game(
    app: AppHandle,
    st: State<'_, AppState>,
    game_id: i64,
    emulator_id: Option<i64>,
    disc_index: Option<usize>,
) -> Res<launcher::LaunchOutcome> {
    let handle = launcher::launch(&st, game_id, emulator_id, disc_index)?;
    let outcome = handle.outcome.clone();
    if let Some(mut child) = handle.child {
        let started = Instant::now();
        let app_handle = app.clone();
        std::thread::spawn(move || {
            let _ = child.wait();
            let seconds = started.elapsed().as_secs() as i64;
            let state = app_handle.state::<AppState>();
            let _ = games::add_play_stats(&state, game_id, seconds);
            let _ = app_handle.emit(
                "game://session-ended",
                serde_json::json!({ "gameId": game_id, "seconds": seconds }),
            );
        });
    }
    Ok(outcome)
}

#[tauri::command]
pub fn set_cover(st: State<'_, AppState>, game_id: i64, source_path: String) -> Res<Game> {
    let mut game = games::get(&st, game_id)?.ok_or_else(|| "找不到该游戏记录".to_string())?;
    let new_cover = cover::store(&st, &source_path)?;
    let old_cover = game.cover_path.clone();
    game.cover_path = new_cover;
    let saved = games::save(&st, game)?;
    if !old_cover.trim().is_empty() {
        cover::remove(&st, &old_cover);
    }
    Ok(saved)
}

#[tauri::command]
pub fn remove_cover(st: State<'_, AppState>, game_id: i64) -> Res<Game> {
    let mut game = games::get(&st, game_id)?.ok_or_else(|| "找不到该游戏记录".to_string())?;
    let old_cover = game.cover_path.clone();
    game.cover_path = String::new();
    let saved = games::save(&st, game)?;
    if !old_cover.trim().is_empty() {
        cover::remove(&st, &old_cover);
    }
    Ok(saved)
}

/// 把文件移入系统回收站（可多选）。
#[tauri::command]
pub fn recycle_files(paths: Vec<String>) -> Res<()> {
    let existing: Vec<&String> = paths
        .iter()
        .filter(|p| !p.trim().is_empty() && Path::new(p).exists())
        .collect();
    if existing.is_empty() {
        return Ok(());
    }
    trash::delete_all(existing).map_err(|e| format!("移入回收站失败：{e}"))?;
    Ok(())
}

#[tauri::command]
pub fn file_size(path: String) -> i64 {
    std::fs::metadata(&path)
        .map(|m| m.len() as i64)
        .unwrap_or(0)
}

#[tauri::command]
pub fn set_game_rating(st: State<'_, AppState>, game_id: i64, rating: i64) -> Res<Game> {
    let mut game = games::get(&st, game_id)?.ok_or_else(|| "找不到该游戏记录".to_string())?;
    game.rating = rating.clamp(0, 5);
    games::save(&st, game)
}

#[tauri::command]
pub fn toggle_favorite(st: State<'_, AppState>, game_id: i64) -> Res<Game> {
    let mut game = games::get(&st, game_id)?.ok_or_else(|| "找不到该游戏记录".to_string())?;
    game.favorite = !game.favorite;
    games::save(&st, game)
}
