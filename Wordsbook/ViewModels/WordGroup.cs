using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;

namespace Wordsbook.ViewModels;

/// <summary>按日期分组的单词段。Day 作用域只有一段且标题为空，不再重复显示日期。</summary>
public sealed class WordGroup : ObservableCollection<WordView>
{
    public WordGroup(string title, IEnumerable<WordView> items) : base(items) => Title = title;

    public string Title { get; }
    public Visibility HeaderVisibility => Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
