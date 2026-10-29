using System.Text.Json.Serialization;

namespace CodexUsageWidget;

public sealed class RateLimitResponse
{
    [JsonPropertyName("rateLimits")]
    public RateLimitSnapshot? RateLimits { get; set; }

    [JsonPropertyName("rateLimitResetCredits")]
    public RateLimitResetCreditsSnapshot? RateLimitResetCredits { get; set; }
}

public sealed class RateLimitSnapshot
{
    [JsonIgnore]
    public Guid? ConnectionSessionId { get; set; }

    [JsonIgnore]
    public int? ResetOpportunityCount { get; set; }

    [JsonIgnore]
    public long? ResetOpportunityExpiresAt { get; set; }

    [JsonIgnore]
    public bool ResetOpportunityReported { get; set; }

    [JsonPropertyName("limitId")]
    public string? LimitId { get; set; }

    [JsonPropertyName("limitName")]
    public string? LimitName { get; set; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; set; }

    [JsonPropertyName("primary")]
    public RateLimitWindow? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public RateLimitWindow? Secondary { get; set; }

    [JsonPropertyName("credits")]
    public CreditsSnapshot? Credits { get; set; }

    [JsonPropertyName("rateLimitReachedType")]
    public string? RateLimitReachedType { get; set; }
}

public sealed class RateLimitWindow
{
    [JsonRequired]
    [JsonPropertyName("usedPercent")]
    public int UsedPercent { get; set; }

    [JsonPropertyName("windowDurationMins")]
    public long? WindowDurationMins { get; set; }

    [JsonPropertyName("resetsAt")]
    public long? ResetsAt { get; set; }
}

public sealed class CreditsSnapshot
{
    [JsonPropertyName("hasCredits")]
    public bool HasCredits { get; set; }

    [JsonPropertyName("unlimited")]
    public bool Unlimited { get; set; }

    [JsonPropertyName("balance")]
    public string? Balance { get; set; }
}

public sealed class RateLimitResetCreditsSnapshot
{
    [JsonPropertyName("availableCount")]
    public int AvailableCount { get; set; }

    [JsonPropertyName("credits")]
    public List<RateLimitResetCreditSnapshot> Credits { get; set; } = [];

    public long? FindNextExpiry() => (Credits ?? [])
        .Where(credit => credit is not null &&
            string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase))
        .Where(credit => credit.ExpiresAt is >= -62135596800 and <= 253402300799)
        .Select(credit => credit.ExpiresAt)
        .DefaultIfEmpty()
        .Min();
}

public sealed class RateLimitResetCreditSnapshot
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("resetType")]
    public string? ResetType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("grantedAt")]
    public long? GrantedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public long? ExpiresAt { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
