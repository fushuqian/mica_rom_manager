import { invoke } from "@tauri-apps/api/core";

export type Game = {
  id: number;
  title: string;
  platform: string;
  genre: string;
  region: string;
  language: string;
  releaseYear: number | null;
  publisher: string;
  developer: string;
  favorite: boolean;
  rating: number;
  status: string;
  romPath: string;
  fileSize: number;
  coverPath: string;
  notes: string;
  tags: string;
  emulatorId: number | null;
  playCount: number;
  playSeconds: number;
  lastPlayedAt: string | null;
  createdAt: string;
  updatedAt: string;
  discPaths: string;
  romHash: string;
  missing: boolean;
};

export type Emulator = {
  id: number;
  name: string;
  exePath: string;
  argsTemplate: string;
  corePath: string;
  supportedExt: string;
  version: string;
  installDir: string;
  configPath: string;
  githubRepo: string;
  websiteUrl: string;
  notes: string;
  enabled: boolean;
  platforms: string[];
  defaultPlatforms: string[];
  available: boolean;
};

export type PlatformSetting = {
  name: string;
  emulatorId: number | null;
  folder: string;
  formats: string;
  corePath: string;
  argsTemplate: string;
  isCustom: boolean;
};

export type AppInfo = {
  version: string;
  dataDir: string;
  exeDir: string;
  dbPath: string;
  libraryRoot: string;
  portable: boolean;
  coversDir: string;
  emulatorsDir: string;
  exportsDir: string;
  downloadsDir: string;
};

export type LaunchOutcome = {
  launched: boolean;
  message: string;
  emulatorName: string;
  path: string;
  availableEmulators: Emulator[];
};

export type LookupActionResult = {
  success: boolean;
  message: string;
  affected: number;
};

export type ImportCandidate = {
  filePath: string;
  fileName: string;
  fileSize: number;
  title: string;
  platform: string;
  romHash: string;
  needsSpecify: boolean;
};

export type ScanResult = {
  candidates: ImportCandidate[];
  duplicateCount: number;
  excludedCount: number;
  formatSkippedCount: number;
  cancelled?: boolean;
};

export type ImportItem = {
  filePath: string;
  title: string;
  platform: string;
  romHash: string;
  fileSize: number;
};

export type ImportOutcome = {
  imported: number;
  failed: number;
  messages: string[];
  cancelled?: boolean;
};

export type PlatformScanOutcome = ScanResult & { removed: number; dir: string };

export type BatchChanges = {
  title?: string;
  platform?: string;
  genre?: string;
  region?: string;
  language?: string;
  releaseYear?: number | null;
  clearReleaseYear?: boolean;
  publisher?: string;
  developer?: string;
  status?: string;
  emulatorId?: number | null;
  clearEmulator?: boolean;
  tagsAdd?: string[];
  tagsRemove?: string[];
  notesMode?: string;
  notes?: string;
};

export type InstallOutcome = { installDir: string; exeCandidates: string[] };

export type ReleaseAsset = { name: string; downloadUrl: string; size: number };

export type ReleaseInfo = {
  tag: string;
  name: string;
  publishedAt: string;
  notes: string;
  assets: ReleaseAsset[];
};

export type ShareRow = {
  title: string;
  platform: string;
  emulatorName: string;
  note: string;
  hasEmulator: boolean;
  romExists: boolean;
};

export type ShareEstimate = { bytes: number; items: number };

export type SharePackResult = {
  packageDir: string;
  zipPath: string | null;
  totalBytes: number;
  warnings: string[];
  launchers: string[];
};

export type MaintenanceStats = {
  total: number;
  missing: number;
  hashMissing: number;
};

export type OrganizeOutcome = {
  renamed: number;
  skipped: number;
  failed: number;
  failedFiles: string[];
  dir: string;
};

export const api = {
  appInfo: () => invoke<AppInfo>("app_info"),
  settingsGetAll: () => invoke<Record<string, string>>("settings_get_all"),
  settingsSet: (key: string, value: string) => invoke<void>("settings_set", { key, value }),
  settingsSaveGeneral: (general: {
    libraryRoot: string;
    portable: boolean;
    importMode: string;
    proxyMode: string;
    proxyAddress: string;
  }) => invoke<AppInfo>("settings_save_general", { general }),

  gamesList: () => invoke<Game[]>("games_list"),
  gamesSave: (game: Game) => invoke<Game>("games_save", { game }),
  gamesCreate: (title: string) => invoke<Game>("games_create", { title }),
  gamesDelete: (id: number) => invoke<void>("games_delete", { id }),
  gamesDeleteMany: (ids: number[]) => invoke<void>("games_delete_many", { ids }),
  gamesDeleteAll: () => invoke<void>("games_delete_all"),
  gamesSetHash: (id: number, hash: string) => invoke<void>("games_set_hash", { id, hash }),
  gamesAddPlayStats: (id: number, seconds: number) =>
    invoke<void>("games_add_play_stats", { id, seconds }),
  gamesBatchUpdate: (ids: number[], changes: BatchChanges) =>
    invoke<number>("games_batch_update", { ids, changes }),
  gamesMergeDiscs: (ids: number[]) => invoke<Game>("games_merge_discs", { ids }),
  gamesSplitDiscs: (id: number) => invoke<Game[]>("games_split_discs", { id }),

  platformsList: () => invoke<PlatformSetting[]>("platforms_list"),
  platformGet: (name: string) => invoke<PlatformSetting | null>("platform_get", { name }),
  platformSave: (setting: PlatformSetting) => invoke<void>("platform_save", { setting }),
  platformDelete: (name: string) => invoke<void>("platform_delete", { name }),

  lookupsKinds: () => invoke<{ key: string; title: string }[]>("lookups_kinds"),
  lookupsList: (kind: string) => invoke<string[]>("lookups_list", { kind }),
  lookupsAdd: (kind: string, value: string) =>
    invoke<LookupActionResult>("lookups_add", { kind, value }),
  lookupsRename: (kind: string, oldValue: string, newValue: string) =>
    invoke<LookupActionResult>("lookups_rename", { kind, oldValue, newValue }),
  lookupsDelete: (kind: string, value: string) =>
    invoke<LookupActionResult>("lookups_delete", { kind, value }),
  lookupsCountUsage: (kind: string, value: string) =>
    invoke<number>("lookups_count_usage", { kind, value }),

  emulatorsList: () => invoke<Emulator[]>("emulators_list"),
  emulatorSave: (emulator: Emulator) => invoke<Emulator>("emulator_save", { emulator }),
  emulatorDelete: (id: number) => invoke<void>("emulator_delete", { id }),
  emulatorSetDefault: (id: number) => invoke<void>("emulator_set_default", { id }),
  emulatorsNormalizePaths: () => invoke<number>("emulators_normalize_paths"),
  emulatorInstallFromFolder: (source: string, name: string) =>
    invoke<InstallOutcome>("emulator_install_from_folder", { source, name }),
  emulatorInstallFromArchive: (archive: string, name: string) =>
    invoke<InstallOutcome>("emulator_install_from_archive", { archive, name }),
  emulatorApplyInstall: (id: number, installDir: string, exe: string) =>
    invoke<Emulator>("emulator_apply_install", { id, installDir, exe }),
  emulatorCheckUpdate: (repo: string) => invoke<ReleaseInfo>("emulator_check_update", { repo }),
  emulatorDownloadAsset: (url: string, fileName: string) =>
    invoke<string>("emulator_download_asset", { url, fileName }),
  emulatorTestLaunch: (id: number) => invoke<string>("emulator_test_launch", { id }),

  fileExists: (path: string) => invoke<boolean>("file_exists", { path }),
  detectPlatform: (path: string) => invoke<string | null>("detect_platform", { path }),
  networkTestGithub: () => invoke<string>("network_test_github"),
  coverDataUrl: (path: string) => invoke<string>("cover_data_url", { path }),
  fileSize: (path: string) => invoke<number>("file_size", { path }),
  appVersion: () => invoke<string>("app_version"),

  launchGame: (gameId: number, emulatorId: number | null, discIndex: number | null) =>
    invoke<LaunchOutcome>("launch_game", { gameId, emulatorId, discIndex }),
  setCover: (gameId: number, sourcePath: string) => invoke<Game>("set_cover", { gameId, sourcePath }),
  removeCover: (gameId: number) => invoke<Game>("remove_cover", { gameId }),
  recycleFiles: (paths: string[]) => invoke<void>("recycle_files", { paths }),
  setGameRating: (gameId: number, rating: number) =>
    invoke<Game>("set_game_rating", { gameId, rating }),
  toggleFavorite: (gameId: number) => invoke<Game>("toggle_favorite", { gameId }),

  importScan: (
    inputs: string[],
    recursive: boolean,
    allowedExtensions: string[] | null,
    platformOverride: string | null
  ) => invoke<ScanResult>("import_scan", { inputs, recursive, allowedExtensions, platformOverride }),
  importApply: (items: ImportItem[], mode: string) =>
    invoke<ImportOutcome>("import_apply", { items, mode }),
  importSupportedExtensions: () => invoke<string[]>("import_supported_extensions"),
  cancelTask: () => invoke<void>("cancel_task"),
  platformScan: (name: string, formats: string | null) =>
    invoke<PlatformScanOutcome>("platform_scan", { name, formats }),
  platformOrganize: (name: string) => invoke<OrganizeOutcome>("platform_organize", { name }),

  maintenanceStats: () => invoke<MaintenanceStats>("maintenance_stats"),
  maintenanceHashMissing: () =>
    invoke<{ computed: number; skipped: number; cancelled: boolean }>("maintenance_hash_missing"),
  maintenanceCleanMissing: () => invoke<number>("maintenance_clean_missing"),
  maintenanceExportCsv: (path: string) => invoke<number>("maintenance_export_csv", { path }),
  maintenanceBackupDb: (path: string) => invoke<void>("maintenance_backup_db", { path }),
  maintenanceRestoreDb: (path: string) => invoke<void>("maintenance_restore_db", { path }),
  maintenanceSyncTags: () => invoke<void>("maintenance_sync_tags"),

  shareRows: (gameIds: number[]) => invoke<ShareRow[]>("share_rows", { gameIds }),
  shareEstimate: (gameIds: number[], includeEmulators: boolean, saveDir: string | null) =>
    invoke<ShareEstimate>("share_estimate", { gameIds, includeEmulators, saveDir }),
  shareExport: (payload: {
    gameIds: number[];
    exportDir: string;
    packageName: string;
    includeEmulators: boolean;
    includeSaves: boolean;
    saveDir: string | null;
    createZip: boolean;
  }) => invoke<SharePackResult>("share_export", payload),
};
