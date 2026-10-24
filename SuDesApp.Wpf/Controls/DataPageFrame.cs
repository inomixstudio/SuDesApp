using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Controls;

/// <summary>
/// Shared data-page shell. It owns the common header surface and exposes slots
/// for the page-specific header content and body layout.
/// </summary>
public sealed class DataPageFrame : ContentControl
{
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent), typeof(object), typeof(DataPageFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty PageContentProperty = DependencyProperty.Register(
        nameof(PageContent), typeof(object), typeof(DataPageFrame), new PropertyMetadata(null));

    static DataPageFrame() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DataPageFrame), new FrameworkPropertyMetadata(typeof(DataPageFrame)));

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? PageContent
    {
        get => GetValue(PageContentProperty);
        set => SetValue(PageContentProperty, value);
    }
}
