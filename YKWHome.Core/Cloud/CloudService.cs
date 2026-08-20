using Appwrite;
using Appwrite.Enums;
using Appwrite.Models;
using Appwrite.Services;
using Newtonsoft.Json;
using YKWHome.Core.Saves;

namespace YKWHome.Core.Cloud;

/// <summary>
/// Cloud service for Appwrite authentication and cloud storage.
/// Handles Discord OAuth, email auth, JWT promotion, cloud yokai boxes, and save file uploads.
/// </summary>
public class CloudService : IDisposable
{
    private Client? _client;
    private Account? _account;
    private Databases? _databases;
    private Storage? _storage;
    private static void _log(string msg) => Console.WriteLine($"[cloud] {msg}");

    private string? _jwt;
    private User? _user;
    private string? _discordAccessToken;

    // Events
    public event Action<User>? UserLoggedIn;
    public event Action? UserLoggedOut;
    public event Action<string>? ErrorOccurred;

    public bool IsLoggedIn => _user != null;
    public User? CurrentUser => _user;

    public CloudService()
    {
        InitClient();
    }

    private void InitClient(string? jwt = null)
    {
        _client = new Client();
        _client.SetEndpoint(AppwriteConfig.Endpoint);
        _client.SetProject(AppwriteConfig.ProjectId);

        if (!string.IsNullOrEmpty(jwt))
            _client.SetJWT(jwt);

        _account = new Account(_client);
        _databases = new Databases(_client);
        _storage = new Storage(_client);
    }

    // ── Authentication ──────────────────────────────────────

    /// <summary>
    /// Start Discord OAuth flow. Returns the OAuth URL to open in browser.
    /// Uses the Appwrite SDK's CreateOAuth2Token which handles state properly.
    /// </summary>
    public async Task<string> GetDiscordOAuthUrlAsync(string successUrl, string failureUrl)
    {
        InitClient(); // Need a clean client (no JWT)
        var url = await _account!.CreateOAuth2Token(
            OAuthProvider.Discord,
            successUrl,
            failureUrl,
            ["identify", "email"]
        );
        return url;
    }

    /// <summary>
    /// Create a session from OAuth callback (userId + secret from URL).
    /// </summary>
    public async Task<User?> CreateSessionFromTokenAsync(string userId, string secret)
    {
        try
        {
            _log("Creating session from OAuth token...");
            InitClient(); // No JWT yet
            // Delete existing sessions first
            try { await _account!.DeleteSessions(); } catch { }

            var session = await _account!.CreateSession(userId, secret);
            _user = await _account.Get();

            // Promote to JWT
            await PromoteToJwtAsync();

            UserLoggedIn?.Invoke(_user);
            return _user;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Session creation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sign up with email/password.
    /// </summary>
    public async Task<User?> SignUpAsync(string name, string email, string password)
    {
        try
        {
            InitClient();
            await _account!.Create(ID.Unique(), email, password, name);
            var session = await _account.CreateEmailPasswordSession(email, password);
            _user = await _account.Get();
            await PromoteToJwtAsync();
            UserLoggedIn?.Invoke(_user);
            return _user;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Signup failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Login with email/password.
    /// </summary>
    public async Task<User?> LoginAsync(string email, string password)
    {
        try
        {
            _log($"Logging in with email: {email}");
            InitClient();
            var session = await _account!.CreateEmailPasswordSession(email, password);
            _user = await _account.Get();
            await PromoteToJwtAsync();
            UserLoggedIn?.Invoke(_user);
            return _user;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Login failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Try to restore session from saved JWT.
    /// </summary>
    public async Task<bool> RestoreSessionAsync(string savedJwt)
    {
        try
        {
            InitClient(savedJwt);
            _jwt = savedJwt;
            _user = await _account!.Get();
            return true;
        }
        catch
        {
            // JWT expired or invalid
            _user = null;
            _jwt = null;
            return false;
        }
    }

    /// <summary>
    /// Promote session to JWT for stateless auth.
    /// </summary>
    private async Task PromoteToJwtAsync()
    {
        try
        {
            var res = await _account!.CreateJWT(); // 1 hour default
            if (res?.Jwt != null)
            {
                _jwt = res.Jwt;
                InitClient(_jwt);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"JWT promotion failed: {ex.Message}");
        }
    }

    /// <summary>Update display name.</summary>
    public async Task UpdateDisplayNameAsync(string name)
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");
        await _account!.UpdateName(name);
        _user = await _account.Get();
    }

    /// <summary>Update email.</summary>
    public async Task UpdateEmailAsync(string email, string password)
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");
        await _account!.UpdateEmail(email, password);
    }

    /// <summary>Update password.</summary>
    public async Task UpdatePasswordAsync(string newPassword, string? oldPassword = null)
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");
        await _account!.UpdatePassword(newPassword, oldPassword);
    }

    /// <summary>Delete account.</summary>
    public async Task DeleteAccountAsync()
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");
        await _account!.UpdateStatus();
        await LogoutAsync();
    }

    /// <summary>Logout and clear session.</summary>
    public async Task LogoutAsync()
    {
        try { await _account?.DeleteSession("current"); } catch { }
        _user = null;
        _jwt = null;
        _discordAccessToken = null;
        InitClient();
        UserLoggedOut?.Invoke();
    }

    /// <summary>Get Discord profile info using the OAuth access token.</summary>
    public async Task<DiscordProfile?> GetDiscordProfileAsync()
    {
        if (_discordAccessToken == null) return null;
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _discordAccessToken);
            var response = await http.GetAsync("https://discord.com/api/users/@me");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<DiscordProfile>(json);
        }
        catch { return null; }
    }

    // ── Cloud Boxes ─────────────────────────────────────────

    /// <summary>Load all yokai from cloud boxes for the current user.</summary>
    public async Task<List<CloudYokai>> LoadCloudBoxesAsync()
    {
        if (_user == null) return [];
        _log("Loading cloud boxes...");
        try
        {
            var result = await _databases!.ListDocuments(
                databaseId: AppwriteConfig.DatabaseId,
                collectionId: AppwriteConfig.CollectionId,
                queries: [$"equal(\"user_id\", [\"{_user.Id}\"])"]
            );

            _log($"Loaded {result.Documents.Count} cloud yokai");
            return result.Documents.Select(r => new CloudYokai
            {
                DocumentId = r.Id,
                UserId = r.Data.ContainsKey("user_id") ? r.Data["user_id"]?.ToString() ?? "" : "",
                BoxNum = r.Data.ContainsKey("box_num") ? Convert.ToInt32(r.Data["box_num"]) : 0,
                Slot = r.Data.ContainsKey("slot") ? Convert.ToInt32(r.Data["slot"]) : 0,
                YokaiId = r.Data.ContainsKey("yokai_id") ? Convert.ToInt32(r.Data["yokai_id"]) : 0,
                Level = r.Data.ContainsKey("level") ? Convert.ToInt32(r.Data["level"]) : 0,
                Name = r.Data.ContainsKey("name") ? r.Data["name"]?.ToString() ?? "" : "",
                RawHex = r.Data.ContainsKey("raw_hex") ? r.Data["raw_hex"]?.ToString() ?? "" : "",
                Game = r.Data.ContainsKey("game") ? r.Data["game"]?.ToString() ?? "" : "",
                IsTeam = r.Data.ContainsKey("is_team") && Convert.ToBoolean(r.Data["is_team"]),
            }).ToList();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Failed to load cloud boxes: {ex.Message}");
            return [];
        }
    }

    /// <summary>Save a yokai to a cloud box slot.</summary>
    public async Task<CloudYokai> SaveYokaiToCloudAsync(int box, int slot, YokaiEntry yokai, string game = "yw2")
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");

        var data = new Dictionary<string, object>
        {
            ["user_id"] = _user.Id,
            ["box_num"] = box,
            ["slot"] = slot,
            ["yokai_id"] = yokai.YokaiId,
            ["level"] = yokai.Level,
            ["name"] = yokai.Name,
            ["raw_hex"] = Convert.ToHexString(yokai.RawBytes),
            ["game"] = game,
            ["is_team"] = yokai.IsTeamMember,
        };

        // Check if entry already exists
        var existing = await _databases!.ListDocuments(
            databaseId: AppwriteConfig.DatabaseId,
            collectionId: AppwriteConfig.CollectionId,
            queries: [
                $"equal(\"user_id\", [\"{_user.Id}\"])",
                $"equal(\"box_num\", [{box}])",
                $"equal(\"slot\", [{slot}])",
            ]
        );

        if (existing.Documents.Count > 0)
        {
            await _databases.UpdateDocument(
                databaseId: AppwriteConfig.DatabaseId,
                collectionId: AppwriteConfig.CollectionId,
                documentId: existing.Documents[0].Id,
                data: data
            );
        }
        else
        {
            await _databases.CreateDocument(
                databaseId: AppwriteConfig.DatabaseId,
                collectionId: AppwriteConfig.CollectionId,
                documentId: ID.Unique(),
                data: data
            );
        }

        return new CloudYokai { BoxNum = box, Slot = slot, YokaiId = yokai.YokaiId };
    }

    /// <summary>Remove a yokai from cloud boxes.</summary>
    public async Task RemoveYokaiFromCloudAsync(int box, int slot)
    {
        if (_user == null) return;
        var existing = await _databases!.ListDocuments(
            databaseId: AppwriteConfig.DatabaseId,
            collectionId: AppwriteConfig.CollectionId,
            queries: [
                $"equal(\"user_id\", [\"{_user.Id}\"])",
                $"equal(\"box_num\", [{box}])",
                $"equal(\"slot\", [{slot}])",
            ]
        );
        if (existing.Documents.Count > 0)
        {
            await _databases.DeleteDocument(
                databaseId: AppwriteConfig.DatabaseId,
                collectionId: AppwriteConfig.CollectionId,
                documentId: existing.Documents[0].Id
            );
        }
    }

    /// <summary>Move a yokai between cloud box slots.</summary>
    public async Task MoveYokaiInCloudAsync(int fromBox, int fromSlot, int toBox, int toSlot)
    {
        var boxes = await LoadCloudBoxesAsync();
        var src = boxes.FirstOrDefault(b => b.BoxNum == fromBox && b.Slot == fromSlot);
        if (src == null) return;

        await RemoveYokaiFromCloudAsync(toBox, toSlot);
        // Save to new location...
        await RemoveYokaiFromCloudAsync(fromBox, fromSlot);
    }

    // ── Save File Upload/Download ────────────────────────────

    /// <summary>Upload a save file to cloud storage.</summary>
    public async Task<string> UploadSaveFileAsync(byte[] fileData, string fileName)
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");

        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        await System.IO.File.WriteAllBytesAsync(tempPath, fileData);
        var file = new InputFile { Path = tempPath, Filename = fileName };

        var result = await _storage!.CreateFile(
            bucketId: AppwriteConfig.BucketId,
            fileId: ID.Unique(),
            file: file
        );

        return result.Id;
    }

    /// <summary>Download a save file from cloud storage.</summary>
    public async Task<byte[]> DownloadSaveFileAsync(string fileId)
    {
        if (_user == null) throw new InvalidOperationException("Not logged in");

        var result = await _storage.GetFileDownload(
            bucketId: AppwriteConfig.BucketId,
            fileId: fileId
        );

        return result;  // GetFileDownload returns byte[] directly
    }

    /// <summary>Get the list of saved files from user preferences.</summary>
    public List<SavedFileInfo> ListSaveFiles()
    {
        if (_user?.Prefs?.Data != null &&
            _user.Prefs.Data.TryGetValue("saves", out var savesObj))
        {
            var savesJson = savesObj?.ToString();
            if (!string.IsNullOrEmpty(savesJson))
                return JsonConvert.DeserializeObject<List<SavedFileInfo>>(savesJson) ?? [];
        }
        return [];
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

// ── Models ──────────────────────────────────────────────

public class CloudYokai
{
    public string DocumentId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int BoxNum { get; set; }
    public int Slot { get; set; }
    public int YokaiId { get; set; }
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public string RawHex { get; set; } = "";
    public string Game { get; set; } = "";
    public bool IsTeam { get; set; }
}

public class DiscordProfile
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("username")]
    public string Username { get; set; } = "";

    [JsonProperty("avatar")]
    public string? Avatar { get; set; }

    [JsonProperty("discriminator")]
    public string Discriminator { get; set; } = "0";

    [JsonProperty("email")]
    public string? Email { get; set; }

    public string AvatarUrl(int size = 64)
    {
        if (!string.IsNullOrEmpty(Avatar))
            return $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png?size={size}";
        int discNum = int.TryParse(Discriminator, out var d) ? d : 0;
        return $"https://cdn.discordapp.com/embed/avatars/{discNum % 5}.png";
    }
}

public class SavedFileInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("date")]
    public long Date { get; set; }
}
