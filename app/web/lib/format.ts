import type { Game } from "./api";

export function formatSize(bytes: number): string {
  if (!bytes || bytes <= 0) return "";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let size = bytes;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  if (unit === 0) return `${bytes} B`;
  const text = (Math.round(size * 10) / 10).toFixed(1).replace(/\.0$/, "");
  return `${text} ${units[unit]}`;
}

export function playSecondsDisplay(seconds: number): string {
  if (!seconds || seconds <= 0) return "";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
}

export function lastPlayedDisplay(value: string | null): string {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours()
  )}:${pad(date.getMinutes())}`;
}

export function discCount(game: Game): number {
  return discs(game).length;
}

export function discs(game: Game): string[] {
  const list: string[] = [];
  if (game.romPath && game.romPath.trim()) list.push(game.romPath.trim());
  for (const raw of (game.discPaths || "").split("\n")) {
    const path = raw.trim();
    if (!path) continue;
    if (!list.some((p) => p.toLowerCase() === path.toLowerCase())) list.push(path);
  }
  return list;
}

export function formatExt(game: Game): string {
  const path = (game.romPath || "").trim();
  if (!path) return "";
  const name = path.split(/[\\/]/).pop() || "";
  const dot = name.lastIndexOf(".");
  if (dot < 0) return "";
  return name.slice(dot + 1).toUpperCase();
}

export function fileName(path: string): string {
  return (path || "").split(/[\\/]/).pop() || "";
}

export function favoriteDisplay(game: Game): string {
  return game.favorite ? "★" : "";
}

export function ratingStars(rating: number): string {
  const n = Math.max(0, Math.min(5, rating || 0));
  return "★".repeat(n) + "☆".repeat(5 - n);
}

export function splitTags(raw: string): string[] {
  return (raw || "")
    .split(/[,，;；]/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}
