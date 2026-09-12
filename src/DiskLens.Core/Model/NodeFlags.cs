namespace DiskLens.Core.Model;

[Flags]
public enum NodeFlags : ushort
{
    None          = 0,
    Directory     = 1 << 0,
    Hidden        = 1 << 1,
    System        = 1 << 2,
    ReparsePoint  = 1 << 3,   // symlink / junction / mount point – not descended into
    Compressed    = 1 << 4,
    Sparse        = 1 << 5,
    Encrypted     = 1 << 6,
    AccessDenied  = 1 << 7,   // directory could not be enumerated
    Error         = 1 << 8,   // any other enumeration error
    Root          = 1 << 9,
    HardLink      = 1 << 10,  // file has more than one name on the volume
}
