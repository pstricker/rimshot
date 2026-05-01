using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DrumApp.Services;

namespace DrumApp.Views;

public partial class TempoView : UserControl
{
    public event Action<int>? BpmChanged;
    public event Action<MetronomeSubdivision>? SubdivisionChanged;

    private bool _suppressBpmEvent;

    public TempoView()
    {
        InitializeComponent();
    }

    public void SetBpm(int bpm)
    {
        _suppressBpmEvent = true;
        BpmControl.Value = bpm;
        _suppressBpmEvent = false;
    }

    private void OnBpmChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressBpmEvent) return;
        BpmChanged?.Invoke((int)(e.NewValue ?? 90));
    }

    private void OnSubdivisionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.IsChecked != true) return;
        var sub = rb.Name switch
        {
            "SubWhole"   => MetronomeSubdivision.Whole,
            "SubHalf"    => MetronomeSubdivision.Half,
            "SubQuarter" => MetronomeSubdivision.Quarter,
            "SubEighth"  => MetronomeSubdivision.Eighth,
            _ => MetronomeSubdivision.Quarter,
        };
        SubdivisionChanged?.Invoke(sub);
    }
}
