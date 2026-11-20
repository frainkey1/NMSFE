using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    private async Task VerifyAccessories()
    {
        string output = Path.Combine(Path.GetDirectoryName(_session.ResourceRoot)!, "Artifacts", "Preview11");
        Directory.CreateDirectory(output);
        try
        {
            SelectNavigation(6);
            RenderSelectedPage();
            var page = _domainPages[6];
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            static IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is T match) yield return match;
                    foreach (var nested in Controls<T>(child)) yield return nested;
                }
            }
            Controls<Button>(page).Single(b => Equals(b.Content, "Accessories"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var records = Controls<ListBox>(page).First();
            for (int record = 0; record < records.Items.Count && !Controls<ComboBox>(page).Any(b => b.Items.Count > 1); record++)
            {
                records.SelectedIndex = record;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            var picker = Controls<ComboBox>(page).FirstOrDefault(b => b.Items.Count > 1)
                ?? throw new Exception("No named accessory choices displayed. " + string.Join(" | ", Controls<TextBlock>(page).Select(t => t.Text)));
            picker.SelectedIndex = 1;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!Controls<TextBox>(page).Any()) throw new Exception("Accessory form missing.");
            foreach (bool dark in new[] { false, true })
            {
                Themes.Apply(dark);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(this);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, dark ? "accessories-dark.png" : "accessories-light.png"));
                encoder.Save(file);
            }
            File.WriteAllText(Path.Combine(output, "accessories-result.txt"), "PASS: named choices displayed, empty slot equipped, light and dark pages rendered. Edits remained in memory.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "accessories-result.txt"), ex.ToString());
        }
        if (_session.SourcePath is { } source) _session.Load(source);
        ClearDomainPageState();
        Close();
    }
}
