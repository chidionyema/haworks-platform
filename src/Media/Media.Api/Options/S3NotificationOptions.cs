namespace Haworks.Media.Api.Options;

public sealed class S3NotificationOptions
{
    public const string SectionName = "S3Notifications";

    /// <summary>When false, S3 event notifications are disabled and the SQS consumer is not started.</summary>
    public bool Enabled { get; set; }

    /// <summary>SQS queue URL for S3 ObjectCreated events.</summary>
    public string SqsQueueUrl { get; set; } = string.Empty;

    /// <summary>AWS region for SQS client.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Polling interval in seconds. Default 5.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Max messages per SQS receive call. Default 10.</summary>
    public int MaxMessages { get; set; } = 10;

    /// <summary>SQS visibility timeout in seconds. Must exceed max scan + transcode time. Default 300.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 300;
}
