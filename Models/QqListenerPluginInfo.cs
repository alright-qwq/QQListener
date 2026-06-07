namespace QQListener.Models;

/// <summary>
/// 把 manifest.yml 派生的展示元数据集中到一处，供设置页"页头版本号"和"关于"区使用。
/// 唯一权威是 manifest.yml；SDK 版本是项目里的 csproj 同步硬编码。
/// </summary>
public sealed class QqListenerPluginInfo
{
    /// <summary>
    /// 与 QQListener.csproj 的 <ClassIslandPluginSdkVersion> 同步维护。
    /// SDK 当前未公开运行时元数据 API，所以暂时双写。
    /// </summary>
    public const string DefaultSdkVersion = "1.7.106.2-dev-v2";

    public const string DefaultLicense = "MIT";
    public const string DefaultCopyrightYear = "2026";
    public const string DefaultQaUrl = "https://al-right.top/archives/58";
    public const string OriginalAuthorLine = "原作者：B 站 @BSOD-MEMZ（PySide6 版）";

    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Url { get; init; } = "";
    public string IssuesUrl { get; init; } = "";
    public string QaUrl { get; init; } = DefaultQaUrl;
    public string SdkVersion { get; init; } = DefaultSdkVersion;
    public string License { get; init; } = DefaultLicense;

    /// <summary>页头小字："v1.0.1.0 · by alright-qwq"。任一字段缺失时给"未知"占位。</summary>
    public string HeaderLine
    {
        get
        {
            var v = string.IsNullOrWhiteSpace(Version) ? "未知" : Version;
            var a = string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
            return $"v{v} · by {a}";
        }
    }

    /// <summary>关于卡片版本号显示，例如 "v1.0.1.0"。预格式化以避免在 XAML 里
    /// 用 StringFormat 触发 Run.Text 的 TwoWay 回写循环（会导致 StackOverflow）。</summary>
    public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "v 未知" : $"v{Version}";

    /// <summary>关于卡片："Copyright © 2026 alright-qwq"。</summary>
    public string CopyrightLine =>
        $"Copyright © {DefaultCopyrightYear} {(string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author)}";

    /// <summary>关于卡片："Released under the MIT License"。</summary>
    public string LicenseLine => $"Released under the {License} License";

    /// <summary>关于卡片："基于 ClassIsland.PluginSdk X.Y.Z"。</summary>
    public string SdkLine => $"基于 ClassIsland.PluginSdk {SdkVersion}";

    /// <summary>关于卡片底部固定行。</summary>
    public string OriginalAuthor => OriginalAuthorLine;

    /// <summary>设计器与未通过宿主加载场景的占位实例。</summary>
    public static QqListenerPluginInfo Designer { get; } = new()
    {
        Version = "设计器",
        Author = "alright-qwq",
        Url = "https://github.com/alright-qwq/QQListener",
        IssuesUrl = "https://github.com/alright-qwq/QQListener/issues"
    };

    /// <summary>从 manifest 字段构造。Url 为空时 IssuesUrl 也为空，由 UI 用 ProviderAvailable 控制可点。</summary>
    public static QqListenerPluginInfo FromManifest(string version, string author, string? url)
    {
        var safeUrl = url ?? "";
        var trimmed = safeUrl.TrimEnd('/');
        var issues = string.IsNullOrWhiteSpace(trimmed) ? "" : $"{trimmed}/issues";
        return new QqListenerPluginInfo
        {
            Version = version ?? "",
            Author = author ?? "",
            Url = safeUrl,
            IssuesUrl = issues
        };
    }
}
