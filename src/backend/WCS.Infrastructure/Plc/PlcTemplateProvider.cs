using WCS.Application.Plc;
using WCS.Domain.Plc;

namespace WCS.Infrastructure.Plc;

/// <summary>The Default IO Mapping Template: the signal list of the controlled workbook, loaded once from the embedded catalog. Read only.</summary>
internal sealed class PlcTemplateProvider(PlcCatalog catalog) : IPlcTemplateProvider
{
    private readonly Lazy<IReadOnlyList<TemplateSignal>> _signals = new(() => catalog.Template
        .Select(s => new TemplateSignal(s.Seq, s.Group, s.SignalName, Enum.Parse<PlcSignalDirection>(s.Direction), Enum.Parse<PlcDataType>(s.DataType),
            s.TemplateAddressReference, s.Description, s.Values, s.SharedRegisterWith)).ToList());

    public IReadOnlyList<TemplateSignal> Signals => _signals.Value;
}
