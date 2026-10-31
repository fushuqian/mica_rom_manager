namespace MicaRomManager.Models;

/// <summary>
/// 主机的年代顺序：按各机型首发年份排，别用字典序（那样 GameCube 会插在 Famicom 和 Super Famicom 中间）。
/// 表里同时收短名和展示名，用户自建的平台排在同厂商最后。
/// </summary>
public static class PlatformOrder
{
    private static readonly Dictionary<string, int> Years = new(StringComparer.OrdinalIgnoreCase)
    {
        // Nintendo
        ["Famicom"] = 1983, ["NES"] = 1983, ["FC"] = 1983,
        ["Game Boy"] = 1989, ["GB"] = 1989,
        ["Super Famicom"] = 1990, ["SNES"] = 1990, ["SFC"] = 1990,
        ["Virtual Boy"] = 1995, ["VB"] = 1995,
        ["Nintendo 64"] = 1996, ["N64"] = 1996,
        ["Game Boy Color"] = 1998, ["GBC"] = 1998,
        ["Game Boy Advance"] = 2001, ["GBA"] = 2001,
        ["GameCube"] = 2001, ["NGC"] = 2001,
        ["Nintendo DS"] = 2004, ["NDS"] = 2004,
        ["Wii"] = 2006, ["WII"] = 2006,
        ["Nintendo 3DS"] = 2011, ["3DS"] = 2011, ["N3DS"] = 2011,
        ["Wii U"] = 2012, ["WiiU"] = 2012, ["WIIU"] = 2012,
        ["Switch"] = 2017, ["SWITCH"] = 2017,

        // Sony
        ["PlayStation"] = 1994, ["PS1"] = 1994,
        ["PlayStation 2"] = 2000, ["PS2"] = 2000,
        ["PSP"] = 2004,
        ["PlayStation 3"] = 2006, ["PS3"] = 2006,
        ["PS Vita"] = 2011, ["PSVITA"] = 2011,
        ["PlayStation 4"] = 2013, ["PS4"] = 2013,
        ["PlayStation 5"] = 2020, ["PS5"] = 2020,

        // Sega
        ["Master System"] = 1985, ["SMS"] = 1985,
        ["Mega Drive"] = 1988, ["MD"] = 1988, ["Genesis"] = 1988,
        ["Game Gear"] = 1990, ["GG"] = 1990,
        ["32X"] = 1994,
        ["Saturn"] = 1994, ["SS"] = 1994,
        ["Dreamcast"] = 1998, ["DC"] = 1998,

        // NEC
        ["PC Engine"] = 1987, ["PCE"] = 1987,
        ["PC Engine CD"] = 1988, ["PCECD"] = 1988,
        ["PC-FX"] = 1994, ["PCFX"] = 1994,

        // SNK
        ["Neo Geo"] = 1990, ["NEOGEO"] = 1990,
        ["Neo Geo Pocket"] = 1998, ["NGP"] = 1998,

        // Bandai
        ["WonderSwan"] = 1999, ["WS"] = 1999,
        ["WonderSwan Color"] = 2000, ["WSC"] = 2000,

        // Atari
        ["Atari 2600"] = 1977, ["ATARI2600"] = 1977,
        ["Atari 5200"] = 1982, ["ATARI5200"] = 1982,
        ["Atari 7800"] = 1986, ["ATARI7800"] = 1986,
        ["Atari Lynx"] = 1989, ["LYNX"] = 1989,
        ["Atari Jaguar"] = 1993, ["JAGUAR"] = 1993,

        // Microsoft
        ["Xbox"] = 2001, ["XBOX"] = 2001,
        ["Xbox 360"] = 2005, ["XBOX360"] = 2005,

        // 其他
        ["Arcade"] = 1979, ["ARCADE"] = 1979, ["街机"] = 1979,
        ["CD-i"] = 1991, ["CDI"] = 1991,
        ["3DO"] = 1993,
        ["N-Gage"] = 2003, ["NGAGE"] = 2003,
    };

    /// <summary>首发年份；表里没有（用户自建）返回 int.MaxValue，排在最后。</summary>
    public static int YearOf(string? platform) =>
        platform != null && Years.TryGetValue(platform.Trim(), out var year) ? year : int.MaxValue;

    /// <summary>同厂商内：先按年份，再按展示名（同年份的 Game Boy Advance / GameCube 不会乱跳）。</summary>
    public static int CompareByEra(string? left, string? right)
    {
        var byYear = YearOf(left).CompareTo(YearOf(right));
        if (byYear != 0) return byYear;

        return string.Compare(PlatformDisplayNames.Of(left), PlatformDisplayNames.Of(right),
            StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>先按厂商（预置清单顺序，自定义最后），再按年代。</summary>
    public static int CompareByVendorThenEra(string? left, string? right)
    {
        var byVendor = PlatformDisplayNames.VendorRank(left).CompareTo(PlatformDisplayNames.VendorRank(right));
        return byVendor != 0 ? byVendor : CompareByEra(left, right);
    }

    public static IComparer<string> ByEra { get; } = Comparer<string>.Create(CompareByEra);

    public static IComparer<string> ByVendorThenEra { get; } = Comparer<string>.Create(CompareByVendorThenEra);
}
