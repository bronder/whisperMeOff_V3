using System.Windows;

namespace whisperMeOff.Views;

public partial class RecordingOverlayWindow : Window
{
    public RecordingOverlayWindow()
    {
        InitializeComponent();

        // Position at bottom-center of primary display using system parameters
        var workArea = SystemParameters.WorkArea;
        Left = (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 20;

        // Make sure it doesn't steal focus
        ShowInTaskbar = false;
        Topmost = true;
    }
}
