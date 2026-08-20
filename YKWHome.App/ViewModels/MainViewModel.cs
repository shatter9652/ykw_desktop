using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YKWHome.Core.Cloud;
using YKWHome.Core.Saves;

namespace YKWHome.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CloudService _cloud = new();
    private readonly IconService _icons = new();
    private SaveHandler? _currentSave;
    private GameInfo? _currentGame;

    // Logging
    public event Action<string>? Log;
    private void _log(string msg) { Log?.Invoke(msg); Console.WriteLine($"[ykw] {msg}"); }

    // ── Observable State ──────────────────────────────────
    [ObservableProperty] public partial string AppVersion { get; set; } = "1.0.0";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Ready";
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsLoggedIn { get; set; }
    [ObservableProperty] public partial string CurrentView { get; set; } = "home";
    [ObservableProperty] public partial string? UserDisplayName { get; set; }
    [ObservableProperty] public partial string? UserAvatarUrl { get; set; }
    [ObservableProperty] public partial GameInfo? SelectedGame { get; set; }
    [ObservableProperty] public partial YokaiEntry? SelectedYokai { get; set; }
    [ObservableProperty] public partial int SelectedBoxIndex { get; set; }

    // ── Collections ───────────────────────────────────────
    public ObservableCollection<GameInfo> Games { get; } = [];
    public ObservableCollection<YokaiEntry> YokaiGrid { get; } = [];
    public ObservableCollection<CloudYokai> CloudBox { get; } = [];
    public ObservableCollection<string> BoxTabs { get; } = [];

    // ── Auth State ────────────────────────────────────────
    [ObservableProperty] public partial string AuthEmail { get; set; } = "";
    [ObservableProperty] public partial string AuthPassword { get; set; } = "";
    [ObservableProperty] public partial string AuthName { get; set; } = "";
    [ObservableProperty] public partial bool AuthRememberMe { get; set; } = true;
    [ObservableProperty] public partial bool HasIcons { get; set; }
    [ObservableProperty] public partial bool ShowAuthModal { get; set; }
    [ObservableProperty] public partial string AuthMode { get; set; } = "login"; // login | signup
    public bool IsSignUpMode => AuthMode == "signup";
    public string AuthButtonText => AuthMode == "signup" ? "Create Account" : "Sign In";

    partial void OnAuthModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSignUpMode));
        OnPropertyChanged(nameof(AuthButtonText));
    }

    // ── Settings ──────────────────────────────────────────
    [ObservableProperty] public partial string SettingsName { get; set; } = "";
    [ObservableProperty] public partial string SettingsEmail { get; set; } = "";

    public MainViewModel()
    {
        _log($"Initializing YKW Home...");
        _log($"Icons available: {_icons.HasIcons}");
        HasIcons = _icons.HasIcons;
        // Initialize box tabs (local mode: 9000 boxes)
        for (int i = 0; i < 100; i++)
            BoxTabs.Add($"Box {i + 1}");
        _log("Ready");
    }

    // ── File Operations ───────────────────────────────────

    [RelayCommand]
    private async Task OpenSaveFileAsync()
    {
        var topLevel = App.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Open YKW Save Files (select game*.yw + head.yw, or YW4 data.bin)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Yo-kai Watch Saves")
                {
                    Patterns = ["*.yw", "*.yw_g", "*.bin", "data.bin"]
                },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                {
                    Patterns = ["*.*"]
                }
            }
        });

        if (files.Count == 0) return;

        // Handle YW4 directory structure (select data.bin inside USERDATA00/)
        if (files.Count == 1)
        {
            var path = files[0].Path.LocalPath;
            // Check if this is a YW4 data.bin
            if (Path.GetFileName(path) == "data.bin")
            {
                // Walk up to find the slot directory
                var dir = Path.GetDirectoryName(path);
                if (dir != null)
                {
                    var slotDir = Path.GetDirectoryName(dir); // USERDATA00 -> slot dir
                    if (slotDir != null)
                    {
                        var gameDir = Path.GetDirectoryName(slotDir); // slot dir -> game dir
                        if (gameDir != null && SaveHandler.DetectGame(gameDir) == GameType.YW4)
                        {
                            await LoadSaveFileAsync(gameDir);
                            return;
                        }
                    }
                }
            }
            await LoadSaveFileAsync(path);
        }
        else
        {
            // Multiple files: load each as a separate game
            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                try
                {
                    await LoadSaveFileAsync(path);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to load {Path.GetFileName(path)}: {ex.Message}";
                }
            }
        }
    }

    public async Task LoadSaveFileAsync(string path)
    {
        IsLoading = true;
        StatusMessage = $"Loading {Path.GetFileName(path)}...";

        try
        {
            var handler = new SaveHandler();
            _log($"Loading save: {Path.GetFileName(path)}");
            var game = handler.Load(path);
            _log($"Detected: {game.GameName} ({game.Platform}) — {game.YokaiCount} yokai");

            // Add to games list (avoid duplicates)
            var existing = Games.FirstOrDefault(g => g.GameId == game.GameId && g.GameName == game.GameName);
            if (existing == null)
            {
                Games.Add(game);
            }
            else
            {
                // Update existing
                existing.YokaiList = game.YokaiList;
                existing.YokaiCount = game.YokaiCount;
            }

            _currentSave = handler;
            _currentGame = game;
            SelectedGame = game;

            YokaiGrid.Clear();
            string gameKey = game.GameId.ToString().ToLower();
            foreach (var yokai in game.YokaiList)
            {
                yokai.IconPath = _icons.GetIconPath(gameKey, yokai.YokaiId);
                YokaiGrid.Add(yokai);
            }

            StatusMessage = $"Loaded {game.GameName}: {game.YokaiCount} Yo-kai";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading {Path.GetFileName(path)}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportSaveFileAsync()
    {
        if (_currentSave == null) { StatusMessage = "No save loaded"; return; }

        var topLevel = App.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export Save File",
            SuggestedFileName = $"game1_exported.bin",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Save Files")
                {
                    Patterns = ["*.bin", "*.yw", "*.yw_g"]
                }
            }
        });

        if (file == null) return;

        try
        {
            byte[] data = _currentSave.ExportSave();
            await System.IO.File.WriteAllBytesAsync(file.Path.LocalPath, data);
            StatusMessage = $"Exported to {file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    // ── Navigation ────────────────────────────────────────

    [RelayCommand]
    private void NavigateTo(string view)
    {
        CurrentView = view;
        if (view == "settings" && IsLoggedIn && _cloud.CurrentUser != null)
        {
            SettingsName = _cloud.CurrentUser.Name ?? "";
            SettingsEmail = _cloud.CurrentUser.Email ?? "";
        }
    }

    [RelayCommand]
    private void SelectGame(GameInfo game)
    {
        SelectedGame = game;
        YokaiGrid.Clear();
        foreach (var yokai in game.YokaiList)
            YokaiGrid.Add(yokai);
        StatusMessage = $"{game.GameName}: {game.YokaiCount} Yo-kai";
    }

    [RelayCommand]
    private void SelectYokai(YokaiEntry? yokai)
    {
        SelectedYokai = yokai;
    }

    // ── Cloud Operations ──────────────────────────────────

    [RelayCommand]
    private void ShowLogin() { ShowAuthModal = true; AuthMode = "login"; }

    [RelayCommand]
    private void ShowSignup() { ShowAuthModal = true; AuthMode = "signup"; }

    [RelayCommand]
    private void CloseAuthModal() { ShowAuthModal = false; }

    [RelayCommand]
    private async Task LoginWithEmailAsync()
    {
        IsLoading = true;
        try
        {
            if (AuthMode == "signup")
            {
                var user = await _cloud.SignUpAsync(AuthName, AuthEmail, AuthPassword);
                if (user != null)
                {
                    IsLoggedIn = true;
                    UserDisplayName = user.Name ?? user.Email;
                    ShowAuthModal = false;
                    StatusMessage = $"Account created: {user.Name ?? user.Email}";
                }
            }
            else
            {
                var user = await _cloud.LoginAsync(AuthEmail, AuthPassword);
                if (user != null)
                {
                    IsLoggedIn = true;
                    UserDisplayName = user.Name ?? user.Email;
                    ShowAuthModal = false;
                    StatusMessage = $"Logged in as {user.Name ?? user.Email}";
                }
                else
                {
                    StatusMessage = "Login failed";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoginWithDiscordAsync()
    {
        StatusMessage = "Starting Discord login...";
        IsLoading = true;

        try
        {
            using var server = new OAuthCallbackServer(5287);

            // Start the listener FIRST, before opening browser
            var callbackTask = server.WaitForCallbackAsync(TimeSpan.FromMinutes(2));
            Console.WriteLine($"[auth] Callback server listening on {server.CallbackUrl}");

            // Get OAuth URL
            string url = await _cloud.GetDiscordOAuthUrlAsync(
                server.CallbackUrl,
                server.CallbackUrl + "?error=auth"
            );
            Console.WriteLine($"[auth] OAuth URL: {url}");

            // Open browser
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            StatusMessage = "Complete login in browser...";

            // Wait for callback
            var (userId, secret) = await callbackTask;
            Console.WriteLine($"[auth] Got callback: userId={userId}");

            StatusMessage = "Creating session...";
            var user = await _cloud.CreateSessionFromTokenAsync(userId, secret);
            if (user != null)
            {
                IsLoggedIn = true;
                UserDisplayName = user.Name ?? user.Email;
                ShowAuthModal = false;
                StatusMessage = $"Welcome, {user.Name ?? user.Email}!";
                Console.WriteLine($"[auth] Login successful: {user.Name ?? user.Email}");
            }
            else
            {
                StatusMessage = "Discord login failed — no user returned";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Login timed out (2 min)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Login error: {ex.Message}";
            Console.WriteLine($"[auth] Error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _cloud.LogoutAsync();
        IsLoggedIn = false;
        UserDisplayName = null;
        UserAvatarUrl = null;
        CloudBox.Clear();
        StatusMessage = "Logged out";
    }

    [RelayCommand]
    private async Task LoadCloudBoxesAsync()
    {
        if (!IsLoggedIn) { StatusMessage = "Not logged in"; return; }

        IsLoading = true;
        try
        {
            var boxes = await _cloud.LoadCloudBoxesAsync();
            CloudBox.Clear();
            foreach (var yokai in boxes)
                CloudBox.Add(yokai);
            StatusMessage = $"Loaded {boxes.Count} cloud Yo-kai";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cloud error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveYokaiToCloudAsync()
    {
        if (!IsLoggedIn) { StatusMessage = "Not logged in"; return; }
        if (SelectedYokai == null) { StatusMessage = "No Yo-kai selected"; return; }

        try
        {
            await _cloud.SaveYokaiToCloudAsync(SelectedBoxIndex, 0, SelectedYokai);
            StatusMessage = $"Saved {SelectedYokai.Name} to cloud box {SelectedBoxIndex + 1}";
            await LoadCloudBoxesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save error: {ex.Message}";
        }
    }

    // ── Settings ──────────────────────────────────────────

    [RelayCommand]
    private async Task UpdateDisplayNameAsync()
    {
        if (!IsLoggedIn) return;
        try
        {
            await _cloud.UpdateDisplayNameAsync(SettingsName);
            UserDisplayName = SettingsName;
            StatusMessage = "Display name updated";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
