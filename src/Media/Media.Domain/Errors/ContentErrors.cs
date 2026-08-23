namespace Haworks.Media.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class ContentErrors
{
    public static readonly Error FileNotFound = new("Content.FileNotFound", "File not found");
    public static readonly Error InvalidFileType = new("Content.InvalidFileType", "Invalid file type");
    public static readonly Error FileTooLarge = new("Content.FileTooLarge", "File exceeds maximum size");
    public static readonly Error UploadFailed = new("Content.UploadFailed", "File upload failed");
    public static readonly Error VirusDetected = new("Content.VirusDetected", "Virus detected in file");
    public static readonly Error NotFound = new("Content.NotFound", "Content not found.", ErrorType.NotFound);
    public static readonly Error SessionNotFound = new("Content.SessionNotFound", "Upload session not found.", ErrorType.NotFound);
    public static readonly Error EmptyFile = new("Content.EmptyFile", "No file provided or file is empty.", ErrorType.Validation);
    public static readonly Error ValidationFailed = new("Content.ValidationFailed", "File validation failed.", ErrorType.Validation);
    public static readonly Error CompletionFailed = new("Content.CompletionFailed", "Failed to complete upload session.", ErrorType.Internal);
    public static readonly Error InvalidOperation = new("Content.InvalidOperation", "Invalid operation.", ErrorType.Validation);
    public static readonly Error Forbidden = new("Content.Forbidden", "Access denied.", ErrorType.Forbidden);
    public static readonly Error InvalidChunk = new("Content.InvalidChunk", "Invalid or empty chunk file.", ErrorType.Validation);
    public static readonly Error InvalidChunkIndex = new("Content.InvalidChunkIndex", "Chunk index cannot be negative.", ErrorType.Validation);
    public static readonly Error ChunkOutOfRange = new("Content.ChunkOutOfRange", "Chunk index out of range.", ErrorType.Validation);
    public static readonly Error StreamError = new("Content.StreamError", "Stream processing error.", ErrorType.Internal);
    public static readonly Error InvalidChunkParams = new("Content.InvalidChunkParams", "Invalid chunk parameters.", ErrorType.Validation);
    public static readonly Error MetadataValidationFailed = new("Content.MetadataValidationFailed", "Metadata validation failed.", ErrorType.Validation);
    public static readonly Error InvalidArgument = new("Content.InvalidArgument", "Invalid argument.", ErrorType.Validation);
}
