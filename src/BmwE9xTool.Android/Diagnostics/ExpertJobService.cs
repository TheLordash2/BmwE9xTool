using BmwE9xTool.Ediabas;
using BmwE9xTool.Vehicle;

namespace BmwE9xTool.Diagnostics;

public sealed class ExpertJobService
{
    private readonly EdiabasSession _session;
    public ExpertJobService(EdiabasSession session) => _session = session;

    public Task<JobResult> RunAsync(string sgbd, string job, string? args = null, CancellationToken ct = default) =>
        _session.RunJobAsync(sgbd, job, args, ct: ct);

    public Task<bool> ExistsAsync(string sgbd, string job, CancellationToken ct = default) =>
        _session.JobExistsAsync(sgbd, job, ct);
}
