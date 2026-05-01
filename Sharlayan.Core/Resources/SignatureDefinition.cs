namespace Sharlayan.Core.Resources;

// JSON shape matching the sharlayan-resources signature files.
internal sealed class SignatureDefinition
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool ASMSignature { get; set; }
    public List<long>? PointerPath { get; set; }
}
