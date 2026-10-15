#if DEBUG && WINDOWS
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DonyshMobile.Core;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
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
    private static ConnectionScenario? _lastConnectionScenario;

    public static bool IsEnabled => OutputPath is not null;

    public static Task<MobileConnection?> LoadConnectionAsync() => IsEnabled
        ? Task.FromResult<MobileConnection?>(null)
        : MobileConnectionStore.LoadAsync();

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

    public static ConnectionPage CreateConnectionPage(SmsMonitorStore store)
    {
        if (!IsEnabled) return new ConnectionPage(store);
        _lastConnectionScenario = new ConnectionScenario();
        return CreateConnectionPage(store, _lastConnectionScenario);
    }

    private static ConnectionPage CreateConnectionPage(SmsMonitorStore store, ConnectionScenario scenario) =>
        new(store,
            new MobileApiClient(() => new HttpClient(new StubTransport(scenario))),
            scenario.Connectivity,
            () =>
            {
                scenario.ConnectionLoadAttempts++;
                return scenario.ThrowOnConnectionLoad
                    ? Task.FromException<MobileConnection?>(new InvalidOperationException("Simulated unavailable credential storage."))
                    : Task.FromResult<MobileConnection?>(new MobileConnection("https://donysh.ir", new string('A', 64), "فضای آزمایشی"));
            },
            (title, _, _) =>
            {
                scenario.AlertTitles.Add(title);
                return scenario.ThrowOnAlert
                    ? Task.FromException(new InvalidOperationException("Simulated unavailable native alert."))
                    : Task.CompletedTask;
            },
            (_, _, _, _) =>
            {
                scenario.ConfirmationAttempts++;
                return scenario.ThrowOnConfirm
                    ? Task.FromException<bool>(new InvalidOperationException("Simulated unavailable native confirmation."))
                    : Task.FromResult(true);
            });

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

        await ExerciseConnectionButtonsAsync(shell, home, store, address, preset.SampleMessage);
        await ExerciseFailureBoundariesAsync(shell, store, address, preset.SampleMessage);
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

    private static async Task ExerciseConnectionButtonsAsync(Shell shell, MainPage home, SmsMonitorStore store,
        string address, string sampleMessage)
    {
        SmsReceivedNotifier.Notify();
        await WaitUntilAsync(() => home.SenderCount == "۱" && home.NextStepAction == "اتصال به دونیش",
            "The home CTA did not switch to the connection step for the isolated queue.");
        var previousDepth = shell.Navigation.NavigationStack.Count;
        var settings = home.FindByName<Button>("SettingsButton");
        settings.SendClicked();
        settings.SendClicked();
        await WaitUntilAsync(() => shell.CurrentPage is ConnectionPage { IsLoaded: true },
            "The actual Settings Clicked handler did not open settings.");
        var page = (ConnectionPage)shell.CurrentPage;
        var scenario = _lastConnectionScenario ?? throw new InvalidOperationException("Settings did not use the isolated dependency factory.");
        await WaitUntilAsync(() => ConnectionIsReady(page),
            "Settings did not finish loading the isolated connection.");
        await Task.Delay(100);
        Check(shell.Navigation.NavigationStack.Count == previousDepth + 1,
            "Rapid Settings taps produce one navigation");
        Check(page.FindByName<Entry>("PairCodeEntry").FontFamily == "YekanBakh",
            "Settings input uses the registered Persian font");

        var send = page.FindByName<Button>("ManualSyncButton");
        var alertsBefore = scenario.AlertTitles.Count;
        send.SendClicked();
        await WaitUntilAsync(() => scenario.AlertTitles.Count > alertsBefore && send.IsEnabled
            && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
            "Offline manual sending did not return to a usable state.");
        Check(scenario.RequestCount == 0 && (await store.GetSyncSummaryAsync()).Pending == 1,
            "Actual Send button preserves the queue while offline");

        scenario.Connectivity.ThrowOnNetworkRead = true;
        alertsBefore = scenario.AlertTitles.Count;
        send.SendClicked();
        await WaitUntilAsync(() => scenario.AlertTitles.Count > alertsBefore && send.IsEnabled
            && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
            "A network-permission failure escaped the Send handler.");
        Check(scenario.RequestCount == 0, "Send survives an unavailable network-state permission");

        scenario.Connectivity.ThrowOnNetworkRead = false;
        scenario.Connectivity.Access = NetworkAccess.Internet;
        send.SendClicked();
        send.SendClicked();
        await WaitUntilAsync(() => scenario.RequestCount == 1 && send.IsEnabled
            && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
            "The failed transport did not release the Send button.");
        var pending = (await store.GetMessagesAsync((await store.GetSenderSummariesAsync()).Single().Id)).Single();
        Check(pending.DeliveryStatus == SmsDeliveryStatus.Pending && pending.DeliveryAttempts == 1,
            "Rapid Send taps attempt one batch and retain unacknowledged SMS");

        page.FindByName<Button>("ChangeConnectionButton").SendClicked();
        page.FindByName<Entry>("ServerEntry").Text = "https://donysh.ir";
        page.FindByName<Entry>("PairCodeEntry").Text = new string('A', 32);
        var connect = page.FindByName<Button>("ConnectButton");
        alertsBefore = scenario.AlertTitles.Count;
        connect.SendClicked();
        connect.SendClicked();
        await WaitUntilAsync(() => scenario.RequestCount == 2 && scenario.AlertTitles.Count > alertsBefore
            && connect.IsEnabled && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
            "Offline pairing did not recover the connection form.");
        Check(scenario.AlertTitles.Last() == "اتصال کامل نشد", "Pairing transport failure is shown without saving credentials");
        await shell.Navigation.PopAsync(animated: false);

        // The CTA has its own handler; exercising it catches a route regression that
        // directly constructing ConnectionPage cannot detect.
        await WaitUntilAsync(() => shell.CurrentPage == home && home.IsLoaded, "Home did not return after settings.");
        var nextStep = home.FindByName<Button>("NextStepButton");
        nextStep.SendClicked();
        nextStep.SendClicked();
        await WaitUntilAsync(() => shell.CurrentPage is ConnectionPage { IsLoaded: true },
            "The actual next-step Clicked handler did not open settings.");
        await Task.Delay(100);
        Check(shell.Navigation.NavigationStack.Count == previousDepth + 1,
            "Rapid connection CTA taps produce one navigation");
        await shell.Navigation.PopAsync(animated: false);

        var unavailableSubscription = new ConnectionScenario();
        unavailableSubscription.Connectivity.ThrowOnSubscribe = true;
        await VisitAsync(shell, CreateConnectionPage(store, unavailableSubscription), async faultyPage =>
        {
            await WaitUntilAsync(() => !string.IsNullOrWhiteSpace(faultyPage.FindByName<Label>("ConnectionStatusLabel").Text),
                "A connectivity-subscription permission failure interrupted settings startup.");
            Check(unavailableSubscription.Connectivity.SubscriptionAttempts > 0,
                "Settings survives connectivity listener registration failure");
        });

        // This fresh queue avoids the retry delay of the intentionally failed batch.
        var acknowledgedStore = IsolatedStore(Path.Combine(DataDirectory!, "acknowledged-send"));
        Check(await acknowledgedStore.AddSenderAsync(address) == AddSenderResult.Added
            && await acknowledgedStore.CaptureAsync(address, sampleMessage, DateTimeOffset.UtcNow),
            "Fresh isolated queue prepared for a receipt test");
        var acknowledged = new ConnectionScenario { AcknowledgeMessages = true };
        acknowledged.Connectivity.Access = NetworkAccess.Internet;
        await VisitAsync(shell, CreateConnectionPage(acknowledgedStore, acknowledged), async receiptPage =>
        {
            var receiptSend = receiptPage.FindByName<Button>("ManualSyncButton");
            await WaitUntilAsync(() => ConnectionIsReady(receiptPage), "The receipt-test Send button did not become enabled.");
            receiptSend.SendClicked();
            await WaitUntilAsync(() => acknowledged.RequestCount == 1 && receiptSend.IsEnabled
                && !receiptPage.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
                "The receipt-test Send operation did not finish.");
            var summary = await acknowledgedStore.GetSyncSummaryAsync();
            Check(summary.Sent == 1 && summary.Pending == 0,
                "Actual Send button marks SMS sent only after an exact simulated receipt");
        });
    }

    private static async Task VisitAsync<TPage>(Shell shell, TPage page, Func<TPage, Task> verify) where TPage : Page
    {
        await shell.Navigation.PushAsync(page, animated: false);
        await WaitUntilAsync(() => page.IsLoaded && page.Handler is not null, $"{typeof(TPage).Name} did not load.");
        await verify(page);
        Check(true, $"Runtime page: {typeof(TPage).Name}");
        await shell.Navigation.PopAsync(animated: false);
    }

    private static async Task ExerciseFailureBoundariesAsync(Shell shell, SmsMonitorStore store,
        string address, string sampleMessage)
    {
        var unavailableCredentials = new ConnectionScenario();
        unavailableCredentials.Connectivity.Access = NetworkAccess.Internet;
        await VisitAsync(shell, CreateConnectionPage(store, unavailableCredentials), async page =>
        {
            var send = page.FindByName<Button>("ManualSyncButton");
            await WaitUntilAsync(() => ConnectionIsReady(page), "Credential-failure settings did not finish loading.");
            unavailableCredentials.ThrowOnConnectionLoad = true;
            var previousLoads = unavailableCredentials.ConnectionLoadAttempts;
            send.SendClicked();
            await WaitUntilAsync(() => unavailableCredentials.ConnectionLoadAttempts > previousLoads
                && unavailableCredentials.AlertTitles.Count > 0 && send.IsEnabled
                && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
                "The actual Send handler did not recover from credential-loader failure.");
            Check(unavailableCredentials.RequestCount == 0 && (await store.GetSyncSummaryAsync()).Pending == 1,
                "Send preserves the queue when credential storage becomes unavailable");
        });

        var unavailableAlert = new ConnectionScenario { ThrowOnAlert = true };
        await VisitAsync(shell, CreateConnectionPage(store, unavailableAlert), async page =>
        {
            var send = page.FindByName<Button>("ManualSyncButton");
            await WaitUntilAsync(() => ConnectionIsReady(page), "Alert-failure settings did not finish loading.");
            send.SendClicked();
            await WaitUntilAsync(() => unavailableAlert.AlertTitles.Count == 1 && send.IsEnabled
                && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
                "A failing native alert escaped the actual Send handler.");
            Check(unavailableAlert.RequestCount == 0 && (await store.GetSyncSummaryAsync()).Pending == 1,
                "Send remains usable when the native alert cannot open");
        });

        var unavailableConfirmation = new ConnectionScenario { ThrowOnConfirm = true };
        await VisitAsync(shell, CreateConnectionPage(store, unavailableConfirmation), async page =>
        {
            await WaitUntilAsync(() => ConnectionIsReady(page),
                "Confirmation-failure settings did not finish loading.");
            page.FindByName<Button>("ChangeConnectionButton").SendClicked();
            page.FindByName<Entry>("ServerEntry").Text = "https://donysh.ir";
            page.FindByName<Entry>("PairCodeEntry").Text = new string('A', 32);
            var connect = page.FindByName<Button>("ConnectButton");
            connect.SendClicked();
            await WaitUntilAsync(() => unavailableConfirmation.ConfirmationAttempts == 1 && connect.IsEnabled
                && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
                "A failing native confirmation escaped the actual Connect handler.");
            Check(unavailableConfirmation.RequestCount == 0 && (await store.GetSyncSummaryAsync()).Pending == 1,
                "Connection changes fail closed when the destination confirmation cannot open");
        });

        var unsubscribeFailure = new ConnectionScenario();
        unsubscribeFailure.Connectivity.ThrowOnUnsubscribe = true;
        await VisitAsync(shell, CreateConnectionPage(store, unsubscribeFailure), async page =>
        {
            await WaitUntilAsync(() => ConnectionIsReady(page),
                "Unsubscribe-failure settings did not finish loading.");
        });
        Check(unsubscribeFailure.Connectivity.UnsubscriptionAttempts == 1,
            "Popping settings survives a connectivity listener removal failure");

        var damagedBytes = "DNYSHSMS1"u8.ToArray().Concat(new byte[32]).ToArray();
        var damagedDirectory = Path.Combine(DataDirectory!, "damaged-connection-queue");
        var damagedStore = IsolatedStore(damagedDirectory);
        var damagedPath = Path.Combine(damagedDirectory, "sms-monitor-state.json");
        await File.WriteAllBytesAsync(damagedPath, damagedBytes);
        var damaged = new ConnectionScenario();
        await VisitAsync(shell, CreateConnectionPage(damagedStore, damaged), async page =>
        {
            await WaitUntilAsync(() => page.FindByName<Label>("LastStatusLabel").Text?.Contains("خواندن اطلاعات کامل نشد", StringComparison.Ordinal) == true,
                "Settings did not show the damaged-queue recovery state.");
            Check(!page.FindByName<Button>("ManualSyncButton").IsEnabled && damaged.RequestCount == 0,
                "Settings loads safely with an unreadable encrypted queue");
        });
        Check((await File.ReadAllBytesAsync(damagedPath)).SequenceEqual(damagedBytes),
            "Opening settings preserves unreadable queue bytes");

        // A queue may become unreadable after a usable settings page has already opened.
        var sendFailureDirectory = Path.Combine(DataDirectory!, "queue-failure-during-send");
        var sendFailureStore = IsolatedStore(sendFailureDirectory);
        Check(await sendFailureStore.AddSenderAsync(address) == AddSenderResult.Added
            && await sendFailureStore.CaptureAsync(address, sampleMessage, DateTimeOffset.UtcNow),
            "Fresh isolated queue prepared for a storage-failure test");
        var sendFailure = new ConnectionScenario();
        sendFailure.Connectivity.Access = NetworkAccess.Internet;
        await VisitAsync(shell, CreateConnectionPage(sendFailureStore, sendFailure), async page =>
        {
            var send = page.FindByName<Button>("ManualSyncButton");
            await WaitUntilAsync(() => ConnectionIsReady(page), "Queue-failure settings did not finish loading.");
            var path = Path.Combine(sendFailureDirectory, "sms-monitor-state.json");
            await File.WriteAllBytesAsync(path, damagedBytes);
            send.SendClicked();
            await WaitUntilAsync(() => sendFailure.AlertTitles.Count > 0 && send.IsEnabled
                && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning,
                "The actual Send handler did not recover from an unreadable queue.");
            Check(sendFailure.RequestCount == 0 && (await File.ReadAllBytesAsync(path)).SequenceEqual(damagedBytes),
                "Send reports a queue read failure without overwriting the stored bytes");
        });
    }

    private static SmsMonitorStore IsolatedStore(string directory) =>
        new(directory, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StorageKey.ToArray());
        });

    private static bool ConnectionIsReady(ConnectionPage page) =>
        !string.IsNullOrWhiteSpace(page.FindByName<Label>("ConnectionStatusLabel").Text)
        && page.FindByName<Button>("ManualSyncButton").IsEnabled
        && !page.FindByName<ActivityIndicator>("BusyIndicator").IsRunning;

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

    private sealed class ConnectionScenario
    {
        public FakeConnectivity Connectivity { get; } = new();
        public List<string> AlertTitles { get; } = [];
        public int RequestCount { get; set; }
        public bool AcknowledgeMessages { get; init; }
        public int ConnectionLoadAttempts { get; set; }
        public int ConfirmationAttempts { get; set; }
        public bool ThrowOnConnectionLoad { get; set; }
        public bool ThrowOnAlert { get; init; }
        public bool ThrowOnConfirm { get; init; }
    }

    private sealed class FakeConnectivity : IConnectivity
    {
        public NetworkAccess Access { get; set; } = NetworkAccess.None;
        public bool ThrowOnNetworkRead { get; set; }
        public bool ThrowOnSubscribe { get; set; }
        public bool ThrowOnUnsubscribe { get; set; }
        public int SubscriptionAttempts { get; private set; }
        public int UnsubscriptionAttempts { get; private set; }
        public IEnumerable<ConnectionProfile> ConnectionProfiles => [ConnectionProfile.WiFi];
        public NetworkAccess NetworkAccess => ThrowOnNetworkRead
            ? throw new PermissionException("Simulated missing network-state permission.") : Access;
        private EventHandler<ConnectivityChangedEventArgs>? _changed;
        public event EventHandler<ConnectivityChangedEventArgs> ConnectivityChanged
        {
            add
            {
                SubscriptionAttempts++;
                if (ThrowOnSubscribe) throw new PermissionException("Simulated connectivity listener failure.");
                _changed += value;
            }
            remove
            {
                UnsubscriptionAttempts++;
                if (ThrowOnUnsubscribe) throw new PermissionException("Simulated connectivity listener removal failure.");
                _changed -= value;
            }
        }
    }

    private sealed class StubTransport(ConnectionScenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            scenario.RequestCount++;
            if (!scenario.AcknowledgeMessages) throw new HttpRequestException("Simulated offline transport; no real network request was made.");
            if (request.RequestUri?.AbsolutePath != "/api/mobile/sms")
                throw new InvalidOperationException("The runtime test never permits successful pairing or credential writes.");
            var batch = await request.Content!.ReadFromJsonAsync<SmsBatch>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SmsBatchResult(batch!.Messages.Select(message => new SmsResult(message.Key, "created")).ToList()))
            };
        }
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
