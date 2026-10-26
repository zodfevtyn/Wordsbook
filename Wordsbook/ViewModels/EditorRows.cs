using Microsoft.UI.Xaml;
using Wordsbook.Models;

namespace Wordsbook.ViewModels;

public sealed class RelatedRow : NotifyBase
{
    string _form = "";
    public string Form { get => _form; set => Set(ref _form, value); }

    string _nature = "";
    public string Nature { get => _nature; set => Set(ref _nature, value); }
}

public sealed class CategoryChip : NotifyBase
{
    public Category? Model { get; init; }
    public bool IsAdd { get; init; }
    public string Name => Model?.Name ?? "";

    public Visibility ChipVisibility => IsAdd ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AddVisibility => IsAdd ? Visibility.Visible : Visibility.Collapsed;

    bool _on;
    public bool On { get => _on; set => Set(ref _on, value); }

    public static CategoryChip Add() => new() { IsAdd = true };
}
