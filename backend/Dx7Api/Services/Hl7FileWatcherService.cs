using Dx7Api.Data;
using Dx7Api.Services.Hl7;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Services;

public class Hl7FileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Hl7FileWatcherService> _logger;
    private readonly IConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = new();
    // Serialize all file processing — prevents concurrent DB writes and log file contention
    private readonly SemaphoreSlim _processLock = new(1, 1);

    public Hl7FileWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<Hl7FileWatcherService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inboxRoot = _config["Hl7:InboxPath"] ?? Path.Combine(AppContext.BaseDirectory, "HL7Inbox");
        Directory.CreateDirectory(inboxRoot);
        _logger.LogInformation("HL7 Watcher started. Root: {Path}", inboxRoot);

        // Initial scan
        await ScanRootAsync(inboxRoot, stoppingToken);

        // Watch the ROOT only (not subdirectories) for new tenant folders being created
        WatchForNewTenantFolders(inboxRoot, stoppingToken);

        // Watch each existing tenant folder directly (NOT recursively)
        foreach (var tenantDir in Directory.GetDirectories(inboxRoot))
            WatchTenantFolder(tenantDir, stoppingToken);

        // Periodic scan as safety net
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await ScanRootAsync(inboxRoot, stoppingToken);
        }
    }

    /// Watch the inbox root for new tenant subdirectories being created
    private void WatchForNewTenantFolders(string inboxRoot, CancellationToken ct)
    {
        var watcher = new FileSystemWatcher(inboxRoot)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,  // root only
            EnableRaisingEvents = true
        };
        watcher.Created += (_, e) =>
        {
            if (Directory.Exists(e.FullPath))
                WatchTenantFolder(e.FullPath, ct);
        };
        _watchers.Add(watcher);
    }

    /// Watch a single tenant folder directly — NOT recursively.
    /// This means processed/ and error/ subfolders are completely invisible.
    private void WatchTenantFolder(string tenantDir, CancellationToken ct)
    {
        if (!Directory.Exists(tenantDir)) return;

        // Create archive folders upfront so the watcher never sees files land there
        Directory.CreateDirectory(Path.Combine(tenantDir, "processed"));
        Directory.CreateDirectory(Path.Combine(tenantDir, "error"));

        var watcher = new FileSystemWatcher(tenantDir)
        {
            Filter = "*.hl7",
            IncludeSubdirectories = false,  // KEY: only watch THIS folder, not processed/ or error/
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        watcher.Created += async (_, e) =>
        {
            if (ct.IsCancellationRequested) return;
            await Task.Delay(300, ct); // let file finish writing
            await _processLock.WaitAsync(ct);
            try   { await ProcessFileAsync(e.FullPath, tenantDir, ct); }
            finally { _processLock.Release(); }
        };

        _watchers.Add(watcher);
        _logger.LogInformation("HL7: Watching tenant folder {Dir}", tenantDir);
    }

    /// Scan only the top-level of each tenant folder (not subfolders)
    private async Task ScanRootAsync(string inboxRoot, CancellationToken ct)
    {
        foreach (var tenantDir in Directory.GetDirectories(inboxRoot))
        {
            // GetFiles without SearchOption.AllDirectories = top level only
            var files = Directory.GetFiles(tenantDir, "*.hl7");
            if (files.Length > 0)
                _logger.LogInformation("HL7 Scan: {Count} pending in {Dir}", files.Length, Path.GetFileName(tenantDir));

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) return;
                await ProcessFileAsync(file, tenantDir, ct);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath, string tenantDir, CancellationToken ct)
    {
        if (!File.Exists(filePath)) return;

        var fileName   = Path.GetFileName(filePath);
        var tenantSlug = Path.GetFileName(tenantDir);

        _logger.LogInformation("HL7: Processing {File}", fileName);

        string rawContent;
        try { rawContent = await ReadWithRetryAsync(filePath); }
        catch (Exception ex)
        {
            _logger.LogError("HL7: Cannot read {File}: {Error}", fileName, ex.Message);
            return;
        }

        Hl7ProcessResult result;
        try
        {
            var msg = Hl7Parser.Parse(rawContent);

            using var scope  = _scopeFactory.CreateScope();
            var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Hl7Processor>>();

            var tenant = await db.Tenants.FirstOrDefaultAsync(t =>
                t.Code == tenantSlug || t.Name.ToLower() == tenantSlug.ToLower(), ct)
                ?? await db.Tenants.FirstOrDefaultAsync(t => t.IsActive, ct);

            if (tenant == null)
            {
                result = new Hl7ProcessResult { Status = "error", Notes = $"No tenant for: {tenantSlug}" };
            }
            else
            {
                db.CurrentTenantId = tenant.Id;
                var processor = new Hl7Processor(db, logger);
                result = await processor.ProcessAsync(msg, tenant.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HL7: Error in {File}", fileName);
            result = new Hl7ProcessResult { Status = "error", Notes = ex.Message };
        }

        // Move to processed/ or error/ (quarantine) 
        // duplicates go to quarantine — visible for review, no silent discard
        var archive = (result.Status == "error" || result.Status == "duplicate")
            ? Path.Combine(tenantDir, "error")
            : Path.Combine(tenantDir, "processed");

        var archiveName = result.Status == "duplicate" ? "dup_" + fileName : fileName;
        MoveToArchive(filePath, archive, archiveName);
        WriteLog(tenantDir, fileName, result);

        _logger.LogInformation("HL7 [{Status}] {File} — {Notes}", result.Status, fileName, result.Notes);
    }

    private static void MoveToArchive(string source, string archiveDir, string fileName)
    {
        try
        {
            var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(archiveDir, $"{ts}_{fileName}");
            File.Move(source, dest, overwrite: true);
        }
        catch (Exception ex) { Console.WriteLine($"HL7: Move failed for {fileName}: {ex.Message}"); }
    }

    private static void WriteLog(string tenantDir, string fileName, Hl7ProcessResult r)
    {
        try
        {
            // Format must match controller parser: timestamp|status|msgType|patient|accession|saved|file|notes
            var patient = string.IsNullOrEmpty(r.PatientName) ? r.PatientId : $"{r.PatientId} {r.PatientName}".Trim();
            var line = string.Join("|",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                r.Status,
                r.MessageType,
                patient,
                r.AccessionId,
                r.ResultsSaved.ToString(),
                $"File: {fileName}",
                (r.Notes ?? "").ReplaceLineEndings(" ").Trim()
            );
            File.AppendAllText(Path.Combine(tenantDir, "dx7_hl7.log"), line + Environment.NewLine);
        }
        catch { }
    }

    private static async Task<string> ReadWithRetryAsync(string path, int retries = 5)
    {
        for (int i = 0; i < retries; i++)
        {
            try { return await File.ReadAllTextAsync(path); }
            catch (IOException) when (i < retries - 1) { await Task.Delay(200 * (i + 1)); }
        }
        return await File.ReadAllTextAsync(path);
    }

    public override void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        base.Dispose();
    }
}