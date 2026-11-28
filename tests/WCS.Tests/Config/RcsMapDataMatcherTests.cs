using WCS.Application.Common;
using WCS.Application.RcsMaps;

namespace WCS.Tests.Config;

/// <summary>CR-067: rows are attached by the coded position and QR code only; every other row is kept and reported.</summary>
public class RcsMapDataMatcherTests
{
    private static readonly MapPoint[] Points = [new(1, 123.5, 129.324, null, null), new(2, 115.9, 129.25, null, null), new(3, 7, 8, null, null)];
    private static MapDataSet Match(params string?[][] rows) => RcsMapDataMatcher.Match(
        [new MapSheet("s", [["Map Data ID", "Call code ", "Data type", "Rack No."], .. rows])], "m.xls", "h", "CC", Points);

    [Fact]
    public void Rows_are_matched_by_code_and_the_others_are_reported_with_a_reason()
    {
        var d = Match(["115900CC129250", "A1", "Working Area", "100011"], ["123500CC129324", "123500CC129324", "Passing Point", null], ["123500TT129324", "A9", "Working Area", null],
            ["000001CC000001", "x", "Passing Point", null], ["bad", "y", "Passing Point", null], ["007000CC008000", "d", "Parking Point", null], ["007000CC008000", "d2", "Parking Point", null], [null, null, null, null]);
        Assert.Equal([("Matched", 2L), ("Matched", 1L), ("OtherMap", null), ("NoPoint", null), ("Invalid", null), ("Duplicate", null), ("Duplicate", (long?)null)], d.Rows.Select(r => (r.Status, r.PointId)));
        Assert.Equal(("A1", "100011", 2), (d.Rows[0].CallCode, d.Rows[0].RackNo, d.Rows[0].RowNumber));
        Assert.Equal([3L, 3L], d.Rows.Where(r => r.Status == "Duplicate").Select(r => r.ConflictPointId!.Value));   // CR-068
    }

    [Fact]
    public void Files_that_do_not_belong_together_or_lack_the_columns_are_refused()
    {
        Assert.Equal("RCS_MAPDATA_MISMATCH", Assert.ThrowsAny<AppException>(() => Match(["123500TT129324", "A", "Working Area", null])).Code);
        Assert.Equal("RCS_MAPDATA_INVALID", Assert.ThrowsAny<AppException>(() => RcsMapDataMatcher.Match([new MapSheet("s", [["Other"]])], "m", "h", "CC", Points)).Code);
    }
}
