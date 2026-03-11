using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace whisperMeOff.Views;

public partial class RecordingOverlayWindow : Window
{
    private readonly Random _random = new();
    private readonly DispatcherTimer _spectrumTimer;
    private readonly DispatcherTimer _recordingTimer;
    private DateTime _recordingStartTime;
    private double _currentLevel = 0;
    private double _targetLevel = 0;
    
    private readonly List<System.Windows.Shapes.Rectangle> _bars = new();
    private readonly List<double> _barLevels = new();
    private Canvas? _spectrumCanvas;
    private System.Windows.Shapes.Ellipse? _spectrumGlow;
    private System.Windows.Controls.TextBlock? _timerText;
    private const int BarCount = 16;
    
    public RecordingOverlayWindow()
    {
        InitializeComponent();

        // Position at bottom-center of primary display
        var workArea = SystemParameters.WorkArea;
        Left = (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 20;

        ShowInTaskbar = false;
        Topmost = true;

        // Get references to XAML elements after they're loaded
        Loaded += OnLoaded;
        
        // Set up spectrum animation timer (60fps)
        _spectrumTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _spectrumTimer.Tick += OnSpectrumTimerTick;
        
        // Set up recording timer (1fps for elapsed time display)
        _recordingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _recordingTimer.Tick += OnRecordingTimerTick;
        
        // Subscribe to audio level changes
        App.Audio.AudioLevelChanged += OnAudioLevelChanged;
        
        // Unsubscribe when window closes
        Closed += OnWindowClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _spectrumCanvas = (Canvas)FindName("SpectrumCanvas");
        _spectrumGlow = (System.Windows.Shapes.Ellipse)FindName("SpectrumGlow");
        _timerText = (System.Windows.Controls.TextBlock)FindName("TimerText");
        
        // Initialize bars - 16 bars in wider canvas
        double barWidth = 8;
        double barSpacing = 1;
        double totalWidth = BarCount * barWidth + (BarCount - 1) * barSpacing;
        double startX = (150 - totalWidth) / 2;
        
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = 2,
                RadiusX = 3.5,
                RadiusY = 3.5,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            
            Canvas.SetLeft(bar, startX + i * (barWidth + barSpacing));
            Canvas.SetBottom(bar, 2);
            
            _bars.Add(bar);
            _barLevels.Add(0);
            
            if (_spectrumCanvas != null)
            {
                _spectrumCanvas.Children.Add(bar);
            }
        }
        
        _spectrumTimer.Start();
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        // Apply boost multiplier to make the meter more visible
        _targetLevel = Math.Min(1.0, level * 2.5);
    }

    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        if (_spectrumCanvas == null || _bars.Count == 0) return;
        
        // Interpolate toward target
        _currentLevel = _currentLevel + (_targetLevel - _currentLevel) * 0.5;
        
        // Update each bar
        for (int i = 0; i < BarCount; i++)
        {
            // Random variation for dynamic movement
            double variation = 0.3 + _random.NextDouble() * 1.2;
            double targetBarLevel = _currentLevel * variation;
            targetBarLevel = Math.Min(targetBarLevel, 1.0);
            
            // Slower decay - only move toward target if it's higher, otherwise decay slowly
            if (targetBarLevel > _barLevels[i])
            {
                _barLevels[i] = _barLevels[i] + (targetBarLevel - _barLevels[i]) * 0.4;
            }
            else
            {
                // Slower decay when audio drops
                _barLevels[i] = _barLevels[i] - 0.02;
                _barLevels[i] = Math.Max(_barLevels[i], targetBarLevel);
            }
            
            // Calculate bar height
            double height = Math.Max(2, _barLevels[i] * 30);
            
            _bars[i].Height = height;
            Canvas.SetBottom(_bars[i], 2);
            
            // Gradient-like color based on bar height
            System.Windows.Media.Color barColor;
            double relativeHeight = height / 30.0;
            
            if (relativeHeight < 0.35)
            {
                barColor = System.Windows.Media.Color.FromRgb(34, 197, 94); // Green
            }
            else if (relativeHeight < 0.65)
            {
                barColor = System.Windows.Media.Color.FromRgb(234, 179, 8); // Yellow
            }
            else
            {
                barColor = System.Windows.Media.Color.FromRgb(239, 68, 68); // Red
            }
            
            _bars[i].Fill = new SolidColorBrush(barColor);
            
            // Glow effect for bars
            if (height > 8)
            {
                _bars[i].Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = barColor,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };
            }
            else
            {
                _bars[i].Effect = null;
            }
        }
        
        // Update background glow based on overall level
        if (_spectrumGlow != null)
        {
            _spectrumGlow.Opacity = 0.1 + _currentLevel * 0.5;
        }
    }

    private void OnRecordingTimerTick(object? sender, EventArgs e)
    {
        if (_timerText == null) return;
        
        var elapsed = DateTime.Now - _recordingStartTime;
        _timerText.Text = elapsed.ToString(@"mm\:ss");
    }

    public void StartRecordingTimer()
    {
        _recordingStartTime = DateTime.Now;
        if (_timerText != null)
        {
            _timerText.Text = "00:00";
        }
        _recordingTimer.Start();
    }

    public void StopRecordingTimer()
    {
        _recordingTimer.Stop();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _spectrumTimer.Stop();
        _recordingTimer.Stop();
        App.Audio.AudioLevelChanged -= OnAudioLevelChanged;
    }
}
