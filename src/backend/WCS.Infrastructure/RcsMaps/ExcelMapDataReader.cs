using System.Globalization;
using System.Text;
using ExcelDataReader;
using WCS.Application.Common;
using WCS.Application.RcsMaps;

namespace WCS.Infrastructure.RcsMaps;

/// <summary>CR-067: reads .xls (BIFF) and .xlsx. Values only; formulas are not evaluated and nothing is written.</summary>
internal sealed class ExcelMapDataReader : IMapDataReader
{
    private const int MaxSheets = 20;
    private const int MaxColumns = 100;

    static ExcelMapDataReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // old .xls files use code pages

    public IReadOnlyList<MapSheet> Read(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var sheets = new List<MapSheet>();
            do
            {
                var rows = new List<IReadOnlyList<string?>>();
                while (reader.Read() && rows.Count <= RcsMapDataMatcher.MaxRows + 1)
                    rows.Add(Enumerable.Range(0, Math.Min(reader.FieldCount, MaxColumns)).Select(i => Text(reader.GetValue(i))).ToList());
                sheets.Add(new MapSheet(reader.Name, rows));
            } while (sheets.Count < MaxSheets && reader.NextResult());
            return sheets;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {   // a file of another kind or a damaged workbook
            throw AppException.Validation("RCS_MAPDATA_INVALID", "The file cannot be read as an Excel workbook.");
        }
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
