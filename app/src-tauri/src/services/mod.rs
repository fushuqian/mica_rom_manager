pub mod args;
pub mod cover;
pub mod extension_map;
pub mod formats;
pub mod hasher;
pub mod importer;
pub mod installer;
pub mod launcher;
pub mod maintenance;
pub mod organizer;
pub mod sharer;

pub const UNSPECIFIED_PLATFORM: &str = "未指定";
pub const UNSPECIFIED_FOLDER: &str = "未分类";

/// 内置平台清单（与 WPF PlatformCatalog.Common 完全一致）。
pub const PLATFORM_CATALOG: [&str; 24] = [
    "NES",
    "SNES",
    "N64",
    "Game Boy",
    "GBC",
    "GBA",
    "NDS",
    "3DS",
    "Switch",
    "GameCube",
    "Wii",
    "Mega Drive",
    "Master System",
    "Game Gear",
    "Saturn",
    "Dreamcast",
    "PlayStation",
    "PS2",
    "PS3",
    "PSP",
    "PC Engine",
    "Virtual Boy",
    "街机",
    "其他",
];
