using Microsoft.Win32;
using PSXperia.Engine;
using PSXperia.Library;
using PSXperia.Device;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Media;
using System.Diagnostics;

namespace PSXperia.App;

public partial class MainWindow : Window
{
    private readonly GameLibraryStore _library = new();
    private readonly ObservableCollection<GameEntry> _games = new();
    private readonly LibretroArtworkService _artwork = new();
    private AdbXperiaDeviceService? _adbService;
    private XperiaDevice? _connectedXperia;
    private readonly AppStateStore _stateStore = new();
    private List<ConvertedGameRecord> _convertedGames = new();
    private List<CompatibilityRecord> _compatibility = new();
    private UserSettings _settings = new();
    private readonly TranslateTransform _waveMotion = new(-18, 0);
    private readonly TranslateTransform _bubbleMotion = new(0, 0);
    private readonly MediaPlayer _backgroundPlayer = new();
    private bool _musicOpened;

    public MainWindow()
    {
        InitializeComponent();
        var waveTransforms = new TransformGroup();
        waveTransforms.Children.Add(new ScaleTransform(1.10, 1.10));
        waveTransforms.Children.Add(_waveMotion);
        WaveBackground.RenderTransform = waveTransforms;
        BubbleGlow.RenderTransform = _bubbleMotion;
        GamesList.ItemsSource = _games;
        Closed += (_, _) => { _backgroundPlayer.Stop(); _backgroundPlayer.Close(); };
        Loaded += async (_, _) =>
        {
            await RefreshLibraryAsync();
            await LoadAppStateAsync();
            ApplyWaveSettings();
            ConfigureBackgroundMusic();
        };
    }


    private void OpenConverter_Click(object sender, RoutedEventArgs e) => ShowConverter();
    private void DashboardNav_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void LibraryNav_Click(object sender, RoutedEventArgs e) => ShowConvertedLibrary();
    private void CompatibilityNav_Click(object sender, RoutedEventArgs e) => ShowCompatibility();
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void DeviceNav_Click(object sender, RoutedEventArgs e) => ShowDevicePage();

    private async Task LoadAppStateAsync()
    {
        try
        {
            _convertedGames = await _stateStore.LoadConvertedAsync();
            _compatibility = await _stateStore.LoadCompatibilityAsync();
            _settings = await _stateStore.LoadSettingsAsync();
            UpdateLibraryDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Some PSXperia preferences could not be loaded. The app will use safe defaults.\n{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            _settings = new UserSettings();
        }
    }

    private async Task RecordConvertedAsync(ConvertedGameRecord game)
    {
        _convertedGames.RemoveAll(x => string.Equals(x.TitleId, game.TitleId, StringComparison.OrdinalIgnoreCase));
        _convertedGames.Insert(0, game);
        await _stateStore.SaveConvertedAsync(_convertedGames);
        UpdateLibraryDisplay();
    }

    private void ShowConverter()
    {
        WorkspaceHost.Content = new ConverterWindow(_games.ToList(), RecordConvertedAsync);
        ShowWorkspace();
        SetActiveNavigation(ConverterNav);
    }

    public void ShowDashboard()
    {
        WorkspaceHost.Visibility = Visibility.Collapsed;
        WorkspaceHost.Content = null;
        DashboardView.Visibility = Visibility.Visible;
        SetActiveNavigation(DashboardNav);
        PlayUiSound();
    }

    private void ShowWorkspace()
    {
        DashboardView.Visibility = Visibility.Collapsed;
        WorkspaceHost.Visibility = Visibility.Visible;
    }

    private void SetActiveNavigation(Button active)
    {
        foreach (var button in new[] { DashboardNav, LibraryNav, ConverterNav, CompatibilityNav, DeviceNav, SettingsNav })
        {
            button.Background = button == active ? new SolidColorBrush(Color.FromRgb(48, 36, 68)) : Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(247, 242, 255));
        }
    }

    private void ShowConvertedLibrary()
    {
        var root = new DockPanel { Margin = new Thickness(34, 28, 34, 28), LastChildFill = true };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var addExisting = MakeButton("＋  Add existing APK", false); addExisting.VerticalAlignment = VerticalAlignment.Center; addExisting.Margin = new Thickness(12, 0, 0, 0); addExisting.Click += async (_, _) => await AddExistingConversionAsync();
        DockPanel.SetDock(addExisting, Dock.Right); heading.Children.Add(addExisting);
        var headingText = new StackPanel();
        headingText.Children.Add(new TextBlock { Text = "Game Library", FontSize = 29, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        headingText.Children.Add(new TextBlock { Text = "Your converted PlayStation Pocket collection · Install-ready games and artwork", Foreground = new SolidColorBrush(Color.FromRgb(205, 192, 224)), Margin = new Thickness(0, 5, 0, 0) });
        headingText.Children.Add(new TextBlock { Text = $"{_convertedGames.Count} converted game(s)", Foreground = new SolidColorBrush(Color.FromRgb(220, 174, 238)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        headingText.Children.Add(new TextBlock { Text = "Original PlayStation box art · one cover per game", Foreground = MutedBrush(), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        heading.Children.Add(headingText);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var shelf = new WrapPanel { Orientation = Orientation.Horizontal }; scroll.Content = shelf; root.Children.Add(scroll);

        if (_convertedGames.Count == 0)
        {
            var empty = MakePanel(560);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24) };
            stack.Children.Add(new TextBlock { Text = "◈", FontSize = 42, Foreground = new SolidColorBrush(Color.FromRgb(173, 125, 255)), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "Your converted shelf is waiting", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Games appear here after PSXperia creates their APK and matching data folder.", Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 430 });
            var go = MakeButton("Open Converter", true); go.Margin = new Thickness(0, 18, 0, 0); go.Click += (_, _) => ShowConverter(); stack.Children.Add(go); empty.Child = stack; shelf.Children.Add(empty);
        }
        else
        {
            foreach (var game in _convertedGames.OrderByDescending(x => x.ConvertedAt))
                shelf.Children.Add(BuildConvertedGameCard(game));
        }

        WorkspaceHost.Content = root; ShowWorkspace(); SetActiveNavigation(LibraryNav); PlayUiSound();
    }

    private Border BuildConvertedGameCard(ConvertedGameRecord game)
    {
        var card = new Border { Width = 222, Margin = new Thickness(0, 0, 18, 20), Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromRgb(31, 24, 45)), CornerRadius = new CornerRadius(14), BorderBrush = new SolidColorBrush(Color.FromRgb(75, 56, 105)), BorderThickness = new Thickness(1) };
        var stack = new StackPanel();
        // Size the artwork frame to the image's real aspect ratio instead of forcing every
        // cover into a tall 166x250 box. Square 170x170 icons now sit in a square frame,
        // while traditional portrait box art keeps its taller shape.
        const double artworkWidth = 166;
        var artworkHeight = 220d;
        Image? coverImage = null;
        if (!string.IsNullOrWhiteSpace(game.CoverPath) && File.Exists(game.CoverPath))
        {
            try
            {
                var bitmap = new BitmapImage(new Uri(game.CoverPath));
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    artworkHeight = Math.Clamp(artworkWidth * bitmap.PixelHeight / bitmap.PixelWidth, 96d, 280d);
                coverImage = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            }
            catch { /* Fall back to the standard placeholder below. */ }
        }

        var artFrame = new Border
        {
            Width = artworkWidth,
            Height = artworkHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(Color.FromRgb(70, 52, 113), Color.FromRgb(29, 22, 43), 90),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        artFrame.Child = coverImage ?? CoverPlaceholder(game.Title);
        stack.Children.Add(artFrame);
        stack.Children.Add(new TextBlock { Text = game.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 2, 3), MaxHeight = 42 });
        stack.Children.Add(new TextBlock { Text = game.TitleId, Foreground = new SolidColorBrush(Color.FromRgb(207, 163, 245)), FontSize = 11 });
        stack.Children.Add(new TextBlock { Text = $"Converted {game.ConvertedAt.ToLocalTime():MMM d, yyyy}", Foreground = MutedBrush(), FontSize = 10, Margin = new Thickness(0, 4, 0, 10) });
        var artButton = MakeButton(string.IsNullOrWhiteSpace(game.CoverPath) || !File.Exists(game.CoverPath) ? "Find box art / rename" : "Change box art", false);
        artButton.Margin = new Thickness(0, 0, 0, 9); artButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        artButton.Click += async (_, _) => await EditConvertedGameArtworkAsync(game);
        stack.Children.Add(artButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var open = MakeButton("Open folder", true); open.Padding = new Thickness(9, 7, 9, 7); open.Click += (_, _) => OpenFolder(game.OutputFolder); actions.Children.Add(open);
        var remove = MakeButton("Remove", false); remove.Padding = new Thickness(9, 7, 9, 7); remove.Margin = new Thickness(7, 0, 0, 0); remove.Click += async (_, _) => await RemoveConvertedAsync(game); actions.Children.Add(remove);
        stack.Children.Add(actions); card.Child = stack; return card;
    }

    private async Task EditConvertedGameArtworkAsync(ConvertedGameRecord game)
    {
        var title = PromptForText("Find PlayStation box art",
            "Enter the game's title as it appears on the original box. You can also choose an image file from your PC.",
            game.Title);
        if (string.IsNullOrWhiteSpace(title)) return;

        try
        {
            var updated = game with { Title = title.Trim() };
            var region = System.Text.RegularExpressions.Regex.Match(game.Title, @"\((USA|Europe|Japan|World)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(region)) region = "USA";

            var candidates = await _artwork.FindCandidatesAsync(title.Trim(), region);
            if (candidates.Count > 0)
            {
                var previews = new List<(ArtworkCandidate Candidate, byte[] Preview)>();
                foreach (var candidate in candidates.Take(12))
                {
                    try { previews.Add((candidate, await _artwork.DownloadAsync(candidate))); }
                    catch { /* Skip an individual missing/broken image and keep the remaining results. */ }
                }

                if (previews.Count > 0)
                {
                    var picker = new ArtworkPickerWindow(title.Trim(), previews) { Owner = this };
                    if (picker.ShowDialog() == true && picker.SelectedArtwork is not null)
                    {
                        var bytes = await _artwork.DownloadAsync(picker.SelectedArtwork);
                        updated = await SaveCoverAsync(updated, bytes);
                    }
                    else return;
                }
            }

            if (string.IsNullOrWhiteSpace(updated.CoverPath) || !File.Exists(updated.CoverPath))
            {
                var choose = MessageBox.Show(
                    "No matching online cover was found. Would you like to choose the box-art image from your computer instead?",
                    "Box art not found", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choose != MessageBoxResult.Yes) return;
                var dialog = new OpenFileDialog
                {
                    Title = "Choose PlayStation box art",
                    Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) != true) return;
                var bytes = await File.ReadAllBytesAsync(dialog.FileName);
                updated = await SaveCoverAsync(updated, bytes);
            }

            _convertedGames.RemoveAll(x => x.Id == game.Id);
            _convertedGames.Insert(0, updated);
            await _stateStore.SaveConvertedAsync(_convertedGames);
            ShowConvertedLibrary();
        }
        catch (Exception ex)
        {
            MessageBox.Show("The box art could not be applied. Please try another title or image.\\n\\n" + ex.Message,
                "Box art", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task<ConvertedGameRecord> SaveCoverAsync(ConvertedGameRecord game, byte[] bytes)
    {
        // Decode and re-encode to a consistent PNG so WPF can display JPEG/WebP source images reliably.
        using var input = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = input;
        bitmap.DecodePixelWidth = 512;
        bitmap.EndInit();
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);

        var coverRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PSXperiaRemastered", "covers");
        Directory.CreateDirectory(coverRoot);
        var safeId = System.Text.RegularExpressions.Regex.Replace(game.TitleId, @"[^A-Za-z0-9_.-]", "_");
        var coverPath = Path.Combine(coverRoot, safeId + ".png");
        await File.WriteAllBytesAsync(coverPath, output.ToArray());
        return game with { CoverPath = coverPath };
    }

    private string? PromptForText(string title, string description, string initialValue)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 470, Height = 205, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(24, 18, 34)) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = description, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var input = new TextBox { Text = initialValue, Background = new SolidColorBrush(Color.FromRgb(42, 32, 58)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(130, 99, 177)), Padding = new Thickness(9) };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = MakeButton("Save and find art", true); ok.MinWidth = 130; ok.Click += (_, _) => dialog.DialogResult = true;
        var cancel = MakeButton("Cancel", false); cancel.Margin = new Thickness(8, 0, 0, 0); cancel.Click += (_, _) => dialog.DialogResult = false;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private static UIElement CoverPlaceholder(string title)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "PS", FontSize = 38, FontWeight = FontWeights.Light, Foreground = new SolidColorBrush(Color.FromRgb(222, 191, 255)), HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        return stack;
    }


    private async Task AddExistingConversionAsync()
    {
        var dialog = new OpenFileDialog { Title = "Select an existing PSXperia game APK", Filter = "Android package (*.apk)|*.apk", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var apk = Path.GetFullPath(dialog.FileName);
        var file = Path.GetFileNameWithoutExtension(apk);
        const string prefix = "com.sony.playstation.";
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("This APK filename does not look like a PSXperia game package.", "Import existing APK", MessageBoxButton.OK, MessageBoxImage.Warning); return;
        }
        var titleId = file[prefix.Length..];
        var outputFolder = Path.GetDirectoryName(apk)!;
        var dataPath = Path.Combine(outputFolder, "data", $"com.sony.playstation.{titleId}");
        if (!Directory.Exists(dataPath))
        {
            MessageBox.Show("The matching data folder was not found beside this APK. Select the APK inside its original PSXperia output folder.", "Import existing APK", MessageBoxButton.OK, MessageBoxImage.Warning); return;
        }
        var source = _games.FirstOrDefault(g => string.Equals(g.DiscSerial, titleId.Split('_')[0], StringComparison.OrdinalIgnoreCase) || string.Equals(g.TitleId, titleId.Split('_')[0], StringComparison.OrdinalIgnoreCase));
        var title = source?.Title ?? titleId;
        _convertedGames.RemoveAll(x => x.TitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase));
        _convertedGames.Insert(0, new ConvertedGameRecord(Guid.NewGuid(), title, titleId, apk, dataPath, outputFolder, source?.CoverPath, DateTime.UtcNow));
        await _stateStore.SaveConvertedAsync(_convertedGames);
        ShowConvertedLibrary();
    }

    private async Task RemoveConvertedAsync(ConvertedGameRecord game)
    {
        if (MessageBox.Show($"Remove {game.Title} from the converted-games shelf?\n\nThe APK and data files on disk will not be deleted.", "Remove converted game", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _convertedGames.RemoveAll(x => x.Id == game.Id); await _stateStore.SaveConvertedAsync(_convertedGames); ShowConvertedLibrary();
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else MessageBox.Show("The saved output folder could not be found. It may have been moved.", "Output folder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowCompatibility()
    {
        var root = new StackPanel { Margin = new Thickness(34, 28, 34, 28) };
        root.Children.Add(PageHeading("Compatibility Lab", "Track game-specific glitches, slowdowns, boot results, and optimization notes for the Xperia Play."));
        var form = MakePanel(); form.Margin = new Thickness(0, 16, 0, 16); var formStack = new StackPanel(); form.Child = formStack;
        var gameBox = new ComboBox { ItemsSource = _games, DisplayMemberPath = nameof(GameEntry.Title), MinHeight = 36, Background = new SolidColorBrush(Color.FromRgb(36, 27, 53)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(117, 90, 157)), Padding = new Thickness(8) };
        if (_games.Count > 0) gameBox.SelectedIndex = 0;
        var statusBox = new ComboBox { ItemsSource = new[] { "Untested", "Boots", "Playable", "Verified", "Issues", "Unplayable" }, MinHeight = 36, SelectedIndex = 0, Background = new SolidColorBrush(Color.FromRgb(36, 27, 53)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(117, 90, 157)), Padding = new Thickness(8) };
        var symptoms = new TextBox { MinHeight = 62, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = new SolidColorBrush(Color.FromRgb(36, 27, 53)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(117, 90, 157)), Padding = new Thickness(10), ToolTip = "e.g. FMV audio stutters, low FPS, texture flicker" };
        var notes = new TextBox { MinHeight = 88, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = new SolidColorBrush(Color.FromRgb(36, 27, 53)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(117, 90, 157)), Padding = new Thickness(10), ToolTip = "What was tested, settings, and possible fixes" };
        formStack.Children.Add(FieldLabel("Game", gameBox)); formStack.Children.Add(FieldLabel("Compatibility status", statusBox)); formStack.Children.Add(FieldLabel("Glitches / performance symptoms", symptoms)); formStack.Children.Add(FieldLabel("Testing and optimization notes", notes));
        var save = MakeButton("Save compatibility report", true); save.Margin = new Thickness(0, 12, 0, 0);
        save.Click += async (_, _) =>
        {
            if (gameBox.SelectedItem is not GameEntry selected) { MessageBox.Show("Import a game before adding a compatibility report.", "Compatibility Lab"); return; }
            var id = selected.DiscSerial ?? selected.TitleId ?? selected.Title;
            _compatibility.RemoveAll(x => x.TitleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            _compatibility.Insert(0, new CompatibilityRecord(id, selected.Title, statusBox.SelectedItem?.ToString() ?? "Untested", symptoms.Text.Trim(), notes.Text.Trim(), DateTime.UtcNow));
            await _stateStore.SaveCompatibilityAsync(_compatibility);
            MessageBox.Show("Compatibility report saved.", "Compatibility Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowCompatibility();
        };
        formStack.Children.Add(save); root.Children.Add(form);
        root.Children.Add(new TextBlock { Text = "TEST LOG", Foreground = new SolidColorBrush(Color.FromRgb(212, 171, 245)), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 4, 0, 10) });
        if (_compatibility.Count == 0) root.Children.Add(new TextBlock { Text = "No reports yet. Save your first game test above.", Foreground = MutedBrush() });
        foreach (var report in _compatibility.OrderByDescending(x => x.UpdatedAt))
        {
            var panel = MakePanel(); panel.Margin = new Thickness(0, 0, 0, 9); var content = new StackPanel(); panel.Child = content;
            content.Children.Add(new TextBlock { Text = report.GameTitle + "  ·  " + report.Status, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            if (!string.IsNullOrWhiteSpace(report.Symptoms)) content.Children.Add(new TextBlock { Text = "Symptoms: " + report.Symptoms, Foreground = new SolidColorBrush(Color.FromRgb(235, 187, 202)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            if (!string.IsNullOrWhiteSpace(report.Notes)) content.Children.Add(new TextBlock { Text = report.Notes, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            root.Children.Add(panel);
        }
        WorkspaceHost.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; ShowWorkspace(); SetActiveNavigation(CompatibilityNav); PlayUiSound();
    }

    private void ShowSettings()
    {
        var root = new StackPanel { Margin = new Thickness(34, 28, 34, 28) };
        root.Children.Add(PageHeading("Settings", "Tune the PlayStation Pocket-inspired look and how PSXperia behaves."));
        var panel = MakePanel(); panel.Margin = new Thickness(0, 18, 0, 0); var stack = new StackPanel(); panel.Child = stack;
        var animated = MakeCheck("Animated violet wave background", _settings.AnimatedBackground, "Slow drifting wave animation behind the workspace.");
        var reduce = MakeCheck("Reduce motion", _settings.ReduceMotion, "Overrides animation and keeps the background still.");
        var sounds = MakeCheck("Interface sound effects", _settings.UiSounds, "Optional Windows system UI tones. Off by default; no copyrighted PlayStation audio is included.");
        var artwork = MakeCheck("Automatic cover-art lookup", _settings.AutomaticArtwork, "Find original PlayStation box art from the Libretro GitHub collection.");
        var music = MakeCheck("Background music", _settings.BackgroundMusic, "Loop your supplied soundtrack quietly in the background.");
        var volume = new Slider { Minimum = 0.03, Maximum = 0.25, Value = Math.Clamp(_settings.BackgroundMusicVolume, 0.03, 0.25), TickFrequency = 0.01, IsSnapToTickEnabled = false, Margin = new Thickness(4, 4, 4, 12) };
        var volumeLabel = new TextBlock { Text = $"Music volume: {volume.Value:P0}", Foreground = MutedBrush(), Margin = new Thickness(2, 0, 0, 0) };
        volume.ValueChanged += (_, _) => { volumeLabel.Text = $"Music volume: {volume.Value:P0}"; if (_musicOpened) _backgroundPlayer.Volume = volume.Value; };
        stack.Children.Add(animated); stack.Children.Add(reduce); stack.Children.Add(music); stack.Children.Add(volumeLabel); stack.Children.Add(volume); stack.Children.Add(sounds); stack.Children.Add(artwork);
        var save = MakeButton("Save settings", true); save.Margin = new Thickness(0, 18, 0, 0);
        save.Click += async (_, _) =>
        {
            _settings.AnimatedBackground = animated.IsChecked == true; _settings.ReduceMotion = reduce.IsChecked == true; _settings.UiSounds = sounds.IsChecked == true; _settings.AutomaticArtwork = artwork.IsChecked == true;
            _settings.BackgroundMusic = music.IsChecked == true; _settings.BackgroundMusicVolume = volume.Value;
            await _stateStore.SaveSettingsAsync(_settings); ApplyWaveSettings(); ConfigureBackgroundMusic(); MessageBox.Show("Your preferences have been saved.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        stack.Children.Add(save); root.Children.Add(panel);
        var note = new TextBlock { Text = "Your library, converted-game shelf, compatibility reports, and preferences are stored locally in %LOCALAPPDATA%\\PSXperiaRemastered.", Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 16, 2, 0) }; root.Children.Add(note);
        WorkspaceHost.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; ShowWorkspace(); SetActiveNavigation(SettingsNav); PlayUiSound();
    }

    private void ShowDevicePage()
    {
        var root = new StackPanel { Margin = new Thickness(34, 28, 34, 28) };
        root.Children.Add(PageHeading("Xperia Device", "Connect your Xperia Play, inspect it, and install converted games directly over USB."));

        var connectionPanel = MakePanel(); connectionPanel.Margin = new Thickness(0, 18, 0, 14);
        var connection = new StackPanel(); connectionPanel.Child = connection;
        connection.Children.Add(new TextBlock { Text = "DEVICE CONNECTION", Foreground = new SolidColorBrush(Color.FromRgb(207, 163, 245)), FontSize = 10, FontWeight = FontWeights.Bold });
        var status = new TextBlock { Text = "Not checked yet", Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        var details = new TextBlock { Text = "Connect your Xperia Play with a data-capable USB cable and enable USB debugging.", Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap };
        connection.Children.Add(status); connection.Children.Add(details);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var refresh = MakeButton("↻  Detect Xperia", true);
        var openHelp = MakeButton("USB debugging help", false); openHelp.Margin = new Thickness(10, 0, 0, 0);
        openHelp.Click += (_, _) => MessageBox.Show("On the Xperia Play, open Settings → Applications → Development → enable USB debugging. Connect with a USB data cable, accept the RSA authorization prompt if shown, then press Detect Xperia again. If Windows does not detect it, install a compatible Sony Ericsson USB driver.", "Connect Xperia Play", MessageBoxButton.OK, MessageBoxImage.Information);
        actions.Children.Add(refresh); actions.Children.Add(openHelp); connection.Children.Add(actions);

        var installPanel = MakePanel(); installPanel.Margin = new Thickness(0, 0, 0, 14);
        var install = new StackPanel(); installPanel.Child = install;
        install.Children.Add(new TextBlock { Text = "INSTALL A CONVERTED GAME", Foreground = new SolidColorBrush(Color.FromRgb(207, 163, 245)), FontSize = 10, FontWeight = FontWeights.Bold });
        install.Children.Add(new TextBlock { Text = "Choose a game from your converted shelf. PSXperia will install its APK, transfer its matching data folder, and verify that Android sees the installed package.", Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 12) });
        var gamePicker = new ComboBox { MinWidth = 360, Height = 36, Background = new SolidColorBrush(Color.FromRgb(43, 34, 58)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(90, 68, 119)), ItemsSource = _convertedGames.OrderBy(x => x.Title).ToList(), DisplayMemberPath = "Title", SelectedIndex = _convertedGames.Count > 0 ? 0 : -1 };
        install.Children.Add(FieldLabel("Converted game", gamePicker));
        var installButton = MakeButton("Install to Xperia Play", true); installButton.Margin = new Thickness(0, 4, 0, 0); installButton.IsEnabled = false; install.Children.Add(installButton);
        var installStatus = new TextBlock { Text = _convertedGames.Count == 0 ? "Convert a game first; it will appear here." : "Waiting for a connected device.", Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        install.Children.Add(installStatus);

        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false; status.Text = "Checking ADB…"; details.Text = "Looking for an authorized Android device."; installButton.IsEnabled = false; _connectedXperia = null;
            try
            {
                var adbPath = AdbXperiaDeviceService.ResolveAdbPath();
                if (adbPath is null) throw new FileNotFoundException("ADB (adb.exe) was not found. Install Android SDK Platform-Tools and add its platform-tools folder to PATH, or set ANDROID_SDK_ROOT.");
                _adbService = new AdbXperiaDeviceService(adbPath);
                var devices = await _adbService.FindDevicesAsync();
                if (devices.Count == 0)
                {
                    status.Text = "No authorized device detected";
                    details.Text = "Check the USB cable, enable USB debugging, and accept the authorization prompt on the phone. If `adb devices` shows unauthorized, reconnect and approve the computer.";
                    installStatus.Text = "No device ready for installation.";
                }
                else
                {
                    _connectedXperia = devices.First();
                    status.Text = "●  Xperia connected";
                    details.Text = $"{_connectedXperia.Model}  ·  Android {_connectedXperia.AndroidVersion}  ·  Serial {_connectedXperia.Serial}\nADB: {_adbService.AdbPath}";
                    installButton.IsEnabled = gamePicker.SelectedItem is ConvertedGameRecord;
                    installStatus.Text = devices.Count > 1 ? $"{devices.Count} authorized devices found. The first device is selected for this test build." : "Device is ready. Select a converted game and install.";
                }
            }
            catch (Exception ex) { status.Text = "Connection check failed"; details.Text = ex.Message; installStatus.Text = "Fix the ADB or USB issue, then detect the device again."; }
            finally { refresh.IsEnabled = true; }
        };
        gamePicker.SelectionChanged += (_, _) => installButton.IsEnabled = _connectedXperia is not null && gamePicker.SelectedItem is ConvertedGameRecord;
        installButton.Click += async (_, _) =>
        {
            if (_connectedXperia is null || _adbService is null || gamePicker.SelectedItem is not ConvertedGameRecord game) return;
            var packageName = "com.sony.playstation." + game.TitleId;
            if (!File.Exists(game.ApkPath) || !Directory.Exists(game.DataPath))
            { MessageBox.Show("The APK or matching game-data folder is missing from the saved output. Open the game output folder and reconvert if necessary.", "Game files missing", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var confirm = MessageBox.Show($"Install {game.Title} to {_connectedXperia.Model}?\n\nThe APK will be installed and its matching data copied to the app's external Android data directory. Existing app data for this same package may be overwritten.", "Confirm Xperia installation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            installButton.IsEnabled = false; refresh.IsEnabled = false;
            try
            {
                installStatus.Text = "Step 1 of 3 · Installing APK…";
                await _adbService.InstallPackageAsync(_connectedXperia.Serial, game.ApkPath);
                installStatus.Text = "Step 2 of 3 · Transferring matching game data…";
                await _adbService.TransferGameDataAsync(_connectedXperia.Serial, game.DataPath, packageName);
                installStatus.Text = "Step 3 of 3 · Verifying installed package…";
                var verified = await _adbService.VerifyPackageInstalledAsync(_connectedXperia.Serial, packageName);
                if (!verified) throw new InvalidOperationException("ADB transferred the files, but Android did not confirm the installed package. Check the phone and installation log before launching the game.");
                installStatus.Text = $"✓ APK installed and game data transferred. Android confirms {packageName} is installed. Launch it from the Xperia Play app drawer.";
                installStatus.Foreground = new SolidColorBrush(Color.FromRgb(159, 224, 181));
                MessageBox.Show("The APK installation and data transfer completed. Android confirms the package is installed. Please launch the game on the Xperia Play and report whether it boots and plays correctly.", "Installation complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { installStatus.Text = "Installation stopped: " + ex.Message; installStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 170)); MessageBox.Show(ex.ToString(), "Xperia installation error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { refresh.IsEnabled = true; installButton.IsEnabled = _connectedXperia is not null && gamePicker.SelectedItem is ConvertedGameRecord; }
        };

        root.Children.Add(connectionPanel); root.Children.Add(installPanel);
        var note = new TextBlock { Text = "First device milestone: ADB detection, APK installation, game-data transfer, and package verification. This build does not yet launch games remotely or verify in-game performance. Keep USB debugging enabled only when needed.", Foreground = MutedBrush(), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) };
        root.Children.Add(note);
        WorkspaceHost.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; ShowWorkspace(); SetActiveNavigation(DeviceNav);
    }

    private void ApplyWaveSettings()
    {
        _waveMotion.BeginAnimation(TranslateTransform.XProperty, null);
        _bubbleMotion.BeginAnimation(TranslateTransform.YProperty, null);
        WaveBackground.BeginAnimation(UIElement.OpacityProperty, null);
        BubbleGlow.BeginAnimation(UIElement.OpacityProperty, null);
        if (!_settings.AnimatedBackground || _settings.ReduceMotion) { WaveBackground.Opacity = 0.25; BubbleGlow.Opacity = 0.10; return; }
        WaveBackground.Opacity = 0.34; BubbleGlow.Opacity = 0.20;
        _waveMotion.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-150, 150, TimeSpan.FromSeconds(34)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        _bubbleMotion.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-34, 42, TimeSpan.FromSeconds(19)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        WaveBackground.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.26, 0.40, TimeSpan.FromSeconds(16)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        BubbleGlow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.12, 0.24, TimeSpan.FromSeconds(17)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    private void ConfigureBackgroundMusic()
    {
        try
        {
            if (!_settings.BackgroundMusic) { _backgroundPlayer.Stop(); return; }
            if (!_musicOpened)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "background-music.mp3");
                if (!File.Exists(path)) return;
                _backgroundPlayer.Open(new Uri(path, UriKind.Absolute));
                _backgroundPlayer.MediaEnded += (_, _) => { _backgroundPlayer.Position = TimeSpan.Zero; _backgroundPlayer.Play(); };
                _musicOpened = true;
            }
            _backgroundPlayer.Volume = Math.Clamp(_settings.BackgroundMusicVolume, 0.03, 0.25);
            _backgroundPlayer.Play();
        }
        catch { /* Optional audio must never block conversion. */ }
    }

    private void PlayUiSound() { if (_settings.UiSounds) SystemSounds.Asterisk.Play(); }
    private static Border MakePanel(double width = double.NaN) => new() { Width = width, Padding = new Thickness(18), Background = new SolidColorBrush(Color.FromRgb(31, 24, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(69, 52, 94)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13) };
    private static Button MakeButton(string text, bool primary) => new() { Content = text, Padding = new Thickness(14, 9, 14, 9), Foreground = Brushes.White, Background = primary ? new SolidColorBrush(Color.FromRgb(112, 76, 210)) : new SolidColorBrush(Color.FromRgb(54, 42, 72)), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.SemiBold };
    private static Brush MutedBrush() => new SolidColorBrush(Color.FromRgb(190, 179, 210));
    private static StackPanel PageHeading(string title, string subtitle) { var p = new StackPanel(); p.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = FontWeights.Bold, Foreground = Brushes.White }); p.Children.Add(new TextBlock { Text = subtitle, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }); return p; }
    private static StackPanel FieldLabel(string label, Control control) { var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; p.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(218, 207, 237)), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) }); p.Children.Add(control); return p; }
    private static CheckBox MakeCheck(string label, bool value, string description)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        content.Children.Add(new TextBlock { Text = description, Foreground = MutedBrush(), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        return new CheckBox { Content = content, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(0, 7, 0, 10), ToolTip = description };
    }

    private async Task RefreshLibraryAsync()
    {
        try
        {
            _games.Clear();
            foreach (var game in await _library.LoadAsync())
                _games.Add(game);
            UpdateLibraryDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the game library:\n{ex.Message}", "Library error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateLibraryDisplay()
    {
        LibraryCount.Text = _games.Count.ToString();
        LibrarySubtitle.Text = _games.Count == 1 ? "1 source game imported" : $"{_games.Count} source games imported";
        ConvertedCount.Text = _convertedGames.Count.ToString();
        ConvertedSubtitle.Text = _convertedGames.Count == 1 ? "1 game converted" : $"{_convertedGames.Count} games converted";
        CompatibilityCount.Text = _compatibility.Count.ToString();
        CompatibilitySubtitle.Text = _compatibility.Count == 1 ? "1 test report saved" : $"{_compatibility.Count} test reports saved";
        EmptyLibraryPanel.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GamesList.Visibility = _games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void AddGames_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import PlayStation disc images (select CUE when available)",
            Filter = "PlayStation images (*.cue;*.bin;*.img;*.iso)|*.cue;*.bin;*.img;*.iso|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) return;

        var selected = dialog.FileNames.Select(Path.GetFullPath).ToList();
        var selectedCues = selected.Where(p => Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();
        var referencedTrackFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        // A CUE sheet is the disc entry point. Do not separately import its referenced BIN files.
        foreach (var cue in selectedCues)
        {
            try
            {
                var inspection = DiscImageInspector.Inspect(cue);
                foreach (var trackFile in inspection.DataFiles)
                    referencedTrackFiles.Add(Path.GetFullPath(trackFile));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(cue)}: {ex.Message}");
            }
        }

        var importCandidates = selected
            .Where(p => !referencedTrackFiles.Contains(p))
            .Where(p => !Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase) ||
                        !errors.Any(e => e.StartsWith(Path.GetFileName(p) + ":", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var imported = 0;
        foreach (var path in importCandidates)
        {
            try
            {
                var inspection = DiscImageInspector.Inspect(path);
                var entry = await _library.ImportAsync(path);
                var existingIndex = _games.ToList().FindIndex(x => x.Id == entry.Id);
                if (existingIndex < 0)
                {
                    _games.Add(entry);
                    imported++;
                }
                else
                {
                    // Re-importing a CUE refreshes track/serial metadata in the visible row.
                    _games[existingIndex] = entry;
                }
                if (_settings.AutomaticArtwork && string.IsNullOrWhiteSpace(entry.CoverPath) && !entry.CoverIsManual)
                    await TryApplyAutomaticArtworkAsync(entry);
                if (inspection.Warnings.Count > 0)
                    errors.Add($"{Path.GetFileName(path)}: {string.Join(" ", inspection.Warnings)}");
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        UpdateLibraryDisplay();

        var message = $"Imported {imported} new game/disc image(s).";
        if (selectedCues.Count > 0 && referencedTrackFiles.Count > 0)
            message += $"\nGrouped {referencedTrackFiles.Count} referenced track file(s) under their CUE sheet(s).";
        if (errors.Count > 0)
            message += "\n\nReview:\n• " + string.Join("\n• ", errors.Distinct());
        MessageBox.Show(message, "PSXperia Remastered", MessageBoxButton.OK,
            errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private async void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid gameId }) return;

        var game = _games.FirstOrDefault(x => x.Id == gameId);
        if (game is null) return;

        var answer = MessageBox.Show(
            $"Remove \"{game.Title}\" from your PSXperia library?\n\nThis only removes the library entry. Your CUE, BIN, ISO, and other source files will NOT be deleted.",
            "Remove game", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var removed = await _library.RemoveAsync(gameId);
            if (!removed)
            {
                MessageBox.Show("This game is no longer in the saved library. Refreshing the list.",
                    "Remove game", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            var current = _games.FirstOrDefault(x => x.Id == gameId);
            if (current is not null) _games.Remove(current);
            UpdateLibraryDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the game from your library:\n{ex.Message}",
                "Remove game error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SetCover_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameEntry selected)
        {
            MessageBox.Show("Select a game in the library first.", "Cover artwork",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Choose cover artwork for {selected.Title}",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var coverDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PSXperiaRemastered", "covers");
            Directory.CreateDirectory(coverDirectory);
            var destination = Path.Combine(coverDirectory, $"{selected.Id:N}.png");
            SaveSquareCover(dialog.FileName, destination, 170);

            await _library.UpdateCoverAsync(selected.Id, destination, isManual: true, source: "User selected");
            var index = _games.IndexOf(selected);
            _games[index] = selected with { CoverPath = destination, CoverIsManual = true, CoverSource = "User selected" };
            GamesList.SelectedItem = _games[index];
            MessageBox.Show("Cover artwork saved as a 170 × 170 PNG.", "Cover artwork",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save cover artwork:\n{ex.Message}", "Cover artwork error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private async Task TryApplyAutomaticArtworkAsync(GameEntry game)
    {
        try
        {
            var candidates = await _artwork.FindCandidatesAsync(game.Title, game.Region);
            if (candidates.Count == 0) return;

            // Download only the leading candidates needed for a useful choice dialog.
            var previews = new List<(ArtworkCandidate Candidate, byte[] Preview)>();
            foreach (var candidate in candidates.Take(8))
            {
                try { previews.Add((candidate, await _artwork.DownloadAsync(candidate))); }
                catch { /* Ignore one broken/missing artwork item and continue with the others. */ }
            }
            if (previews.Count == 0) return;

            ArtworkCandidate? selected = null;
            if (previews.Count == 1)
            {
                selected = previews[0].Candidate;
            }
            else
            {
                var picker = new ArtworkPickerWindow(game.Title, previews) { Owner = this };
                if (picker.ShowDialog() == true) selected = picker.SelectedArtwork;
            }
            if (selected is null) return;

            var selectedPreview = previews.FirstOrDefault(x => x.Candidate == selected);
            if (selectedPreview.Preview is null) return;
            var selectedBytes = selectedPreview.Preview;
            var coverDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSXperiaRemastered", "covers");
            Directory.CreateDirectory(coverDirectory);
            var destination = Path.Combine(coverDirectory, $"{game.Id:N}.png");
            await SaveSquareCoverAsync(selectedBytes, destination, 170);
            await _library.UpdateCoverAsync(game.Id, destination, isManual: false, source: selected.DownloadUrl);
            var index = _games.ToList().FindIndex(x => x.Id == game.Id);
            if (index >= 0) _games[index] = game with { CoverPath = destination, CoverIsManual = false, CoverSource = selected.DownloadUrl };
        }
        catch (Exception ex)
        {
            // Artwork lookup is optional: network/API failures must never undo a successful game import.
            MessageBox.Show($"The game was imported, but automatic artwork lookup was unavailable:\n{ex.Message}",
                "Automatic artwork", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static async Task SaveSquareCoverAsync(byte[] bytes, string destinationPath, int size)
    {
        using var input = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The downloaded artwork contains no image frame.");
        var frame = decoder.Frames[0];
        var scale = Math.Max((double)size / frame.PixelWidth, (double)size / frame.PixelHeight);
        var scaled = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
        var x = Math.Max(0, (scaled.PixelWidth - size) / 2); var y = Math.Max(0, (scaled.PixelHeight - size) / 2);
        var cropped = new CroppedBitmap(scaled, new Int32Rect(x, y, size, size));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(cropped));
        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = File.Create(tempPath)) encoder.Save(output);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    private static void SaveSquareCover(string sourcePath, string destinationPath, int size)
    {
        using var input = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The selected image does not contain a readable frame.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            throw new InvalidDataException("The selected image has invalid dimensions.");

        var scale = Math.Max((double)size / frame.PixelWidth, (double)size / frame.PixelHeight);
        var scaled = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
        var x = Math.Max(0, (scaled.PixelWidth - size) / 2);
        var y = Math.Max(0, (scaled.PixelHeight - size) / 2);
        var cropped = new CroppedBitmap(scaled, new Int32Rect(x, y, size, size));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(tempPath))
                encoder.Save(output);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
