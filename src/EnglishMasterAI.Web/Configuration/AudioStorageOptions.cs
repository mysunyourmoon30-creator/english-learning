namespace EnglishMasterAI.Web.Configuration;

public sealed class AudioStorageOptions
{
    public const string SectionName = "AudioStorage";

    public string Provider { get; set; } = "FileSystem";
    public string CachePath { get; set; } = "Data/AudioCache";
    public string ConnectionStringName { get; set; } = "AudioBlobStorage";
    public string ContainerName { get; set; } = "englishmaster-audio";
    public string ObjectPrefix { get; set; } = "reference-audio";
    public bool CreateContainerIfMissing { get; set; }
    public string CdnBaseUrl { get; set; } = string.Empty;

    public bool IsAzureBlob =>
        Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase);
}
