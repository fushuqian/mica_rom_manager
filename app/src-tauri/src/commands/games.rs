use crate::db::{games, lookups};
use crate::models::{BatchChanges, Game};
use crate::state::AppState;
use crate::util::Res;
use tauri::State;

#[tauri::command]
pub fn games_list(st: State<'_, AppState>) -> Res<Vec<Game>> {
    games::list_all(&st)
}

#[tauri::command]
pub fn games_save(st: State<'_, AppState>, game: Game) -> Res<Game> {
    games::save(&st, game)
}

#[tauri::command]
pub fn games_create(st: State<'_, AppState>, title: String) -> Res<Game> {
    games::create(&st, &title)
}

#[tauri::command]
pub fn games_delete(st: State<'_, AppState>, id: i64) -> Res<()> {
    games::delete(&st, id)
}

#[tauri::command]
pub fn games_delete_many(st: State<'_, AppState>, ids: Vec<i64>) -> Res<()> {
    games::delete_many(&st, &ids)
}

#[tauri::command]
pub fn games_delete_all(st: State<'_, AppState>) -> Res<()> {
    games::delete_all(&st)
}

#[tauri::command]
pub fn games_set_hash(st: State<'_, AppState>, id: i64, hash: String) -> Res<()> {
    games::set_hash(&st, id, &hash)
}

#[tauri::command]
pub fn games_add_play_stats(st: State<'_, AppState>, id: i64, seconds: i64) -> Res<()> {
    games::add_play_stats(&st, id, seconds)
}

fn apply_changes(game: &mut Game, changes: &BatchChanges) {
    if let Some(value) = &changes.title {
        game.title = value.clone();
    }
    if let Some(value) = &changes.platform {
        game.platform = value.clone();
    }
    if let Some(value) = &changes.genre {
        game.genre = value.clone();
    }
    if let Some(value) = &changes.region {
        game.region = value.clone();
    }
    if let Some(value) = &changes.language {
        game.language = value.clone();
    }
    if changes.clear_release_year {
        game.release_year = None;
    } else if let Some(year) = changes.release_year {
        game.release_year = Some(year);
    }
    if let Some(value) = &changes.publisher {
        game.publisher = value.clone();
    }
    if let Some(value) = &changes.developer {
        game.developer = value.clone();
    }
    if let Some(value) = &changes.status {
        game.status = value.clone();
    }
    if changes.clear_emulator {
        game.emulator_id = None;
    } else if let Some(id) = changes.emulator_id {
        game.emulator_id = Some(id);
    }

    if changes.tags_add.is_some() || changes.tags_remove.is_some() {
        let mut tags = lookups::split_tags(&game.tags);
        if let Some(add) = &changes.tags_add {
            for tag in add {
                let tag = tag.trim();
                if !tag.is_empty() && !tags.iter().any(|t| t.eq_ignore_ascii_case(tag)) {
                    tags.push(tag.to_string());
                }
            }
        }
        if let Some(remove) = &changes.tags_remove {
            tags.retain(|t| !remove.iter().any(|r| r.trim().eq_ignore_ascii_case(t)));
        }
        game.tags = tags.join(",");
    }

    if let Some(notes) = &changes.notes {
        let append = changes.notes_mode.as_deref() == Some("append");
        if append {
            if !notes.is_empty() {
                game.notes = if game.notes.trim().is_empty() {
                    notes.clone()
                } else {
                    format!("{}\n{}", game.notes.trim_end(), notes)
                };
            }
        } else {
            game.notes = notes.clone();
        }
    }
}

#[tauri::command]
pub fn games_batch_update(
    st: State<'_, AppState>,
    ids: Vec<i64>,
    changes: BatchChanges,
) -> Res<i64> {
    let mut updated = 0;
    for id in ids {
        let Some(mut game) = games::get(&st, id)? else {
            continue;
        };
        apply_changes(&mut game, &changes);
        games::save(&st, game)?;
        updated += 1;
    }
    Ok(updated)
}

/// 合并多碟：ids[0] 为主条目，其余碟片并入后删除。
#[tauri::command]
pub fn games_merge_discs(st: State<'_, AppState>, ids: Vec<i64>) -> Res<Game> {
    let Some(&main_id) = ids.first() else {
        return Err("请先选择至少 2 个条目".into());
    };
    if ids.len() < 2 {
        return Err("请先选择至少 2 个条目".into());
    }

    let mut main = games::get(&st, main_id)?.ok_or_else(|| "主条目不存在".to_string())?;
    let mut discs = main.discs();
    for id in ids.iter().skip(1) {
        if let Some(other) = games::get(&st, *id)? {
            for disc in other.discs() {
                if !discs.iter().any(|d| d.eq_ignore_ascii_case(&disc)) {
                    discs.push(disc);
                }
            }
        }
    }
    if discs.len() < 2 {
        return Err("没有可合并的碟片".into());
    }

    main.rom_path = discs[0].clone();
    main.disc_paths = discs[1..].join("\n");
    let saved = games::save(&st, main)?;

    for id in ids.iter().skip(1) {
        games::delete(&st, *id)?;
    }
    Ok(saved)
}

/// 拆分多碟：保留第一碟，其余碟片各自生成新条目。
#[tauri::command]
pub fn games_split_discs(st: State<'_, AppState>, id: i64) -> Res<Vec<Game>> {
    let Some(mut game) = games::get(&st, id)? else {
        return Err("条目不存在".into());
    };
    let discs = game.discs();
    if discs.len() < 2 {
        return Err("该条目不是多碟游戏".into());
    }

    game.disc_paths = String::new();
    let main = games::save(&st, game)?;
    let mut created = vec![main.clone()];

    for (index, path) in discs.iter().skip(1).enumerate() {
        let size = std::fs::metadata(path).map(|m| m.len() as i64).unwrap_or(0);
        let entry = Game {
            title: format!("{}（碟{}）", main.title, index + 2),
            platform: main.platform.clone(),
            genre: main.genre.clone(),
            region: main.region.clone(),
            language: main.language.clone(),
            release_year: main.release_year,
            publisher: main.publisher.clone(),
            developer: main.developer.clone(),
            status: main.status.clone(),
            emulator_id: main.emulator_id,
            rom_path: path.clone(),
            file_size: size,
            ..Default::default()
        };
        created.push(games::save(&st, entry)?);
    }
    Ok(created)
}
