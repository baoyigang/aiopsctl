namespace AiOps;

public sealed class IncidentWorker(Store db) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token) { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); try { while (await timer.WaitForNextTickAsync(token)) { db.Atomic(() => { foreach (var space in new[] { "real", "demo" }) foreach (var incident in db.List<Incident>(space, "incidents").Where(x => x.Diagnosis == null)) { db.Put(space, "incidents", incident.Id, incident with { Diagnosis = Rules.Analyze(incident.Name + " target=" + incident.Target) }); } }); } } catch (OperationCanceledException) when (token.IsCancellationRequested) { } }
}
