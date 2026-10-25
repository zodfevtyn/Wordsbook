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

    public event Action? Cancelled;
    public event Action<WordEntry>? Saved;

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
        MeaningBox.Text = entry?.Meaning ?? "";
        PronBox.Text = entry?.Pronunciation ?? "";
        DescriptionBox.Text = entry?.Description ?? "";

        WordHost.ClearValue(Border.BackgroundProperty);
        MeaningHost.ClearValue(Border.BackgroundProperty);

        _related.Clear();
        foreach (var r in entry?.Related ?? []) _related.Add(new RelatedRow { Text = r });

        _chips.Clear();
        foreach (var c in vm.CategoriesOf(_langId))
            _chips.Add(new CategoryChip { Model = c, On = entry?.CategoryIds.Contains(c.Id) ?? false });
        CategoryHost.Visibility = _chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var d = ParseDate(dateKey) ?? DateTime.Today;
        MonthBox.Text = d.ToString("MMM", En);
        DayBox.Text = d.Day.ToString(En);
        YearBox.Text = d.Year.ToString(En);

        var mark = entry?.Mark ?? 0;
        (mark == 2 ? MarkWarn : mark == 1 ? MarkUnknown : MarkOk).IsChecked = true;

        DispatcherQueue.TryEnqueue(() => WordBox.Focus(FocusState.Programmatic));
    }

    // ------------------------------------------------------------ 关联词

    void OnAddRelated(object sender, RoutedEventArgs e)
    {
        var text = AddRelatedBox.Text.Trim();
        if (text.Length == 0) return;

        _related.Add(new RelatedRow { Text = text });
        AddRelatedBox.Text = "";
    }

    void OnRemoveRelated(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RelatedRow row }) _related.Remove(row);
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

        var date = ParseDate($"{MonthBox.Text.Trim()} {DayBox.Text.Trim()} {YearBox.Text.Trim()}")
                   ?? ParseDate(_editing?.Date ?? "")
                   ?? DateTime.Today;

        var entry = _editing is null ? new WordEntry() : _editing;
        entry.LangId = _langId;
        entry.Word = word;
        entry.Meaning = meaning;
        entry.Pronunciation = PronBox.Text.Trim();
        entry.Description = DescriptionBox.Text.Trim();
        entry.Date = date.ToString("yyyy-MM-dd");
        entry.Mark = CurrentMark;
        entry.Related = _related.Select(r => r.Text.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        entry.CategoryIds = _chips.Where(c => c.On).Select(c => c.Model.Id).ToList();

        Saved?.Invoke(entry);
    }

    void OnCancel(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    public void Cancel() => Cancelled?.Invoke();

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
