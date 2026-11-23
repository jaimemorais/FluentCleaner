using FluentCleaner.Services;
using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentCleaner.Views;

public sealed partial class SettingsPage : Page
{
    private bool _pageReady; // true when the page has finished loading and is ready to handle events
    private string _groqKey = "";
    private string _openAiKey = "";
    private string _anthropicKey = "";
    private string _currentProvider = "Groq";
    private bool _navigationReady;

    public SettingsPageViewModel ViewModel { get; } = new();
    public string AppVersion => AppInfo.DisplayVersion;
    public Visibility InsiderBadgeVisibility => AppInfo.IsInsider ? Visibility.Visible : Visibility.Collapsed;

    public SettingsPage()
    {
        InitializeComponent();
        _navigationReady = true;
        ShowSettingsSection("General");
        AiProviderBox.ItemsSource = new[] { "Groq", "OpenAI", "Anthropic" };
        Loaded += (_, _) =>
        {
            _pageReady = false;
            ViewModel.Refresh();                                    //sync database toggles, paths, theme
            LoadAiSettings();
            _pageReady = true;
        };
    }

    // The settings page has one local navigator and one content surface. Keeping the
    // sections here avoids a second Frame/router while still killing the long one-pager.
    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_navigationReady || SettingsNav.SelectedItem is not ListViewItem item) return;
        ShowSettingsSection(item.Tag as string ?? "General");
    }

    private void ShowSettingsSection(string section)
    {
        AppearanceSection.Visibility = DatabaseSection.Visibility =
            section == "General" ? Visibility.Visible : Visibility.Collapsed;
        CookiesSection.Visibility = section == "Cookies" ? Visibility.Visible : Visibility.Collapsed;
        ExclusionsSection.Visibility = section == "Exclusions" ? Visibility.Visible : Visibility.Collapsed;
        PostCleanSection.Visibility = section == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = section == "History" ? Visibility.Visible : Visibility.Collapsed;
        AiSection.Visibility = section == "AI" ? Visibility.Visible : Visibility.Collapsed;
        BackupSection.Visibility = AboutSection.Visibility =
            section == "About" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.ChangeView(null, 0, null, true);
    }

    private async void Link_GitHub(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.GitHub);
    private async void Link_Issues(object sender, RoutedEventArgs e)   => await AppLinks.OpenAsync(AppLinks.Issues);
    private async void Link_Faq(object sender, RoutedEventArgs e)        => await AppLinks.OpenAsync(AppLinks.Faq);
    private async void Link_IconCredit(object sender, RoutedEventArgs e) => await AppLinks.OpenAsync(AppLinks.IconCredit);

    private void LoadAiSettings()
    {
        _groqKey = AppSettings.Instance.GroqApiKey ?? "";
        _openAiKey = AppSettings.Instance.OpenAiApiKey ?? "";
        _anthropicKey = AppSettings.Instance.AnthropicApiKey ?? "";
        _currentProvider = AppSettings.Instance.AiProvider is "OpenAI" or "Anthropic"
            ? AppSettings.Instance.AiProvider : "Groq";

        AiProviderBox.SelectedItem = _currentProvider;
        ApplyProviderToUi();
    }

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageReady) return;

        StashCurrentKey();
        _currentProvider = AiProviderBox.SelectedItem as string ?? "Groq";
        ApplyProviderToUi();

        AppSettings.Instance.AiProvider = _currentProvider;
        AppSettings.Instance.Save();
    }

    private void StashCurrentKey()
    {
        switch (_currentProvider)
        {
            case "OpenAI": _openAiKey = ApiKeyBox.Password.Trim(); break;
            case "Anthropic": _anthropicKey = ApiKeyBox.Password.Trim(); break;
            default: _groqKey = ApiKeyBox.Password.Trim(); break;
        }
    }

    private void ApplyProviderToUi()
    {
        (ApiKeyBox.Password, ApiKeyBox.PlaceholderText, btnGetApiKey.NavigateUri) = _currentProvider switch
        {
            "OpenAI"    => (_openAiKey, "sk-...", new Uri("https://platform.openai.com/api-keys")),
            "Anthropic" => (_anthropicKey, "sk-ant-...", new Uri("https://console.anthropic.com/settings/keys")),
            _           => (_groqKey, "gsk_...", new Uri("https://console.groq.com/keys")),
        };

        lblApiTestResult.Text = "";
        lblApiTestResult.Visibility = Visibility.Collapsed;
    }

    // Saves every provider key so switching providers never drops an edit.
    private void ApiKeySave_Click(object sender, RoutedEventArgs e)
    {
        StashCurrentKey();
        AppSettings.Instance.AiProvider = _currentProvider;
        AppSettings.Instance.GroqApiKey = _groqKey.Length == 0 ? null : _groqKey;
        AppSettings.Instance.OpenAiApiKey = _openAiKey.Length == 0 ? null : _openAiKey;
        AppSettings.Instance.AnthropicApiKey = _anthropicKey.Length == 0 ? null : _anthropicKey;
        AppSettings.Instance.Save();
    }

    // Sends one short request instead of accepting a key based on its prefix.
    private async void ApiKeyTest_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) { lblApiTestResult.Text = ResourceService.Get("St_ApiKeyMissing"); lblApiTestResult.Visibility = Visibility.Visible; return; }

        btnTestKey.IsEnabled        = false;
        lblApiTestResult.Text       = ResourceService.Get("St_ApiKeyTesting");
        lblApiTestResult.Visibility = Visibility.Visible;
        lblApiTestResult.Text       = await AiExplainer.TestKeyAsync(key, _currentProvider);
        btnTestKey.IsEnabled        = true;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".ini");

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.CustomPath = file.Path;
    }

    // Shows the built-in protected paths read-only;these are always skipped, no matter what the database says
    private async void ProtectedPaths_Click(object sender, RoutedEventArgs e)
    {
        var list = new TextBox
        {
            Text            = string.Join("\n", CleaningService.ProtectedPaths),
            IsReadOnly      = true,
            FontFamily      = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize        = 12,
            TextWrapping    = TextWrapping.NoWrap,
            AcceptsReturn   = true,
            BorderThickness = new Thickness(0)
        };

        await new ContentDialog
        {
            XamlRoot        = XamlRoot,
            RequestedTheme  = ActualTheme,
            Title           = ResourceService.Get("DlgProtectedTitle"),
            Content         = new StackPanel
            {
                Spacing  = 10,
                Children =
                {
                    new TextBlock { Text = ResourceService.Get("DlgProtectedDesc"), TextWrapping = TextWrapping.Wrap },
                    list
                }
            },
            CloseButtonText = ResourceService.Get("DlgExplainClose")
        }.ShowAsync();
    }

    // --- Export / Import settings -----------------------------------------

    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop, SuggestedFileName = "settings" };
        picker.FileTypeChoices.Add("JSON", [".json"]);

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            AppSettings.ExportTo(file.Path);
            ViewModel.StatusText = ResourceService.Fmt("St_ExportSuccess", file.Name);
        }
        catch (Exception ex) { ViewModel.StatusText = ResourceService.Fmt("St_ExportFailed", ex.Message); }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add(".json");

        var hwnd = WindowNative.GetWindowHandle((Application.Current as App)?.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            AppSettings.ImportFrom(file.Path);
            ViewModel.Refresh();
            CookiesSection.ReloadSettings();
            ViewModel.StatusText = ResourceService.Fmt("St_ImportSuccess", file.Name);
        }
        catch (Exception ex) { ViewModel.StatusText = ResourceService.Fmt("St_ImportFailed", ex.Message); }
    }
}
