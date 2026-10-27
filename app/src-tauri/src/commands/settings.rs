use crate::db::{emulators, games};
use crate::models::{AppInfo, GeneralSettings};
use crate::settings;
use crate::state::AppState;
use crate::util::Res;
use std::collections::HashMap;
use tauri::State;

#[tauri::command]
pub fn app_info(st: State<'_, AppState>) -> AppInfo {
    st.app_info()
}

#[tauri::command]
pub fn settings_get_all(st: State<'_, AppState>) -> Res<HashMap<String, String>> {
    let conn = st.conn();
    settings::get_all(&conn)
}

#[tauri::command]
pub fn settings_set(st: State<'_, AppState>, key: String, value: String) -> Res<()> {
    let conn = st.conn();
    settings::set(&conn, &key, &value)
}

#[tauri::command]
pub fn settings_save_general(st: State<'_, AppState>, general: GeneralSettings) -> Res<AppInfo> {
    if general.proxy_mode == "manual" && !valid_proxy(&general.proxy_address) {
        return Err("手动代理地址格式不正确，应为 host:port，例如 127.0.0.1:7890。".into());
    }

    let previous_root = st.library_root();
    let new_root = general.library_root.trim().to_string();

    if general.portable && new_root.is_empty() {
        return Err("便携模式下请指定库目录（程序目录下的库文件夹，例如 game_library）。".into());
    }
    if !new_root.is_empty() {
        std::fs::create_dir_all(&new_root).map_err(|e| format!("库目录不可用：{e}"))?;
    }

    let stored_root = st.to_stored(&new_root);
    {
        let conn = st.conn();
        settings::set(&conn, "library.root", &stored_root)?;
        settings::set(
            &conn,
            "library.portable",
            if general.portable { "true" } else { "false" },
        )?;
        settings::set(&conn, "library.import_mode", &general.import_mode)?;
        settings::set(&conn, "network.proxy.mode", &general.proxy_mode)?;
        settings::set(&conn, "network.proxy.address", general.proxy_address.trim())?;
    }

    let relocated = if !previous_root.is_empty()
        && !new_root.is_empty()
        && !previous_root.eq_ignore_ascii_case(&new_root)
    {
        let conn = st.conn();
        games::relocate_library_paths(&conn, &previous_root, &new_root).unwrap_or(0)
    } else {
        0
    };

    st.set_portable(general.portable);
    let converted = games::normalize_paths(&st, general.portable)?;
    emulators::normalize_paths(&st)?;

    let mut info = st.app_info();
    info.library_root = new_root;
    println!("常规设置已保存：重定位 {relocated} 条，转换路径 {converted} 条");
    Ok(info)
}

fn valid_proxy(address: &str) -> bool {
    let text = address.trim();
    let Some((host, port)) = text.rsplit_once(':') else {
        return false;
    };
    !host.trim().is_empty() && port.trim().parse::<u16>().map(|p| p > 0).unwrap_or(false)
}
