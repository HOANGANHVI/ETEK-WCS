using WCS.Application.Common;
using WCS.Application.RcsMaps;

namespace WCS.Tests.Config;

/// <summary>CR-065: the reader of the RCS map XML.</summary>
public class RcsMapParserTests
{
    private const string Map = """
        <MapCfg><MapID>M1</MapID><MapName>N</MapName><MapQRCode>CC</MapQRCode>
          <PointInfo><id>0</id><xpos>1.5</xpos><ypos>2</ypos><value>16</value>
            <NeighbInfo><id>1</id><distance>1</distance><CtrlPoint/><KeepStance>0</KeepStance><id>9</id><distance>2</distance><CtrlPoint><Pos xpos="1.6" ypos="2.1"/></CtrlPoint></NeighbInfo></PointInfo>
          <PointInfo><id>1</id><xpos>2.5</xpos><ypos>2</ypos></PointInfo>
        </MapCfg>
        """;

    [Fact]
    public void Points_and_the_repeated_neighbours_are_read_and_unknown_targets_are_counted()
    {
        var m = RcsMapParser.Parse(Map);
        Assert.Equal(("M1", "N", "CC", 2, 1), (m.MapId, m.MapName, m.MapQrCode, m.Geometry.Points.Count, m.UnknownNeighbours));
        var e = Assert.Single(m.Geometry.Edges);
        Assert.Equal((0L, 1L, 1d, 0), (e.From, e.To, e.Distance, e.Ctrl.Count));
        Assert.Equal(1.5, m.Geometry.Points[0].X);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<Other/>")]
    [InlineData("<MapCfg><MapID>M</MapID><MapName>N</MapName></MapCfg>")]
    [InlineData("<MapCfg><MapID>M</MapID><MapName>N</MapName><PointInfo><id>1</id><xpos>a</xpos><ypos>1</ypos></PointInfo></MapCfg>")]
    [InlineData("<!DOCTYPE MapCfg [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><MapCfg><MapID>&x;</MapID><MapName>N</MapName></MapCfg>")]
    public void Anything_that_is_not_a_complete_map_is_refused(string xml) => Assert.Equal("RCS_MAP_INVALID", Assert.ThrowsAny<AppException>(() => RcsMapParser.Parse(xml)).Code);
}
