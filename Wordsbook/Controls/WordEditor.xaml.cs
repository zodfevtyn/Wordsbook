using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wordsbook.Models;
using Wordsbook.ViewModels;

namespace Wordsbook.Controls;

/// <summary>新建 / 编辑单词的浮层表单。字段与截图一致，word 与 meaning 为必填。</summary>
public sealed partial class WordEditor : UserControl
{
    static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    static readonly SolidColorBrush ErrorTint = new(Windows.UI.Color.FromArgb(0x33, 0xFF, 0x3B, 0x30));

    readonly ObservableCollection<RelatedRow> _related = [];
    readonly ObservableCollection<CategoryChip> _chips = [];

    WordEntry? _editing;
    string _langId = "";
    DateTime _date = DateTime.Today;
    bool _syncingDate;

    public event Action? Cancelled;
    public event Action<WordEntry>? Saved;
    public event Func<string, Task<Category?>>? CategoryRequested;

    public WordEditor()
    {
        InitializeComponent();
        RelatedList.ItemsSource = _related;
        CategoryChips.ItemsSource = _chips;
    }

    public void OpenNew(AppViewModel vm) => OpenCore(vm, null, vm.DraftDate);

    public void OpenEdit(AppViewModel vm, WordEntry entry) => OpenCore(vm, entry, entry.Date);

    void OpenCore(AppViewModel vm, WordEntry? entry, string dateKey)
    {
        _editing = entry;

        TitleText.Text = entry is null ? "New word" : "Edit word";
        _langId = entry?.LangId ?? vm.SelectedLangId ?? vm.Data.Languages.FirstOrDefault()?.Id ?? "";

        WordBox.Text = entry?.Word ?? "";
        NatureBox.Text = entry?.Nature ?? "";
        MeaningBox.Text = entry?.Meaning ?? "";
        PronBox.Text = entry?.Pronunciation ?? "";
        DescriptionBox.Text = entry?.Description ?? "";

        WordHost.ClearValue(Border.BackgroundProperty);
        MeaningHost.ClearValue(Border.BackgroundProperty);
        AddRelatedBox.Text = "";
        AddNatureBox.Text = "";

        _related.Clear();
        var forms = entry?.RelatedForms
            .Where(f => !string.IsNullOrWhiteSpace(f.Form))
            .Select(f => new RelatedForm { Form = f.Form.Trim(), Nature = f.Nature.Trim() })
            .ToList() ?? [];
        if (forms.Count == 0 && entry is not null) forms = RelatedForm.Expand(entry.Related, entry.RelatedUsage);
        foreach (var form in forms) _related.Add(new RelatedRow { Form = form.Form, Nature = form.Nature });

        _chips.Clear();
        foreach (var c in vm.CategoriesOf(_langId))
            _chips.Add(new CategoryChip { Model = c, On = entry?.CategoryIds.Contains(c.Id) ?? false });
        _chips.Add(CategoryChip.Add());

        SetDate(ParseDate(dateKey) ?? DateTime.Today);

        var mark = entry?.Mark ?? 0;
        (mark == 2 ? MarkWarn : mark == 1 ? MarkUnknown : MarkOk).IsChecked = true;

        DispatcherQueue.TryEnqueue(() => WordBox.Focus(FocusState.Programmatic));
    }

    // ------------------------------------------------------------ 变体

    void OnAddRelated(object sender, RoutedEventArgs e)
    {
        var form = AddRelatedBox.Text.Trim();
        if (form.Length == 0)
        {
            AddRelatedBox.Focus(FocusState.Programmatic);
            return;
        }

        _related.Add(new RelatedRow { Form = form, Nature = AddNatureBox.Text.Trim() });
        AddRelatedBox.Text = "";
        AddNatureBox.Text = "";
        AddRelatedBox.Focus(FocusState.Programmatic);
    }

    void OnAddRelatedKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        OnAddRelated(sender, e);
        e.Handled = true;
    }

    void OnRemoveRelated(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RelatedRow row }) _related.Remove(row);
    }

    // ------------------------------------------------------------ 分类

    async void OnAddCategoryChip(object sender, RoutedEventArgs e)
    {
        if (CategoryRequested is null) return;

        var category = await CategoryRequested(_langId);
        if (category is null || _chips.Any(c => c.Model?.Id == category.Id)) return;

        _chips.Insert(Math.Max(0, _chips.Count - 1), new CategoryChip { Model = category, On = true });
    }

    // ------------------------------------------------------------ 日期

    void SetDate(DateTime date)
    {
        _date = date.Date;
        DateText.Text = _date.ToString("ddd, d MMM yyyy", En);
    }

    void OnDateFlyoutOpening(object? sender, object e)
    {
        _syncingDate = true;
        DateCalendar.SetDisplayDate(new DateTimeOffset(_date));
        DateCalendar.SelectedDates.Clear();
        DateCalendar.SelectedDates.Add(new DateTimeOffset(_date));
        _syncingDate = false;
    }

    void OnDateSelected(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_syncingDate || sender.SelectedDates.Count == 0) return;

        SetDate(sender.SelectedDates[^1].DateTime);
        DateFlyout.Hide();
    }

    // ------------------------------------------------------------ 提交

    int CurrentMark => MarkWarn.IsChecked == true ? 2 : MarkUnknown.IsChecked == true ? 1 : 0;

    void OnConfirm(object sender, RoutedEventArgs e)
    {
        var word = WordBox.Text.Trim();
        var meaning = MeaningBox.Text.Trim();

        if (word.Length == 0 || meaning.Length == 0)
        {
            Tint(WordHost, word.Length == 0);
            Tint(MeaningHost, meaning.Length == 0);
            (word.Length == 0 ? WordBox : MeaningBox).Focus(FocusState.Programmatic);
            return;
        }

        var forms = _related
            .Select(r => new RelatedForm { Form = r.Form.Trim(), Nature = r.Nature.Trim() })
            .Where(r => r.Form.Length > 0)
            .DistinctBy(r => r.Form, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entry = _editing ?? new WordEntry();
        entry.LangId = _langId;
        entry.Word = word;
        entry.Nature = NatureBox.Text.Trim();
        entry.Meaning = meaning;
        entry.Pronunciation = PronBox.Text.Trim();
        entry.Description = DescriptionBox.Text.Trim();
        entry.Date = _date.ToString("yyyy-MM-dd");
        entry.Mark = CurrentMark;
        entry.RelatedForms = forms;
        entry.Related = forms.Select(f => f.Form).ToList();
        entry.RelatedUsage = string.Join(" · ", forms.Select(f => f.Nature).Where(n => n.Length > 0));
        entry.CategoryIds = _chips.Where(c => c.On).Select(c => c.Model!.Id).ToList();

        Saved?.Invoke(entry);
    }

    void OnCancel(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    static void Tint(Border host, bool invalid)
    {
        if (invalid) host.Background = ErrorTint;
        else host.ClearValue(Border.BackgroundProperty);
    }

    static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)) return iso;
        if (DateTime.TryParseExact(s, "MMM d yyyy", En, DateTimeStyles.None, out var mdy)) return mdy;
        if (DateTime.TryParseExact(s, "M d yyyy", En, DateTimeStyles.None, out var dmy)) return dmy;
        return DateTime.TryParse(s, En, DateTimeStyles.None, out var loose) ? loose : null;
    }
}
