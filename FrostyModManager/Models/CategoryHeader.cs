namespace FrostyModManager.Models;

/// <summary>
/// A category row in the grouped mod library.
/// </summary>
public sealed record CategoryHeader(string Name, int Count)
{
    public string Label => $"{Name.ToUpperInvariant()}  ·  {Count}";
}
