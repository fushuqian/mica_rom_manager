use sha2::{Digest, Sha256};
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};

const FULL_HASH_LIMIT: u64 = 128 * 1024 * 1024;
const SAMPLE_SIZE: usize = 1024 * 1024;

/// ROM 指纹：≤128MB 全量 SHA256；更大文件用「大小 + 首/中/尾采样」，带 fast: 前缀。
pub fn compute(path: &str) -> String {
    if path.trim().is_empty() {
        return String::new();
    }
    let meta = match std::fs::metadata(path) {
        Ok(m) => m,
        Err(_) => return String::new(),
    };
    if !meta.is_file() {
        return String::new();
    }

    if meta.len() <= FULL_HASH_LIMIT {
        return hash_full(path).unwrap_or_default();
    }
    match hash_sampled(path, meta.len()) {
        Ok(hash) => format!("fast:{}", hash),
        Err(_) => String::new(),
    }
}

fn hash_full(path: &str) -> std::io::Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; SAMPLE_SIZE];
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hex::encode(hasher.finalize()))
}

fn hash_sampled(path: &str, length: u64) -> std::io::Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    hasher.update(length.to_le_bytes());

    let half = SAMPLE_SIZE as u64 / 2;
    let mid_offset = (length / 2).saturating_sub(half);
    let tail_offset = length.saturating_sub(SAMPLE_SIZE as u64);

    let mut buffer = vec![0u8; SAMPLE_SIZE];
    for offset in [0u64, mid_offset, tail_offset] {
        file.seek(SeekFrom::Start(offset))?;
        let read = file.read(&mut buffer)?;
        if read > 0 {
            hasher.update(&buffer[..read]);
        }
    }

    Ok(hex::encode(hasher.finalize()))
}
