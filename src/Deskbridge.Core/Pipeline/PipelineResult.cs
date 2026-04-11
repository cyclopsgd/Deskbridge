namespace Deskbridge.Core.Pipeline;

public record PipelineResult(bool Success, string? FailureReason = null);
