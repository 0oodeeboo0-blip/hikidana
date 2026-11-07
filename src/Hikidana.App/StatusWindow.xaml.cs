using System.Reflection;
using System.Windows;
using System.Windows.Shapes;

namespace Hikidana.App;

/// <summary>段階 0 の仮の画面。自動ビルドと起動を確かめるためのもので、段階 1 で置き換える。</summary>
public partial class StatusWindow : Window
{
    public StatusWindow()
    {
        InitializeComponent();
        Logo.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
        StageText.Text = "準備中です（段階 0: 自動ビルドの確かめ）";
        var version = typeof(StatusWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        VersionText.Text = $"版 {version}";
    }
}
