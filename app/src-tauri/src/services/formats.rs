use crate::services::extension_map::{ExtensionInfo, ExtensionMap};
use crate::services::UNSPECIFIED_PLATFORM;
use std::path::Path;

/// 平台默认格式（与 WPF PlatformFormats.Defaults 完全一致）。
pub fn default_formats(platform: &str) -> &'static [&'static str] {
    match platform {
        "NES" => &["nes", "unf"],
        "SNES" => &["sfc", "smc"],
        "N64" => &["n64", "z64", "v64"],
        "Game Boy" => &["gb"],
        "GBC" => &["gbc"],
        "GBA" => &["gba"],
        "NDS" => &["nds"],
        "3DS" => &["3ds", "cia"],
        "Switch" => &["nsp", "xci"],
        "GameCube" => &["gcm", "iso", "rvz"],
        "Wii" => &["iso", "wbfs", "rvz"],
        "Mega Drive" => &["md", "gen", "smd", "bin"],
        "Master System" => &["sms"],
        "Game Gear" => &["gg"],
        "Saturn" => &["iso", "cue", "bin", "chd"],
        "Dreamcast" => &["iso", "cue", "bin", "chd", "gdi"],
        "PlayStation" => &["iso", "cue", "bin", "chd", "pbp"],
        "PS2" => &["iso", "bin", "chd"],
        "PS3" => &["iso", "pkg"],
        "PSP" => &["iso", "cso", "pbp", "chd"],
        "PC Engine" => &["pce"],
        "Virtual Boy" => &["vb"],
        _ => &[],
    }
}

pub fn default_formats_text(platform: &str) -> String {
    default_formats(platform).join(" ")
}

/// 解析为带点的小写扩展名列表；支持空格、逗号、顿号、分号分隔。
pub fn parse(formats: &str) -> Vec<String> {
    if formats.trim().is_empty() {
        return Vec::new();
    }
    let mut result: Vec<String> = Vec::new();
    for token in formats.split(|c: char| {
        c == ' ' || c == '\t' || c == ',' || c == '，' || c == '、' || c == ';' || c == '；'
    }) {
        let cleaned = token.trim().trim_start_matches('.').to_lowercase();
        if cleaned.is_empty() {
            continue;
        }
        let dotted = format!(".{}", cleaned);
        if !result.contains(&dotted) {
            result.push(dotted);
        }
    }
    result
}

/// 统一存为空格分隔、小写、无点、去重。
pub fn normalize(formats: &str) -> String {
    parse(formats)
        .iter()
        .map(|e| e.trim_start_matches('.').to_string())
        .collect::<Vec<_>>()
        .join(" ")
}

/// 判断扩展名是否在格式列表中（供测试与后续校验使用）。
#[allow(dead_code)]
pub fn supports(formats: &str, extension: &str) -> bool {
    if extension.trim().is_empty() {
        return false;
    }
    let normalized = if extension.starts_with('.') {
        extension.to_lowercase()
    } else {
        format!(".{}", extension.to_lowercase())
    };
    parse(formats).contains(&normalized)
}

/// 导入扫描时确定平台（规则与 WPF PlatformFormats.Resolve 一致）。
pub fn resolve(
    file_path: &str,
    builtin: &ExtensionInfo,
    platform_formats: Option<&std::collections::HashMap<String, Vec<String>>>,
) -> (String, bool) {
    let Some(map) = platform_formats else {
        return (
            builtin
                .platform
                .clone()
                .unwrap_or_else(|| UNSPECIFIED_PLATFORM.to_string()),
            builtin.needs_specify,
        );
    };
    if map.is_empty() {
        return (
            builtin
                .platform
                .clone()
                .unwrap_or_else(|| UNSPECIFIED_PLATFORM.to_string()),
            builtin.needs_specify,
        );
    }

    // 压缩包内容未知，不参与自动识别
    if ExtensionMap::is_archive(file_path) {
        return (UNSPECIFIED_PLATFORM.to_string(), true);
    }

    let extension = Path::new(file_path)
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
        .unwrap_or_default();

    let matches: Vec<&String> = map
        .iter()
        .filter(|(_, formats)| formats.iter().any(|f| f.eq_ignore_ascii_case(&extension)))
        .map(|(name, _)| name)
        .collect();

    if matches.len() == 1 {
        return (matches[0].clone(), false);
    }

    if matches.len() > 1 {
        if let Some(builtin_platform) = &builtin.platform {
            if matches
                .iter()
                .any(|p| p.eq_ignore_ascii_case(builtin_platform))
            {
                return (builtin_platform.clone(), false);
            }
        }
        return (UNSPECIFIED_PLATFORM.to_string(), true);
    }

    (
        builtin
            .platform
            .clone()
            .unwrap_or_else(|| UNSPECIFIED_PLATFORM.to_string()),
        builtin.needs_specify,
    )
}
