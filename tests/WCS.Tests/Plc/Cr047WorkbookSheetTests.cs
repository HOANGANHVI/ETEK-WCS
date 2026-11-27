using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WCS.Infrastructure.Plc;

namespace WCS.Tests.Plc;

/// <summary>
/// CR-047: the catalog the product ships is compared with the workbook itself, read here independently of the generator script: every cell of both sheets, merged ranges included, and the
/// signals of every station. A difference between the IO map and what the system shows fails here.
/// </summary>
public sealed class Cr047WorkbookSheetTests
{
    private static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly PlcCatalog Product = PlcCatalog.LoadEmbedded();

    private static (Dictionary<string, string> Cells, List<(string Col, int From, int To)> Merged) Read(int sheet)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
        using var zip = ZipFile.OpenRead(Path.Combine(d!.FullName, Product.Source.File));
        XDocument Part(string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s); }
        var shared = Part("xl/sharedStrings.xml").Descendants(X + "si").Select(si => string.Concat(si.Descendants(X + "t").Select(t => t.Value))).ToList();
        var rels = Part("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
        var rid = (string)Part("xl/workbook.xml").Descendants(X + "sheet").ElementAt(sheet).Attributes().Single(a => a.Name.LocalName == "id").Value;
        var doc = Part("xl/" + rels[rid].TrimStart('/').Replace("xl/", ""));
        var cells = new Dictionary<string, string>();
        foreach (var c in doc.Descendants(X + "c"))
        {
            var v = c.Element(X + "v")?.Value;
            if ((string?)c.Attribute("t") == "s" && v is not null) v = shared[int.Parse(v)];
            else if ((string?)c.Attribute("t") == "inlineStr") v = string.Concat(c.Descendants(X + "t").Select(t => t.Value));
            if (!string.IsNullOrWhiteSpace(v)) cells[(string)c.Attribute("r")!] = Clean(v);
        }
        var merged = doc.Descendants(X + "mergeCell").Select(m => Regex.Match((string)m.Attribute("ref")!, @"^([A-Z]+)(\d+):([A-Z]+)(\d+)$"))
            .Where(m => m.Groups[1].Value == m.Groups[3].Value).Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[4].Value))).ToList();
        return (cells, merged);
    }

    private static string Clean(string v) => v.Replace("\r\n", "\n").Trim().Normalize(NormalizationForm.FormC);

    [Fact]
    public void Every_cell_of_the_IO_matrix_is_carried_as_written()
    {
        var (cells, merged) = Read(0);
        string At(char col, int row)
        {
            var m = merged.FirstOrDefault(x => x.Col == col.ToString() && x.From <= row && row <= x.To);
            return cells.GetValueOrDefault(m.Col is null ? $"{col}{row}" : $"{col}{m.From}") ?? "";
        }
        Assert.Equal(At('A', 1), Product.Sheet!.Title);
        Assert.Equal("ABCDEFGHIJK".Select(c => At(c, 2)), Product.Sheet.Columns);
        var rows = Enumerable.Range(3, 200).Where(r => "ABCDEFGHIJK".Any(c => At(c, r).Length > 0)).ToList();
        Assert.Equal(rows, Product.Sheet.Rows.Select(r => r.Row));
        foreach (var row in Product.Sheet.Rows)
            Assert.Equal("ABCDEFGHIJK".Select(c => $"{c}{row.Row}={At(c, row.Row)}"), "ABCDEFGHIJK".Select((c, i) => $"{c}{row.Row}={Clean(row.Cells[i])}"));
    }

    [Fact]
    public void Every_station_signal_of_the_catalog_is_a_row_of_the_workbook_and_nothing_else_exists()
    {
        var command = Product.Sheet!.Rows.Where(r => r.Cells[2] == "Command").ToList();
        Assert.Equal(96, command.Count);
        var fromSheet = command.Select(r => $"{r.Cells[1]}|{r.Cells[3]}|{r.Cells[7]}|{r.Cells[8]}|{r.Cells[4].StartsWith("PLC")}").Order().ToList();
        var fromCatalog = Product.Stations.SelectMany(s => Product.Template.Select(t => $"{s.StationId}|{t.SignalName}|{s.Addresses[t.SignalName]}|{t.DataType.ToUpperInvariant()}|{t.Direction == "PlcToSoftware"}")).Order().ToList();
        Assert.Equal(fromSheet, fromCatalog);
        // an address exists in the catalog only where the workbook has one (a merged cell shares it); the heartbeat and spare rows stay PLC rows
        Assert.All(command, r => Assert.Matches("^D[0-9]+$", r.Cells[7]));
        Assert.Equal(24, Product.Sheet.Rows.Count - command.Count);
    }

    [Fact]
    public void The_sequence_sheet_is_carried_with_its_arrows()
    {
        var (cells, _) = Read(1);
        var carried = Product.Sequence!.Rows.SelectMany(r => r.Cells.Select(c => $"{c.Key}{r.Row}={Clean(c.Value)}")).Order().ToList();
        Assert.Equal(cells.Select(c => $"{c.Key}={c.Value}").Order(), carried);
        Assert.Equal(["9:toAgv", "11:toPlc", "16:toPlc", "18:toAgv", "20:toPlc", "22:toAgv"], Product.Sequence.Rows.Where(r => r.Arrow is not null).Select(r => $"{r.Row}:{r.Arrow}"));
    }
}
