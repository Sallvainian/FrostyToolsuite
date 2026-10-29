using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FrostyModManager.Views;

/// <summary>
/// Small modal Yes/No and text prompts in the style of Frosty's message boxes.
/// </summary>
public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(Window inOwner, string inTitle, string inMessage)
    {
        bool result = false;
        Window dialog = CreateWindow(inTitle);

        Button yes = new() { Content = "Yes", MinWidth = 80, Classes = { "accent" } };
        Button no = new() { Content = "No", MinWidth = 80 };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = Layout(new TextBlock { Text = inMessage, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, yes, no);
        await dialog.ShowDialog(inOwner);
        return result;
    }

    public static async Task<string?> PromptAsync(Window inOwner, string inTitle, string inLabel)
    {
        string? result = null;
        Window dialog = CreateWindow(inTitle);

        TextBox input = new() { MinWidth = 300 };
        Button ok = new() { Content = "OK", MinWidth = 80, Classes = { "accent" }, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => { result = input.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        StackPanel body = new() { Spacing = 8, Children = { new TextBlock { Text = inLabel }, input } };
        dialog.Content = Layout(body, ok, cancel);
        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(inOwner);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static Window CreateWindow(string inTitle) => new()
    {
        Title = inTitle,
        SizeToContent = SizeToContent.WidthAndHeight,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false
    };

    private static Control Layout(Control inBody, Button inPrimary, Button inSecondary) => new DockPanel
    {
        Margin = new Avalonia.Thickness(20, 16),
        Children =
        {
            new StackPanel
            {
                [DockPanel.DockProperty] = Dock.Bottom,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Avalonia.Thickness(0, 16, 0, 0),
                Children = { inPrimary, inSecondary }
            },
            inBody
        }
    };
}
