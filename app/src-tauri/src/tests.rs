use crate::db::games;
use crate::models::Game;
use crate::portable;
use crate::services::{args, formats, hasher, organizer};
use crate::state::AppState;
use std::path::PathBuf;

fn temp_dir(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("mica-lite-{name}-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&dir).expect("创建临时目录");
    dir
}

fn user_version(st: &AppState) -> i64 {
    let conn = st.conn();
    conn.query_row("PRAGMA user_version;", [], |r| r.get(0))
        .expect("读取 user_version")
}

#[test]
fn fresh_db_reaches_schema_v10() {
    let dir = temp_dir("fresh");
    let st = AppState::open_in(dir.clone(), dir.join("exe")).expect("初始化数据目录");

    assert_eq!(user_version(&st), 10);

    let conn = st.conn();
    let platforms: i64 = conn
        .query_row("SELECT COUNT(*) FROM platforms", [], |r| r.get(0))
        .expect("统计平台");
    assert!(
        platforms >= 24,
        "平台数量应至少包含内置清单，实际 {platforms}"
    );

    let kinds: Vec<String> = conn
        .prepare("SELECT DISTINCT kind FROM lookup_items ORDER BY kind")
        .expect("准备查询")
        .query_map([], |r| r.get::<_, String>(0))
        .expect("查询字典种类")
        .collect::<Result<Vec<_>, _>>()
        .expect("读取字典种类");
    assert_eq!(
        kinds,
        vec!["language", "platform", "region"],
        "新库预置三类字典"
    );
    drop(conn);

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}

/// 用真实 WPF 版 mica.db 副本验证兼容性（设置 MICA_COMPAT_DB 后手动执行）。
#[test]
fn real_db_is_compatible() {
    let Ok(source) = std::env::var("MICA_COMPAT_DB") else {
        return;
    };
    let dir = temp_dir("compat");
    std::fs::copy(&source, dir.join("mica.db")).expect("复制数据库");

    let st = AppState::open_in(dir.clone(), dir.join("exe")).expect("打开真实数据库");
    assert_eq!(user_version(&st), 10, "真实库应已是 v10");

    let list = games::list_all(&st).expect("读取游戏列表");
    println!("兼容库校验：{} 条游戏记录", list.len());

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn portable_paths_roundtrip() {
    let data = PathBuf::from(r"C:\MicaData");
    let exe = PathBuf::from(r"C:\MicaApp");

    let stored = portable::to_stored(r"C:\MicaData\covers\a.png", false, &data, &exe);
    assert_eq!(stored, r"%DATA%\covers\a.png");
    assert_eq!(
        portable::expand(&stored, &data, &exe),
        r"C:\MicaData\covers\a.png"
    );

    let stored = portable::to_stored(r"C:\MicaApp\roms\a.nes", true, &data, &exe);
    assert_eq!(stored, r"roms\a.nes");
    assert_eq!(
        portable::to_stored(r"C:\MicaApp\roms\a.nes", false, &data, &exe),
        r"C:\MicaApp\roms\a.nes"
    );
    assert_eq!(
        portable::expand(&stored, &data, &exe),
        r"C:\MicaApp\roms\a.nes"
    );
}

#[test]
fn formats_parse_and_supports() {
    assert_eq!(formats::parse(".NES, sfc; gb"), vec![".nes", ".sfc", ".gb"]);
    assert_eq!(formats::normalize(".NES, sfc"), "nes sfc");
    assert!(formats::supports("nes sfc", "NES"));
    assert!(!formats::supports("nes sfc", "gba"));
    assert_eq!(
        formats::default_formats_text("PlayStation"),
        "iso cue bin chd pbp"
    );
}

#[test]
fn organizer_builds_disc_names() {
    let mut game = Game {
        title: "新鬼武者".into(),
        region: "欧版".into(),
        language: "英语".into(),
        rom_path: r"C:\lib\a\disc1.chd".into(),
        ..Default::default()
    };
    assert_eq!(
        organizer::build_file_name(&game, 0, r"C:\lib\a\disc1.chd"),
        "新鬼武者 (欧版)(英语).chd"
    );

    game.disc_paths = r"C:\lib\a\disc2.chd".into();
    assert_eq!(
        organizer::build_file_name(&game, 0, r"C:\lib\a\disc1.chd"),
        "新鬼武者 Disc-1 (欧版)(英语).chd"
    );
}

#[test]
fn import_scan_and_link_apply_writes_games() {
    let dir = temp_dir("import");
    let roms = dir.join("roms");
    std::fs::create_dir_all(&roms).expect("创建 ROM 目录");
    std::fs::write(roms.join("a.nes"), vec![0u8; 64]).expect("写 ROM");
    std::fs::write(roms.join("readme.txt"), b"skip me").expect("写非 ROM");

    let data = dir.join("data");
    let st = AppState::open_in(data.clone(), dir.join("exe")).expect("初始化数据目录");

    let scan = crate::services::importer::scan(
        &st,
        &[roms.to_string_lossy().to_string()],
        true,
        None,
        None,
        &mut crate::progress::NoopReporter,
    )
    .expect("扫描");
    assert_eq!(scan.candidates.len(), 1, "非 ROM 文件应被过滤");
    let candidate = &scan.candidates[0];
    assert_eq!(candidate.platform, "NES", "按扩展名识别平台");
    assert!(!candidate.rom_hash.is_empty(), "扫描时应计算指纹");

    let items = vec![crate::models::ImportItem {
        file_path: candidate.file_path.clone(),
        title: candidate.title.clone(),
        platform: candidate.platform.clone(),
        rom_hash: candidate.rom_hash.clone(),
        file_size: candidate.file_size,
    }];
    let outcome = crate::services::importer::apply(
        &st,
        &items,
        "Link",
        &mut crate::progress::NoopReporter,
    )
    .expect("入库");
    assert_eq!(outcome.imported, 1);

    let games = games::list_all(&st).expect("读取游戏");
    assert_eq!(games.len(), 1);
    assert_eq!(games[0].status, "未玩");
    assert_eq!(games[0].platform, "NES");

    let again = crate::services::importer::scan(
        &st,
        &[roms.to_string_lossy().to_string()],
        true,
        None,
        None,
        &mut crate::progress::NoopReporter,
    )
    .expect("再次扫描");
    assert_eq!(again.candidates.len(), 0, "同路径应判重");
    assert_eq!(again.duplicate_count, 1);

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn now_iso_matches_dotnet_roundtrip_format() {
    let text = crate::util::now_iso();
    // 例：2026-10-27T17:43:12.1234567+08:00
    let bytes = text.as_bytes();
    assert_eq!(text.len(), 33, "O 格式长度应为 33，实际 {text}");
    assert_eq!(&text[4..5], "-");
    assert_eq!(&text[7..8], "-");
    assert_eq!(&text[10..11], "T");
    assert_eq!(&text[19..20], ".");
    assert_eq!(bytes[27], b'+', "时区偏移缺失：{text}");
    assert!(
        text[27..].contains(':'),
        "时区偏移应带冒号：{}",
        &text[27..]
    );
}

#[test]
fn args_templates_build_quoted_paths() {
    assert_eq!(
        args::resolve_template(None, Some("RetroArch"), None),
        args::RETROARCH_DEFAULT_TEMPLATE
    );
    assert_eq!(
        args::resolve_template(None, Some("DeSmuME"), None),
        "\"{rom}\""
    );
    assert_eq!(
        args::build(
            args::RETROARCH_DEFAULT_TEMPLATE,
            r"C:\roms\a.chd",
            Some(r"C:\cores\psx.dll"),
            None
        ),
        r#"-L "C:\cores\psx.dll" "C:\roms\a.chd""#
    );
}

#[test]
fn hasher_matches_sha256_for_small_file() {
    let dir = temp_dir("hash");
    let file = dir.join("a.txt");
    std::fs::write(&file, b"abc").expect("写文件");
    let hash = hasher::compute(file.to_string_lossy().as_ref());
    assert_eq!(
        hash,
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    );
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn game_crud_favorite_hash_and_delete() {
    let dir = temp_dir("games");
    let data = dir.join("data");
    let st = AppState::open_in(data, dir.join("exe")).expect("初始化数据目录");

    let mut game = games::create(&st, "测试游戏").expect("新建");
    assert_eq!(game.title, "测试游戏");
    assert!(games::get(&st, game.id).expect("读取").is_some());

    game.title = "改名后的游戏".into();
    game.platform = "SNES".into();
    game.favorite = true;
    game.rating = 4;
    game.release_year = Some(1995);
    game.tags = "经典,动作".into();
    let saved = games::save(&st, game.clone()).expect("保存");
    assert_eq!(saved.title, "改名后的游戏");

    let loaded = games::get(&st, game.id).expect("读取").expect("存在");
    assert!(loaded.favorite);
    assert_eq!(loaded.rating, 4);
    assert_eq!(loaded.release_year, Some(1995));

    games::set_hash(&st, game.id, "abc123").expect("写指纹");
    let loaded = games::get(&st, game.id).expect("读取").expect("存在");
    assert_eq!(loaded.rom_hash, "abc123");
    assert!(games::list_hashes(&st).expect("指纹列表").contains(&"abc123".to_string()));

    games::delete(&st, game.id).expect("删除");
    assert!(games::get(&st, game.id).expect("读取").is_none());
    assert!(games::list_all(&st).expect("列表").is_empty());

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn sharer_rows_report_missing_rom_and_export_warns_without_emulator() {
    let dir = temp_dir("share");
    let roms = dir.join("roms");
    std::fs::create_dir_all(&roms).expect("创建 ROM 目录");
    let existing = roms.join("ok.nes");
    std::fs::write(&existing, vec![1u8; 32]).expect("写 ROM");

    let data = dir.join("data");
    let st = AppState::open_in(data, dir.join("exe")).expect("初始化数据目录");

    let mut first = games::create(&st, "有 ROM 的游戏").expect("新建");
    first.rom_path = existing.to_string_lossy().to_string();
    first.file_size = 32;
    let first = games::save(&st, first).expect("保存");

    let mut second = games::create(&st, "缺 ROM 的游戏").expect("新建");
    second.rom_path = roms.join("missing.nes").to_string_lossy().to_string();
    let second = games::save(&st, second).expect("保存");

    let rows = crate::services::sharer::rows(&st, &[first.id, second.id]).expect("预览");
    assert_eq!(rows.len(), 2);
    assert!(rows[0].rom_exists);
    assert!(!rows[0].has_emulator, "未绑定模拟器");
    assert!(rows[0].note.contains("未配置模拟器"));
    assert!(!rows[1].rom_exists);
    assert!(rows[1].note.contains("ROM 缺失"));

    // 未绑定模拟器的记录不参与打包估算
    let no_emulator = crate::services::sharer::estimate(&st, &[first.id], false, None).expect("估算");
    assert_eq!(no_emulator.items, 0);

    let mut emulator = crate::models::Emulator {
        id: 0,
        name: "FakeEmu".into(),
        exe_path: "FakeEmu.exe".into(),
        args_template: "{rom}".into(),
        core_path: String::new(),
        supported_ext: String::new(),
        version: String::new(),
        install_dir: dir.join("emu").to_string_lossy().to_string(),
        config_path: String::new(),
        github_repo: String::new(),
        website_url: String::new(),
        notes: String::new(),
        enabled: true,
        platforms: vec![],
        default_platforms: vec![],
        available: false,
    };
    emulator = crate::db::emulators::save(&st, emulator).expect("保存模拟器");

    let mut bound = games::get(&st, first.id).expect("读取").expect("存在");
    bound.emulator_id = Some(emulator.id);
    let bound = games::save(&st, bound).expect("绑定模拟器");

    let estimate = crate::services::sharer::estimate(&st, &[bound.id], false, None).expect("估算");
    assert_eq!(estimate.items, 1, "绑定模拟器后应计入");
    assert_eq!(estimate.bytes, 32);

    let export = dir.join("export");
    let pack = crate::services::sharer::export(
        &st,
        &[second.id],
        &export.to_string_lossy(),
        "测试分享包",
        false,
        false,
        None,
        false,
    )
    .expect("打包");
    assert!(std::path::Path::new(&pack.package_dir).is_dir(), "应创建分享包目录");
    assert!(
        pack.warnings.iter().any(|w| w.contains("未配置模拟器")),
        "缺少模拟器时应给出提示：{:?}",
        pack.warnings
    );

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn installer_from_folder_collects_exe_candidates() {
    let dir = temp_dir("install");
    let source = dir.join("source");
    let nested = source.join("bin");
    std::fs::create_dir_all(&nested).expect("创建源目录");
    std::fs::write(nested.join("FakeEmu.exe"), b"stub").expect("写主程序");
    std::fs::write(source.join("readme.txt"), b"doc").expect("写说明");

    let data = dir.join("data");
    let st = AppState::open_in(data, dir.join("exe")).expect("初始化数据目录");

    let result =
        crate::services::installer::install_from_folder(&st, &source.to_string_lossy(), "FakeEmu")
            .expect("从文件夹安装");

    assert!(
        std::path::Path::new(&result.install_dir).is_dir(),
        "安装目录应存在"
    );
    assert!(
        result
            .exe_candidates
            .iter()
            .any(|p| p.ends_with("FakeEmu.exe")),
        "应收集到 exe 候选：{:?}",
        result.exe_candidates
    );
    assert!(
        result.exe_candidates.len() >= 1,
        "候选至少一个"
    );

    drop(st);
    let _ = std::fs::remove_dir_all(dir);
}
