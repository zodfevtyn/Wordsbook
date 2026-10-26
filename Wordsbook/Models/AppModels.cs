namespace Wordsbook.Models;

/// <summary>一种学习语言。Flag 使用 emoji 区域指示符序列（如 🇪🇸）。</summary>
public sealed class Language
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Flag { get; set; } = "";

    /// <summary>ISO 3166-1 alpha-2，用于绘制矢量旗子。</summary>
    public string Code { get; set; } = "";

    public string ResolveCode()
    {
        if (Code.Length >= 2) return Code[..2].ToLowerInvariant();

        // 从 emoji 区域指示符反推两位码，兼容只填了 Flag 的老数据（代理对要先合成码点）。
        var letters = "";
        for (var i = 0; i < Flag.Length; i++)
        {
            var cp = char.IsHighSurrogate(Flag[i]) && i + 1 < Flag.Length
                ? char.ConvertToUtf32(Flag[i], Flag[i + 1])
                : Flag[i];
            if (cp >= 0x1F1E6 && cp <= 0x1F1FF) letters += (char)('A' + (cp - 0x1F1E6));
            if (char.IsHighSurrogate(Flag[i])) i++;
        }
        return letters.Length >= 2 ? letters[..2].ToLowerInvariant() : "";
    }
}

/// <summary>用户自建分类，隶属于某一语言。</summary>
public sealed class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LangId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>原词的一个变体及该变体的使用性质（如 hij / wij）。</summary>
public sealed class RelatedForm
{
    public string Form { get; set; } = "";
    public string Nature { get; set; } = "";

    /// <summary>把旧数据的 Related + RelatedUsage 按 “ · ” 对齐成逐条变体。</summary>
    public static List<RelatedForm> Expand(IEnumerable<string> forms, string usage)
    {
        var list = forms.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (list.Count == 0) return [];
        if (list.Count == 1) return [new RelatedForm { Form = list[0], Nature = usage.Trim() }];

        var parts = usage.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return list.Select((form, i) => new RelatedForm
        {
            Form = form,
            Nature = i < parts.Length ? parts[i] : "",
        }).ToList();
    }
}

/// <summary>一条单词记录。Date 为 yyyy-MM-dd。</summary>
public sealed class WordEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LangId { get; set; } = "";
    public string Word { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Pronunciation { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>原词的性质，如名词的 de / het，显示在词性左侧。</summary>
    public string Nature { get; set; } = "";

    /// <summary>Related 里那组形式的用法，如 hij / wij，显示在 Related 行最右侧。</summary>
    public string RelatedUsage { get; set; } = "";
    public string Date { get; set; } = "";
    public int Mark { get; set; }
    public List<string> Related { get; set; } = [];
    public List<RelatedForm> RelatedForms { get; set; } = [];
    public List<string> CategoryIds { get; set; } = [];
}

/// <summary>某一语言下已建档的一天，即使还没有单词也会保留在侧边栏里。</summary>
public sealed class DayRecord
{
    public string LangId { get; set; } = "";
    public string Date { get; set; } = "";
}

public sealed class AppSettings
{
    public string? ActiveLangId { get; set; }
    public bool GridView { get; set; } = true;
    public bool AdaptiveView { get; set; }
    public bool FocusMode { get; set; }

    /// <summary>system | light | dark</summary>
    public string Theme { get; set; } = "system";

    /// <summary>界面缩放，仅接受 1 / 1.25 / 1.5。</summary>
    public double Scale { get; set; } = 1;

    /// <summary>sf | system，改动在下次启动生效。</summary>
    public string Font { get; set; } = "sf";

    public bool SidebarOpen { get; set; } = true;

    /// <summary>侧栏展开时的宽度，读取方负责夹在 240..420 之间。</summary>
    public double SidebarWidth { get; set; } = 300;
}

public sealed class AppData
{
    public int Version { get; set; } = 1;
    public List<Language> Languages { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<WordEntry> Words { get; set; } = [];

    /// <summary>已通过 Start today 建档的日期，可与单词日期不一致。</summary>
    public List<DayRecord> Days { get; set; } = [];

    public AppSettings Settings { get; set; } = new();
}
