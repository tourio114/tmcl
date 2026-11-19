using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Tmcl.App;

internal static class Appearance
{
    private static readonly string[] Keys =
    ["PageBrush", "SidebarBrush", "SurfaceBrush", "InsetBrush", "LineBrush", "InkBrush", "MutedBrush", "AccentBrush", "AccentInkBrush", "SelectedBrush"];

    public static void Apply(string style)
    {
        bool claude = style == "Claude";
        string[] light = claude
            ? ["#F8F7F4", "#EFEEE8", "#FFFFFF", "#F3F1EB", "#E5E1D9", "#302D29", "#77716A", "#A4573D", "#FFFFFF", "#E4DFD5"]
            : ["#F3F3F3", "#F3F3F3", "#FFFFFF", "#FFFFFF", "#E5E5E5", "#1A1A1A", "#606060", "#0067C0", "#FFFFFF", "#E6E6E6"];
        string[] dark = claude
            ? ["#242320", "#1D1C19", "#2C2B27", "#24231F", "#403D36", "#EEEAE2", "#ADA69B", "#CE967B", "#241C17", "#35322C"]
            : ["#202020", "#202020", "#2B2B2B", "#1F1F1F", "#3D3D3D", "#F5F5F5", "#BDBDBD", "#60CDFF", "#00324C", "#383838"];
        var app = Application.Current!;
        SetPalette(app, ThemeVariant.Light, light);
        SetPalette(app, ThemeVariant.Dark, dark);
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.Palettes[ThemeVariant.Light].Accent = Color.Parse(light[7]);
            fluent.Palettes[ThemeVariant.Dark].Accent = Color.Parse(dark[7]);
        }
        app.Resources["CardPadding"] = new Thickness(claude ? 24 : 20);
        app.Resources["CardRadius"] = new CornerRadius(claude ? 10 : 6);
        app.Resources["ControlPadding"] = claude ? new Thickness(16, 10) : new Thickness(14, 8);
        app.Resources["ControlHeight"] = claude ? 40d : 36d;
        app.Resources["PageHeaderMargin"] = claude ? new Thickness(40, 32, 40, 24) : new Thickness(32, 28, 32, 24);
        app.Resources["PageContentMargin"] = claude ? new Thickness(40, 0, 40, 32) : new Thickness(32, 0, 32, 24);
        app.Resources["FooterPadding"] = claude ? new Thickness(40, 16, 40, 20) : new Thickness(32, 12, 32, 16);
    }

    private static void SetPalette(Application app, ThemeVariant variant, string[] colors)
    {
        var dictionary = new Avalonia.Controls.ResourceDictionary();
        for (int i = 0; i < Keys.Length; i++) dictionary[Keys[i]] = new SolidColorBrush(Color.Parse(colors[i]));
        app.Resources.ThemeDictionaries[variant] = dictionary;
    }
}
