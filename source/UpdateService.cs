using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilmEcho {
internal sealed class GithubAsset {
    public string name { get; set; }
    public string browser_download_url { get; set; }
    public long size { get; set; }
}
internal sealed class GithubRelease {
    public string tag_name { get; set; }
    public string body { get; set; }
    public string html_url { get; set; }
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public List<GithubAsset> assets { get; set; }
}
internal sealed class UpdateOffer {
    public string Envelope { get; set; }
    public string DownloadUrl { get; set; }
    public string ReleaseUrl { get; set; }
    public string Notes { get; set; }
    public UpdateManifest Manifest { get; set; }
}
internal sealed class UpdateCache {
    public string Repository { get; set; }
    public string Key { get; set; }
    public DateTime LastAttemptUtc { get; set; }
    public DateTime LastSuccessUtc { get; set; }
    public UpdateOffer Offer { get; set; }
}
internal sealed class UpdateService : IDisposable {
    readonly string root,current,cacheFile;
    readonly HttpClient http;
    readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
    UpdateCache cache=new UpdateCache();
    public readonly UpdateSource Source;
    public UpdateOffer Offer { get; private set; }
    public UpdateService(string appRoot,string installed,UpdateSource source,string cachePath,HttpClient client=null) {
        root=appRoot; current=installed; Source=source; cacheFile=cachePath;
        http=client??Network.CreateClient(); http.Timeout=Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        try {
            if(File.Exists(cacheFile)) cache=UpdateJson.Read<UpdateCache>(File.ReadAllText(cacheFile))??new UpdateCache();
            if(cache.Repository!=source.Repository || cache.Key!=source.PublicKeyXml) cache=new UpdateCache();
            if(source.Enabled && cache.Offer!=null) { source.Validate(); ValidateOffer(cache.Offer); if(UpdateSafety.Newer(cache.Offer.Manifest.Version,current)) Offer=cache.Offer; }
        } catch { cache=new UpdateCache(); }
    }
    void ValidateOffer(UpdateOffer offer) {
        offer.Manifest=UpdateSafety.Verify(offer.Envelope,Source.PublicKeyXml);
        AssetUri(offer.DownloadUrl,offer.Manifest.PackageName);
        Uri release; if(!Uri.TryCreate(offer.ReleaseUrl,UriKind.Absolute,out release) || release.Scheme!="https" || release.Host!="github.com" || !release.AbsolutePath.StartsWith("/"+Source.Repository+"/releases/",StringComparison.Ordinal)) throw new UpdateException("source");
    }
    Uri AssetUri(string url,string name) {
        Uri uri;
        if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="github.com" || uri.UserInfo.Length>0 || uri.Port!=443 || !uri.AbsolutePath.StartsWith("/"+Source.Repository+"/releases/download/",StringComparison.Ordinal) || Uri.UnescapeDataString(uri.Segments.Last())!=name || uri.Query.Length>0 || uri.Fragment.Length>0) throw new UpdateException("source");
        return uri;
    }
    public async Task<UpdateOffer> Check(bool manual,CancellationToken token) {
        if(!Source.Enabled) return null;
        Source.Validate();
        if(!manual && !await gate.WaitAsync(0,token)) return Offer;
        if(manual) await gate.WaitAsync(token);
        try {
            if(!manual) {
                DateTime now=DateTime.UtcNow;
                if(cache.LastSuccessUtc<=now && cache.LastSuccessUtc>now.AddHours(-24) || cache.LastAttemptUtc<=now && cache.LastAttemptUtc>now.AddHours(-1)) return Offer;
            }
            cache.Repository=Source.Repository; cache.Key=Source.PublicKeyXml; cache.LastAttemptUtc=DateTime.UtcNow; SaveCache();
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                string endpoint="https://api.github.com/repos/"+Source.Repository+"/releases/latest";
                var release=UpdateJson.Read<GithubRelease>(await GetText(new Uri(endpoint),timeout.Token));
                if(release==null || release.draft || release.prerelease) throw new UpdateException("manifest");
                var version=UpdateSafety.VersionOf(release.tag_name);
                if(version<=UpdateSafety.VersionOf(current)) { Offer=null; cache.Offer=null; }
                else {
                    var signed=release.assets?.SingleOrDefault(a=>a.name=="FilmEcho-update.json");
                    if(signed==null || signed.size<=0 || signed.size>1048576) throw new UpdateException("manifest");
                    string envelope=await GetText(AssetUri(signed.browser_download_url,signed.name),timeout.Token);
                    var manifest=UpdateSafety.Verify(envelope,Source.PublicKeyXml);
                    if(UpdateSafety.VersionOf(manifest.Version)!=version) throw new UpdateException("version");
                    var package=release.assets.SingleOrDefault(a=>a.name==manifest.PackageName);
                    if(package==null || package.size!=manifest.Size) throw new UpdateException("manifest");
                    var offer=new UpdateOffer { Envelope=envelope,Manifest=manifest,DownloadUrl=package.browser_download_url,ReleaseUrl=release.html_url,Notes=(release.body??"").Substring(0,Math.Min(16000,(release.body??"").Length)) };
                    ValidateOffer(offer); Offer=offer; cache.Offer=offer;
                }
                cache.LastSuccessUtc=DateTime.UtcNow; SaveCache(); return Offer;
            }
        } finally { gate.Release(); }
    }
    void SaveCache() { try { UpdateJson.Write(cacheFile,cache); } catch { /* Read-only cache must not prevent checking. */ } }
    async Task<string> GetText(Uri uri,CancellationToken token) {
        using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token)) {
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound) throw new UpdateException(uri.Host=="api.github.com"?"release":"manifest");
            if(response.StatusCode==System.Net.HttpStatusCode.Forbidden || (int)response.StatusCode==429) throw new UpdateException("limit");
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage.RequestUri.Scheme!="https" || response.Content.Headers.ContentLength>1048576) throw new UpdateException("manifest");
            using(var stream=await response.Content.ReadAsStreamAsync()) using(var output=new MemoryStream()) {
                var buffer=new byte[16384]; int n;
                while((n=await stream.ReadAsync(buffer,0,buffer.Length,token))>0) { if(output.Length+n>1048576) throw new UpdateException("manifest"); output.Write(buffer,0,n); }
                return new UTF8Encoding(false,true).GetString(output.ToArray());
            }
        }
    }
    public async Task<string> Download(UpdateOffer offer,IProgress<int> progress,CancellationToken token) {
        ValidateOffer(offer);
        if(!UpdateSafety.Newer(offer.Manifest.Version,current)) throw new UpdateException("version");
        if(File.Exists(UpdateTransaction.Marker(root))) throw new UpdateException("pending");
        long expanded=offer.Manifest.Files.Sum(f=>f.Size),existing=0;
        foreach(var file in offer.Manifest.Files) { string target=UpdateSafety.Inside(root,file.Path); if(File.Exists(target)) existing+=new FileInfo(target).Length; }
        var disk=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)));
        if(disk.AvailableFreeSpace<offer.Manifest.Size+expanded+existing+67108864) throw new UpdateException("space");
        string job=Path.Combine(root,".updates",Guid.NewGuid().ToString("N")); UpdateTransaction.JobRoot(root,job);
        Directory.CreateDirectory(job); bool success=false;
        try {
            string runner=Path.Combine(root,"FilmEcho.Updater.exe"); if(!File.Exists(runner)) throw new UpdateException("updater");
            File.Copy(runner,Path.Combine(job,"FilmEcho.Updater.exe"));
            string archive=Path.Combine(job,"package.zip");
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                using(var response=await http.GetAsync(AssetUri(offer.DownloadUrl,offer.Manifest.PackageName),HttpCompletionOption.ResponseHeadersRead,timeout.Token)) {
                    response.EnsureSuccessStatusCode();
                    if(response.RequestMessage.RequestUri.Scheme!="https" || response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value!=offer.Manifest.Size) throw new UpdateException("hash");
                    using(var stream=await response.Content.ReadAsStreamAsync()) using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        var buffer=new byte[65536]; long size=0; int n;
                        while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0) {
                            size+=n; if(size>offer.Manifest.Size) throw new UpdateException("hash"); await output.WriteAsync(buffer,0,n,timeout.Token); progress?.Report((int)(size*100/offer.Manifest.Size));
                        }
                        if(size!=offer.Manifest.Size) throw new UpdateException("hash");
                    }
                }
            }
            await Task.Run(()=>UpdateSafety.Extract(archive,Path.Combine(job,"stage"),offer.Manifest),token); token.ThrowIfCancellationRequested();
            using(var parent=Process.GetCurrentProcess()) UpdateJson.Write(Path.Combine(job,"plan.json"),new UpdatePlan { Root=root,Envelope=offer.Envelope,PublicKeyXml=Source.PublicKeyXml,CurrentVersion=current,ParentPid=parent.Id,ParentStarted=parent.StartTime.ToUniversalTime().Ticks });
            success=true; return job;
        } finally { if(!success) try { Directory.Delete(job,true); } catch { } }
    }
    public void Dispose() { http.Dispose(); }
}
}
