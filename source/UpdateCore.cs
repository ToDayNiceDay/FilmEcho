using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FilmEcho {
internal static class UpdateJson {
    static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer { MaxJsonLength=1048576 };
    public static T Read<T>(string text) { lock(Serializer) return Serializer.Deserialize<T>(text.TrimStart('\uFEFF')); }
    public static string Text(object value) { lock(Serializer) return Serializer.Serialize(value); }
    public static void Write(string path,object value) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { byte[] bytes=Encoding.UTF8.GetBytes(Text(value)); stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
        if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
}
internal sealed class UpdateException : IOException {
    public readonly string Code;
    public UpdateException(string code) : base(code) { Code=code; }
}
internal sealed class UpdateSource {
    public string Repository { get; set; }="";
    public string PublicKeyXml { get; set; }="";
    public static UpdateSource Load(string root) {
        string file=Path.Combine(root,"update-source.json");
        if(!File.Exists(file)) return new UpdateSource();
        return UpdateJson.Read<UpdateSource>(File.ReadAllText(file,Encoding.UTF8))??new UpdateSource();
    }
    public bool Enabled { get { return !string.IsNullOrWhiteSpace(Repository); } }
    public void Validate() {
        if(!Regex.IsMatch(Repository??"",@"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\z")) throw new UpdateException("source");
        if(string.IsNullOrWhiteSpace(PublicKeyXml) || PublicKeyXml.Contains("<D>")) throw new UpdateException("key");
        using(var rsa=new RSACryptoServiceProvider()) { rsa.PersistKeyInCsp=false; rsa.FromXmlString(PublicKeyXml); if(rsa.KeySize<2048) throw new UpdateException("key"); }
    }
}
internal sealed class UpdateFile {
    public string Path { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
}
internal sealed class UpdateManifest {
    public int Schema { get; set; }
    public string Version { get; set; }
    public string PackageName { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
    public List<UpdateFile> Files { get; set; }
}
internal sealed class SignedUpdate {
    public string Payload { get; set; }
    public string Signature { get; set; }
}
internal static class UpdateSafety {
    public const string ApplicationName="英语跟读播放器.exe";
    public const long MaxPackage=536870912,MaxExpanded=1073741824;
    static readonly HashSet<string> RootFiles=new HashSet<string>(new[]{ApplicationName,"FilmEcho.Updater.exe","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","WebView2Loader.dll","LICENSE.txt","files.sha256.json"},StringComparer.OrdinalIgnoreCase);
    public static Version VersionOf(string text) {
        text=(text??"").Trim(); if(text.StartsWith("v",StringComparison.OrdinalIgnoreCase)) text=text.Substring(1);
        if(!Regex.IsMatch(text,@"\A\d+\.\d+\.\d+(\.\d+)?\z")) throw new UpdateException("version");
        Version v; if(!Version.TryParse(text,out v)) throw new UpdateException("version");
        return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
    }
    public static bool Newer(string candidate,string installed) { return VersionOf(candidate)>VersionOf(installed); }
    public static string Hash(string path) { using(var s=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
    public static string Relative(string path) {
        if(string.IsNullOrWhiteSpace(path) || path.Contains("\\") || path.StartsWith("/") || path.IndexOf(':')>=0 || path.Length>230) throw new UpdateException("path");
        foreach(string part in path.Split('/')) {
            if(part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || Regex.IsMatch(part,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|\z)",RegexOptions.IgnoreCase)) throw new UpdateException("path");
        }
        return path;
    }
    public static bool Managed(string path) {
        Relative(path);
        if(RootFiles.Contains(path)) return true;
        if(path.IndexOf('/')<0 && path.EndsWith(".md",StringComparison.OrdinalIgnoreCase)) return new[]{"使用说明.md","开发说明.md","第三方组件.md","验证报告.md","更新功能接入说明.md"}.Contains(path,StringComparer.OrdinalIgnoreCase);
        return new[]{"assets/","licenses/","source/","vendor/mpv/","vendor/dictionary/"}.Any(p=>path.StartsWith(p,StringComparison.OrdinalIgnoreCase)) || path=="runtime/input.conf";
    }
    public static string Inside(string root,string relative) {
        Relative(relative); root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new UpdateException("path");
        NoLinks(root.TrimEnd(Path.DirectorySeparatorChar));
        string at=full; while(at.Length>=root.Length) { NoLinks(at); at=Path.GetDirectoryName(at); }
        return full;
    }
    static void NoLinks(string path) { if((File.Exists(path)||Directory.Exists(path)) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new UpdateException("path"); }
    public static UpdateManifest Verify(string envelope,string key) {
        if(string.IsNullOrWhiteSpace(envelope) || envelope.Length>1048576) throw new UpdateException("manifest");
        SignedUpdate signed; byte[] payload,signature;
        try { signed=UpdateJson.Read<SignedUpdate>(envelope); payload=Convert.FromBase64String(signed.Payload); signature=Convert.FromBase64String(signed.Signature); }
        catch { throw new UpdateException("signature"); }
        using(var rsa=new RSACryptoServiceProvider()) {
            rsa.PersistKeyInCsp=false;
            try { rsa.FromXmlString(key); if(rsa.KeySize<2048 || !rsa.VerifyData(payload,"SHA256",signature)) throw new UpdateException("signature"); }
            catch(UpdateException) { throw; } catch { throw new UpdateException("signature"); }
        }
        UpdateManifest m;
        try { m=UpdateJson.Read<UpdateManifest>(new UTF8Encoding(false,true).GetString(payload)); } catch { throw new UpdateException("manifest"); }
        if(m==null || m.Schema!=1 || m.Size<=0 || m.Size>MaxPackage || !Regex.IsMatch(m.PackageName??"",@"\AFilmEcho-[A-Za-z0-9_.-]+-win-x64\.zip\z") || !Digest(m.Sha256) || m.Files==null || m.Files.Count==0 || m.Files.Count>10000) throw new UpdateException("manifest");
        VersionOf(m.Version); long total=0; var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var f in m.Files) {
            if(f==null || !Managed(f.Path) || !paths.Add(f.Path) || f.Size<0 || f.Size>MaxExpanded || !Digest(f.Sha256)) throw new UpdateException("manifest");
            total+=f.Size; if(total>MaxExpanded) throw new UpdateException("space");
        }
        if(!paths.Contains(ApplicationName) || !paths.Contains("FilmEcho.Updater.exe")) throw new UpdateException("manifest");
        return m;
    }
    static bool Digest(string value) { return Regex.IsMatch(value??"",@"\A[a-fA-F0-9]{64}\z"); }
    public static void CheckPackage(string file,UpdateManifest manifest) {
        if(new FileInfo(file).Length!=manifest.Size || !string.Equals(Hash(file),manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash");
    }
    public static void Extract(string archive,string stage,UpdateManifest manifest) {
        CheckPackage(archive,manifest); Directory.CreateDirectory(stage);
        var expected=manifest.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var zip=ZipFile.OpenRead(archive)) {
            if(zip.Entries.Count>10000) throw new UpdateException("manifest");
            foreach(var entry in zip.Entries) {
                string path=entry.FullName;
                if(path.EndsWith("/")) { Relative(path.TrimEnd('/')); continue; }
                Relative(path); UpdateFile file;
                if(!Managed(path) || !seen.Add(path) || !expected.TryGetValue(path,out file) || entry.Length!=file.Size) throw new UpdateException("path");
                string target=Inside(stage,path); Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var input=entry.Open()) using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    var buffer=new byte[65536]; long size=0; int n;
                    while((n=input.Read(buffer,0,buffer.Length))>0) { size+=n; if(size>file.Size) throw new UpdateException("hash"); output.Write(buffer,0,n); }
                    if(size!=file.Size) throw new UpdateException("hash");
                }
                if(!string.Equals(Hash(target),file.Sha256,StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash");
            }
        }
        if(seen.Count!=expected.Count) throw new UpdateException("manifest");
        string actual=FileVersionInfo.GetVersionInfo(Inside(stage,ApplicationName)).FileVersion;
        if(VersionOf(actual)!=VersionOf(manifest.Version)) throw new UpdateException("version");
        CheckX64(Inside(stage,ApplicationName)); CheckX64(Inside(stage,"FilmEcho.Updater.exe"));
    }
    static void CheckX64(string file) {
        using(var reader=new BinaryReader(File.OpenRead(file))) {
            if(reader.BaseStream.Length<64 || reader.ReadUInt16()!=0x5a4d) throw new UpdateException("architecture");
            reader.BaseStream.Position=0x3c; int offset=reader.ReadInt32();
            if(offset<64 || offset>reader.BaseStream.Length-6) throw new UpdateException("architecture");
            reader.BaseStream.Position=offset; if(reader.ReadUInt32()!=0x00004550 || reader.ReadUInt16()!=0x8664) throw new UpdateException("architecture");
        }
    }
}
internal sealed class UpdatePlan {
    public string Root { get; set; }
    public string Envelope { get; set; }
    public string PublicKeyXml { get; set; }
    public string CurrentVersion { get; set; }
    public int ParentPid { get; set; }
    public long ParentStarted { get; set; }
    public bool English { get; set; }
}
internal sealed class UpdateJournal {
    public string Job { get; set; }
    public string State { get; set; }
    public List<string> Existed { get; set; }=new List<string>();
    public List<string> Changed { get; set; }=new List<string>();
}
internal static class UpdateTransaction {
    public static string Marker(string root) { return Path.Combine(root,".updates","pending.json"); }
    public static string JobRoot(string root,string job) {
        string parent=Path.GetFullPath(Path.Combine(root,".updates"))+Path.DirectorySeparatorChar;
        job=Path.GetFullPath(job); Guid id;
        if(!job.StartsWith(parent,StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(job),"N",out id) || Path.GetDirectoryName(job)+Path.DirectorySeparatorChar!=parent) throw new UpdateException("path");
        UpdateSafety.Inside(root,".updates/"+id.ToString("N")+"/plan.json"); return job;
    }
    static void Replace(string source,string target,string job) {
        Directory.CreateDirectory(Path.GetDirectoryName(target)); string temp=Path.Combine(job,"swap-"+Guid.NewGuid().ToString("N"));
        try { File.Copy(source,temp,false); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public static void Apply(UpdatePlan plan,string job,Action<int> afterWrite=null) {
        job=JobRoot(plan.Root,job); var m=UpdateSafety.Verify(plan.Envelope,plan.PublicKeyXml);
        if(!UpdateSafety.Newer(m.Version,plan.CurrentVersion)) throw new UpdateException("version");
        string stage=Path.Combine(job,"stage"),backup=Path.Combine(job,"backup"),marker=Marker(plan.Root);
        if(File.Exists(marker)) throw new UpdateException("pending");
        Directory.CreateDirectory(backup);
        var journal=new UpdateJournal { Job=job,State="Applying" };
        // Validate and back up every destination before replacing the first file.
        foreach(var file in m.Files) {
            string from=UpdateSafety.Inside(stage,file.Path),to=UpdateSafety.Inside(plan.Root,file.Path);
            if(new FileInfo(from).Length!=file.Size || !string.Equals(UpdateSafety.Hash(from),file.Sha256,StringComparison.OrdinalIgnoreCase)) throw new UpdateException("hash");
            if(Directory.Exists(to)) throw new UpdateException("locked");
            if(File.Exists(to)) {
                using(var probe=new FileStream(to,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
                string copy=UpdateSafety.Inside(backup,file.Path); Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(to,copy,false); journal.Existed.Add(file.Path);
            }
        }
        UpdateJson.Write(marker,journal);
        try {
            int count=0;
            foreach(var file in m.Files) {
                journal.Changed.Add(file.Path); UpdateJson.Write(marker,journal);
                Replace(UpdateSafety.Inside(stage,file.Path),UpdateSafety.Inside(plan.Root,file.Path),job); afterWrite?.Invoke(++count);
            }
            journal.State="Committed"; UpdateJson.Write(marker,journal);
        } catch {
            try { Rollback(plan.Root,job,journal); } catch { throw new UpdateException("rollback"); }
            throw;
        }
    }
    public static void Rollback(string root,string job,UpdateJournal journal) {
        JobRoot(root,job);
        foreach(string path in journal.Changed.AsEnumerable().Reverse()) {
            if(!UpdateSafety.Managed(path)) throw new UpdateException("path");
            string target=UpdateSafety.Inside(root,path);
            if(journal.Existed.Contains(path,StringComparer.OrdinalIgnoreCase)) Replace(UpdateSafety.Inside(Path.Combine(job,"backup"),path),target,job);
            else if(File.Exists(target)) File.Delete(target);
        }
        File.Delete(Marker(root));
    }
    public static void Recover(string root) {
        string marker=Marker(root); if(!File.Exists(marker)) return;
        var journal=UpdateJson.Read<UpdateJournal>(File.ReadAllText(marker)); JobRoot(root,journal.Job);
        if(journal.State=="Committed") File.Delete(marker);
        else if(journal.State=="Applying") Rollback(root,journal.Job,journal);
        else throw new UpdateException("pending");
    }
}
}
