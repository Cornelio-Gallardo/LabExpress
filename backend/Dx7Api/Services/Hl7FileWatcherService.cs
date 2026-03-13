using Dx7Api.Data;
using Dx7Api.Services.Hl7;
using Microsoft.EntityFrameworkCore;

namespace Dx7Api.Services;

/// <summary>
/// Background service that watches a folder for incoming HL7 files.
/// Files dropped into the inbox are parsed, processed, and moved to
/// processed/ or error/ subfolders with a log entry.
/// 
/// Folder structure:
///   HL7Inbox/
///     {tenantSlug}/           ← one folder per tenant
///       *.hl7                 ← drop files here
///       processed/            ← successfully processed
///       error/                ← failed to parse/process
///       dx7_hl7.log           ← audit log
/// </summary>
public class Hl7FileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Hl7FileWatcherService> _logger;
    private readonly IConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = new();

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

        _logger.LogInformation("HL7 Watcher started. Inbox: {Path}", inboxRoot);

        // Watch all subdirectories (one per tenant slug) for new HL7 files
        // Also scan on startup for any files dropped while service was down
        await ScanAllAsync(inboxRoot, stoppingToken);

        // Set up live watchers for each tenant folder
        SetupWatchers(inboxRoot, stoppingToken);

        // Keep running and also do a periodic scan every 30s as safety net
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            await ScanAllAsync(inboxRoot, stoppingToken);
        }
    }

    private void SetupWatchers(string inboxRoot, CancellationToken ct)
    {
        // Watch the root — picks up new tenant subdirs too
        var watcher = new FileSystemWatcher(inboxRoot)
        {
            Filter = "*.hl7",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Created += async (_, e) =>
        {
            if (ct.IsCancellationRequested) return;
            // Ignore files already in processed/ or error/ folders
            var dir = Path.GetDirectoryName(e.FullPath) ?? "";
            var dirName = Path.GetFileName(dir);
            if (dirName == "processed" || dirName == "error") return;
            if (dir.Contains(Path.DirectorySeparatorChar + "processed") ||
                dir.Contains(Path.DirectorySeparatorChar + "error")) return;
            // Brief delay to ensure file is fully written
            await Task.Delay(500, ct);
            await ProcessFileAsync(e.FullPath, ct);
        };

        _watchers.Add(watcher);
        _logger.LogInformation("HL7 Watcher watching: {Path}/**/*.hl7", inboxRoot);
    }

    private async Task ScanAllAsync(string inboxRoot, CancellationToken ct)
    {
        var files = Directory.GetFiles(inboxRoot, "*.hl7", SearchOption.AllDirectories)
            .Where(f => {
                var dir = Path.GetDirectoryName(f) ?? "";
                var dirName = Path.GetFileName(dir);
                // Skip if file is inside a processed/ or error/ folder (at any depth)
                return dirName != "processed" && dirName != "error"
                    && !dir.Contains(Path.DirectorySeparatorChar + "processed" + Path.DirectorySeparatorChar)
                    && !dir.Contains(Path.DirectorySeparatorChar + "error" + Path.DirectorySeparatorChar);
            })
            .ToList();

        if (files.Count > 0)
            _logger.LogInformation("HL7 Startup scan: found {Count} pending file(s)", files.Count);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessFileAsync(file, ct);
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath)) return;

        var fileName = Path.GetFileName(filePath);
        var dir      = Path.GetDirectoryName(filePath)!;

        // Infer tenant from folder name (parent of the file)
        // Structure: HL7Inbox/{tenantSlug}/file.hl7
        var tenantSlug = Path.GetFileName(dir);

        _logger.LogInformation("HL7: Processing {File} (tenant: {Slug})", fileName, tenantSlug);

        string rawContent;
        try
        {
            // Read with retry for file locks
            rawContent = await ReadWithRetryAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("HL7: Cannot read {File}: {Error}", fileName, ex.Message);
            return;
        }

        Hl7ProcessResult result;
        try
        {
            var msg = Hl7Parser.Parse(rawContent);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Resolve tenant
            var tenant = await db.Tenants
                .FirstOrDefaultAsync(t => t.Code == tenantSlug || t.Name.ToLower() == tenantSlug.ToLower(), ct);

            if (tenant == null)
            {
                // Fallback: use first active tenant (single-tenant deployments)
                tenant = await db.Tenants.FirstOrDefaultAsync(t => t.IsActive, ct);
            }

            if (tenant == null)
            {
                _logger.LogWarning("HL7: No tenant found for slug '{Slug}', skipping {File}", tenantSlug, fileName);
                MoveFile(filePath, Path.Combine(dir, "error"), fileName);
                return;
            }

            // Disable global query filter for this operation
            db.CurrentTenantId = tenant.Id;

            var processor = new Hl7Processor(db, scope.ServiceProvider.GetRequiredService<ILogger<Hl7Processor>>());
            result = await processor.ProcessAsync(msg, tenant.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HL7: Parse/process error for {File}", fileName);
            result = new Hl7ProcessResult
            {
                Status = "error",
                Notes  = ex.Message,
                MessageId = fileName
            };
        }

        // Move file to processed/ or error/
        var destFolder = result.Status == "error"
            ? Path.Combine(dir, "error")
            : Path.Combine(dir, "processed");

        MoveFile(filePath, destFolder, fileName);

        // Write audit log entry
        WriteLog(dir, fileName, result);

        _logger.LogInformation("HL7 {File}: {Status} — {Notes}", fileName, result.Status, result.Notes);
    }

    private static void MoveFile(string sourcePath, string destFolder, string fileName)
    {
        try
        {
            Directory.CreateDirectory(destFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var destPath  = Path.Combine(destFolder, $"{timestamp}_{fileName}");
            if (File.Exists(destPath)) destPath = Path.Combine(destFolder, $"{timestamp}_{Guid.NewGuid():N}_{fileName}");
            File.Move(sourcePath, destPath, overwrite: false);
        }
        catch (Exception ex)
        {
            // Don't crash the service if file move fails
            Console.WriteLine($"HL7: Failed to move file {fileName}: {ex.Message}");
        }
    }

    private static void WriteLog(string dir, string fileName, Hl7ProcessResult result)
    {
        try
        {
            var logPath = Path.Combine(dir, "dx7_hl7.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {result.Status,-16} | {result.MessageType,-12} | " +
                       $"Patient: {result.PatientId,-12} {result.PatientName,-30} | " +
                       $"Acc: {result.AccessionId,-15} | Saved: {result.ResultsSaved,3} | File: {fileName} | {result.Notes}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch { /* log write failures are non-fatal */ }
    }

    private static async Task<string> ReadWithRetryAsync(string path, int retries = 5)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return await File.ReadAllTextAsync(path);
            }
            catch (IOException) when (i < retries - 1)
            {
                await Task.Delay(200 * (i + 1));
            }
        }
        return await File.ReadAllTextAsync(path);
    }

    public override void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        base.Dispose();
    }
}