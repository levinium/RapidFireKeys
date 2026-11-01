using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace RapidFireKeys.Updates;

/// <summary>Why an install could not be attempted, or did not finish.</summary>
public sealed record InstallFailure(string Message);

/// <summary>
/// Downloads a release and replaces this copy of the app with it.
/// <para>
/// RapidFireKeys ships as one exe, which keeps the swap simple. Windows will
/// not let a running exe be overwritten or deleted, but it will let it be
/// renamed. So the running exe is renamed aside to RapidFireKeys.exe.old, the
/// verified download takes its name, and the new copy is started before this
/// one exits. The .old file is deleted by the new copy once this process has
/// let go of it.
/// </para>
/// <para>
/// Everything that can fail is done before the rename: the download lands
/// beside the exe and is checked against the size and checksum GitHub recorded,
/// so the only step left to go wrong is two renames on the same disk, and the
/// first is undone if the second fails.
/// </para>
/// </summary>
public sealed class UpdateInstaller
{
    private readonly HttpClient _http;

    public UpdateInstaller(HttpClient? http = null)
    {
        _http = http ?? UpdateService.CreateClient();
    }

    /// <summary>The exe this process is running from.</summary>
    public static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RapidFireKeys.exe");

    /// <summary>Where the running exe is moved during an update.</summary>
    public static string OldExePath => ExePath + ".old";

    private static string DownloadPath => ExePath + ".download";

    /// <summary>
    /// Whether the app could replace its own exe.
    /// <para>
    /// Placed in Program Files, or on a read-only share, it cannot — and finding
    /// that out AFTER downloading 70 MB would be the worst possible moment.
    /// Asked first, a person is simply sent to the releases page instead.
    /// </para>
    /// </summary>
    public static bool CanWriteToInstallDirectory()
    {
        try
        {
            var directory = Path.GetDirectoryName(ExePath)!;
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches the release, checks it, swaps it in and starts it. Returns a
    /// failure to report, or null when the new copy is running and the caller
    /// should exit.
    /// </summary>
    /// <param name="asset">The exe named by the release feed.</param>
    /// <param name="progress">Fraction downloaded, 0 to 1.</param>
    public async Task<InstallFailure?> InstallAsync(
        ReleaseAsset asset,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!CanWriteToInstallDirectory())
            return new InstallFailure(
                "RapidFireKeys is in a folder it is not allowed to write to. Download the new version manually, or move the app to a folder of its own, for example in Documents.");

        try
        {
            var download = await DownloadAsync(asset, DownloadPath, progress, ct).ConfigureAwait(false);
            if (download is not null)
            {
                TryDelete(DownloadPath);
                return download;
            }

            var verify = Verify(DownloadPath, asset);
            if (verify is not null)
            {
                TryDelete(DownloadPath);
                return verify;
            }

            var swap = Swap();
            if (swap is not null) TryDelete(DownloadPath);
            return swap;
        }
        catch (OperationCanceledException)
        {
            TryDelete(DownloadPath);
            return new InstallFailure("Update cancelled. Nothing has been changed.");
        }
        catch (Exception ex)
        {
            TryDelete(DownloadPath);
            return new InstallFailure($"The update could not be prepared: {ex.Message}. Nothing has been changed.");
        }
    }

    private async Task<InstallFailure?> DownloadAsync(
        ReleaseAsset asset,
        string destination,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new InstallFailure($"The download failed ({(int)response.StatusCode}). Nothing has been changed.");

        // Content-Length is what the server actually promises; the feed's size is
        // the fallback when a proxy strips it.
        var total = response.Content.Headers.ContentLength ?? asset.Size;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long done = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;

            if (total > 0) progress?.Report(Math.Min(1d, (double)done / total));
        }

        return null;
    }

    /// <summary>
    /// Checks the download is the file the feed described.
    /// <para>
    /// This is the one check that matters. The app is about to replace its own
    /// exe and run it — elevated, since RapidFireKeys always runs as
    /// administrator — so what arrived has to be what was published: not a
    /// truncated download, and not a captive portal's login page saved under an
    /// .exe name. GitHub records a SHA-256 for every asset it stores; when it is
    /// present it is compared, and a mismatch stops everything.
    /// </para>
    /// <para>
    /// Assets uploaded before GitHub added the field carry no digest. Those fall
    /// back to a length check, which catches the truncation and the login page
    /// but proves nothing about the contents.
    /// </para>
    /// </summary>
    internal static InstallFailure? Verify(string path, ReleaseAsset asset)
    {
        var length = new FileInfo(path).Length;

        if (asset.Size > 0 && length != asset.Size)
            return new InstallFailure(
                $"The download was {length:N0} bytes but should have been {asset.Size:N0}. Nothing has been changed.");

        if (string.IsNullOrWhiteSpace(asset.Digest)) return null;

        var expected = asset.Digest.Split(':', 2) switch
        {
            ["sha256", var hex] => hex,
            _ => null,
        };

        // An algorithm this build does not know is not a failure to report. The
        // length already matched, and refusing every future digest format would
        // be a time bomb in an app people cannot easily update by then.
        if (expected is null) return null;

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? null
            : new InstallFailure("The download did not match its published checksum, so it has been discarded. Nothing has been changed.");
    }

    /// <summary>
    /// Moves the running exe aside, puts the download in its place and starts it.
    /// </summary>
    private static InstallFailure? Swap()
    {
        var exe = ExePath;
        var old = OldExePath;

        // Left behind by an earlier update whose cleanup never ran. Nothing has
        // it open now, so it can simply go.
        if (File.Exists(old)) File.Delete(old);

        File.Move(exe, old);

        try
        {
            File.Move(DownloadPath, exe);
        }
        catch (Exception ex)
        {
            // Put the running exe back under its own name, so a failed update
            // costs nothing but the download.
            File.Move(old, exe);
            return new InstallFailure($"The new version could not be put in place: {ex.Message}. Nothing has been changed.");
        }

        try
        {
            // This process is elevated, so the new one is too, with no second
            // UAC prompt.
            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = false });
            return null;
        }
        catch (Exception ex)
        {
            // The new exe is in place but would not start. Roll back rather than
            // leave a copy nobody has seen run.
            File.Move(exe, DownloadPath, overwrite: true);
            File.Move(old, exe);
            return new InstallFailure($"The new version could not be started: {ex.Message}. Nothing has been changed.");
        }
    }

    /// <summary>
    /// Deletes the exe an update moved aside, once the copy that was running it
    /// has exited. Called at startup, in the background: the old copy is still
    /// on its way out when the new one starts.
    /// </summary>
    public static async Task CleanUpAfterUpdateAsync()
    {
        var old = OldExePath;
        if (!File.Exists(old)) return;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                File.Delete(old);
                Log.Write("Removed the previous version left by the update");
                return;
            }
            catch
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A leftover file is not worth telling anyone about.
        }
    }
}
