using WCS.Domain.Common;

namespace WCS.Domain.Inventory;

/// <summary>CR-036: a product model (for example "F-BAL-10R80"). A location, a rack and a process may belong to one model; a rack never goes to a location or process of another model.</summary>
public class ProductModel
{
    private ProductModel() { }

    public ProductModel(string code, string name, DateTimeOffset now)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        if (c.Length is 0 or > 40) throw new DomainException("MODEL_CODE_INVALID", "Model code is required (max 40 characters).");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("MODEL_NAME_REQUIRED", "Model name is required.");
        Id = Guid.NewGuid(); Code = c; Name = name.Trim(); Enabled = true; CreatedAt = now; UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, bool enabled, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("MODEL_NAME_REQUIRED", "Model name is required.");
        Name = name.Trim(); Enabled = enabled; UpdatedAt = now;
    }
}
