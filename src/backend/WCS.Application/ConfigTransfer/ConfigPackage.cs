using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WCS.Application.Common;

namespace WCS.Application.ConfigTransfer;

/// <summary>
/// CR-026 package format 1.0 (see docs/02_Interface_Specification/CONFIG_PACKAGE_FORMAT.md): a ZIP with manifest.json, checksums.sha256, README.txt and one JSON file per group.
/// Natural keys only; no Guid, no row version, no operational field, no approval state, no secret.
/// </summary>
public static class ConfigPackage
{
    public const string FormatVersion = "1.0";
    public const string ManifestFile = "manifest.json";
    public const string ChecksumsFile = "checksums.sha256";
    public const string ReadmeFile = "README.txt";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string Render(JsonNode? n) => n is null ? "null" : n.ToJsonString(Compact);

    // ------------------------------------------------------------------ write
    public static byte[] Write(PackageManifest manifestWithoutGroups, IReadOnlyList<(string Group, string File, IReadOnlyList<ConfigRecord> Records)> groups) =>
        WriteWithManifest(manifestWithoutGroups, groups).Data;

    /// <summary>Same as <see cref="Write"/>, and also returns the manifest exactly as written (with its group list, counts and checksums).</summary>
    public static (byte[] Data, PackageManifest Manifest) WriteWithManifest(PackageManifest manifestWithoutGroups, IReadOnlyList<(string Group, string File, IReadOnlyList<ConfigRecord> Records)> groups)
    {
        var files = new List<(string Name, byte[] Data)>();
        var manifestGroups = new List<ManifestGroup>();
        foreach (var (group, file, records) in groups)
        {
            var doc = new JsonObject
            {
                ["group"] = group,
                ["records"] = new JsonArray(records.OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => (JsonNode)new JsonObject { ["key"] = r.Key, ["fields"] = r.Fields.DeepClone() }).ToArray()),
            };
            var data = Encoding.UTF8.GetBytes(doc.ToJsonString(Json));
            files.Add((file, data));
            manifestGroups.Add(new ManifestGroup(group, file, records.Count, Sha256(data)));
        }
        var manifest = manifestWithoutGroups with { Groups = manifestGroups };
        files.Insert(0, (ManifestFile, JsonSerializer.SerializeToUtf8Bytes(manifest, Json)));
        var readme = Encoding.UTF8.GetBytes(
            "ETEK WCS configuration package (CR-026), format " + FormatVersion + ".\n" +
            "Contains configuration only: no password, token or secret, no approval state (Approved / WriteAllowed), no user account, no inventory, no task or history.\n" +
            "Records are identified by their natural key (Code / StationId); no database id is stored.\n" +
            "Import it only through the WCS Admin page (Preview first). Do not edit the files: the checksums are verified.\n");
        files.Add((ReadmeFile, readme));
        var checksums = new StringBuilder();
        foreach (var (name, data) in files) checksums.Append(Sha256(data)).Append("  ").Append(name).Append('\n');
        files.Add((ChecksumsFile, Encoding.UTF8.GetBytes(checksums.ToString())));

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in files)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var s = e.Open();
                s.Write(data, 0, data.Length);
            }
        return (ms.ToArray(), manifest);
    }

    // ------------------------------------------------------------------ read (hardened)
    /// <param name="groupFiles">group id to file name (the allow-list of data files).</param>
    /// <param name="validate">schema check of one record of a group; returns the problems found.</param>
    public static PackageContent Read(byte[] bytes, IReadOnlyDictionary<string, string> groupFiles, ConfigTransferOptions limits, Func<string, ConfigRecord, IReadOnlyList<string>> validate)
    {
        var errors = new List<string>();
        if (bytes.Length == 0) throw Invalid(["The file is empty."]);
        if (bytes.Length > (long)limits.MaxPackageMb * 1024 * 1024) throw Invalid([$"The package is larger than {limits.MaxPackageMb} MB."]);
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B) throw Invalid(["The file is not a ZIP package."]);

        var allowed = new HashSet<string>(groupFiles.Values, StringComparer.Ordinal) { ManifestFile, ChecksumsFile, ReadmeFile };
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        var cap = (long)limits.MaxUncompressedMb * 1024 * 1024;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > 16) throw Invalid(["The package has too many entries."]);
            foreach (var e in zip.Entries)
            {
                if (e.FullName.Length == 0 || e.FullName.Contains('/') || e.FullName.Contains('\\') || e.FullName.Contains("..")) { errors.Add($"Entry name '{e.FullName}' is not allowed."); continue; }
                if (!allowed.Contains(e.FullName)) { errors.Add($"Entry '{e.FullName}' is not part of the package format."); continue; }
                if (data.ContainsKey(e.FullName)) { errors.Add($"Entry '{e.FullName}' appears twice."); continue; }
                if (e.Length > cap - total) throw Invalid(["The package is larger than allowed once unpacked."]);
                using var s = e.Open();
                using var buf = new MemoryStream();
                var chunk = new byte[16384];
                int n;
                while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += n;
                    if (total > cap) throw Invalid(["The package is larger than allowed once unpacked."]);
                    buf.Write(chunk, 0, n);
                }
                data[e.FullName] = buf.ToArray();
            }
        }
        catch (InvalidDataException) { throw Invalid(["The file is not a valid ZIP package."]); }
        if (errors.Count > 0) throw Invalid(errors);

        if (!data.TryGetValue(ManifestFile, out var mBytes)) throw Invalid(["manifest.json is missing."]);
        if (!data.TryGetValue(ChecksumsFile, out var cBytes)) throw Invalid(["checksums.sha256 is missing."]);
        PackageManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<PackageManifest>(mBytes, Json); }
        catch (JsonException) { throw Invalid(["manifest.json is not valid."]); }
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.FormatVersion)) throw Invalid(["manifest.json is not valid."]);
        if (!manifest.FormatVersion.StartsWith("1.", StringComparison.Ordinal)) throw Invalid([$"Package format {manifest.FormatVersion} is not supported (this version reads 1.x)."]);

        var sums = Encoding.UTF8.GetString(cBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split("  ", 2)).Where(p => p.Length == 2).ToDictionary(p => p[1].Trim(), p => p[0].Trim(), StringComparer.Ordinal);
        foreach (var (name, bytesOf) in data)
        {
            if (name == ChecksumsFile) continue;
            if (!sums.TryGetValue(name, out var expected)) { errors.Add($"{name} has no checksum."); continue; }
            if (!string.Equals(expected, Sha256(bytesOf), StringComparison.OrdinalIgnoreCase)) errors.Add($"{name} was modified (checksum mismatch).");
        }
        if (errors.Count > 0) throw Invalid(errors);

        var groups = new Dictionary<string, IReadOnlyList<ConfigRecord>>();
        foreach (var mg in manifest.Groups ?? [])
        {
            if (!groupFiles.TryGetValue(mg.Group, out var file) || file != mg.File) { errors.Add($"Group '{mg.Group}' / file '{mg.File}' is not recognised."); continue; }
            if (!data.TryGetValue(file, out var gBytes)) { errors.Add($"{file} is missing."); continue; }
            if (!string.Equals(mg.Sha256, Sha256(gBytes), StringComparison.OrdinalIgnoreCase)) { errors.Add($"{file} does not match the manifest checksum."); continue; }
            var records = new List<ConfigRecord>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = JsonNode.Parse(gBytes) as JsonObject;
                if (root?["group"]?.GetValue<string>() != mg.Group || root["records"] is not JsonArray arr) { errors.Add($"{file} has an unexpected structure."); continue; }
                foreach (var item in arr)
                {
                    var key = (item as JsonObject)?["key"]?.GetValue<string>();
                    var fields = (item as JsonObject)?["fields"] as JsonObject;
                    if (string.IsNullOrWhiteSpace(key) || fields is null) { errors.Add($"{file}: a record has no key or fields."); continue; }
                    if (!seen.Add(key)) { errors.Add($"{file}: key '{key}' appears more than once."); continue; }
                    var rec = new ConfigRecord(key, fields);
                    foreach (var problem in validate(mg.Group, rec)) errors.Add($"{mg.Group} '{key}': {problem}");
                    records.Add(rec);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { errors.Add($"{file} is not valid JSON."); continue; }
            if (records.Count != mg.Count) errors.Add($"{file}: the manifest says {mg.Count} records, the file has {records.Count}.");
            groups[mg.Group] = records;
        }
        if (errors.Count > 0) throw Invalid(errors);
        return new PackageContent(manifest, groups, Sha256(bytes));
    }

    private static ConfigTransferException Invalid(IReadOnlyList<string> problems) =>
        new(ErrorKind.Validation, "PACKAGE_INVALID", "The configuration package is not valid: " + problems[0] + (problems.Count > 1 ? $" (+{problems.Count - 1} more)" : ""), problems);
}
