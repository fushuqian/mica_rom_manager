import {
  api,
  type AppInfo,
  type Emulator,
  type Game,
  type PlatformSetting,
} from "./api";

export const ALL_PLATFORMS = "全部平台";
export const ALL_STATUSES = "全部状态";
export const ALL_TAGS = "全部标签";
export const ALL_REGIONS = "全部区域";
export const ALL_LANGUAGES = "全部语言";
export const ALL_DEVELOPERS = "全部开发商";
export const STATUSES = ["未玩", "在玩", "已通关", "已放弃"];

export type ListViewState = {
  Search: string;
  Platform: string;
  Status: string;
  Tag: string;
  Region: string;
  Language: string;
  Developer: string;
  OnlyFavorites: boolean;
  OnlyMissing: boolean;
  SortColumn: string;
  SortDirection: string;
  ColumnWidths: Record<string, number>;
};

class AppStore {
  info = $state<AppInfo | null>(null);
  games = $state<Game[]>([]);
  emulators = $state<Emulator[]>([]);
  platformSettings = $state<PlatformSetting[]>([]);
  regions = $state<string[]>([]);
  languages = $state<string[]>([]);
  developers = $state<string[]>([]);
  genres = $state<string[]>([]);
  publishers = $state<string[]>([]);
  tags = $state<string[]>([]);
  platformNames = $state<string[]>([]);

  search = $state("");
  platform = $state(ALL_PLATFORMS);
  status = $state(ALL_STATUSES);
  region = $state(ALL_REGIONS);
  language = $state(ALL_LANGUAGES);
  developer = $state(ALL_DEVELOPERS);
  tag = $state(ALL_TAGS);
  onlyFavorites = $state(false);
  onlyMissing = $state(false);

  sortColumn = $state("");
  sortDirection = $state<"" | "asc" | "desc">("");
  columnWidths = $state<Record<string, number>>({});
  editMode = $state(false);

  selectedIds = $state<number[]>([]);
  anchorId = $state<number | null>(null);

  statusMessage = $state("");
  busy = $state(false);
  lastRefresh = $state("");
  ready = $state(false);
  viewStateLoaded = $state(false);

  get selectedGames(): Game[] {
    const ids = new Set(this.selectedIds);
    return this.games.filter((g) => ids.has(g.id));
  }

  get current(): Game | null {
    const id = this.selectedIds[this.selectedIds.length - 1];
    const found = this.games.find((g) => g.id === id);
    return found ?? null;
  }

  get platformFilters(): { name: string; count: number }[] {
    const counts = new Map<string, number>();
    const catalog = this.platformNames.length
      ? this.platformNames
      : Array.from(new Set(this.games.map((g) => g.platform).filter(Boolean)));
    for (const name of catalog) counts.set(name, 0);
    for (const game of this.games) {
      if (!game.platform) continue;
      const key = catalog.find((c) => c.toLowerCase() === game.platform.toLowerCase());
      if (!key) continue;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return Array.from(counts.entries())
      .filter(([, count]) => count > 0)
      .map(([name, count]) => ({ name, count }));
  }

  async loadAll(): Promise<void> {
    this.info = await api.appInfo();
    await Promise.all([
      api.gamesList().then((list) => (this.games = list)),
      api.emulatorsList().then((list) => (this.emulators = list)),
      api.platformsList().then((list) => (this.platformSettings = list)),
      api.lookupsList("region").then((v) => (this.regions = v)),
      api.lookupsList("language").then((v) => (this.languages = v)),
      api.lookupsList("developer").then((v) => (this.developers = v)),
      api.lookupsList("genre").then((v) => (this.genres = v)),
      api.lookupsList("publisher").then((v) => (this.publishers = v)),
      api.lookupsList("tag").then((v) => (this.tags = v)),
      api.lookupsList("platform").then((v) => (this.platformNames = v)),
    ]);
    this.lastRefresh = `上次刷新：${new Date().toTimeString().slice(0, 8)}`;
    this.ready = true;
  }

  async reloadGames(): Promise<void> {
    this.games = await api.gamesList();
    this.lastRefresh = `上次刷新：${new Date().toTimeString().slice(0, 8)}`;
  }

  async reloadLookups(): Promise<void> {
    [this.regions, this.languages, this.developers, this.genres, this.publishers, this.tags, this.platformNames] =
      await Promise.all([
        api.lookupsList("region"),
        api.lookupsList("language"),
        api.lookupsList("developer"),
        api.lookupsList("genre"),
        api.lookupsList("publisher"),
        api.lookupsList("tag"),
        api.lookupsList("platform"),
      ]);
  }

  replaceGame(game: Game): void {
    const index = this.games.findIndex((g) => g.id === game.id);
    if (index >= 0) this.games[index] = game;
  }

  removeGames(ids: number[]): void {
    const set = new Set(ids);
    this.games = this.games.filter((g) => !set.has(g.id));
    this.selectedIds = this.selectedIds.filter((id) => !set.has(id));
  }

  select(id: number, mode: "single" | "toggle" | "range"): void {
    const ids = this.filteredIds;
    if (mode === "single") {
      this.selectedIds = [id];
      this.anchorId = id;
      return;
    }
    if (mode === "toggle") {
      this.selectedIds = this.selectedIds.includes(id)
        ? this.selectedIds.filter((x) => x !== id)
        : [...this.selectedIds, id];
      this.anchorId = id;
      return;
    }
    const anchor = this.anchorId ?? id;
    const from = ids.indexOf(anchor);
    const to = ids.indexOf(id);
    if (from < 0 || to < 0) {
      this.selectedIds = [id];
      this.anchorId = id;
      return;
    }
    const [start, end] = from <= to ? [from, to] : [to, from];
    this.selectedIds = ids.slice(start, end + 1);
  }

  filteredIds: number[] = [];

  filterGames(): Game[] {
    const keyword = this.search.trim().toLowerCase();
    const platform = this.platform.toLowerCase();
    const status = this.status.toLowerCase();
    const region = this.region.toLowerCase();
    const language = this.language.toLowerCase();
    const developer = this.developer.toLowerCase();
    const tag = this.tag.toLowerCase();

    return this.games.filter((game) => {
      if (this.onlyFavorites && !game.favorite) return false;
      if (this.onlyMissing && !game.missing) return false;
      if (platform && this.platform !== ALL_PLATFORMS && game.platform.toLowerCase() !== platform)
        return false;
      if (this.status !== ALL_STATUSES && game.status.toLowerCase() !== status) return false;
      if (this.region !== ALL_REGIONS && game.region.toLowerCase() !== region) return false;
      if (this.language !== ALL_LANGUAGES && game.language.toLowerCase() !== language) return false;
      if (this.developer !== ALL_DEVELOPERS && game.developer.toLowerCase() !== developer)
        return false;
      if (
        this.tag &&
        this.tag !== ALL_TAGS &&
        !game.tags
          .split(/[,，;；]/)
          .map((s) => s.trim().toLowerCase())
          .includes(tag)
      )
        return false;

      if (!keyword) return true;
      const fields = [
        game.title,
        game.romPath,
        game.tags,
        game.platform,
        game.genre,
        game.developer,
      ];
      return fields.some((f) => (f || "").toLowerCase().includes(keyword));
    });
  }
}

export const store = new AppStore();

export async function saveViewState(): Promise<void> {
  const state: ListViewState = {
    Search: store.search,
    Platform: store.platform,
    Status: store.status,
    Tag: store.tag,
    Region: store.region,
    Language: store.language,
    Developer: store.developer,
    OnlyFavorites: store.onlyFavorites,
    OnlyMissing: store.onlyMissing,
    SortColumn: store.sortColumn,
    SortDirection: store.sortDirection === "asc" ? "Ascending" : store.sortDirection === "desc" ? "Descending" : "",
    ColumnWidths: store.columnWidths,
  };
  await api.settingsSet("list_view_state", JSON.stringify(state));
}

export async function restoreViewState(): Promise<void> {
  const all = await api.settingsGetAll();
  const raw = all["list_view_state"];
  if (!raw) return;
  let state: ListViewState;
  try {
    state = JSON.parse(raw);
  } catch {
    return;
  }
  if (state.Search) store.search = state.Search;
  if (state.Platform) store.platform = state.Platform;
  if (state.Status) store.status = state.Status;
  if (state.Tag) store.tag = state.Tag;
  if (state.Region) store.region = state.Region;
  if (state.Language) store.language = state.Language;
  if (state.Developer) store.developer = state.Developer;
  store.onlyFavorites = !!state.OnlyFavorites;
  store.onlyMissing = !!state.OnlyMissing;
  if (state.ColumnWidths) {
    const widths: Record<string, number> = {};
    for (const [key, value] of Object.entries(state.ColumnWidths)) {
      if (typeof value === "number" && value >= 24) widths[key] = Math.round(value);
    }
    store.columnWidths = widths;
  }
  if (state.SortColumn && state.SortDirection) {
    store.sortColumn = state.SortColumn;
    store.sortDirection = state.SortDirection === "Descending" ? "desc" : "asc";
  }
  store.viewStateLoaded = true;
}
