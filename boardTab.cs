namespace boardzz;

// One tab: a name, an optional emoji, and its own grid of pads.
internal sealed class boardTab(string name, string? emoji, int padCount)
{
    public string name = name;
    public string? emoji = emoji;
    public padSlot[] slots { get; } = new padSlot[padCount];
    public bool hasSounds => slots.Any(s => s.filePath != null);
}
