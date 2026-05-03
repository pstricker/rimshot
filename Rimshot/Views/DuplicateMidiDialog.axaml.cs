using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Rimshot.Views;

public partial class DuplicateMidiDialog : Window
{
    public DuplicateMidiDialog() : this("This file") { }

    public DuplicateMidiDialog(string existingDisplayName)
    {
        InitializeComponent();
        SubtitleLabel.Text = $"\"{existingDisplayName}\" is already in your library.";
    }

    private void OnOverwriteClicked(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
