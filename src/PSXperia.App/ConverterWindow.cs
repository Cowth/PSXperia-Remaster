using Microsoft.Win32;
using PSXperia.Engine;
using PSXperia.Library;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PSXperia.App;

/// <summary>First usable converter workspace: local runtime selection/validation and original-format payload generation.</summary>
public sealed class ConverterWindow : UserControl
{
    private readonly IReadOnlyList<GameEntry> _games;
    private readonly RuntimeProfileStore _profiles = new();
    private readonly RuntimeValidator _validator = new();
    private readonly ComboBox _game = new();
    private readonly TextBox _apk = new();
    private readonly TextBox _zpak = new();
    private readonly TextBox _titleId = new();
    private readonly TextBox _output = new();
    private readonly TextBlock _runtimeStatus = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _validateButton = new();
    private readonly Button _convertButton = new();
    private RuntimeSelection? _runtime;
    private bool _runtimeValid;
    private readonly Func<ConvertedGameRecord, Task>? _onConverted;

    public ConverterWindow(IReadOnlyList<GameEntry> games, Func<ConvertedGameRecord, Task>? onConverted = null)
    {
        _games = games;
        _onConverted = onConverted;
        Background = Brushes.Transparent;
        Foreground = Brush("#F7F4FF");
        Content = BuildLayout();
        Loaded += async (_, _) => await LoadRuntimeAsync();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(30, 26, 30, 28), LastChildFill = true };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var back = new Button { Content = "←  Dashboard", Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => { if (Window.GetWindow(this) is MainWindow main) main.ShowDashboard(); };
        DockPanel.SetDock(back, Dock.Left); heading.Children.Add(back);
        var headingText = new StackPanel();
        headingText.Children.Add(new TextBlock { Text = "Converter", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brush("#FFFFFF") });
        headingText.Children.Add(new TextBlock { Text = "Select a game, validate your own Sony runtime, and build the original PSXperia APK plus matching game data.", Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#D0C7DF"), TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(headingText);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel(); scroll.Content = body; root.Children.Add(scroll);
        body.Children.Add(Section("1 · Select game", "Choose an imported CUE sheet, BIN, IMG, or ISO from your library."));
        _game.ItemsSource = _games; _game.DisplayMemberPath = nameof(GameEntry.Title); _game.SelectionChanged += (_, _) => UpdateSuggestedTitleId();
        if (_games.Count > 0) _game.SelectedIndex = 0;
        body.Children.Add(Field("Game library", _game));
        _titleId.Text = _games.Count > 0 ? SuggestedTitleId(_games[0]) : "";
        body.Children.Add(Field("Output title ID", _titleId, "Unique package identifier, e.g. SLUS00128_1"));

        body.Children.Add(Section("2 · Runtime Manager", "Choose the Crash Bandicoot APK and matching standalone ZPAK from your own Xperia runtime files. Files remain on this PC."));
        _apk.IsReadOnly = true; _zpak.IsReadOnly = true; _output.IsReadOnly = true;
        body.Children.Add(FileField("Crash Bandicoot APK", _apk, BrowseApk));
        body.Children.Add(FileField("Matching runtime ZPAK", _zpak, BrowseZpak));
        var runtimeButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        _validateButton.Content = "Validate runtime"; StyleButton(_validateButton, true); _validateButton.Click += async (_, _) => await ValidateRuntimeAsync();
        runtimeButtons.Children.Add(_validateButton); body.Children.Add(runtimeButtons);
        _runtimeStatus.Text = "Runtime not selected"; _runtimeStatus.Foreground = Brush("#B8ACD2"); _runtimeStatus.TextWrapping = TextWrapping.Wrap; _runtimeStatus.Margin = new Thickness(0, 6, 0, 14); body.Children.Add(_runtimeStatus);

        body.Children.Add(Section("3 · Output", "Choose a destination folder. The original converter will create a game-specific APK and its matching Android/data folder. Java is required for the original compatibility engine."));
        body.Children.Add(FileField("Output folder", _output, BrowseOutput));
        _convertButton.Content = "Convert game"; StyleButton(_convertButton, true); _convertButton.Margin = new Thickness(0, 16, 0, 8); _convertButton.IsEnabled = false; _convertButton.Click += async (_, _) => await ConvertAsync(); body.Children.Add(_convertButton);
        _progress.Minimum = 0; _progress.Maximum = 100; _progress.Height = 14; _progress.Margin = new Thickness(0, 6, 0, 8); body.Children.Add(_progress);
        _status.Text = "Ready when your game and runtime are selected."; _status.Foreground = Brush("#D0C7DF"); _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status);
        return root;
    }

    private static Border Section(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F7F4FF") });
        panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = Brush("#B8ACD2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new Border { Child = panel, Padding = new Thickness(0, 0, 0, 2) };
    }

    private static Border Field(string label, FrameworkElement control, string? hint = null)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#D8CFF0"), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        control.MinHeight = 34; control.Margin = new Thickness(0, 0, 0, 0);
        if (control is TextBox tb) { tb.Background = Brush("#201827"); tb.Foreground = Brush("#FFFFFF"); tb.CaretBrush = Brushes.White; tb.BorderBrush = Brush("#65517F"); tb.Padding = new Thickness(10, 7, 10, 7); }
        if (control is ComboBox cb) ApplyDarkCombo(cb);
        stack.Children.Add(control);
        if (!string.IsNullOrWhiteSpace(hint)) stack.Children.Add(new TextBlock { Text = hint, Foreground = Brush("#9488AA"), FontSize = 10, Margin = new Thickness(0, 4, 0, 0) });
        return new Border { Child = stack, Padding = new Thickness(14), Background = Brush("#1C1728"), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 8) };
    }

    private static void ApplyDarkCombo(ComboBox combo)
    {
        combo.Background = Brush("#241B35"); combo.Foreground = Brush("#FFFFFF"); combo.BorderBrush = Brush("#755A9D");
        combo.Padding = new Thickness(9, 6, 9, 6); combo.FontSize = 12; combo.MinHeight = 36;
        combo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
        {
            Setters = { new Setter(Control.BackgroundProperty, Brush("#241B35")), new Setter(Control.ForegroundProperty, Brushes.White), new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)), new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        };
    }

    private Border FileField(string label, TextBox box, RoutedEventHandler browse)
    {
        box.Background = Brush("#201827"); box.Foreground = Brushes.White; box.CaretBrush = Brushes.White;
        box.BorderBrush = Brush("#65517F"); box.Padding = new Thickness(10, 7, 10, 7);
        box.FontSize = 12; box.VerticalContentAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        var button = new Button { Content = "Browse…", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        button.Click += browse; DockPanel.SetDock(button, Dock.Right); row.Children.Add(button); row.Children.Add(box);
        return Field(label, row);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Padding = new Thickness(16, 10, 16, 10); button.MinHeight = 38; button.FontWeight = FontWeights.SemiBold;
        button.Foreground = Brushes.White; button.Background = primary ? Brush("#7450D8") : Brush("#302642"); button.BorderBrush = Brush("#6C54A0");
    }
    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private void BrowseApk(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select your Crash Bandicoot APK", Filter = "Android package (*.apk)|*.apk|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { _apk.Text = dialog.FileName; _runtimeValid = false; _convertButton.IsEnabled = false; }
    }
    private void BrowseZpak(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select the matching standalone runtime ZPAK", Filter = "Sony ZPAK (*.zpak)|*.zpak|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { _zpak.Text = dialog.FileName; _runtimeValid = false; _convertButton.IsEnabled = false; }
    }
    private void BrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose conversion output folder", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _output.Text = dialog.FolderName;
    }

    private async Task LoadRuntimeAsync()
    {
        _runtime = await _profiles.LoadAsync();
        if (_runtime is null) return;
        _apk.Text = _runtime.ApkPath; _zpak.Text = _runtime.ZpakPath;
        await ValidateRuntimeAsync();
    }

    private async Task ValidateRuntimeAsync()
    {
        _runtimeValid = false; _convertButton.IsEnabled = false;
        if (string.IsNullOrWhiteSpace(_apk.Text) || string.IsNullOrWhiteSpace(_zpak.Text))
        { _runtimeStatus.Text = "Select both the APK and matching ZPAK first."; return; }
        _validateButton.IsEnabled = false; _runtimeStatus.Text = "Validating APK, emulator libraries, ZPAK, and metadata…";
        try
        {
            var selection = new RuntimeSelection(Path.GetFullPath(_apk.Text), Path.GetFullPath(_zpak.Text));
            var result = await _validator.ValidateAsync(selection);
            _runtime = selection;
            if (!result.IsValid)
            {
                _runtimeStatus.Text = "Runtime validation failed:\n• " + string.Join("\n• ", result.Errors);
                _runtimeStatus.Foreground = Brush("#FF9CA8"); return;
            }
            await _profiles.SaveAsync(selection);
            _runtimeValid = true; _convertButton.IsEnabled = _games.Count > 0;
            _runtimeStatus.Text = $"Runtime validated ✓  {result.TitleId ?? "Sony runtime"}  |  Version: {result.RuntimeVersion ?? "unknown"}  |  APK CRC32: {result.ApkCrc32}\n" + string.Join("\n", result.Checks.Concat(result.Warnings.Select(w => "Warning: " + w)));
            _runtimeStatus.Foreground = Brush("#9FE0B5");
        }
        catch (Exception ex) { _runtimeStatus.Text = "Runtime validation error: " + ex.Message; _runtimeStatus.Foreground = Brush("#FF9CA8"); }
        finally { _validateButton.IsEnabled = true; }
    }

    private void UpdateSuggestedTitleId()
    {
        if (_game.SelectedItem is GameEntry selected && string.IsNullOrWhiteSpace(_titleId.Text)) _titleId.Text = SuggestedTitleId(selected);
        _convertButton.IsEnabled = _runtimeValid && _game.SelectedItem is not null;
    }
    private static string SuggestedTitleId(GameEntry game)
    {
        var serial = game.DiscSerial ?? game.TitleId;
        if (string.IsNullOrWhiteSpace(serial)) return "";
        var clean = new string(serial.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length > 0 ? clean + "_1" : "";
    }

    private async Task ConvertAsync()
    {
        if (_game.SelectedItem is not GameEntry selected || _runtime is null || !_runtimeValid) return;
        if (string.IsNullOrWhiteSpace(_output.Text) || !Directory.Exists(_output.Text))
        { MessageBox.Show(Window.GetWindow(this), "Choose an existing output folder first.", "Output folder", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var titleId = _titleId.Text.Trim();
        if (string.IsNullOrWhiteSpace(titleId) || titleId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || titleId.Contains('/') || titleId.Contains('\\'))
        { MessageBox.Show(Window.GetWindow(this), "Enter a valid unique title ID. Avoid slashes and filename-invalid characters.", "Title ID", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        _convertButton.IsEnabled = false; _validateButton.IsEnabled = false; _progress.Value = 0;
        var progress = new Progress<ConversionProgress>(p => { _progress.Value = p.Percent; _status.Text = $"{p.Percent}% · {p.Stage}: {p.Message}"; });
        try
        {
            var engine = new OriginalPsxperiaCliEngine(_validator);
            await engine.ConvertAsync(new ConversionRequest(selected.ImagePath, _output.Text, titleId, selected.Title, selected.CoverPath, _runtime), progress);
            var outputFolder = Path.Combine(Path.GetFullPath(_output.Text), "PSXperia-" + titleId);
            var apkPath = Path.Combine(outputFolder, $"com.sony.playstation.{titleId}.apk");
            var dataPath = Path.Combine(outputFolder, "data", $"com.sony.playstation.{titleId}");
            if (!File.Exists(apkPath) || !Directory.Exists(dataPath))
                throw new InvalidDataException("The converter returned, but the expected installable APK or game data folder is missing. The output has not been added to your converted-games shelf.");
            if (_onConverted is not null)
                await _onConverted(new ConvertedGameRecord(Guid.NewGuid(), selected.Title, titleId, apkPath, dataPath, outputFolder, selected.CoverPath, DateTime.UtcNow));
            _status.Text = $"✓ Installable APK and matching game data created successfully.\nOutput: {outputFolder}\nAdded to your Converted Games shelf.";
            _status.Foreground = Brush("#9FE0B5");
        }
        catch (Exception ex)
        {
            _status.Text = "Conversion stopped: " + ex.Message;
            MessageBox.Show(Window.GetWindow(this), ex.ToString(), "Conversion error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _validateButton.IsEnabled = true; _convertButton.IsEnabled = _runtimeValid && _game.SelectedItem is not null; }
    }
}
