using System.IO;
using System.Windows;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using Microsoft.Win32;

namespace GBDeliveryCalc.App.Infrastructure;

/// <summary>
/// Applies Light, Dark or Follow Windows. Standard controls use WPF's Fluent theme (Application.ThemeMode); the
/// app's own colours come from Themes/Light.xaml or Themes/Dark.xaml. The choice is saved per Windows user in
/// appearance.txt in the app data folder (not in the database, so restoring a backup doesn't change it).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string FileName = "appearance.txt";
    private readonly string _path;
    private ResourceDictionary? _palette;

    public ThemeService(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppDataPaths.DataDirectory, FileName);
        Current = Load();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Current == AppTheme.FollowWindows)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(Current, save: false));
        };
    }

    public AppTheme Current { get; private set; }

    public void Apply(AppTheme theme) => Apply(theme, save: true);

    private void Apply(AppTheme theme, bool save)
    {
        Current = theme;
        var app = Application.Current;
        if (app is null) return;
        var dark = theme == AppTheme.Dark || (theme == AppTheme.FollowWindows && WindowsUsesDarkTheme());
#pragma warning disable WPF0001 // ThemeMode is marked experimental in .NET 10 but is the supported way to use the Fluent theme.
        app.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
        var palette = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (_palette is not null) app.Resources.MergedDictionaries.Remove(_palette);
        app.Resources.MergedDictionaries.Add(palette);
        _palette = palette;
        ApplySpacing(app);
        if (save) Save(theme);
    }

    /// <summary>The app's spacing for common controls, layered on whatever theme style is current.</summary>
    private static void ApplySpacing(Application app)
    {
        Add(app, typeof(System.Windows.Controls.TextBox), (FrameworkElement.MarginProperty, new Thickness(0, 2, 8, 2)),
            (System.Windows.Controls.Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Add(app, typeof(System.Windows.Controls.PasswordBox), (FrameworkElement.MarginProperty, new Thickness(0, 2, 8, 2)));
        Add(app, typeof(System.Windows.Controls.ComboBox), (FrameworkElement.MarginProperty, new Thickness(0, 2, 8, 2)));
        Add(app, typeof(System.Windows.Controls.Button), (FrameworkElement.MarginProperty, new Thickness(0, 4, 8, 4)),
            (System.Windows.Controls.Control.PaddingProperty, new Thickness(12, 4, 12, 4)), (FrameworkElement.MinWidthProperty, 80d));
        Add(app, typeof(System.Windows.Controls.GroupBox), (FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10)),
            (System.Windows.Controls.Control.PaddingProperty, new Thickness(8)));

        // Selected grid cells use the palette, not the Windows accent colour (a pale accent made white text unreadable).
        var selected = new Trigger { Property = System.Windows.Controls.DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new DynamicResourceExtension("SelectionBackgroundBrush")));
        selected.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new DynamicResourceExtension("SelectionTextBrush")));
        selected.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new DynamicResourceExtension("SelectionBackgroundBrush")));
        Add(app, typeof(System.Windows.Controls.DataGridCell), [selected]);
    }

    private static void Add(Application app, Type type, params (DependencyProperty Property, object Value)[] setters) =>
        Add(app, type, [], setters);

    private static void Add(Application app, Type type, TriggerBase[] triggers, params (DependencyProperty Property, object Value)[] setters)
    {
        app.Resources.Remove(type); // so the lookup below finds the theme's style, not ours
        var style = new Style(type, app.TryFindResource(type) as Style);
        foreach (var (property, value) in setters) style.Setters.Add(new Setter(property, value));
        foreach (var trigger in triggers) style.Triggers.Add(trigger);
        app.Resources[type] = style; // the style is sealed from here on
    }

    /// <summary>Windows "app mode" setting: AppsUseLightTheme = 0 means dark.</summary>
    public static bool WindowsUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    private AppTheme Load()
    {
        try { return File.Exists(_path) && Enum.TryParse<AppTheme>(File.ReadAllText(_path).Trim(), out var t) && Enum.IsDefined(t) ? t : AppTheme.FollowWindows; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return AppTheme.FollowWindows; }
    }

    private void Save(AppTheme theme)
    {
        try { File.WriteAllText(_path, theme.ToString()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the choice still applies for this session */ }
    }
}
