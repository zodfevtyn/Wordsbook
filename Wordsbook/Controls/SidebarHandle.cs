using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Wordsbook.Controls;

/// <summary>
/// 侧栏分隔条。ProtectedCursor 是 UIElement 的受保护成员，只能在子类构造里设，
/// 所以专门做一个空壳控件，免得回调 Win32 的 WM_SETCURSOR。Border 是 sealed，用 Grid。
/// </summary>
public sealed class SidebarHandle : Grid
{
    public SidebarHandle() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
