using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.ModSupport.Mod;
using Frosty.ModSupport.Mod.Resources;

namespace FrostyModManager.Models;

public sealed record AffectedFile(string Kind, string Name)
{
    private static readonly IBrush s_res = new SolidColorBrush(Color.Parse("#2BB3A9"));
    private static readonly IBrush s_ebx = new SolidColorBrush(Color.Parse("#8C7CF0"));
    private static readonly IBrush s_chunk = new SolidColorBrush(Color.Parse("#E0A040"));
    private static readonly IBrush s_other = new SolidColorBrush(Color.Parse("#6B7280"));

    public IBrush Brush => Kind switch
    {
        "RES" => s_res,
        "EBX" => s_ebx,
        "CHK" => s_chunk,
        _ => s_other
    };
}

/// <summary>
/// A mod file in the mods folder, with everything the lists and the description tab show.
/// </summary>
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
    public Bitmap? Icon { get; }
    public IReadOnlyList<Bitmap> Screenshots { get; }
    public IReadOnlyList<AffectedFile> AffectedFiles { get; }

    public string Byline => string.IsNullOrEmpty(Author) ? string.Empty : $"by {Author}";
    public string VersionLabel => string.IsNullOrWhiteSpace(Version) ? string.Empty : $"v{Version.TrimStart('v', 'V')}";
    public bool HasVersion => VersionLabel.Length > 0;
    public bool HasScreenshots => Screenshots.Count > 0;
    public string AffectedSummary => AffectedFiles.Count == 1 ? "1 file" : $"{AffectedFiles.Count} files";
    public bool HasIcon => Icon is not null;

    [ObservableProperty] private bool m_isApplied;

    private ModItem(string inFilePath, FrostyModDetails inDetails, Bitmap? inIcon, IReadOnlyList<Bitmap> inScreenshots,
        IReadOnlyList<AffectedFile> inAffectedFiles)
    {
        FilePath = inFilePath;
        Title = string.IsNullOrWhiteSpace(inDetails.Title) ? Path.GetFileNameWithoutExtension(inFilePath) : inDetails.Title;
        Author = inDetails.Author;
        Version = inDetails.Version;
        Category = string.IsNullOrWhiteSpace(inDetails.Category) ? "Misc" : inDetails.Category;
        Description = inDetails.Description;
        ModPageLink = inDetails.ModPageLink;
        Icon = inIcon;
        Screenshots = inScreenshots;
        AffectedFiles = inAffectedFiles;
    }

    /// <summary>
    /// Reads a .fbmod or .fbcollection, returns null if it is not a mod for the loaded profile.
    /// </summary>
    public static ModItem? TryLoad(string inPath)
    {
        try
        {
            if (Path.GetExtension(inPath).Equals(".fbcollection", StringComparison.OrdinalIgnoreCase))
            {
                FrostyModDetails? collection = FrostyModCollection.GetModDetails(inPath);
                return collection is null
                    ? null
                    : new ModItem(inPath, collection, null, Array.Empty<Bitmap>(), Array.Empty<AffectedFile>());
            }

            FrostyMod? mod = FrostyMod.Load(inPath);
            if (mod is null)
            {
                return null;
            }

            Bitmap? icon = null;
            List<Bitmap> screenshots = new();
            List<AffectedFile> affected = new();
            foreach (BaseModResource resource in mod.Resources)
            {
                switch (resource.Type)
                {
                    case ModResourceType.Embedded when resource.ResourceIndex >= 0:
                    {
                        Bitmap? image = LoadImage(mod, resource.ResourceIndex);
                        if (resource.Name == "Icon")
                        {
                            icon = image;
                        }
                        else if (image is not null)
                        {
                            screenshots.Add(image);
                        }

                        break;
                    }
                    case ModResourceType.Ebx:
                        affected.Add(new AffectedFile("EBX", resource.Name));
                        break;
                    case ModResourceType.Res:
                        affected.Add(new AffectedFile("RES", resource.Name));
                        break;
                    case ModResourceType.Chunk:
                        affected.Add(new AffectedFile("CHK", resource.Name));
                        break;
                    case ModResourceType.FsFile:
                        affected.Add(new AffectedFile("FS", resource.Name));
                        break;
                }
            }

            // same order as Frosty 1.x: res and ebx first, then the raw chunks
            affected = affected.OrderBy(a => a.Kind == "CHK").ThenBy(a => a.Name).ToList();
            return new ModItem(inPath, mod.ModDetails, icon, screenshots, affected);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? LoadImage(FrostyMod inMod, int inIndex)
    {
        try
        {
            using Frosty.Sdk.Utils.Block<byte> data = inMod.GetData(inIndex).GetData();
            if (data.Size == 0)
            {
                return null;
            }

            using MemoryStream stream = new(data.ToArray());
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
