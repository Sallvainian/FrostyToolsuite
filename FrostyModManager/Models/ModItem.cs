using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.ModSupport.Mod;

namespace FrostyModManager.Models;

public sealed partial class ModItem : ObservableObject
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    public string Title { get; }
    public string Author { get; }
    public string Version { get; }
    public string Category { get; }
    public string Description { get; }
    public string ModPageLink { get; }

    public string Byline => string.IsNullOrEmpty(Author) ? $"v{Version}" : $"by {Author} · v{Version}";

    [ObservableProperty] private bool m_isEnabled;

    public ModItem(string inFilePath, FrostyModDetails inDetails, bool inIsEnabled)
    {
        FilePath = inFilePath;
        Title = string.IsNullOrWhiteSpace(inDetails.Title) ? Path.GetFileNameWithoutExtension(inFilePath) : inDetails.Title;
        Author = inDetails.Author;
        Version = string.IsNullOrWhiteSpace(inDetails.Version) ? "?" : inDetails.Version;
        Category = inDetails.Category;
        Description = inDetails.Description;
        ModPageLink = inDetails.ModPageLink;
        m_isEnabled = inIsEnabled;
    }
}
