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
            : ["#FAFAFA", "#F0F0F0", "#FFFFFF", "#FFFFFF", "#E0E0E0", "#242424", "#616161", "#0F6CBD", "#FFFFFF", "#EBF3FC"];
        string[] dark = claude
            ? ["#242320", "#1D1C19", "#2C2B27", "#24231F", "#403D36", "#EEEAE2", "#ADA69B", "#CE967B", "#241C17", "#35322C"]
            : ["#1F1F1F", "#141414", "#292929", "#242424", "#424242", "#FFFFFF", "#ADADAD", "#479EF5", "#0A1B2C", "#0A2E4A"];
        var app = Application.Current!;
        SetPalette(app, ThemeVariant.Light, light);
        SetPalette(app, ThemeVariant.Dark, dark);
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.Palettes[ThemeVariant.Light].Accent = Color.Parse(light[7]);
            fluent.Palettes[ThemeVariant.Dark].Accent = Color.Parse(dark[7]);
        }
        app.Resources["CardPadding"] = new Thickness(claude ? 24 : 20);
        app.Resources["CardRadius"] = new CornerRadius(claude ? 10 : 8);
        app.Resources["ControlRadius"] = new CornerRadius(claude ? 6 : 4);
        app.Resources["CardShadow"] = claude ? default(BoxShadows) : BoxShadows.Parse("0 1 2 0 #10000000");
        app.Resources["ControlPadding"] = claude ? new Thickness(16, 10) : new Thickness(12, 8);
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
