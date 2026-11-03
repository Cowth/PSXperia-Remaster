using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using PSXperia.App;
using PSXperia.Library;
using Xunit;

namespace PSXperia.App.Tests;

public sealed class ConverterSelectionTitleIdTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void SelectingLibraryGames_UpdatesTheRealConverterTitleIdField()
    {
        RunOnSta(() =>
        {
            var games = new[]
            {
                CreateGame("D (USA)", "SLUS-001.28"),
                CreateGame("Spyro 2 - Ripto's Rage! (USA)", "SCUS-944.25"),
                CreateGame("Crash Bash (USA)", "SCUS-945.70"),
                CreateGame("No serial", null)
            };
            var converter = new ConverterWindow(games);
            var selector = GetPrivateField<ComboBox>(converter, "_game");
            var titleId = GetPrivateField<TextBox>(converter, "_titleId");
            var runtimeStatus = GetPrivateField<TextBlock>(converter, "_runtimeStatus");

            Assert.Equal("SLUS00128_1", titleId.Text);

            titleId.Text = "MANUAL_9";
            runtimeStatus.Text = "An unrelated status update";
            Assert.Equal("MANUAL_9", titleId.Text);

            selector.SelectedIndex = 1;
            Assert.Equal("SCUS94425_1", titleId.Text);

            selector.SelectedIndex = 2;
            Assert.Equal("SCUS94570_1", titleId.Text);

            selector.SelectedIndex = 3;
            Assert.Equal("", titleId.Text);
        });
    }

    private static GameEntry CreateGame(string title, string? serial) =>
        new(Guid.NewGuid(), title, serial, "synthetic.cue", null, CompatibilityStatus.Untested, DateTime.UnixEpoch)
        {
            DiscSerial = serial
        };

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class =>
        (T)(typeof(ConverterWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"ConverterWindow field '{fieldName}' was not found."));

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        })
        {
            IsBackground = true,
            Name = "Converter selection UI test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("The converter selection UI test did not finish within 15 seconds.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
