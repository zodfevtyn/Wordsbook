using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Wordsbook.Services;

namespace Wordsbook.ViewModels;

public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>可折叠的目录节点，统一提供子树可见性与箭头字形。</summary>
public abstract class ExpandableNode : NotifyBase
{
    bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value))
            {
                Raise(nameof(ChildrenVisibility));
                Raise(nameof(Chevron));
            }
        }
    }

    public Visibility ChildrenVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string Chevron => _isExpanded ? "\uE70D" : "\uE76C";
}

public sealed class DayNode : NotifyBase
{
    public required string Key { get; init; }
    public string Label { get; set; } = "";

    string _count = "";
    public string Count { get => _count; set => Set(ref _count, value); }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class MonthNode : ExpandableNode
{
    public required string Key { get; init; }
    public string Label { get; set; } = "";

    string _stat = "";
    public string Stat { get => _stat; set => Set(ref _stat, value); }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public ObservableCollection<DayNode> Days { get; } = [];
}

public sealed class LangNode : ExpandableNode
{
    public required string Id { get; init; }
    public string Flag { get; set; } = "";
    public string Name { get; set; } = "";

    UIElement? _flag;
    string _flagCode = "";

    /// <summary>矢量小旗，按 ISO 码惰性创建并缓存，避免每次重建目录都生成新可视树。</summary>
    public UIElement FlagVisual => _flag ??= Flags.Create(_flagCode);

    public void SetFlag(string code)
    {
        if (_flagCode == code) return;
        _flagCode = code;
        _flag = null;
        Raise(nameof(FlagVisual));
    }


    string _count = "";
    public string Count { get => _count; set => Set(ref _count, value); }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public ObservableCollection<MonthNode> Months { get; } = [];
}

public sealed class CategoryNode : NotifyBase
{
    public required string Id { get; init; }

    string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    string _count = "";
    public string Count { get => _count; set => Set(ref _count, value); }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class CategoryGroupNode : ExpandableNode
{
    public required string Id { get; init; }
    public string Flag { get; set; } = "";
    public string Name { get; set; } = "";

    UIElement? _flag;
    string _flagCode = "";

    /// <summary>矢量小旗，按 ISO 码惰性创建并缓存，避免每次重建目录都生成新可视树。</summary>
    public UIElement FlagVisual => _flag ??= Flags.Create(_flagCode);

    public void SetFlag(string code)
    {
        if (_flagCode == code) return;
        _flagCode = code;
        _flag = null;
        Raise(nameof(FlagVisual));
    }


    public ObservableCollection<CategoryNode> Items { get; } = [];
}
