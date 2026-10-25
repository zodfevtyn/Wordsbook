using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wordsbook.Models;
using Wordsbook.Services;

namespace Wordsbook.ViewModels;

public enum ScopeKind { Language, Month, Day, Category }

public sealed record HeatCell(DateTime Date, int Count, bool InMonth, bool IsFuture);

public sealed class AppViewModel : NotifyBase
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    readonly Store _store;
    readonly DispatcherQueueTimer _sidebarTimer;
    double _sidebarTarget;
    readonly Dictionary<string, LangNode> _lang = [];
    readonly Dictionary<string, MonthNode> _month = [];
    readonly Dictionary<string, DayNode> _day = [];
    readonly Dictionary<string, CategoryGroupNode> _catGroup = [];
    readonly Dictionary<string, CategoryNode> _cat = [];
    readonly HashSet<string> _expanded = [];

    string? _selectedLangId;
    ScopeKind _scope = ScopeKind.Day;
    string? _scopeKey;
    string _search = "";
    int _heatYear;
    int _heatMonth;

    public AppViewModel(Store store, DispatcherQueue queue)
    {
        _store = store;
        _sidebarTimer = queue.CreateTimer();
        _sidebarTimer.Interval = TimeSpan.FromMilliseconds(16);
        _sidebarTimer.IsRepeating = false;
        _sidebarTimer.Tick += (_, _) => StepSidebar();
        Data = store.Data;
        var today = DateTime.Today;
        _heatYear = today.Year;
        _heatMonth = today.Month;
    }

    public AppData Data { get; }

    public ObservableCollection<LangNode> LangNodes { get; } = [];
    public ObservableCollection<CategoryGroupNode> CategoryGroups { get; } = [];
    public ObservableCollection<WordView> Words { get; } = [];

    public string? SelectedLangId => _selectedLangId;
    public ScopeKind Scope => _scope;
    public string? ScopeKey => _scopeKey;

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) RebuildWords(); }
    }

    public bool GridView
    {
        get => Data.Settings.GridView;
        set
        {
            if (Data.Settings.GridView == value) return;
            Data.Settings.GridView = value;
            Raise();
            RebuildWords();
            _store.RequestSave();
        }
    }

    public bool FocusMode
    {
        get => Data.Settings.FocusMode;
        set
        {
            if (Data.Settings.FocusMode == value) return;
            Data.Settings.FocusMode = value;
            Raise();
            RebuildWords();
            _store.RequestSave();
        }
    }

    double _sidebarWidth = 300;

    /// <summary>侧栏当前宽度，折叠动画逐帧改这个值，两处列宽共用同一个源。</summary>
    public double SidebarWidth
    {
        get => _sidebarWidth;
        set { if (Set(ref _sidebarWidth, value)) Raise(nameof(SidebarVisibility)); }
    }

    public Visibility SidebarVisibility => _sidebarWidth > 1 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSidebarOpen => Data.Settings.SidebarOpen;

    public string Theme
    {
        get => Data.Settings.Theme;
        set { if (Data.Settings.Theme == value) return; Data.Settings.Theme = value; Raise(); _store.RequestSave(); }
    }

    public double Scale
    {
        get => Data.Settings.Scale;
        set { if (Math.Abs(Data.Settings.Scale - value) < 0.001) return; Data.Settings.Scale = value; Raise(); _store.RequestSave(); }
    }

    public string Font
    {
        get => Data.Settings.Font;
        set { if (Data.Settings.Font == value) return; Data.Settings.Font = value; Raise(); _store.RequestSave(); }
    }

    public void ToggleSidebar() => SetSidebarOpen(!Data.Settings.SidebarOpen, animate: true);

    public void SetSidebarOpen(bool open, bool animate)
    {
        Data.Settings.SidebarOpen = open;
        Raise(nameof(IsSidebarOpen));
        _store.RequestSave();

        _sidebarTarget = open ? _fullWidth : 0;
        if (animate)
        {
            if (_sidebarWidth != _sidebarTarget) _sidebarTimer.Start();
        }
        else
        {
            _sidebarTimer.Stop();
            SidebarWidth = _sidebarTarget;
        }
    }

    public const double MinSidebarWidth = 240;
    public const double MaxSidebarWidth = 420;

    /// <summary>展开时的目标宽度。拖分隔条、改窗口都不该越过这两条线。</summary>
    double _fullWidth = 300;

    public void SetSidebarWidth(double width)
    {
        var w = Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth);
        if (Math.Abs(w - _fullWidth) < 0.01 && Data.Settings.SidebarOpen) return;

        _fullWidth = w;
        Data.Settings.SidebarWidth = w;
        _store.RequestSave();

        if (!Data.Settings.SidebarOpen) return;
        _sidebarTimer.Stop();
        _sidebarTarget = w;
        SidebarWidth = w;
    }

    void StepSidebar()
    {
        // 指数趋近：无需记时长，掉帧也不会走形。
        var delta = (_sidebarTarget - _sidebarWidth) * 0.28;
        if (Math.Abs(_sidebarTarget - _sidebarWidth) < 1.5) delta = _sidebarTarget - _sidebarWidth;
        SidebarWidth += delta;
        if (SidebarWidth != _sidebarTarget) _sidebarTimer.Start();
    }

    public string HeaderTitle { get; private set; } = "";
    public bool ShowStartToday { get; private set; }

    public Visibility StartTodayVisibility => ShowStartToday ? Visibility.Visible : Visibility.Collapsed;
    public bool IsEmpty { get; private set; } = true;

    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public string HeatLabel { get; private set; } = "";
    public string PerDayLabel { get; private set; } = "";
    public IReadOnlyList<HeatCell> HeatCells { get; private set; } = [];
    public DateTime HeatToday { get; private set; } = DateTime.Today;

    public Language? ActiveLanguage => Data.Languages.FirstOrDefault(l => l.Id == _selectedLangId);

    /// <summary>新建单词的默认日期：跟随当前选中的日期节点，否则回落到今天。</summary>
    public string DraftDate => _scope == ScopeKind.Day ? _scopeKey! : DateTime.Today.ToString("yyyy-MM-dd");

    // ---------------------------------------------------------------- 启动

    public void Initialize()
    {
        if (_selectedLangId is null || Data.Languages.All(l => l.Id != _selectedLangId))
            _selectedLangId = Data.Settings.ActiveLangId ?? Data.Languages.FirstOrDefault()?.Id;

        if (_selectedLangId is not null) _expanded.Add(_selectedLangId);
        _fullWidth = Math.Clamp(Data.Settings.SidebarWidth, MinSidebarWidth, MaxSidebarWidth);
        _sidebarTarget = Data.Settings.SidebarOpen ? _fullWidth : 0;
        // 必须走属性：只写字段的话 x:Bind 收不到通知，列宽会停在初始值 300。
        SidebarWidth = _sidebarTarget;
        Rebuild();

        var latest = LangNodes.FirstOrDefault(n => n.Id == _selectedLangId)?.Months
            .SelectMany(m => m.Days).OrderByDescending(d => d.Key).FirstOrDefault();

        if (latest is not null) SelectDay(latest.Key);
        else if (!TodayStarted()) StartToday();
    }

    bool TodayStarted()
    {
        var key = DateTime.Today.ToString("yyyy-MM-dd");
        return Data.Days.Any(d => d.LangId == _selectedLangId && d.Date == key)
            || Data.Words.Any(w => w.LangId == _selectedLangId && w.Date == key);
    }

    // ---------------------------------------------------------------- 侧边栏

    public void Rebuild()
    {
        RebuildLanguages();
        RebuildCategories();
        RebuildWords();
        ShowStartToday = !TodayStarted();
        Raise(nameof(ShowStartToday));
        Raise(nameof(StartTodayVisibility));
        RebuildHeat();
    }

    void RebuildLanguages()
    {
        foreach (var lang in Data.Languages)
        {
            var node = GetOrAdd(_lang, lang.Id, () => new LangNode { Id = lang.Id });
            node.SetFlag(lang.ResolveCode());
            node.Name = lang.Name;
            node.Count = Num(Data.Words.Count(w => w.LangId == lang.Id));
            node.IsSelected = lang.Id == _selectedLangId && _scope == ScopeKind.Language;
            node.IsExpanded = _expanded.Contains(lang.Id);

            var byDay = new SortedDictionary<string, int>();
            foreach (var w in Data.Words.Where(w => w.LangId == lang.Id && w.Date.Length == 10))
                byDay[w.Date] = byDay.GetValueOrDefault(w.Date) + 1;
            foreach (var d in Data.Days.Where(d => d.LangId == lang.Id && d.Date.Length == 10))
                if (!byDay.ContainsKey(d.Date)) byDay[d.Date] = 0;

            var monthKeys = byDay.Keys.Select(k => k[..7]).Distinct().OrderDescending().ToList();
            SyncOrdered(node.Months, monthKeys, mk =>
                GetOrAdd(_month, lang.Id + "|" + mk, () => new MonthNode { Key = mk, Label = MonthLabel(mk) }));

            foreach (var month in node.Months)
            {
                var days = byDay.Keys.Where(k => k.StartsWith(month.Key, StringComparison.Ordinal)).OrderDescending().ToList();
                SyncOrdered(month.Days, days, dk =>
                    GetOrAdd(_day, lang.Id + "|" + dk, () => new DayNode { Key = dk, Label = DayLabel(dk) }));

                foreach (var day in month.Days)
                {
                    day.Count = Num(byDay.GetValueOrDefault(day.Key));
                    day.IsSelected = _scope == ScopeKind.Day && _selectedLangId == lang.Id && _scopeKey == day.Key;
                }

                month.Stat = $"{Num(days.Sum(d => byDay.GetValueOrDefault(d)))}, {days.Count}d";
                month.IsExpanded = _expanded.Contains(month.Key);
                month.IsSelected = _scope == ScopeKind.Month && _selectedLangId == lang.Id && _scopeKey == month.Key;
            }
        }

        SyncOrdered(LangNodes, Data.Languages.Select(l => l.Id).ToList(), id => _lang[id]);
    }

    void RebuildCategories()
    {
        foreach (var lang in Data.Languages)
        {
            var items = Data.Categories
                .Where(c => c.LangId == lang.Id)
                .OrderBy(c => c.Name, StringComparer.CurrentCulture)
                .ToList();

            if (items.Count == 0) continue;

            var group = GetOrAdd(_catGroup, lang.Id, () => new CategoryGroupNode { Id = lang.Id });
            group.SetFlag(lang.ResolveCode());
            group.Name = lang.Name;
            group.IsExpanded = _expanded.Contains("cat:" + lang.Id);

            SyncOrdered(group.Items, items.Select(c => c.Id).ToList(), cid =>
                GetOrAdd(_cat, cid, () => new CategoryNode { Id = cid }));

            foreach (var node in group.Items)
            {
                var model = items.First(c => c.Id == node.Id);
                node.Name = model.Name;
                node.Count = Num(Data.Words.Count(w => w.LangId == lang.Id && w.CategoryIds.Contains(model.Id)));
                node.IsSelected = _scope == ScopeKind.Category && _selectedLangId == lang.Id && _scopeKey == node.Id;
            }
        }

        var visible = Data.Languages.Select(l => l.Id).Where(_catGroup.ContainsKey).ToList();
        SyncOrdered(CategoryGroups, visible, id => _catGroup[id]);
    }

    // ---------------------------------------------------------------- 选择

    public void ToggleExpanded(string key)
    {
        if (!_expanded.Remove(key)) _expanded.Add(key);
        RebuildLanguages();
        RebuildCategories();
    }

    public void SelectLanguage(string langId)
    {
        _selectedLangId = langId;
        Data.Settings.ActiveLangId = langId;
        _scope = ScopeKind.Language;
        _scopeKey = null;
        Rebuild();
        _store.RequestSave();
    }

    public void SelectMonth(string monthKey)
    {
        _scope = ScopeKind.Month;
        _scopeKey = monthKey;
        if (!_expanded.Remove(monthKey)) _expanded.Add(monthKey);
        Rebuild();
    }

    public void SelectDay(string dayKey)
    {
        _scope = ScopeKind.Day;
        _scopeKey = dayKey;
        _expanded.Add(dayKey[..7]);
        var d = ParseDate(dayKey);
        _heatYear = d.Year;
        _heatMonth = d.Month;
        Rebuild();
    }

    public void SelectCategory(string categoryId)
    {
        _scope = ScopeKind.Category;
        _scopeKey = categoryId;
        Rebuild();
    }

    public void StartToday()
    {
        _selectedLangId ??= Data.Languages.FirstOrDefault()?.Id;
        if (_selectedLangId is null) return;

        var key = DateTime.Today.ToString("yyyy-MM-dd");
        if (!Data.Days.Any(d => d.LangId == _selectedLangId && d.Date == key))
            Data.Days.Add(new DayRecord { LangId = _selectedLangId, Date = key });

        SelectDay(key);
        _store.RequestSave();
    }

    // ---------------------------------------------------------------- 单词

    void RebuildWords()
    {
        Words.Clear();

        var query = Data.Words.AsEnumerable();
        if (_selectedLangId is not null) query = query.Where(w => w.LangId == _selectedLangId);

        query = _scope switch
        {
            ScopeKind.Day when _scopeKey is not null => query.Where(w => w.Date == _scopeKey),
            ScopeKind.Month when _scopeKey is not null => query.Where(w => w.Date.StartsWith(_scopeKey, StringComparison.Ordinal)),
            ScopeKind.Category when _scopeKey is not null => query.Where(w => w.CategoryIds.Contains(_scopeKey)),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var q = _search.Trim();
            query = query.Where(w =>
                w.Word.Contains(q, StringComparison.OrdinalIgnoreCase)
                || w.Meaning.Contains(q, StringComparison.OrdinalIgnoreCase)
                || w.Related.Any(r => r.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var list = query
            .OrderByDescending(w => w.Date)
            .ThenBy(w => w.Word, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var w in list) Words.Add(WordView.From(w, FocusMode));
        Words.Add(WordView.Placeholder());

        IsEmpty = list.Count == 0;
        HeaderTitle = BuildTitle();
        Raise(nameof(IsEmpty));
        Raise(nameof(EmptyVisibility));
        Raise(nameof(HeaderTitle));
    }

    string BuildTitle() => _scope switch
    {
        ScopeKind.Day when _scopeKey is not null => ParseDate(_scopeKey).ToString("ddd, d MMM yyyy", En),
        ScopeKind.Month when _scopeKey is not null => ParseMonth(_scopeKey).ToString("MMMM yyyy", En),
        ScopeKind.Category when _scopeKey is not null =>
            ActiveLanguage?.Name + " · " + (Data.Categories.FirstOrDefault(c => c.Id == _scopeKey)?.Name ?? ""),
        _ => ActiveLanguage?.Name ?? "",
    };

    public void Save(WordEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LangId)) entry.LangId = _selectedLangId ?? Data.Languages.First().Id;
        _selectedLangId = entry.LangId;

        var existing = Data.Words.FirstOrDefault(w => w.Id == entry.Id);
        if (existing is null) Data.Words.Add(entry);
        else
        {
            existing.Word = entry.Word;
            existing.Meaning = entry.Meaning;
            existing.Pronunciation = entry.Pronunciation;
            existing.Description = entry.Description;
            existing.Date = entry.Date;
            existing.Mark = entry.Mark;
            existing.Related = entry.Related;
            existing.CategoryIds = entry.CategoryIds;
        }

        if (entry.Date.Length >= 7)
        {
            if (!Data.Days.Any(d => d.LangId == entry.LangId && d.Date == entry.Date))
                Data.Days.Add(new DayRecord { LangId = entry.LangId, Date = entry.Date });
            _expanded.Add(entry.Date[..7]);
        }

        Rebuild();
        _store.RequestSave();
    }

    public void Delete(WordEntry entry)
    {
        Data.Words.RemoveAll(w => w.Id == entry.Id);
        Rebuild();
        _store.RequestSave();
    }

    public Category AddCategory(string langId, string name)
    {
        var c = new Category { LangId = langId, Name = name };
        Data.Categories.Add(c);
        _expanded.Add("cat:" + langId);
        Rebuild();
        _store.RequestSave();
        return c;
    }

    public List<Category> CategoriesOf(string? langId) => langId is null
        ? []
        : Data.Categories.Where(c => c.LangId == langId).OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList();

    // ---------------------------------------------------------------- 语言

    public Language AddLanguage(string name, string code)
    {
        var lang = new Language { Name = name, Code = code };
        Data.Languages.Add(lang);
        _selectedLangId = lang.Id;
        Data.Settings.ActiveLangId = lang.Id;
        _expanded.Add(lang.Id);
        Rebuild();
        _store.RequestSave();
        return lang;
    }

    public void UpdateLanguage(string id, string name, string code)
    {
        var lang = Data.Languages.FirstOrDefault(l => l.Id == id);
        if (lang is null) return;

        lang.Name = name;
        lang.Code = code;
        Rebuild();
        _store.RequestSave();
    }

    /// <summary>连带删除该语言下的单词、分类与日期建档，调用方需先向用户确认。</summary>
    public void DeleteLanguage(string id)
    {
        Data.Languages.RemoveAll(l => l.Id == id);
        Data.Words.RemoveAll(w => w.LangId == id);
        Data.Categories.RemoveAll(c => c.LangId == id);
        Data.Days.RemoveAll(d => d.LangId == id);

        if (_selectedLangId == id) _selectedLangId = Data.Languages.FirstOrDefault()?.Id;
        Data.Settings.ActiveLangId = _selectedLangId;
        Rebuild();
        _store.RequestSave();
    }

    // ---------------------------------------------------------------- 热力图

    public void StepHeat(int delta)
    {
        var m = _heatMonth + delta + 12;
        var y = _heatYear + (m - 1) / 12;
        m = (m - 1) % 12 + 1;
        _heatYear = y;
        _heatMonth = m;
        RebuildHeat();
    }

    void RebuildHeat()
    {
        var first = new DateTime(_heatYear, _heatMonth, 1);
        var days = DateTime.DaysInMonth(_heatYear, _heatMonth);
        var today = DateTime.Today;
        HeatToday = today;

        HeatLabel = first.ToString("MMM", En);
        Raise(nameof(HeatLabel));
        Raise(nameof(HeatToday));

        var counts = new Dictionary<string, int>();
        foreach (var w in Data.Words.Where(w => w.LangId == _selectedLangId && w.Date.Length == 10))
            counts[w.Date] = counts.GetValueOrDefault(w.Date) + 1;

        // 以周一为第一列，对应截图的 M T W T F S S。
        var lead = ((int)first.DayOfWeek + 6) % 7;
        var cells = new List<HeatCell>(lead + days + 7);
        for (var i = 0; i < lead; i++) cells.Add(new HeatCell(first.AddDays(i - lead), 0, false, false));
        for (var d = 0; d < days; d++)
        {
            var date = first.AddDays(d);
            cells.Add(new HeatCell(date, counts.GetValueOrDefault(date.ToString("yyyy-MM-dd")), true, date > today));
        }
        while (cells.Count % 7 != 0) cells.Add(new HeatCell(cells[^1].Date.AddDays(1), 0, false, false));

        HeatCells = cells;
        Raise(nameof(HeatCells));

        var monthKey = first.ToString("yyyy-MM");
        var monthTotal = counts.Where(kv => kv.Key.StartsWith(monthKey, StringComparison.Ordinal)).Sum(kv => kv.Value);
        var elapsed = first.Year == today.Year && first.Month == today.Month ? today.Day : days;
        PerDayLabel = (elapsed <= 0 ? 0d : (double)monthTotal / elapsed).ToString("0.00", En) + " / day";
        Raise(nameof(PerDayLabel));
    }

    // ---------------------------------------------------------------- 工具

    static T GetOrAdd<T>(Dictionary<string, T> map, string key, Func<T> factory) where T : class =>
        map.TryGetValue(key, out var existing) ? existing : map[key] = factory();

    /// <summary>按 keys 顺序重排 collection，复用已有节点以免整棵树重新实例化。</summary>
    static void SyncOrdered<T>(ObservableCollection<T> collection, IReadOnlyList<string> keys, Func<string, T> resolve)
    {
        var desired = new List<T>(keys.Count);
        foreach (var k in keys) desired.Add(resolve(k));

        for (var i = 0; i < desired.Count; i++)
        {
            var at = collection.IndexOf(desired[i]);
            if (at < 0) collection.Insert(i, desired[i]);
            else if (at != i) collection.Move(at, i);
        }

        for (var i = collection.Count - 1; i >= 0; i--)
            if (!desired.Contains(collection[i])) collection.RemoveAt(i);
    }

    static DateTime ParseDate(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;

    static DateTime ParseMonth(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;

    static string Num(int v) => v.ToString("N0", En);

    static string DayLabel(string key) => ParseDate(key).ToString("ddd, d", En);
    static string MonthLabel(string key) => ParseMonth(key).ToString("MMM yyyy", En);
}
