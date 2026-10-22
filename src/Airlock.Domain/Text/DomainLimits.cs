namespace Airlock.Domain.Text;

/// <summary>
/// Upper bounds for text that comes from outside Airlock — uploaded artifacts, Syft and NVD (U1-NFR-04).
/// Infrastructure cleans incoming data to these limits before it builds Domain objects.
/// </summary>
public static class DomainLimits
{
    public const int PackageType = 64;
    public const int PackageName = 512;
    public const int PackageVersion = 256;
    public const int Cpe = 1024;
    public const int FileName = 255;
    public const int CveId = 32;
    public const int CveDescription = 16384;
    public const int Path = 4096;
    public const int Message = 2048;
}
