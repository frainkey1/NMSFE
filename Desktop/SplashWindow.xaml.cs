using System.Windows;
using System.Windows.Media.Animation;

namespace NMSE.Next.Desktop;

public partial class SplashWindow : Window
{
    internal const double MinimumDisplaySeconds = 4;

    public SplashWindow()
    {
        InitializeComponent();
        ContentRendered += (_, _) =>
        {
            var animation = new DoubleAnimation(0, 140, TimeSpan.FromSeconds(MinimumDisplaySeconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressFill.BeginAnimation(WidthProperty, animation);
        };
    }
}
