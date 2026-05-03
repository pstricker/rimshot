using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Rimshot.Views;

public partial class SoundFontPromptWindow : Window
{
    public SoundFontPromptWindow()
    {
        InitializeComponent();
    }

    private void OnBrowseClicked(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
