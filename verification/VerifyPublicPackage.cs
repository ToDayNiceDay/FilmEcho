using System;
using System.IO;
using System.Linq;
using System.Text;
namespace FilmEcho {
internal static class VerifyPublicPackage {
    static int Main(string[] args) {
        try {
            var source=UpdateSource.Load(args[0]); source.Validate();
            var manifest=UpdateSafety.Verify(File.ReadAllText(Path.Combine(args[1],"FilmEcho-update.json"),Encoding.UTF8),source.PublicKeyXml);
            if(manifest.Version!="1.4.3" || manifest.Files.Any(f=>f.Path=="update-source.json")) throw new Exception("Version/trust config");
            UpdateSafety.Extract(Path.Combine(args[1],manifest.PackageName),args[2],manifest);
            Console.WriteLine("PASS: production signature, ZIP and all "+manifest.Files.Count+" file hashes, executable version/x64; trust config excluded");
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
}
