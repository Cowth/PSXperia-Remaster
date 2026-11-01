using PSXperia.Engine;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PSXperia.App;

public sealed class ArtworkPickerWindow : Window
{
    private readonly ListBox _choices = new();
    public ArtworkCandidate? SelectedArtwork => (_choices.SelectedItem as CandidateView)?.Candidate;

    public ArtworkPickerWindow(string gameTitle, IReadOnlyList<(ArtworkCandidate Candidate, byte[] Preview)> candidates)
    {
        Title = $"Choose box art — {gameTitle}";
        Width = 760; Height = 540; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(25, 20, 35));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock { Text = "Select the artwork you want to apply", FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var skip = new Button { Content = "Skip", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var apply = new Button { Content = "Apply Artwork", Padding = new Thickness(18, 8, 18, 8), IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(154, 115, 245)), Foreground = Brushes.White };
        apply.Click += (_, _) => { if (SelectedArtwork is not null) DialogResult = true; };
        DockPanel.SetDock(buttons, Dock.Bottom); buttons.Children.Add(skip); buttons.Children.Add(apply); root.Children.Add(buttons);

        _choices.Background = new SolidColorBrush(Color.FromRgb(35, 29, 48));
        _choices.BorderThickness = new Thickness(0);
        _choices.SelectionMode = SelectionMode.Single;
        _choices.ItemsSource = candidates.Select(c => new CandidateView(c.Candidate, c.Preview)).ToArray();
        _choices.ItemTemplate = BuildTemplate();
        if (candidates.Count > 0) _choices.SelectedIndex = 0;
        root.Children.Add(_choices);
        Content = root;
    }

    private static DataTemplate BuildTemplate()
    {
        var template = new DataTemplate(typeof(CandidateView));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(FrameworkElement.WidthProperty, 100d); image.SetValue(FrameworkElement.HeightProperty, 100d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0)); image.SetValue(Image.StretchProperty, Stretch.Uniform);
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(CandidateView.PreviewImage)));
        panel.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(StackPanel)); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); name.SetValue(TextBlock.FontSizeProperty, 14d);
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(CandidateView.FileName))); text.AppendChild(name);
        var region = new FrameworkElementFactory(typeof(TextBlock)); region.SetValue(TextBlock.OpacityProperty, .75d); region.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(CandidateView.Region))); text.AppendChild(region);
        panel.AppendChild(text); template.VisualTree = panel; return template;
    }

    public sealed class CandidateView
    {
        public ArtworkCandidate Candidate { get; }
        public string FileName => Candidate.FileName;
        public string Region => $"{Candidate.RegionLabel} • {Candidate.VariantLabel}";
        public BitmapImage PreviewImage { get; }
        public CandidateView(ArtworkCandidate candidate, byte[] bytes)
        {
            Candidate = candidate;
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            PreviewImage = image;
        }
    }
}
