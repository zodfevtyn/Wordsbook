using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Wordsbook.Models;

namespace Wordsbook.ViewModels;

/// <summary>卡片里可以单独关掉的字段。专注模式之外，用户还能用眼睛菜单逐个隐藏。</summary>
public readonly record struct CardFields(
    bool Nature,
    bool Pos,
    bool Meaning,
    bool Pronunciation,
    bool Variants,
    bool Description,
    bool Mark)
{
    public static readonly CardFields All = new(true, true, true, true, true, true, true);

    /// <summary>细节区是否还有东西可显示：全关掉时整块直接收起，不留一段空白。</summary>
    public bool AnyDetail => Meaning || Pronunciation || Variants || Description || Mark;
}

/// <summary>右侧单词卡片 / 列表行的视图模型，也承载末尾的“+”占位块。</summary>
public sealed class WordView : NotifyBase
{
    static readonly SolidColorBrush Green = Hex("#34C759");
    static readonly SolidColorBrush Orange = Hex("#FF9F0A");
    static readonly SolidColorBrush Grey = Hex("#98989D");

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

    string _description = "";
    public string Description { get => _description; set => Set(ref _description, value); }

    IReadOnlyList<RelatedForm> _relatedForms = [];
    public IReadOnlyList<RelatedForm> RelatedForms { get => _relatedForms; set => Set(ref _relatedForms, value); }

    string _related = "";
    public string Related { get => _related; set => Set(ref _related, value); }

    string _nature = "";
    public string Nature { get => _nature; set => Set(ref _nature, value); }

    string _relatedUsage = "";
    public string RelatedUsage { get => _relatedUsage; set => Set(ref _relatedUsage, value); }

    string _markGlyph = "";
    public string MarkGlyph { get => _markGlyph; set => Set(ref _markGlyph, value); }

    Brush _markFill = Green;
    public Brush MarkFill { get => _markFill; set => Set(ref _markFill, value); }

    // 可见性在 From() 里一次算好：改字段开关或专注模式都会重建整批视图。
    bool _focus;
    CardFields _fields = CardFields.All;

    static Visibility Vis(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    bool HasNature => _nature.Length > 0;
    bool HasPos => _pos.Length > 0;
    bool HasMeaning => !string.IsNullOrWhiteSpace(_meaning);
    bool HasPron => _pronunciation.Length > 0;

    /// <summary>词性小标跟着单词本体走，专注模式下也留着，它算单词的一部分。</summary>
    public Visibility NatureVisibility => Vis(_fields.Nature && HasNature);

    public Visibility PosVisibility => Vis(!_focus && _fields.Pos && HasPos);

    public Visibility MetaVisibility => Vis(!_focus && ((_fields.Nature && HasNature) || (_fields.Pos && HasPos)));

    public Visibility MeaningVisibility => Vis(!_focus && _fields.Meaning && HasMeaning);

    public Visibility PronunciationVisibility => Vis(!_focus && _fields.Pronunciation && HasPron);

    public Visibility RelatedVisibility => Vis(!_focus && _fields.Variants && _related.Length > 0);

    public Visibility RelatedUsageVisibility => Vis(_relatedUsage.Length > 0);

    public Visibility RelatedFormsVisibility => Vis(!_focus && _fields.Variants && _relatedForms.Count > 0);

    public Visibility DescriptionVisibility =>
        Vis(!_focus && _fields.Description && !string.IsNullOrWhiteSpace(_description));

    public Visibility MarkVisibility => Vis(!_focus && _fields.Mark);

    /// <summary>列表行里的记忆状态圆点不吃眼睛菜单的开关：行只留词头和这个状态点。</summary>
    public Visibility RowMarkVisibility => Vis(!_focus);

    public Visibility DetailVisibility => Vis(!_focus && _fields.AnyDetail);

    public static WordView Placeholder() => new() { IsPlaceholder = true };

    public static WordView From(WordEntry e, bool focusMode, CardFields fields)
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
            _focus = focusMode,
            _fields = fields,
            MarkGlyph = e.Mark switch { 0 => Icons.Check, 2 => Icons.Exclamation, _ => Icons.Question },
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
