use std::path::Path;

pub const RETROARCH_DEFAULT_TEMPLATE: &str = "-L \"{core}\" \"{rom}\"";

pub fn requires_core(template: &str) -> bool {
    !template.trim().is_empty() && template.to_lowercase().contains("{core}")
}

/// 平台未配置启动参数时的回退规则（与 WPF ArgsBuilder.ResolveTemplate 一致）。
pub fn resolve_template(
    platform_template: Option<&str>,
    emulator_name: Option<&str>,
    emulator_template: Option<&str>,
) -> String {
    if let Some(t) = platform_template {
        if !t.trim().is_empty() {
            return t.trim().to_string();
        }
    }

    let emulator_args = emulator_template.unwrap_or("").trim().to_string();
    if requires_core(&emulator_args) {
        return emulator_args;
    }

    if emulator_name
        .unwrap_or("")
        .to_lowercase()
        .contains("retroarch")
    {
        return RETROARCH_DEFAULT_TEMPLATE.to_string();
    }

    if emulator_args.is_empty() {
        "\"{rom}\"".to_string()
    } else {
        emulator_args
    }
}

/// 占位符统一替换为带引号的路径（与 WPF 行为一致）。
pub fn build(
    template: &str,
    rom_path: &str,
    core_path: Option<&str>,
    config_path: Option<&str>,
) -> String {
    let rom_dir = Path::new(rom_path)
        .parent()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    let rom_name = Path::new(rom_path)
        .file_name()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    build_with_paths(
        template,
        rom_path,
        &rom_dir,
        &rom_name,
        core_path.unwrap_or(""),
        config_path.unwrap_or(""),
    )
}

pub fn build_with_paths(
    template: &str,
    rom: &str,
    rom_dir: &str,
    rom_name: &str,
    core: &str,
    config: &str,
) -> String {
    let map = [
        ("{rom}", rom),
        ("{romdir}", rom_dir),
        ("{romname}", rom_name),
        ("{core}", core),
        ("{config}", config),
    ];

    let mut result = template.to_string();
    for (token, value) in map {
        let quoted = format!("\"{}\"", value);
        result = result.replace(&format!("\"{}\"", token), &quoted);
        result = replace_ci(&result, token, &quoted);
    }
    result.trim().to_string()
}

/// 大小写不敏感的字符串替换。
fn replace_ci(haystack: &str, needle: &str, replacement: &str) -> String {
    if needle.is_empty() {
        return haystack.to_string();
    }
    let lower_hay = haystack.to_lowercase();
    let lower_needle = needle.to_lowercase();
    let mut result = String::with_capacity(haystack.len());
    let mut start = 0usize;
    while let Some(pos) = lower_hay[start..].find(&lower_needle) {
        let abs = start + pos;
        result.push_str(&haystack[start..abs]);
        result.push_str(replacement);
        start = abs + needle.len();
    }
    result.push_str(&haystack[start..]);
    result
}
