using Avalonia.Media;

namespace FrostyModManager.Models;

public sealed record LogLine(string Text, IBrush Brush)
{
    public static readonly IBrush InfoBrush = Brushes.Gainsboro;
    public static readonly IBrush WarningBrush = Brushes.Gold;
    public static readonly IBrush ErrorBrush = Brushes.IndianRed;
}
