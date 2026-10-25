using Wordsbook.Models;

namespace Wordsbook.ViewModels;

public sealed class RelatedRow : NotifyBase
{
    string _text = "";
    public string Text { get => _text; set => Set(ref _text, value); }
}

public sealed class CategoryChip : NotifyBase
{
    public required Category Model { get; init; }
    public string Name => Model.Name;

    bool _on;
    public bool On { get => _on; set => Set(ref _on, value); }
}
