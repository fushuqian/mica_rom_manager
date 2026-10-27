use std::path::Path;

#[derive(Debug, Clone)]
pub struct ExtensionInfo {
    pub platform: Option<String>,
    pub needs_specify: bool,
}

impl ExtensionInfo {
    fn new(platform: Option<&str>, needs_specify: bool) -> Self {
        Self {
            platform: platform.map(|s| s.to_string()),
            needs_specify,
        }
    }
}

pub struct ExtensionMap;

impl ExtensionMap {
    pub fn detect(file_path: &str) -> ExtensionInfo {
        let ext = Path::new(file_path)
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
            .unwrap_or_default();
        match ext.as_str() {
            ".nes" | ".unf" => ExtensionInfo::new(Some("NES"), false),
            ".sfc" | ".smc" => ExtensionInfo::new(Some("SNES"), false),
            ".gb" => ExtensionInfo::new(Some("Game Boy"), false),
            ".gbc" => ExtensionInfo::new(Some("GBC"), false),
            ".gba" => ExtensionInfo::new(Some("GBA"), false),
            ".nds" => ExtensionInfo::new(Some("NDS"), false),
            ".n64" | ".z64" | ".v64" => ExtensionInfo::new(Some("N64"), false),
            ".md" | ".gen" | ".smd" => ExtensionInfo::new(Some("Mega Drive"), false),
            ".sms" => ExtensionInfo::new(Some("Master System"), false),
            ".gg" => ExtensionInfo::new(Some("Game Gear"), false),
            ".pce" => ExtensionInfo::new(Some("PC Engine"), false),
            ".vb" => ExtensionInfo::new(Some("Virtual Boy"), false),
            ".gcm" => ExtensionInfo::new(Some("GameCube"), false),
            ".3ds" | ".cia" => ExtensionInfo::new(Some("3DS"), false),
            ".nsp" | ".xci" => ExtensionInfo::new(Some("Switch"), false),
            ".pbp" | ".cso" => ExtensionInfo::new(Some("PSP"), false),
            _ => ExtensionInfo::new(None, true),
        }
    }

    pub fn is_known(file_path: &str) -> bool {
        let ext = Path::new(file_path)
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
            .unwrap_or_default();
        matches!(
            ext.as_str(),
            ".nes"
                | ".unf"
                | ".sfc"
                | ".smc"
                | ".gb"
                | ".gbc"
                | ".gba"
                | ".nds"
                | ".n64"
                | ".z64"
                | ".v64"
                | ".md"
                | ".gen"
                | ".smd"
                | ".sms"
                | ".gg"
                | ".pce"
                | ".vb"
                | ".gcm"
                | ".3ds"
                | ".cia"
                | ".nsp"
                | ".xci"
                | ".pbp"
                | ".cso"
                | ".iso"
                | ".chd"
                | ".cue"
                | ".bin"
                | ".wbfs"
                | ".rvz"
                | ".zip"
                | ".7z"
                | ".rar"
        )
    }

    pub fn is_archive(file_path: &str) -> bool {
        let ext = Path::new(file_path)
            .extension()
            .map(|e| e.to_string_lossy().to_lowercase())
            .unwrap_or_default();
        matches!(ext.as_str(), "zip" | "7z" | "rar")
    }

    pub fn is_known_or_archive(file_path: &str) -> bool {
        Self::is_known(file_path) || Self::is_archive(file_path)
    }

    pub fn extensions() -> Vec<&'static str> {
        vec![
            ".nes", ".unf", ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".n64", ".z64", ".v64",
            ".md", ".gen", ".smd", ".sms", ".gg", ".pce", ".vb", ".gcm", ".3ds", ".cia", ".nsp",
            ".xci", ".pbp", ".cso", ".iso", ".chd", ".cue", ".bin", ".wbfs", ".rvz", ".zip", ".7z",
            ".rar",
        ]
    }
}
