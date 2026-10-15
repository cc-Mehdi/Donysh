#if DEBUG && WINDOWS
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DonyshMobile.Core;
using Microsoft.Maui.Controls;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace DonyshMobile.WinUI;

/// <summary>
/// Opt-in runtime regression check. Normal runs never enter the harness or use its data.
/// The external runner supplies an output path below the system temporary directory.
/// </summary>
internal static class StartupSmokeCheck
{
    private static readonly object ReportGate = new();
    private static readonly List<string> Checks = [];
    private static readonly byte[] StorageKey = RandomNumberGenerator.GetBytes(32);
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    private static readonly string? OutputPath = ReadOutputPath();
    private static readonly string? DataDirectory = OutputPath is null ? null
        : Path.Combine(Path.GetDirectoryName(OutputPath)!, "test-data-" + Guid.NewGuid().ToString("N"));
    private static int _started;
    private static int _finished;

    public static void WatchUnhandled(App app)
    {
        if (OutputPath is null) return;
        app.UnhandledException += (_, args) => Finish(false, args.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Finish(false, args.ExceptionObject.ToString() ?? "Unhandled application failure.");
        TaskScheduler.UnobservedTaskException += (_, args) => Finish(false, args.Exception.ToString());
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(50));
            Finish(false, "The runtime startup check did not complete within 50 seconds.");
        });
    }

    public static SmsMonitorStore CreateStore() => DataDirectory is null
        ? new SmsMonitorStore(FileSystem.AppDataDirectory)
        : IsolatedStore(DataDirectory);

    public static void Attach(MauiWindow window)
    {
        if (OutputPath is null) return;
        void Start(object? sender, EventArgs args)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            window.Dispatcher.Dispatch(async () =>
            {
                try { await RunAsync(window); }
                catch (Exception exception) { Finish(false, exception.ToString()); }
            });
        }
        window.Created += Start;
        if (window.Page is { } page) page.Loaded += Start;
        if (window.Page?.IsLoaded == true) Start(null, EventArgs.Empty);
    }

    private static async Task RunAsync(MauiWindow window)
    {
        await WaitUntilAsync(() => window.Page is Shell { CurrentPage: MainPage { IsLoaded: true } },
            "The initial page was not loaded.");
        var shell = (Shell)window.Page!;
        var home = (MainPage)shell.CurrentPage;
        Check(window.Handler is not null && home.Handler is not null, "Native window and home page loaded");

        var nativeResources = global::Microsoft.UI.Xaml.Application.Current.Resources;
        foreach (var name in new[] { "ContentControlThemeFontFamily", "TextControlThemeFontFamily" })
        {
            Check(nativeResources[name] is global::Microsoft.UI.Xaml.Media.FontFamily family
                && family.Source.Contains("YekanBakh-Regular.ttf", StringComparison.Ordinal),
                $"Native font resource: {name}");
        }

        var store = CreateStore();
        const string address = "+989123456789";
        Check(await store.AddSenderAsync(address) == AddSenderResult.Added, "Isolated sender registered");
        var sender = (await store.GetSenderSummariesAsync()).Single();
        var preset = SmsTemplatePresets.All[0];
        var built = SmsKeyValueTemplateBuilder.Build(Guid.NewGuid(), sender.Id, "قالب آزمایشی", preset.SampleMessage, preset.Fields);
        Check(built.Template is not null && await store.SaveTemplateAsync(built.Template) is null,
            "Isolated bank template saved");
        Check(await store.CaptureAsync(address, preset.SampleMessage, DateTimeOffset.UtcNow), "Isolated SMS captured");

        // Appearance checks change only this test process; no saved preference is modified.
        var application = MauiApplication.Current!;
        var previousTheme = application.UserAppTheme;
        try
        {
            foreach (var (theme, background) in new[]
            {
                (AppTheme.Light, "#F4F7FC"), (AppTheme.Dark, "#0B1120")
            })
            {
                application.UserAppTheme = theme;
                await WaitUntilAsync(() => home.BackgroundColor?.Equals(Color.FromArgb(background)) == true,
                    $"The {theme} page background was not applied. Actual: {home.BackgroundColor}");
                Check(true, $"{theme} page theme applied");
            }
        }
        finally { application.UserAppTheme = previousTheme; }

        await VisitAsync(shell, new ConnectionPage(store), async page =>
        {
            await WaitUntilAsync(() => !string.IsNullOrWhiteSpace(page.FindByName<Label>("ConnectionStatusLabel").Text),
                "Settings did not finish loading.");
            var input = page.FindByName<Entry>("PairCodeEntry");
            Check(input.FontFamily == "YekanBakh", "Settings input uses the registered Persian font");
        });
        await VisitAsync(shell, new MessagesPage(store, sender.Id, address), async page =>
        {
            await WaitUntilAsync(() => page.Messages.Count == 1, "The message list did not load its isolated SMS.");
        });
        await VisitAsync(shell, new TemplatesPage(store, sender.Id, address), async page =>
        {
            await WaitUntilAsync(() => page.Templates.Count == 1, "The template list did not load.");
        });
        await VisitAsync(shell, new TemplateEditorPage(store, sender.Id, address), async page =>
        {
            await WaitUntilAsync(() => page.FindByName<Picker>("SavedMessagePicker").Items.Count == 1,
                "The new-template editor did not load the sample message.");
        });
        await VisitAsync(shell, new TemplateEditorPage(store, sender.Id, address, built.Template), async page =>
        {
            await WaitUntilAsync(() => page.FindByName<Entry>("NameEntry").Text == "قالب آزمایشی",
                "The existing-template editor did not load.");
        });
        await VisitAsync(shell, new SenderPickerPage([address, "3000505"], _ => Task.CompletedTask), page =>
        {
            Check(page.FindByName<CollectionView>("SenderList").ItemsSource is not null, "Sender picker populated");
            return Task.CompletedTask;
        });
        await VisitAsync(shell, new PendingTitlesPage(store), async page =>
        {
            await WaitUntilAsync(() => page.FindByName<CollectionView>("PendingList").ItemsSource is not null,
                "The pending-title list did not load.");
        });

        // A corrupt encrypted queue used to escape the catch in notification-driven OnAppearing.
        var corruptDirectory = Path.Combine(DataDirectory!, "corrupt-queue");
        var corruptStore = IsolatedStore(corruptDirectory);
        await File.WriteAllBytesAsync(Path.Combine(corruptDirectory, "sms-monitor-state.json"),
            "DNYSHSMS1"u8.ToArray().Concat(new byte[32]).ToArray());
        await VisitAsync(shell, new PendingTitlesPage(corruptStore, promptFirstMessage: true), async page =>
        {
            await WaitUntilAsync(() => page.FindByName<Label>("RemainingLabel").Text?.Contains("خواندن پیام‌ها کامل نشد", StringComparison.Ordinal) == true,
                "The damaged queue did not produce its recoverable error state.");
            await Task.Delay(250);
            Check(true, "Notification title page survived an unreadable encrypted queue");
        });
        Finish(true, null);
    }

    private static async Task VisitAsync<TPage>(Shell shell, TPage page, Func<TPage, Task> verify) where TPage : Page
    {
        await shell.Navigation.PushAsync(page, animated: false);
        await WaitUntilAsync(() => page.IsLoaded && page.Handler is not null, $"{typeof(TPage).Name} did not load.");
        await verify(page);
        Check(true, $"Runtime page: {typeof(TPage).Name}");
        await shell.Navigation.PopAsync(animated: false);
    }

    private static SmsMonitorStore IsolatedStore(string directory) =>
        new(directory, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StorageKey.ToArray());
        });

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(failure);
            await Task.Delay(50);
        }
    }

    private static void Check(bool success, string description)
    {
        if (!success) throw new InvalidOperationException(description);
        lock (ReportGate) Checks.Add(description);
    }

    private static string? ReadOutputPath()
    {
        var value = Environment.GetEnvironmentVariable("DONYSH_STARTUP_CHECK_OUTPUT");
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var full = Path.GetFullPath(value);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var directory = Path.GetDirectoryName(full);
            if (directory is null || !full.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                || directory.TrimEnd(Path.DirectorySeparatorChar).Equals(temporaryRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(directory)) return null;
            return full;
        }
        catch (Exception) { return null; }
    }

    private static void Finish(bool success, string? error)
    {
        if (OutputPath is null || Interlocked.Exchange(ref _finished, 1) != 0) return;
        try
        {
            lock (ReportGate)
            {
                var result = new
                {
                    success,
                    processId = Environment.ProcessId,
                    startedAtUtc = StartedAt,
                    completedAtUtc = DateTimeOffset.UtcNow,
                    isolatedDataDirectory = DataDirectory,
                    checks = Checks.ToArray(),
                    error
                };
                File.WriteAllText(OutputPath + ".tmp", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(OutputPath + ".tmp", OutputPath, overwrite: true);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Environment.Exit(2);
        }
        Environment.Exit(success ? 0 : 1);
    }
}
#endif
