using System.Windows;

namespace Rain;

/// <summary>Attached properties used by the styles in App.xaml.</summary>
public static class Ui
{
    /// <summary>Segoe Fluent Icons glyph shown at the start of an input box.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));

    public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);
}
