using System.Reflection;
[assembly: AssemblyTitle("FilmEcho")]
[assembly: AssemblyProduct("FilmEcho English Movie Shadowing Player")]
[assembly: AssemblyDescription("Local movie playback, bilingual captions and vocabulary study")]
[assembly: AssemblyVersion(FilmEcho.AppInfo.Version)]
[assembly: AssemblyFileVersion(FilmEcho.AppInfo.Version)]
namespace FilmEcho { static class AppInfo {
#if PUBLIC_RELEASE
    public const string Version="1.4.3";
    public static readonly bool PublicRelease=true;
#else
    public const string Version="1.4.2";
    public static readonly bool PublicRelease=false;
#endif
    public static readonly int SavedTabIndex=PublicRelease?1:2;
} }
