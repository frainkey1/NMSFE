using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using NMSE.Data;

namespace NMSE.Next.Desktop;

/// <summary>
/// Applies the existing UI string tables to ordinary WPF text properties. Bound
/// fields are skipped so save data and user-entered text stay intact.
/// </summary>
internal static class UiLocalizer
{
    private sealed class PropertyState(DependencyProperty property, string original)
    {
        internal DependencyProperty Property { get; } = property;
        internal string Original { get; set; } = original;
        internal string LastApplied { get; set; } = original;
        internal bool Applying { get; set; }
    }

    private static readonly ConditionalWeakTable<DependencyObject, List<PropertyState>> States = new();
    private static bool _registered;

    internal static void Register()
    {
        if (_registered) return;
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnElementLoaded),
            true);
        _registered = true;
    }

    internal static void RefreshAll()
    {
        if (Application.Current is not { } app) return;
        foreach (Window window in app.Windows)
            RefreshTree(window);
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element)
            RefreshTree(element);
    }

    private static void RefreshTree(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            RefreshNode(current);
            foreach (var child in Children(current)) pending.Push(child);
        }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        if (parent is TextBlock textBlock)
            foreach (Inline inline in textBlock.Inlines)
                yield return inline;

        int visualCount = 0;
        try { visualCount = VisualTreeHelper.GetChildrenCount(parent); }
        catch (InvalidOperationException) { }
        for (int i = 0; i < visualCount; i++)
            if (VisualTreeHelper.GetChild(parent, i) is DependencyObject visualChild)
                yield return visualChild;

        IEnumerable logicalChildren;
        try { logicalChildren = LogicalTreeHelper.GetChildren(parent); }
        catch (InvalidOperationException) { yield break; }
        foreach (object? child in logicalChildren)
            if (child is DependencyObject logicalChild)
                yield return logicalChild;
    }

    private static void RefreshNode(DependencyObject target)
    {
        if (target is DataGrid dataGrid)
            foreach (DataGridColumn column in dataGrid.Columns)
                ObserveColumnHeader(column, column.Header, value => column.Header = value);
        if (target is ListView { View: GridView gridView })
            foreach (GridViewColumn column in gridView.Columns)
                ObserveColumnHeader(column, column.Header, value => column.Header = value);
        if (target is Window)
            Observe(target, Window.TitleProperty);
        if (target is TextBlock)
            Observe(target, TextBlock.TextProperty);
        if (target is AccessText)
            Observe(target, AccessText.TextProperty);
        if (target is Run)
            Observe(target, Run.TextProperty);
        if (target is ContentControl && target is not TextBox)
            Observe(target, ContentControl.ContentProperty);
        if (target is HeaderedContentControl)
            Observe(target, HeaderedContentControl.HeaderProperty);
        else if (target is HeaderedItemsControl)
            Observe(target, HeaderedItemsControl.HeaderProperty);
        if (target is FrameworkElement)
            Observe(target, FrameworkElement.ToolTipProperty);
    }

    private sealed class ColumnHeaderState(string original)
    {
        internal string Original { get; set; } = original;
        internal string LastApplied { get; set; } = original;
    }

    private static readonly ConditionalWeakTable<object, ColumnHeaderState> ColumnHeaders = new();

    private static void ObserveColumnHeader(object column, object? header, Action<string> setHeader)
    {
        if (header is not string current) return;
        if (!ColumnHeaders.TryGetValue(column, out var state))
        {
            state = new ColumnHeaderState(current);
            ColumnHeaders.Add(column, state);
        }
        else if (!string.Equals(current, state.LastApplied, StringComparison.Ordinal))
        {
            state.Original = current;
        }

        string localized = UiStrings.LocalizeText(state.Original, column.GetType().Name + ".Header");
        state.LastApplied = localized;
        if (!string.Equals(current, localized, StringComparison.Ordinal)) setHeader(localized);
    }

    private static void Observe(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(target, property)) return;
        var states = States.GetOrCreateValue(target);
        var state = states.FirstOrDefault(item => item.Property == property);
        if (state is null)
        {
            if (target.GetValue(property) is not string original) return;
            state = new PropertyState(property, original);
            states.Add(state);
            DependencyPropertyDescriptor.FromProperty(property, target.GetType())?
                .AddValueChanged(target, (_, _) => OnPropertyChanged(target, state));
        }
        Apply(target, state);
    }

    private static void OnPropertyChanged(DependencyObject target, PropertyState state)
    {
        if (state.Applying || BindingOperations.IsDataBound(target, state.Property)) return;
        if (target.GetValue(state.Property) is not string current) return;
        if (!string.Equals(current, state.LastApplied, StringComparison.Ordinal))
            state.Original = current;
        Apply(target, state);
    }

    private static void Apply(DependencyObject target, PropertyState state)
    {
        string localized = UiStrings.LocalizeText(state.Original, target.GetType().Name + "." + state.Property.Name);
        state.LastApplied = localized;
        if (localized == target.GetValue(state.Property) as string) return;
        state.Applying = true;
        try { target.SetValue(state.Property, localized); }
        finally { state.Applying = false; }
    }
}
