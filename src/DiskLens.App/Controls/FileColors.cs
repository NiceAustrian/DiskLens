using System.Collections.Frozen;
using SkiaSharp;

namespace DiskLens.App.Controls;

public enum FileCategory { Other, Image, Video, Audio, Archive, Document, Code, Binary, DiskImage, Database, Directory }

/// <summary>Maps extensions to categories and categories to treemap colours. One palette for both themes.</summary>
public static class FileColors
{
    private static readonly FrozenDictionary<string, FileCategory> ByExtension = new Dictionary<string, FileCategory>
    {
        // images
        ["jpg"] = FileCategory.Image, ["jpeg"] = FileCategory.Image, ["png"] = FileCategory.Image, ["gif"] = FileCategory.Image,
        ["webp"] = FileCategory.Image, ["bmp"] = FileCategory.Image, ["tif"] = FileCategory.Image, ["tiff"] = FileCategory.Image,
        ["heic"] = FileCategory.Image, ["heif"] = FileCategory.Image, ["raw"] = FileCategory.Image, ["cr2"] = FileCategory.Image,
        ["nef"] = FileCategory.Image, ["arw"] = FileCategory.Image, ["dng"] = FileCategory.Image, ["psd"] = FileCategory.Image,
        ["svg"] = FileCategory.Image, ["ico"] = FileCategory.Image, ["avif"] = FileCategory.Image,
        // video
        ["mp4"] = FileCategory.Video, ["mkv"] = FileCategory.Video, ["avi"] = FileCategory.Video, ["mov"] = FileCategory.Video,
        ["wmv"] = FileCategory.Video, ["webm"] = FileCategory.Video, ["m4v"] = FileCategory.Video, ["ts"] = FileCategory.Video,
        ["mpg"] = FileCategory.Video, ["mpeg"] = FileCategory.Video, ["flv"] = FileCategory.Video, ["m2ts"] = FileCategory.Video,
        // audio
        ["mp3"] = FileCategory.Audio, ["flac"] = FileCategory.Audio, ["wav"] = FileCategory.Audio, ["aac"] = FileCategory.Audio,
        ["ogg"] = FileCategory.Audio, ["m4a"] = FileCategory.Audio, ["wma"] = FileCategory.Audio, ["opus"] = FileCategory.Audio,
        ["aiff"] = FileCategory.Audio, ["alac"] = FileCategory.Audio,
        // archives
        ["zip"] = FileCategory.Archive, ["7z"] = FileCategory.Archive, ["rar"] = FileCategory.Archive, ["tar"] = FileCategory.Archive,
        ["gz"] = FileCategory.Archive, ["tgz"] = FileCategory.Archive, ["bz2"] = FileCategory.Archive, ["xz"] = FileCategory.Archive,
        ["zst"] = FileCategory.Archive, ["cab"] = FileCategory.Archive, ["pak"] = FileCategory.Archive, ["nupkg"] = FileCategory.Archive,
        ["jar"] = FileCategory.Archive, ["apk"] = FileCategory.Archive,
        // documents
        ["pdf"] = FileCategory.Document, ["doc"] = FileCategory.Document, ["docx"] = FileCategory.Document, ["xls"] = FileCategory.Document,
        ["xlsx"] = FileCategory.Document, ["ppt"] = FileCategory.Document, ["pptx"] = FileCategory.Document, ["txt"] = FileCategory.Document,
        ["md"] = FileCategory.Document, ["rtf"] = FileCategory.Document, ["odt"] = FileCategory.Document, ["ods"] = FileCategory.Document,
        ["epub"] = FileCategory.Document, ["csv"] = FileCategory.Document, ["log"] = FileCategory.Document, ["json"] = FileCategory.Document,
        ["xml"] = FileCategory.Document, ["yaml"] = FileCategory.Document, ["yml"] = FileCategory.Document, ["html"] = FileCategory.Document,
        // code
        ["cs"] = FileCategory.Code, ["js"] = FileCategory.Code, ["ts"] = FileCategory.Code, ["tsx"] = FileCategory.Code, ["jsx"] = FileCategory.Code,
        ["py"] = FileCategory.Code, ["cpp"] = FileCategory.Code, ["c"] = FileCategory.Code, ["h"] = FileCategory.Code, ["hpp"] = FileCategory.Code,
        ["java"] = FileCategory.Code, ["rs"] = FileCategory.Code, ["go"] = FileCategory.Code, ["rb"] = FileCategory.Code, ["php"] = FileCategory.Code,
        ["css"] = FileCategory.Code, ["scss"] = FileCategory.Code, ["sh"] = FileCategory.Code, ["ps1"] = FileCategory.Code, ["kt"] = FileCategory.Code,
        ["swift"] = FileCategory.Code, ["lua"] = FileCategory.Code, ["sql"] = FileCategory.Code, ["axaml"] = FileCategory.Code, ["xaml"] = FileCategory.Code,
        // binaries
        ["exe"] = FileCategory.Binary, ["dll"] = FileCategory.Binary, ["so"] = FileCategory.Binary, ["dylib"] = FileCategory.Binary,
        ["sys"] = FileCategory.Binary, ["msi"] = FileCategory.Binary, ["bin"] = FileCategory.Binary, ["obj"] = FileCategory.Binary,
        ["pdb"] = FileCategory.Binary, ["lib"] = FileCategory.Binary, ["a"] = FileCategory.Binary, ["o"] = FileCategory.Binary,
        ["winmd"] = FileCategory.Binary, ["mui"] = FileCategory.Binary, ["efi"] = FileCategory.Binary,
        // disk images / VMs
        ["iso"] = FileCategory.DiskImage, ["img"] = FileCategory.DiskImage, ["vhd"] = FileCategory.DiskImage, ["vhdx"] = FileCategory.DiskImage,
        ["vmdk"] = FileCategory.DiskImage, ["qcow2"] = FileCategory.DiskImage, ["vdi"] = FileCategory.DiskImage, ["wim"] = FileCategory.DiskImage,
        ["esd"] = FileCategory.DiskImage, ["dmg"] = FileCategory.DiskImage,
        // databases
        ["db"] = FileCategory.Database, ["sqlite"] = FileCategory.Database, ["sqlite3"] = FileCategory.Database, ["mdf"] = FileCategory.Database,
        ["ldf"] = FileCategory.Database, ["accdb"] = FileCategory.Database, ["mdb"] = FileCategory.Database, ["parquet"] = FileCategory.Database,
        ["ibd"] = FileCategory.Database, ["frm"] = FileCategory.Database,
    }.ToFrozenDictionary();

    public static FileCategory Categorize(string extension) =>
        ByExtension.TryGetValue(extension, out var c) ? c : FileCategory.Other;

    public static SKColor ColorOf(FileCategory category) => category switch
    {
        FileCategory.Image     => new SKColor(0xE8, 0x6A, 0xB0),
        FileCategory.Video     => new SKColor(0x9B, 0x6E, 0xF3),
        FileCategory.Audio     => new SKColor(0x3F, 0xD6, 0xC6),
        FileCategory.Archive   => new SKColor(0xF5, 0xA2, 0x4C),
        FileCategory.Document  => new SKColor(0x5C, 0xA8, 0xFF),
        FileCategory.Code      => new SKColor(0x6F, 0xD8, 0x7A),
        FileCategory.Binary    => new SKColor(0xF0, 0x6A, 0x6A),
        FileCategory.DiskImage => new SKColor(0xE9, 0xC4, 0x5A),
        FileCategory.Database  => new SKColor(0x8A, 0x8F, 0xFF),
        FileCategory.Directory => new SKColor(0x6B, 0x74, 0x86),
        _                      => new SKColor(0x8E, 0x97, 0xA8),
    };

    public static string Label(FileCategory category) => category switch
    {
        FileCategory.Image => "Images",
        FileCategory.Video => "Video",
        FileCategory.Audio => "Audio",
        FileCategory.Archive => "Archives",
        FileCategory.Document => "Documents",
        FileCategory.Code => "Code",
        FileCategory.Binary => "Programs & libraries",
        FileCategory.DiskImage => "Disk images",
        FileCategory.Database => "Databases",
        FileCategory.Directory => "Folders",
        _ => "Other",
    };

    /// <summary>Deterministic per-extension tint for "Other" so unknown types are still distinguishable.</summary>
    public static SKColor ColorOfExtension(string extension)
    {
        var cat = Categorize(extension);
        if (cat != FileCategory.Other || extension.Length == 0) return ColorOf(cat);
        var hash = 2166136261;
        foreach (var ch in extension) hash = (hash ^ ch) * 16777619;
        var hue = hash % 360;
        return SKColor.FromHsl(hue, 22, 62);
    }
}
