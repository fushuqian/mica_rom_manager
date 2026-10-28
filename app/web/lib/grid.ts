import type { Game } from "./api";
import {
  discCount,
  favoriteDisplay,
  formatExt,
  formatSize,
  lastPlayedDisplay,
  playSecondsDisplay,
  ratingStars,
} from "./format";

export type EditorKind = "text" | "year" | "platform" | "region" | "language" | "status" | "rating" | "none";

export type GridColumn = {
  header: string;
  width: number;
  sortKey: keyof Game | null;
  sortValue?: (game: Game) => string | number;
  editable: EditorKind;
  align?: "left" | "center" | "right";
  tip?: (game: Game) => string;
};

export const COLUMNS: GridColumn[] = [
  { header: "★", width: 36, sortKey: null, editable: "none", align: "center" },
  { header: "平台", width: 110, sortKey: "platform", editable: "platform" },
  { header: "标题", width: 220, sortKey: "title", editable: "text" },
  { header: "评分", width: 104, sortKey: "rating", editable: "rating", align: "center" },
  { header: "区域", width: 64, sortKey: "region", editable: "region" },
  { header: "语言", width: 86, sortKey: "language", editable: "language" },
  { header: "类型", width: 90, sortKey: "genre", editable: "text" },
  { header: "年份", width: 64, sortKey: "releaseYear", editable: "year" },
  { header: "光碟数", width: 60, sortKey: null, sortValue: (game) => discCount(game), editable: "none", align: "center" },
  { header: "格式", width: 60, sortKey: null, sortValue: (game) => formatExt(game), editable: "none" },
  { header: "状态", width: 76, sortKey: "status", editable: "status" },
  { header: "次数", width: 56, sortKey: "playCount", editable: "none", align: "center" },
  { header: "时长", width: 72, sortKey: "playSeconds", editable: "none" },
  { header: "最近游玩", width: 124, sortKey: "lastPlayedAt", editable: "none" },
  { header: "大小", width: 82, sortKey: "fileSize", editable: "none", align: "right" },
  { header: "标签", width: 120, sortKey: "tags", editable: "text" },
  { header: "指纹", width: 340, sortKey: "romHash", editable: "none" },
];

export function cellText(game: Game, header: string): string {
  switch (header) {
    case "★":
      return favoriteDisplay(game);
    case "平台":
      return game.platform;
    case "标题":
      return game.title;
    case "评分":
      return "";
    case "区域":
      return game.region;
    case "语言":
      return game.language;
    case "类型":
      return game.genre;
    case "年份":
      return game.releaseYear ? String(game.releaseYear) : "";
    case "光碟数":
      return String(discCount(game));
    case "格式":
      return formatExt(game);
    case "状态":
      return game.status;
    case "次数":
      return game.playCount ? String(game.playCount) : "";
    case "时长":
      return playSecondsDisplay(game.playSeconds);
    case "最近游玩":
      return lastPlayedDisplay(game.lastPlayedAt);
    case "大小":
      return formatSize(game.fileSize);
    case "标签":
      return game.tags;
    case "指纹":
      return game.romHash;
    default:
      return "";
  }
}

export function ratingText(game: Game): string {
  return ratingStars(game.rating);
}

export function canSort(column: GridColumn): boolean {
  return column.sortKey !== null || !!column.sortValue;
}

export function compareGames(a: Game, b: Game, column: GridColumn, dir: "asc" | "desc"): number {
  if (!canSort(column)) return 0;
  const av = column.sortValue ? column.sortValue(a) : a[column.sortKey!];
  const bv = column.sortValue ? column.sortValue(b) : b[column.sortKey!];
  const aHas = av !== null && av !== undefined && av !== "";
  const bHas = bv !== null && bv !== undefined && bv !== "";
  if (!aHas && !bHas) return 0;
  if (!aHas) return 1;
  if (!bHas) return -1;

  let result: number;
  if (typeof av === "number" || typeof bv === "number") {
    result = Number(av) - Number(bv);
  } else {
    result = String(av).localeCompare(String(bv), "zh-Hans-CN", { numeric: true, sensitivity: "base" });
  }
  if (result !== 0 && dir === "desc") result = -result;
  return result;
}
