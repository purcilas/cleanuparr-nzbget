using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Domain.Exceptions;
using Cleanuparr.Shared.Attributes;

namespace Cleanuparr.Persistence.Models.Configuration;

/// <summary>
/// Configuration for a specific download client
/// </summary>
[Table("download_clients")]
public sealed record DownloadClientConfig
{
    /// <summary>
    /// Unique identifier for this client
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Whether this client is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Friendly name for this client
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Type name of download client
    /// </summary>
    public required DownloadClientTypeName TypeName { get; set; }
    
    /// <summary>
    /// Type of download client
    /// </summary>
    public required DownloadClientType Type { get; set; }
    
    /// <summary>
    /// Host address for the download client
    /// </summary>
    public Uri? Host { get; set; }
    
    /// <summary>
    /// Username for authentication
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password for authentication
    /// </summary>
    [SensitiveData]
    public string? Password { get; set; }
    
    /// <summary>
    /// The base URL path component, used by clients like Transmission and Deluge
    /// </summary>
    public string? UrlBase { get; set; }

    /// <summary>
    /// Optional external URL for notifications when internal Docker URLs are not reachable externally
    /// </summary>
    public Uri? ExternalUrl { get; set; }

    /// <summary>
    /// The computed full URL for the client
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public Uri Url => new($"{Host?.ToString().TrimEnd('/')}/{UrlBase?.TrimStart('/').TrimEnd('/')}");

    /// <summary>
    /// Returns ExternalUrl if set, otherwise falls back to computed Url
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public Uri ExternalOrInternalUrl => ExternalUrl ?? Url;

    /// <summary>
    /// The path prefix reported by the download client (e.g., "/downloads").
    /// Replaced with <see cref="DownloadDirectoryTarget"/> when resolving file paths across all features.
    /// </summary>
    public string? DownloadDirectorySource { get; set; }

    /// <summary>
    /// The actual local mount path (e.g., "/data/downloads").
    /// Replaces <see cref="DownloadDirectorySource"/> in file paths for hardlink checking and orphan detection.
    /// </summary>
    public string? DownloadDirectoryTarget { get; set; }

    /// <summary>
    /// Validates the configuration
    /// </summary>
    [JsonIgnore]
    public string? UsenetOptionsJson { get; set; }

    [NotMapped]
    public UsenetOptions UsenetOptions
    {
        get => string.IsNullOrEmpty(UsenetOptionsJson) ? new() : System.Text.Json.JsonSerializer.Deserialize<UsenetOptions>(UsenetOptionsJson)!;
        set => UsenetOptionsJson = System.Text.Json.JsonSerializer.Serialize(value);
    }

    public void Validate()
    {
        if (TypeName == DownloadClientTypeName.NZBGet)
        {
            if (Type != DownloadClientType.Usenet) throw new ValidationException("NZBGet requires the Usenet protocol");
            if (Host is null || !Host.IsAbsoluteUri || (Host.Scheme != "http" && Host.Scheme != "https") || !string.IsNullOrEmpty(Host.UserInfo) || !string.IsNullOrEmpty(Host.Query) || !string.IsNullOrEmpty(Host.Fragment))
                throw new ValidationException("NZBGet requires an absolute HTTP(S) host without embedded credentials");
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                throw new ValidationException("NZBGet requires RPC username and password");
            if (UrlBase?.Contains("..") == true || UrlBase?.Contains('?') == true || UrlBase?.Contains('#') == true)
                throw new ValidationException("NZBGet URL base must be a path");
            UsenetOptions.Validate();
        }
        else if (Type == DownloadClientType.Usenet) throw new ValidationException("Only NZBGet is supported for Usenet");

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ValidationException($"Client name cannot be empty");
        }

        if (Host is null)
        {
            throw new ValidationException($"Host cannot be empty");
        }

        if (!string.IsNullOrWhiteSpace(DownloadDirectorySource) != !string.IsNullOrWhiteSpace(DownloadDirectoryTarget))
        {
            throw new ValidationException("Both download directory source and target must be set, or both must be empty");
        }
    }
}
