namespace Wordsbook;

/// <summary>
/// 图标码位：Apple SF Pro Display 自带的 SF Symbols，落在 16 平面私用区（本机 2924 个）。
/// 每个码位都用字形图核对过：SF Pro 的私用区不是按字母序排的，只能逐个看形状确认。
/// XAML 里的同码位字面量（&#x100188; 这种）要和这里保持一致。
/// SF Pro 不可随包分发，换机没有该字体会退化成豆腐块。
/// </summary>
public static class Icons
{
    /// <summary>代码里现建 FontIcon 时用，XAML 统一走 AppIconFont。</summary>
    public static readonly Microsoft.UI.Xaml.Media.FontFamily Font = new("SF Pro Display");

    public const string ChevronDown = "\U00100188";
    public const string ChevronLeft = "\U00100189";
    public const string ChevronRight = "\U00100BFB";
    public const string Plus = "\U0010017C";
    public const string Minus = "\U0010017D";
    public const string Tag = "\U001002E1";
    public const string TagFill = "\U001002E2";
    public const string Settings = "\U001008CB";
    public const string List = "\U001002F2";
    public const string Grid = "\U001006E6";
    public const string Search = "\U001002AB";
    public const string Eye = "\U001002EE";
    public const string EyeSlash = "\U001002F0";
    public const string Check = "\U00100185";
    public const string Exclamation = "\U0010014E";
    public const string Question = "\U0010014D";
    public const string Cross = "\U00100184";
    public const string Edit = "\U0010020E";
    public const string Delete = "\U00100211";
    public const string Calendar = "\U00100249";
    public const string Expand = "\U00100C76";
    public const string Shrink = "\U0010014B";
    public const string AdaptiveOff = "\U001001F4";
    public const string AdaptiveOn = "\U00100685";
    public const string Sidebar = "\U00100C31";

    // 悬浮单词卡：叠放的两个圆角窗口 / 播放 / 暂停（字形已对照字体图确认）
    public const string ScreenCard = "\U001003E7";
    public const string Play = "\U00100284";
    public const string Pause = "\U00100285";
    public const string Dock = "\U00100C32";
}
