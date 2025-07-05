namespace Sinos.Data.Projects;

public record ProgressReport(
    ProgressReportType ReprotType,
    string? Line, double? Progress);
