using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Rimshot.Views;

public partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/pstricker/rimshot";
    private const string IssuesNewUrl = "https://github.com/pstricker/rimshot/issues/new/choose";
    private const string LicenseUrl = "https://github.com/pstricker/rimshot/blob/main/LICENSE";

    public AboutWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {MainWindow.AppVersion}";
    }

    private void OnRepoClicked(object? sender, RoutedEventArgs e) => Open(RepoUrl);
    private void OnReportBugClicked(object? sender, RoutedEventArgs e) => Open(IssuesNewUrl);
    private void OnLicenseClicked(object? sender, RoutedEventArgs e) => Open(LicenseUrl);
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
