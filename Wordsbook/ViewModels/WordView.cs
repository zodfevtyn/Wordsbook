using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Wordsbook.Models;

namespace Wordsbook.ViewModels;

/// <summary>右侧单词卡片 / 列表行的视图模型，也承载末尾的“+”占位块。</summary>
public sealed class WordView : NotifyBase
{
    static readonly SolidColorBrush Green = Hex("#34C759");
    static readonly SolidColorBrush Orange = Hex("#FF9F0A");
    static readonly SolidColorBrush Grey = Hex("#98989D");

    static readonly FontFamily Icon = new("Segoe Fluent Icons");
    static readonly FontFamily Display = new("SF Pro Display");

    static SolidColorBrush Hex(string s) =>
        new(ColorHelper.FromArgb(0xFF,
            Convert.ToByte(s.Substring(1, 2), 16),
            Convert.ToByte(s.Substring(3, 2), 16),
            Convert.ToByte(s.Substring(5, 2), 16)));

    public WordEntry? Model { get; init; }
    public bool IsPlaceholder { get; init; }

    public Visibility CardVisibility => IsPlaceholder ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlaceholderVisibility => IsPlaceholder ? Visibility.Visible : Visibility.Collapsed;

    string _word = "";
    public string Word { get => _word; set => Set(ref _word, value); }

    string _pos = "";
    public string Pos { get => _pos; set => Set(ref _pos, value); }

    string _meaning = "";
    public string Meaning { get => _meaning; set => Set(ref _meaning, value); }

    string _pronunciation = "";
    public string Pronunciation { get => _pronunciation; set => Set(ref _pronunciation, value); }
    public Visibility PronunciationVisibility => _pronunciation.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    string _description = "";
    public string Description { get => _description; set => Set(ref _description, value); }
    public Visibility DescriptionVisibility => string.IsNullOrWhiteSpace(_description) ? Visibility.Collapsed : Visibility.Visible;

    IReadOnlyList<RelatedForm> _relatedForms = [];
    public IReadOnlyList<RelatedForm> RelatedForms { get => _relatedForms; set => Set(ref _relatedForms, value); }
    public Visibility RelatedFormsVisibility => _relatedForms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    string _related = "";
    public string Related { get => _related; set => Set(ref _related, value); }

    string _nature = "";
    public string Nature { get => _nature; set => Set(ref _nature, value); }
    public Visibility NatureVisibility => _nature.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    string _relatedUsage = "";
    public string RelatedUsage { get => _relatedUsage; set => Set(ref _relatedUsage, value); }
    public Visibility RelatedUsageVisibility => _relatedUsage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;


    Visibility _extras = Visibility.Visible;
    public Visibility Extras { get => _extras; set => Set(ref _extras, value); }

    string _markGlyph = "";
    public string MarkGlyph { get => _markGlyph; set => Set(ref _markGlyph, value); }

    FontFamily _markFont = Display;
    public FontFamily MarkFont { get => _markFont; set => Set(ref _markFont, value); }

    Microsoft.UI.Xaml.Media.Brush _markFill = Green;
    public Microsoft.UI.Xaml.Media.Brush MarkFill { get => _markFill; set => Set(ref _markFill, value); }

    public static WordView Placeholder() => new() { IsPlaceholder = true };

    public static WordView From(WordEntry e, bool focusMode)
    {
        var (pos, meaning) = SplitPos(e.Meaning);
        var pron = e.Pronunciation.Trim();
        if (pron.Length > 0 && !pron.StartsWith('/') && !pron.StartsWith('[')) pron = "/" + pron + "/";
        var forms = e.RelatedForms
            .Where(f => !string.IsNullOrWhiteSpace(f.Form))
            .Select(f => new RelatedForm { Form = f.Form.Trim(), Nature = f.Nature.Trim() })
            .ToList();
        if (forms.Count == 0) forms = RelatedForm.Expand(e.Related, e.RelatedUsage);

        return new WordView
        {
            Model = e,
            Word = e.Word,
            Pos = pos,
            Meaning = meaning,
            Pronunciation = pron,
            Description = e.Description.Trim(),
            RelatedForms = forms,
            Related = string.Join("  ", forms.Select(f => f.Form)),
            Nature = e.Nature,
            RelatedUsage = e.RelatedUsage,
            Extras = focusMode ? Visibility.Collapsed : Visibility.Visible,
            MarkGlyph = e.Mark switch { 0 => Icons.Check, 2 => "!", _ => "?" },
            MarkFont = e.Mark == 0 ? Icon : Display,
            MarkFill = e.Mark switch { 0 => Green, 2 => Orange, _ => Grey },
        };
    }

    static readonly string[] PosTags = ["adj", "adv", "noun", "v", "vt", "vi", "verb", "n", "prep", "conj", "pron", "det", "int", "num", "aux"];

    /// <summary>释义常以 "adj. nice" 这类词性前缀开头，拆出来单独显示在词旁。</summary>
    public static (string Pos, string Meaning) SplitPos(string meaning)
    {
        var m = meaning.Trim();
        var space = m.IndexOf(' ');
        if (space <= 0 || space > 8) return ("", m);

        var head = m[..space].TrimEnd('.');
        if (Array.IndexOf(PosTags, head.ToLowerInvariant()) < 0) return ("", m);

        return (m[..space], m[(space + 1)..].Trim());
    }
}
