namespace YKWHome.Core.Cloud;

/// <summary>
/// Appwrite project configuration.
/// Matches the web version's config.js.
/// </summary>
public static class AppwriteConfig
{
    public const string Endpoint = "https://tor.cloud.appwrite.io/v1";
    public const string ProjectId = "6a86504b0033f733c338";
    public const string DatabaseId = "6a8656f000147e1b67b0";
    public const string CollectionId = "6a8658c8ede182b58e7e";
    public const string BucketId = "6a865718003c43ddcbc7";

    public const int MaxBoxes = 100;
    public const int MonsPerBox = 30;
    public const int MonsPerRow = 6;

    /// <summary>Local boxes for offline use (9000 boxes).</summary>
    public const int LocalMaxBoxes = 9000;
}
