using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
internal sealed class UpdateFixtureHandler : HttpMessageHandler {
    public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply;
    public int Requests;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { Requests++; var response=await Reply(request,token); response.RequestMessage=request; return response; }
}
internal sealed class UpdateVerificationForm : Form {
    readonly StringBuilder report=new StringBuilder(); int failures,passes;
    readonly string workspace=Application.StartupPath;
    string testRoot;
    RSACryptoServiceProvider rsa;
    UpdateSource source;
    UpdateManifest manifest;
    string envelope;
    byte[] package;
    void Check(bool ok,string message) { report.AppendLine((ok?"PASS: ":"FAIL: ")+message); if(ok) passes++; else failures++; SaveReport(); }
    void SaveReport() { File.WriteAllText(Path.Combine(workspace,"verification","update-results.txt"),report.ToString(),Encoding.UTF8); }
    bool Reject(Action action) { try { action(); return false; } catch { return true; } }
    async Task<bool> RejectAsync(Func<Task> action) { try { await action(); return false; } catch { return true; } }
    public UpdateVerificationForm() {
        ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; Location=new Point(-30000,-30000); Size=new Size(100,100);
        Shown+=async(s,e)=> {
            try { await Run(); } catch(Exception ex) { Check(false,"验证异常："+ex); }
            finally { rsa?.Dispose(); report.AppendLine("PASS="+passes+" FAIL="+failures); SaveReport(); Environment.ExitCode=failures==0?0:1; Close(); }
        };
    }
    string Sign(UpdateManifest m) {
        byte[] bytes=Encoding.UTF8.GetBytes(UpdateJson.Text(m));
        return UpdateJson.Text(new SignedUpdate { Payload=Convert.ToBase64String(bytes),Signature=Convert.ToBase64String(rsa.SignData(bytes,"SHA256")) });
    }
    void BuildPackage(IEnumerable<KeyValuePair<string,byte[]>> entries=null) {
        var items=entries??new Dictionary<string,byte[]> {
            {UpdateSafety.ApplicationName,File.ReadAllBytes(Path.Combine(workspace,UpdateSafety.ApplicationName))},
            {"FilmEcho.Updater.exe",File.ReadAllBytes(Path.Combine(workspace,"FilmEcho.Updater.exe"))},
            {"使用说明.md",Encoding.UTF8.GetBytes("新版说明")}
        };
        using(var output=new MemoryStream()) {
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)) foreach(var item in items) using(var stream=zip.CreateEntry(item.Key).Open()) stream.Write(item.Value,0,item.Value.Length);
            package=output.ToArray();
        }
        manifest=new UpdateManifest { Schema=1,Version=AppInfo.Version,PackageName="FilmEcho-"+AppInfo.Version+"-win-x64.zip",Size=package.Length,Sha256=Digest(package),Files=items.Select(p=>new UpdateFile { Path=p.Key,Size=p.Value.Length,Sha256=Digest(p.Value) }).ToList() };
        envelope=Sign(manifest);
    }
    static string Digest(byte[] bytes) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    UpdateFixtureHandler Handler(string tag=null) {
        return new UpdateFixtureHandler { Reply=(request,token)=> {
            string url=request.RequestUri.AbsoluteUri;
            if(url.Contains("api.github.com")) return Task.FromResult(TextResponse(UpdateJson.Text(new GithubRelease { tag_name=tag??"v"+manifest.Version,html_url="https://github.com/fixture/FilmEcho/releases/tag/v"+manifest.Version,body="修复和改进 · fixture release",assets=new List<GithubAsset> {
                new GithubAsset { name="FilmEcho-update.json",size=Encoding.UTF8.GetByteCount(envelope),browser_download_url="https://github.com/fixture/FilmEcho/releases/download/v"+manifest.Version+"/FilmEcho-update.json" },
                new GithubAsset { name=manifest.PackageName,size=manifest.Size,browser_download_url="https://github.com/fixture/FilmEcho/releases/download/v"+manifest.Version+"/"+manifest.PackageName }
            }})));
            if(url.EndsWith("FilmEcho-update.json")) return Task.FromResult(TextResponse(envelope));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(package) });
        }};
    }
    static HttpResponseMessage TextResponse(string text) { return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(text,Encoding.UTF8,"application/json") }; }
    UpdateService Service(UpdateFixtureHandler handler,string version="1.4.1",string root=null,string cache=null,UpdateSource config=null) {
        root=root??testRoot; return new UpdateService(root,version,config??source,cache??Path.Combine(root,"cache-"+Guid.NewGuid().ToString("N")+".json"),new HttpClient(handler));
    }
    Dictionary<string,string> Snapshot(string root) { return Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(f=>!f.Substring(root.Length).StartsWith(Path.DirectorySeparatorChar+".updates"+Path.DirectorySeparatorChar)).ToDictionary(f=>f.Substring(root.Length),UpdateSafety.Hash,StringComparer.OrdinalIgnoreCase); }
    static bool Same(Dictionary<string,string> a,Dictionary<string,string> b) { return a.Count==b.Count && a.All(p=>b.ContainsKey(p.Key)&&b[p.Key]==p.Value); }
    async Task Run() {
        testRoot=Path.Combine(workspace,"verification","update-fixture-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testRoot);
        File.Copy(Path.Combine(workspace,"FilmEcho.Updater.exe"),Path.Combine(testRoot,"FilmEcho.Updater.exe"));
        report.AppendLine("隔离目录："+testRoot+"；HTTP 数据为本地夹具，不连接 GitHub、不修改个人 data。");
        rsa=new RSACryptoServiceProvider(2048); rsa.PersistKeyInCsp=false; source=new UpdateSource { Repository="fixture/FilmEcho",PublicKeyXml=rsa.ToXmlString(false) };
        Check(UpdateSafety.Newer("v1.10.0","1.9.0") && !UpdateSafety.Newer("1.4.2","1.4.2.0"),"按数字段比较版本，v 前缀与四段版本兼容");
        Check(Reject(()=>UpdateSafety.VersionOf("1.4.3-beta")) && Reject(()=>new UpdateSource { Repository="../evil",PublicKeyXml=source.PublicKeyXml }.Validate()),"预发布格式和无效仓库地址拒绝");
        Check(Reject(()=>new UpdateSource { Repository=source.Repository }.Validate()),"缺少公钥时拒绝联网更新");
        BuildPackage();
        Check(UpdateSafety.Verify(envelope,source.PublicKeyXml).Files.Count==3,"RSA-SHA256 签名和完整文件清单通过验证");
        Check(UpdateSafety.Verify("\uFEFF"+envelope,source.PublicKeyXml).Files.Count==3,"带 UTF-8 BOM 的清单兼容 Windows PowerShell 输出");
        var altered=UpdateJson.Read<SignedUpdate>(envelope); altered.Payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(UpdateJson.Text(new { Schema=1,Version="9.9.9" })));
        Check(Reject(()=>UpdateSafety.Verify(UpdateJson.Text(altered),source.PublicKeyXml)),"篡改清单 payload 会被拒绝");
        using(var other=new RSACryptoServiceProvider(2048)) { other.PersistKeyInCsp=false; Check(Reject(()=>UpdateSafety.Verify(envelope,other.ToXmlString(false))),"错误发布者公钥会被拒绝"); }
        foreach(string path in new[]{"../outside.txt","/absolute.txt","C:/file.txt","data/learning.sqlite3","vendor/mpv/../../data/a","assets/a:stream","assets/CON.txt","assets/name. ",".updates/pending.json"}) Check(Reject(()=> { if(!UpdateSafety.Managed(path)) throw new UpdateException("path"); }),"禁止更新路径："+path);
        var inactiveHandler=Handler(); using(var inactive=Service(inactiveHandler,config:new UpdateSource())) Check(await inactive.Check(true,CancellationToken.None)==null && inactiveHandler.Requests==0,"未配置更新源时不发起任何网络请求");
        string cache=Path.Combine(testRoot,"update-cache.json"); var normal=Handler();
        using(var updater=Service(normal,cache:cache)) {
            var offer=await updater.Check(true,CancellationToken.None); Check(offer!=null && offer.Manifest.Version==AppInfo.Version && normal.Requests==2,"检查正式新版并验证签名，取得说明和下载地址");
            await updater.Check(false,CancellationToken.None); Check(normal.Requests==2,"24 小时成功检查缓存避免重复请求");
            string job=await updater.Download(offer,null,CancellationToken.None);
            Check(File.Exists(Path.Combine(job,"plan.json")) && File.ReadAllText(Path.Combine(job,"stage","使用说明.md"))=="新版说明","完整下载、哈希验证、解压和更新计划落盘");
            Check(!File.Exists(UpdateTransaction.Marker(testRoot)),"仅下载准备不会替换程序或写入安装授权");
            string beforeApp=Path.Combine(testRoot,UpdateSafety.ApplicationName); File.WriteAllText(beforeApp,"OLD EXE"); File.WriteAllText(Path.Combine(testRoot,"使用说明.md"),"旧版说明");
            using(var store=new Store(Path.Combine(testRoot,"data"))) { var movie=new MovieData { MoviePath=Path.Combine(testRoot,"user.mkv"),Position=17.5 }; movie.Words.Add(new WordRecord { Word="hello",Sentence="Hello world",Note="用户笔记" }); store.SaveMovie(movie); store.SaveSettings(new AppSettings { Volume=23,Language="en" }); }
            File.WriteAllText(Path.Combine(testRoot,"我的文件.txt"),"用户自放文件"); var before=Snapshot(testRoot);
            var plan=UpdateJson.Read<UpdatePlan>(File.ReadAllText(Path.Combine(job,"plan.json")));
            Check(Reject(()=>UpdateTransaction.Apply(plan,job,n=> { if(n==1) throw new IOException("injected failure"); })) && Same(before,Snapshot(testRoot)) && !File.Exists(UpdateTransaction.Marker(testRoot)),"替换首个文件后故障可完整回滚，用户数据和自放文件保持哈希一致");
            string second=await updater.Download(offer,null,CancellationToken.None); var plan2=UpdateJson.Read<UpdatePlan>(File.ReadAllText(Path.Combine(second,"plan.json")));
            using(var locked=new FileStream(Path.Combine(testRoot,"使用说明.md"),FileMode.Open,FileAccess.Read,FileShare.None)) Check(Reject(()=>UpdateTransaction.Apply(plan2,second)) && UpdateSafety.Hash(beforeApp)==before[Path.DirectorySeparatorChar+UpdateSafety.ApplicationName],"文件占用在替换前被发现，旧 EXE 保持不变");
            string third=await updater.Download(offer,null,CancellationToken.None); var plan3=UpdateJson.Read<UpdatePlan>(File.ReadAllText(Path.Combine(third,"plan.json")));
            UpdateTransaction.Apply(plan3,third); var after=Snapshot(testRoot);
            Check(File.ReadAllText(Path.Combine(testRoot,"使用说明.md"))=="新版说明" && UpdateSafety.Hash(beforeApp)==manifest.Files[0].Sha256,"正常替换程序与随包说明文件");
            Check(before.Where(p=>p.Key.StartsWith(Path.DirectorySeparatorChar+"data"+Path.DirectorySeparatorChar)||p.Key.EndsWith("我的文件.txt")).All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value),"成功升级后整个学习数据目录和用户文件哈希保持一致");
            using(var store=new Store(Path.Combine(testRoot,"data"))) { var movie=store.LoadMovie(Path.Combine(testRoot,"user.mkv")); Check(movie.Position==17.5 && movie.Words.Single().Note=="用户笔记" && store.LoadSettings().Volume==23 && store.IntegrityCheck()=="ok","升级后 SQLite 生词、原句、笔记、进度、设置可正常读取"); }
            UpdateTransaction.Recover(testRoot); Check(!File.Exists(UpdateTransaction.Marker(testRoot)),"已提交的更新清除残留标记，不回退成功版本");
            // Simulate an interrupted transaction after one replacement, using the real backup.
            var interrupted=new UpdateJournal { Job=third,State="Applying",Changed=new List<string> { UpdateSafety.ApplicationName },Existed=new List<string> { UpdateSafety.ApplicationName } };
            UpdateJson.Write(UpdateTransaction.Marker(testRoot),interrupted); UpdateTransaction.Recover(testRoot);
            Check(File.ReadAllText(beforeApp)=="OLD EXE" && !File.Exists(UpdateTransaction.Marker(testRoot)),"中断事务可从持久化日志恢复旧程序文件");
        }
        using(var cached=Service(Handler(),cache:cache)) Check(cached.Offer!=null,"重启读取缓存时重新验签，并恢复新版本提示");
        var latestHandler=Handler(); using(var latest=Service(latestHandler,AppInfo.Version)) Check(await latest.Check(true,CancellationToken.None)==null && latestHandler.Requests==1,"已是最新版时不请求清单或下载包");
        var failure=Handler(); failure.Reply=(r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using(var unavailable=Service(failure)) Check(await RejectAsync(async()=>await unavailable.Check(true,CancellationToken.None)),"HTTP 服务失败保持错误，不误报已是最新版");
        var pre=Handler(); pre.Reply=(r,t)=>Task.FromResult(TextResponse(UpdateJson.Text(new GithubRelease { tag_name="v"+AppInfo.Version,prerelease=true })));
        using(var prerelease=Service(pre)) Check(await RejectAsync(async()=>await prerelease.Check(true,CancellationToken.None)),"正式通道拒绝预发布版");
        var slow=Handler(); slow.Reply=async(r,t)=> { await Task.Delay(5000,t); return TextResponse("{}"); };
        using(var cancelled=Service(slow)) using(var token=new CancellationTokenSource(100)) Check(await RejectAsync(async()=>await cancelled.Check(true,token.Token)),"后台请求支持真实取消");
        await CheckBadPackages();
        var installed=UpdateSafety.VersionOf(AppInfo.Version); manifest.Version=installed.Major+"."+installed.Minor+"."+(installed.Build+1); envelope=Sign(manifest);
        using(var preview=Service(Handler(),AppInfo.Version)) { await preview.Check(true,CancellationToken.None); await VerifyLayout(preview); }
        string prepared=Path.Combine(workspace,"verification","prepared-update-fixture");
        if(Directory.Exists(prepared)) {
            var config=UpdateJson.Read<UpdateSource>(File.ReadAllText(Path.Combine(prepared,"update-source.example.json")));
            var signed=UpdateSafety.Verify(File.ReadAllText(Path.Combine(prepared,"FilmEcho-update.json")),config.PublicKeyXml);
            UpdateSafety.Extract(Path.Combine(prepared,signed.PackageName),Path.Combine(testRoot,"prepared-stage"),signed);
            Check(signed.Version==AppInfo.Version && File.Exists(Path.Combine(testRoot,"prepared-stage","source","updater","Program.cs")),"真实本地制包脚本生成的完整更新包可验签、校验并解压，包含更新器源码");
        }
    }
    async Task CheckBadPackages() {
        BuildPackage(); var handler=Handler();
        using(var update=Service(handler)) {
            var offer=await update.Check(true,CancellationToken.None); package[package.Length/2]^=1;
            Check(await RejectAsync(async()=>await update.Download(offer,null,CancellationToken.None)),"损坏下载包在 SHA256 检查时拒绝，程序不被替换");
        }
        BuildPackage(); var valid=package;
        using(var file=new MemoryStream()) {
            file.Write(valid,0,valid.Length); using(var zip=new ZipArchive(file,ZipArchiveMode.Update,true)) using(var s=zip.CreateEntry("../outside.txt").Open()) s.WriteByte(1); package=file.ToArray();
        }
        manifest.Size=package.Length; manifest.Sha256=Digest(package); envelope=Sign(manifest);
        using(var malicious=Service(Handler())) {
            var offer=await malicious.Check(true,CancellationToken.None);
            Check(await RejectAsync(async()=>await malicious.Download(offer,null,CancellationToken.None)) && !File.Exists(Path.Combine(testRoot,"outside.txt")),"ZIP 路径穿越被拒绝，即使包整体哈希和签名正确");
        }
        BuildPackage(); manifest.Files[2].Sha256=new string('0',64); envelope=Sign(manifest);
        using(var badFile=Service(Handler())) { var offer=await badFile.Check(true,CancellationToken.None); Check(await RejectAsync(async()=>await badFile.Download(offer,null,CancellationToken.None)),"每个解压文件单独验哈希，清单不符时拒绝"); }
        BuildPackage(); manifest.Version="1.99.0"; envelope=Sign(manifest);
        using(var wrongVersion=Service(Handler())) { var offer=await wrongVersion.Check(true,CancellationToken.None); Check(await RejectAsync(async()=>await wrongVersion.Download(offer,null,CancellationToken.None)),"实际 EXE 版本与签名版本不一致时拒绝"); }
        BuildPackage();
    }
    async Task VerifyLayout(UpdateService preview) {
        using(var form=new MainForm(null,true,updatePreview:true)) {
            form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-30000,-30000); form.ShowInTaskbar=false; form.Show(); await Task.Delay(60);
            form.VerifyUpdateLayout(Check,preview);
        }
        foreach(bool english in new[]{false,true}) foreach(bool dark in new[]{false,true}) using(var dialog=new UpdateDialog(preview,ThemePalette.For(dark),english,null,()=>{},job=>false)) {
            dialog.StartPosition=FormStartPosition.Manual; dialog.Location=new Point(-30000,-30000); dialog.Show(); await Task.Delay(30);
            Check(dialog.Controls.Cast<Control>().SelectMany(All).OfType<Label>().Any(c=>c.Text.Contains(english?"New version":"新版本")),"更新窗口版本说明完整："+(english?"英文":"中文")+(dark?"深色":"浅色"));
            Preview(dialog,Path.Combine(workspace,"verification","update-dialog-"+(dark?"dark":"light")+"-"+(english?"en":"zh")+".png")); dialog.Close();
        }
        using(var dialog=new UpdateDialog(null,ThemePalette.For(false),false,null,()=>{},job=>false)) {
            dialog.StartPosition=FormStartPosition.Manual; dialog.Location=new Point(-30000,-30000); dialog.Show(); await Task.Delay(30);
            Check(dialog.Controls.Cast<Control>().SelectMany(All).OfType<Label>().Any(c=>c.Text.Contains("尚未启用")) && dialog.Controls.Cast<Control>().SelectMany(All).OfType<Button>().Count(c=>c.Enabled)==1,"未配置服务时明确显示未启用，只保留关闭操作");
            Preview(dialog,Path.Combine(workspace,"verification","update-dialog-inactive.png")); dialog.Close();
        }
    }
    static IEnumerable<Control> All(Control c) { yield return c; foreach(Control child in c.Controls) foreach(var x in All(child)) yield return x; }
    internal static void Preview(Form form,string path) {
        using(var bitmap=new Bitmap(form.Width,form.Height)) {
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            using(var g=Graphics.FromImage(bitmap)) foreach(var text in form.Controls.Cast<Control>().SelectMany(All).OfType<RichTextBox>().Where(t=>t.Visible)) {
                var p=form.PointToClient(text.PointToScreen(Point.Empty)); p.Offset((form.Width-form.ClientSize.Width)/2,form.Height-form.ClientSize.Height-(form.Width-form.ClientSize.Width)/2);
                using(var brush=new SolidBrush(text.BackColor)) g.FillRectangle(brush,new Rectangle(p,text.Size));
                using(var brush=new SolidBrush(text.ForeColor)) g.DrawString(text.Text,text.Font,brush,new RectangleF(p.X+4,p.Y+3,text.Width-12,text.Height-6));
            }
            bitmap.Save(path,ImageFormat.Png);
        }
    }
}
public sealed partial class MainForm {
    internal void VerifyUpdateLayout(Action<bool,string> check,UpdateService preview) {
        updates=preview;
        foreach(bool english in new[]{false,true}) foreach(bool dark in new[]{false,true}) {
            Size=MinimumSize; ApplyLanguage(english?"en":"zh",false); ApplyTheme(dark?"dark":"light",false); PerformLayout(); RefreshUpdateButton();
            var bounds=RectangleToScreen(ClientRectangle); var button=updateButton.RectangleToScreen(updateButton.ClientRectangle);
            check(bounds.Contains(button) && !button.IntersectsWith(languageChoice.RectangleToScreen(languageChoice.ClientRectangle)) && !button.IntersectsWith(themeChoice.RectangleToScreen(themeChoice.ClientRectangle)),"最小窗口更新按钮可见且不与主题、语言重叠："+(english?"英文":"中文")+(dark?"深色":"浅色"));
            check(updateButton.Available && updateButton.Text==(english?"New version":"新版本"),"新版本提示及亮色状态随语言切换");
            UpdateVerificationForm.Preview(this,Path.Combine(root,"verification","update-main-"+(dark?"dark":"light")+"-"+(english?"en":"zh")+".png"));
        }
        updates=null;
    }
}
}
