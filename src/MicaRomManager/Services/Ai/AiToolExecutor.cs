using System.IO;
using System.Text.Json;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;
using MicaRomManager.Services.Recommendation;

namespace MicaRomManager.Services.Ai;

public sealed class AiToolExecution
{
    public string ToolCallId { get; init; } = "";
    public string ResultForModel { get; init; } = "{}";
    public AiPendingAction? Pending { get; init; }
}

/// <summary>执行模型请求的工具；所有写操作只生成待确认动作，不直接落库。</summary>
public sealed class AiToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] WindowNames =
    {
        "settings", "device_settings", "import", "share", "task_list",
        "connect_device", "scrape", "ai_tags", "ai_translate", "recommend",
    };

    public async Task<AiToolExecution> ExecuteAsync(AiToolCall call, IAiAppHost host, CancellationToken token)
    {
        try
        {
            return call.Name switch
            {
                "query_games" => QueryGames(call, host),
                "get_game" => GetGame(call, host),
                "get_statistics" => GetStatistics(call, host),
                "list_emulators" => ListEmulators(call),
                "recommend_games" => Recommend(call, host),
                "get_settings" => GetSettings(call, host),
                "list_drives" => ListDrives(call),
                "scan_device_tree" => await ScanDeviceTreeAsync(call, token),
                "update_games" => UpdateGames(call, host),
                "delete_games" => DeleteGames(call, host),
                "launch_game" => LaunchGame(call, host),
                "send_games" => SendGames(call, host),
                "set_settings" => SetSettings(call, host),
                "save_device_profile" => await SaveDeviceProfileAsync(call, token),
                "open_window" => OpenWindow(call, host),
                _ => Error(call, $"未知工具：{call.Name}"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error(call, $"工具执行失败：{ex.Message}");
        }
    }

    private static AiToolExecution QueryGames(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var filter = ParseFilter(Obj(root, "filter"));
        var apply = Bool(root, "apply_to_list") ?? false;
        var limit = Math.Clamp(Int(root, "limit") ?? 20, 1, 50);

        var matches = host.AllGames.Where(game => AiQueryMatcher.Matches(game, filter)).ToList();
        if (apply) host.ApplyLibraryFilter(filter);

        return Ok(call, new
        {
            ok = true,
            total = matches.Count,
            applied = apply,
            games = matches.Take(limit).Select(Compact).ToList(),
        });
    }

    private static AiToolExecution GetGame(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var id = Int(root, "game_id");
        var game = id is int gameId
            ? host.AllGames.FirstOrDefault(item => item.Id == gameId)
            : host.SelectedGame;

        if (game == null) return Error(call, "没有找到指定游戏，也没有当前选中游戏。");

        return Ok(call, new
        {
            ok = true,
            game = new
            {
                id = game.Id,
                title = game.Title,
                platform = game.Platform,
                genre = game.Genre,
                region = game.Region,
                language = game.Language,
                special_note = game.SpecialNote,
                release_year = game.ReleaseYear,
                publisher = game.Publisher,
                developer = game.Developer,
                status = game.Status,
                rating = game.Rating,
                favorite = game.Favorite,
                tags = game.Tags,
                notes = game.Notes,
                file_name = game.FileName,
                file_size = game.FileSize,
                rom_path = game.RomPath,
                missing = game.IsMissing,
                play_count = game.PlayCount,
                play_seconds = game.PlaySeconds,
                last_played_at = game.LastPlayedAt,
                discs = game.Discs.Select((path, index) => new
                {
                    index,
                    file_name = Path.GetFileName(path),
                    exists = File.Exists(path),
                }).ToList(),
            },
        });
    }

    private static AiToolExecution GetStatistics(AiToolCall call, IAiAppHost host)
    {
        var games = host.AllGames;
        var platforms = games.Where(g => g.Platform.Length > 0)
            .GroupBy(g => g.Platform)
            .OrderByDescending(group => group.Count())
            .Take(20)
            .ToDictionary(group => group.Key, group => group.Count());
        var statuses = games.GroupBy(g => g.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        return Ok(call, new
        {
            ok = true,
            total = games.Count,
            favorites = games.Count(g => g.Favorite),
            missing = games.Count(g => g.IsMissing),
            played = games.Count(g => g.PlayCount > 0),
            total_play_seconds = games.Sum(g => g.PlaySeconds),
            platforms,
            statuses,
        });
    }

    private static AiToolExecution ListEmulators(AiToolCall call)
    {
        var items = AppServices.EmulatorRepo.ListAll().Select(emulator => new
        {
            id = emulator.Id,
            name = emulator.Name,
            enabled = emulator.Enabled,
            version = emulator.Version,
            platforms = emulator.Platforms,
        }).ToList();

        return Ok(call, new { ok = true, emulators = items });
    }

    private static AiToolExecution Recommend(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var id = Int(root, "seed_game_id");
        var seed = id is int gameId
            ? host.AllGames.FirstOrDefault(item => item.Id == gameId)
            : host.SelectedGame;
        var limit = Math.Clamp(Int(root, "limit") ?? 10, 1, 20);

        var items = RecommendationService.Recommend(seed, host.AllGames, limit)
            .Select(item => new
            {
                id = item.Game.Id,
                title = item.Game.Title,
                platform = item.Game.Platform,
                status = item.Game.Status,
                rating = item.Game.Rating,
                reason = item.Reason,
                score = item.Score,
            }).ToList();

        return Ok(call, new { ok = true, recommendations = items });
    }

    private static AiToolExecution GetSettings(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var requested = StringArray(root, "keys");
        var keys = requested.Length == 0
            ? AiToolRegistry.SettingsWhitelist
            : requested.Where(key => AiToolRegistry.SettingsWhitelist.Contains(key)).ToArray();

        var values = keys.ToDictionary(key => key, key => host.GetSetting(key));
        return Ok(call, new { ok = true, settings = values });
    }

    private static AiToolExecution ListDrives(AiToolCall call)
    {
        var drives = new List<object>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                drives.Add(new
                {
                    root = drive.RootDirectory.FullName,
                    label = drive.VolumeLabel,
                    free_gb = Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 1),
                    total_gb = Math.Round(drive.TotalSize / 1024d / 1024d / 1024d, 1),
                });
            }
            catch
            {
                // 单个盘不可读不影响其它盘
            }
        }

        return Ok(call, new { ok = true, drives });
    }

    private static async Task<AiToolExecution> ScanDeviceTreeAsync(AiToolCall call, CancellationToken token)
    {
        var root = Args(call);
        var driveRoot = Str(root, "drive_root").Trim();
        if (driveRoot.Length == 0) return Error(call, "缺少 drive_root。");

        var readyRoots = DriveInfo.GetDrives().Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .ToList();
        var matched = readyRoots.FirstOrDefault(item =>
            string.Equals(item.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (matched == null)
            return Error(call, $"盘符「{driveRoot}」不存在或未就绪。");

        var summary = await DriveTreeScanner.ScanAsync(matched, null, token);
        return Ok(call, new
        {
            ok = true,
            drive_root = summary.DriveRoot,
            directories = summary.Nodes.Count,
            top_level = summary.TopLevelDirectories.ToList(),
            text = summary.ToPromptText(6000),
        });
    }

    private static AiToolExecution UpdateGames(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var ids = LongArray(root, "game_ids");
        if (ids.Count == 0) return Error(call, "缺少 game_ids。");

        var fields = Obj(root, "fields");
        if (fields == null) return Error(call, "缺少 fields。");

        var patch = ParsePatch(fields.Value, out var patchError);
        if (patchError.Length > 0) return Error(call, patchError);
        if (!patch.HasAny) return Error(call, "没有要修改的字段。");

        var targets = host.AllGames.Where(game => ids.Contains(game.Id)).ToList();
        if (targets.Count == 0) return Error(call, "没有找到要修改的游戏。");

        var names = string.Join("、", targets.Take(3).Select(game => game.Title));
        if (targets.Count > 3) names += " 等";
        var summary = $"将更新 {targets.Count} 条游戏：{names}\n修改字段：{patch.Describe()}";

        return Pending(call, new AiPendingAction
        {
            Title = "修改游戏信息",
            Summary = summary,
            ExecuteAsync = () =>
            {
                var updated = host.UpdateGames(ids, patch);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, updated }, JsonOptions));
            },
        });
    }

    private static AiToolExecution DeleteGames(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var ids = LongArray(root, "game_ids");
        if (ids.Count == 0) return Error(call, "缺少 game_ids。");
        var recycle = Bool(root, "recycle_files") ?? false;

        var targets = host.AllGames.Where(game => ids.Contains(game.Id)).ToList();
        if (targets.Count == 0) return Error(call, "没有找到要删除的游戏。");

        var discCount = targets.Sum(game => game.Discs.Count);
        var names = string.Join("、", targets.Take(3).Select(game => game.Title));
        if (targets.Count > 3) names += " 等";
        var summary = recycle
            ? $"将删除 {targets.Count} 条记录，并把 {discCount} 个 ROM 文件移到回收站：{names}"
            : $"只删除 {targets.Count} 条数据库记录，不动 ROM 文件：{names}";

        return Pending(call, new AiPendingAction
        {
            Title = recycle ? "删除记录并移入回收站（危险）" : "从库中删除记录（危险）",
            Summary = summary,
            IsDestructive = true,
            ExecuteAsync = () =>
            {
                var removed = host.DeleteGames(ids, recycle);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, removed }, JsonOptions));
            },
        });
    }

    private static AiToolExecution LaunchGame(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var id = Int(root, "game_id");
        var discIndex = Math.Max(0, Int(root, "disc_index") ?? 0);

        var game = id is int gameId
            ? host.AllGames.FirstOrDefault(item => item.Id == gameId)
            : PickLaunchCandidate(host, null);
        if (game == null) return Error(call, "库里没有可启动的游戏。");

        return Pending(call, BuildLaunchAction(host, game, discIndex));
    }

    /// <summary>启动推荐：从全部游戏里随机，优先有名字、未玩/在玩、文件存在。</summary>
    private static Game? PickLaunchCandidate(IAiAppHost host, long? excludeId)
    {
        var candidates = host.AllGames
            .Where(game => game.Id != excludeId && !game.IsMissing)
            .ToList();
        if (candidates.Count == 0) return null;

        var named = candidates
            .Where(game => !string.IsNullOrWhiteSpace(game.Title)
                           && !System.Text.RegularExpressions.Regex.IsMatch(game.Title.Trim(), @"^\d+$"))
            .ToList();
        if (named.Count == 0) named = candidates;

        var preferred = named
            .Where(game => game.Status is GameStatus.NotPlayed or GameStatus.Playing)
            .ToList();
        if (preferred.Count > 0) named = preferred;

        return named[Random.Shared.Next(named.Count)];
    }

    private static AiPendingAction BuildLaunchAction(IAiAppHost host, Game game, int discIndex)
    {
        var discs = game.Discs.Count;
        var index = discs == 0 ? 0 : Math.Clamp(discIndex, 0, discs - 1);
        var details = new List<string> { game.Platform };
        if (game.ReleaseYear is int year) details.Add(year.ToString());
        details.Add(game.Status);
        if (game.Rating > 0) details.Add($"{game.Rating} 星");
        if (game.IsMultiDisc) details.Add($"第 {index + 1}/{discs} 碟");

        var summary = $"{game.Title}\n{string.Join(" · ", details.Where(item => !string.IsNullOrWhiteSpace(item)))}";
        if (!string.IsNullOrWhiteSpace(game.Tags)) summary += $"\n标签：{game.Tags}";

        return new AiPendingAction
        {
            Title = "启动游戏",
            Summary = summary,
            IsLaunchSuggestion = true,
            AlternativeAsync = () =>
            {
                var next = PickLaunchCandidate(host, game.Id);
                return Task.FromResult(next == null ? null : BuildLaunchAction(host, next, 0));
            },
            ExecuteAsync = () =>
            {
                var ok = host.LaunchGame(game.Id, index, out var message);
                return Task.FromResult(JsonSerializer.Serialize(new { ok, message }, JsonOptions));
            },
        };
    }

    private static AiToolExecution SendGames(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var ids = LongArray(root, "game_ids");
        if (ids.Count == 0) return Error(call, "缺少 game_ids。");
        if (string.IsNullOrWhiteSpace(host.ConnectedDevice))
            return Error(call, "还没有连接设备，请让用户先点击「连接到设备」。");

        var targets = host.AllGames.Where(game => ids.Contains(game.Id)).ToList();
        if (targets.Count == 0) return Error(call, "没有找到要发送的游戏。");

        var totalSize = targets.Sum(game => game.FileSize);
        var summary = $"将把 {targets.Count} 个游戏（{Game.FormatSize(totalSize)}）发送到 {host.ConnectedDevice}。";

        return Pending(call, new AiPendingAction
        {
            Title = "发送到设备",
            Summary = summary,
            ExecuteAsync = () =>
            {
                var message = host.SendGames(ids);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, message }, JsonOptions));
            },
        });
    }

    private static AiToolExecution SetSettings(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var settings = Obj(root, "settings");
        if (settings == null) return Error(call, "缺少 settings。");

        var updates = new Dictionary<string, string>();
        var lines = new List<string>();
        foreach (var property in settings.Value.EnumerateObject())
        {
            var key = property.Name;
            if (!AiToolRegistry.SettingsWhitelist.Contains(key))
                return Error(call, $"设置项「{key}」不允许通过 AI 修改。");

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()?.Trim() ?? ""
                : property.Value.ToString().Trim();

            var error = ValidateSetting(key, value);
            if (error.Length > 0) return Error(call, error);

            updates[key] = value;
            var oldValue = host.GetSetting(key);
            lines.Add($"{key}：{(oldValue.Length == 0 ? "（空）" : oldValue)} → {(value.Length == 0 ? "（空）" : value)}");
        }

        if (updates.Count == 0) return Error(call, "没有要修改的设置。");

        return Pending(call, new AiPendingAction
        {
            Title = "修改设置",
            Summary = string.Join("\n", lines),
            ExecuteAsync = () =>
            {
                foreach (var pair in updates) host.SetSetting(pair.Key, pair.Value);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, updated = updates.Count }, JsonOptions));
            },
        });
    }

    private static async Task<AiToolExecution> SaveDeviceProfileAsync(AiToolCall call, CancellationToken token)
    {
        var root = Args(call);
        var driveRoot = Str(root, "drive_root").Trim();
        var profileElement = Obj(root, "profile");
        if (driveRoot.Length == 0) return Error(call, "缺少 drive_root。");
        if (profileElement == null) return Error(call, "缺少 profile。");

        var readyRoots = DriveInfo.GetDrives().Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .ToList();
        var matched = readyRoots.FirstOrDefault(item =>
            string.Equals(item.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (matched == null) return Error(call, $"盘符「{driveRoot}」不存在或未就绪。");

        var summary = await DriveTreeScanner.ScanAsync(matched, null, token);
        var draft = new DeviceProfile
        {
            Name = Str(profileElement.Value, "name"),
            Brand = Str(profileElement.Value, "brand"),
            Model = Str(profileElement.Value, "model"),
            Transport = "drive",
            RootFolder = Str(profileElement.Value, "rootFolder"),
            FolderTemplate = Str(profileElement.Value, "folderTemplate"),
            AllowUnmappedPlatforms = Bool(profileElement.Value, "allowUnmappedPlatforms") ?? true,
            Notes = Str(profileElement.Value, "notes"),
            PlatformFolders = ParseStringMap(Obj(profileElement.Value, "platformFolders")),
        };

        var platforms = AppServices.Lookups.List(LookupRepository.Platform);
        var validation = AiDeviceProfiler.Validate(summary, draft, Str(root, "confidence"), platforms);
        if (!validation.CanApply)
            return Error(call, validation.Error + (validation.Warnings.Count == 0
                ? ""
                : "\n" + string.Join("\n", validation.Warnings)));

        var warnings = validation.Warnings.Count == 0
            ? ""
            : "\n注意：" + string.Join("；", validation.Warnings);
        var deviceSummary = $"将保存设备「{validation.Profile!.Name}」\n"
                            + $"根目录：{validation.Profile.RootFolder}，模板：{validation.Profile.FolderTemplate}\n"
                            + $"平台映射 {validation.Profile.PlatformFolders.Count} 条{warnings}";

        return Pending(call, new AiPendingAction
        {
            Title = "保存设备配置",
            Summary = deviceSummary,
            ExecuteAsync = () =>
            {
                var id = AppServices.DeviceProfiles.Save(validation.Profile!);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, id }, JsonOptions));
            },
        });
    }

    private static AiToolExecution OpenWindow(AiToolCall call, IAiAppHost host)
    {
        var root = Args(call);
        var window = Str(root, "window").Trim().ToLowerInvariant();
        if (!WindowNames.Contains(window))
            return Error(call, $"不支持的窗口：{window}。");

        host.OpenWindow(window);
        return Ok(call, new { ok = true, opened = window });
    }

    private static object Compact(Game game) => new
    {
        id = game.Id,
        title = game.Title,
        platform = game.Platform,
        genre = game.Genre,
        region = game.Region,
        language = game.Language,
        release_year = game.ReleaseYear,
        status = game.Status,
        rating = game.Rating,
        favorite = game.Favorite,
        tags = game.Tags,
        missing = game.IsMissing,
    };

    private static AiQueryFilter ParseFilter(JsonElement? element)
    {
        var filter = new AiQueryFilter();
        if (element == null || element.Value.ValueKind != JsonValueKind.Object) return filter;

        var root = element.Value;
        filter.Platform = Str(root, "platform");
        filter.Status = Str(root, "status");
        filter.Region = Str(root, "region");
        filter.Language = Str(root, "language");
        filter.Genre = Str(root, "genre");
        filter.Developer = Str(root, "developer");
        filter.Publisher = Str(root, "publisher");
        filter.Tag = Str(root, "tag");
        filter.Keyword = Str(root, "keyword");
        filter.Favorite = Bool(root, "favorite");
        filter.OnlyMissing = Bool(root, "only_missing") ?? false;
        filter.YearMin = Int(root, "year_min");
        filter.YearMax = Int(root, "year_max");
        return filter;
    }

    private static GamePatch ParsePatch(JsonElement element, out string error)
    {
        error = "";
        var patch = new GamePatch
        {
            Title = Has(element, "title") ? Str(element, "title") : null,
            Genre = Has(element, "genre") ? Str(element, "genre") : null,
            Region = Has(element, "region") ? Str(element, "region") : null,
            Language = Has(element, "language") ? Str(element, "language") : null,
            SpecialNote = Has(element, "special_note") ? Str(element, "special_note") : null,
            Publisher = Has(element, "publisher") ? Str(element, "publisher") : null,
            Developer = Has(element, "developer") ? Str(element, "developer") : null,
            Notes = Has(element, "notes") ? Str(element, "notes") : null,
            Tags = Has(element, "tags") ? Str(element, "tags") : null,
            Status = Has(element, "status") ? Str(element, "status") : null,
            ReleaseYear = Int(element, "release_year"),
            Rating = Int(element, "rating"),
            Favorite = Bool(element, "favorite"),
        };

        if (patch.Title != null && patch.Title.Length == 0)
            error = "标题不能为空。";
        if (patch.Status != null && !GameStatus.All.Contains(patch.Status, StringComparer.Ordinal))
            error = $"状态「{patch.Status}」不合法。";
        if (patch.ReleaseYear is < 1950 or > 2100)
            error = "年份必须在 1950–2100 之间。";
        if (patch.Rating is < 0 or > 5)
            error = "评分必须在 0–5 之间。";
        return patch;
    }

    private static string ValidateSetting(string key, string value) => key switch
    {
        "network.proxy.mode" => value is "none" or "system" or "manual"
            ? ""
            : "代理模式只能是 none / system / manual。",
        "network.proxy.address" => value.Length == 0
                                   || NetworkClientProvider.TryParseProxyAddress(value, out _, out _)
            ? ""
            : "代理地址格式应为 host:port，例如 127.0.0.1:7890。",
        "library.import_mode" => value is "link" or "copy" or "move"
            ? ""
            : "导入方式只能是 link / copy / move。",
        "ai.base_url" => value.Length == 0
                         || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            ? ""
            : "BaseUrl 必须是 http:// 或 https:// 地址。",
        "ai.model" => value.Length > 0 ? "" : "模型名不能为空。",
        "ai.timeout_seconds" => int.TryParse(value, out var seconds) && seconds is >= 10 and <= 600
            ? ""
            : "超时必须是 10–600 的整数。",
        "ai.batch_limit" => int.TryParse(value, out var limit) && limit is >= 1 and <= 200
            ? ""
            : "批量上限必须是 1–200 的整数。",
        _ => "该设置项不允许修改。",
    };

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static int? Int(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? Bool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool Has(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    private static JsonElement? Obj(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        return value.Clone();
    }

    private static List<long> LongArray(JsonElement element, string name)
    {
        var result = new List<long>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id)) result.Add(id);
            else if (long.TryParse(item.ToString(), out var parsed)) result.Add(parsed);
        }

        return result;
    }

    private static string[] StringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static Dictionary<string, string> ParseStringMap(JsonElement? element)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element == null || element.Value.ValueKind != JsonValueKind.Object) return map;
        foreach (var property in element.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value)) map[property.Name] = value;
        }

        return map;
    }

    private static JsonElement Args(AiToolCall call)
    {
        var text = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static AiToolExecution Ok(AiToolCall call, object data) => new()
    {
        ToolCallId = call.Id,
        ResultForModel = JsonSerializer.Serialize(data, JsonOptions),
    };

    private static AiToolExecution Error(AiToolCall call, string message) =>
        Ok(call, new { ok = false, error = message });

    private static AiToolExecution Pending(AiToolCall call, AiPendingAction action) => new()
    {
        ToolCallId = call.Id,
        ResultForModel = JsonSerializer.Serialize(new
        {
            ok = true,
            pending = true,
            message = "已生成待用户确认的操作，不要声称已经执行。",
            title = action.Title,
            summary = action.Summary,
        }, JsonOptions),
        Pending = action,
    };
}
