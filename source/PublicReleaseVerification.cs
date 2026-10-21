using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
internal sealed class PublicReleaseVerificationForm : Form {
    readonly StringBuilder report=new StringBuilder(); int failed;
    public PublicReleaseVerificationForm() {
        ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; Location=new Point(-30000,-30000); Size=new Size(100,100);
        Shown+=async(s,e)=> {
            Directory.CreateDirectory(Path.Combine(Application.StartupPath,"verification"));
            Action<bool,string> check=(ok,name)=> { if(!ok) failed++; report.AppendLine((ok?"PASS: ":"FAIL: ")+name); };
            try {
                check(AppInfo.PublicRelease && AppInfo.Version=="1.4.3","运行独立的 1.4.3 发布版");
                check(!typeof(MainForm).Assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("WebView2")),"发布 EXE 没有 WebView2 程序集依赖");
                using(var form=new MainForm(null,true,networkClient:new HttpClient(new PublicOfflineFixture()),updatePreview:true)) {
                    form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-30000,-30000); form.ShowInTaskbar=false; form.Show();
                    await form.VerifyPublicRelease(check);
                }
            } catch(Exception ex) { check(false,"发布版验证异常："+ex); }
            finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(Application.StartupPath,"verification","public-release-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
        };
    }
    sealed class PublicOfflineFixture : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage=message,Content=new StringContent("{}") }); }
    }
}
public sealed partial class MainForm {
    internal async Task VerifyPublicRelease(Action<bool,string> check) {
        settings.LibraryFolder=Path.Combine(root,"verification"); launchMovie=Path.Combine(root,"verification","sample.mkv");
        await Initialize();
        check(ready && movie!=null && !loading,"无 source 文件夹和 WebView2 Runtime 依赖即可启动并加载测试片");
        check(tabs.TabPages.Count==2 && AppInfo.SavedTabIndex==1 && !AllControls(this).Any(c=>c.Visible && c.Text.Contains("Google")),"删除 Google 页签，释义与本片生词页签位置正确");
        var initial=DefaultBrowser.StartInfo();
        check(File.Exists(initial.FileName) && initial.Arguments=="" && initial.UseShellExecute,"解析 Windows 当前默认浏览器，只启动 EXE、不携带网页或查询参数");
        int launches=0; ProcessStartInfo launched=null; BrowserLauncher=info=> { launches++; launched=info; };
        query.Text="browser draft"; note.Text="笔记草稿"; sentence.Text="sentence draft";
        AllControls(this).OfType<Button>().Single(b=>b.Text=="打开浏览器").PerformClick();
        check(launches==1 && launched.FileName==initial.FileName && launched.Arguments=="" && query.Text=="browser draft" && note.Text=="笔记草稿" && sentence.Text=="sentence draft","实际点击浏览器入口调用启动流程，保留输入且不发送学习内容（测试启动器替身）");
        check(!HelpDialog.HelpText(false).Contains("Google") && !HelpDialog.HelpText(true).Contains("Google") && !HelpDialog.HelpText(false).Contains("点“查看示例”"),"欢迎页与帮助说明同步移除 Google 和统计操作指引");
        workspaceTabs.SelectedIndex=1; await Task.Delay(80); usagePage.Dashboard.Refresh();
        check(usagePage.Dashboard.MapOnly && !AllControls(usagePage).Any(c=>c is Button || c is ModernComboBox) && !usagePage.HasData,"地区页只有地图看板，没有统计按钮、下拉框或虚构数据");
        using(var bitmap=new Bitmap(usagePage.Dashboard.Width,usagePage.Dashboard.Height)) usagePage.Dashboard.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
        var view=usagePage.Dashboard.MapView;
        check(view!=null && UsageGeography.Instance.Regions.Count(r=>r.Code!="border")==34 && view.Sea["HI"].PointCount>0,"省级边界和南海诸岛附图完整保留");
        usagePage.Dashboard.SelectRegion("BJ");
        check(usagePage.Dashboard.SelectedCode=="BJ","地区看板保留地区选择交互");
        foreach(bool english in new[]{false,true}) foreach(bool dark in new[]{false,true}) {
            ApplyLanguage(english?"en":"zh",false); ApplyTheme(dark?"dark":"light",false); Size=MinimumSize; PerformLayout();
            workspaceTabs.SelectedIndex=0;
            var button=AllControls(this).OfType<Button>().Single(b=>b.Text==(english?"Open browser":"打开浏览器"));
            check(RectangleToScreen(ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)) && !definition.Text.Contains("Google"),"浏览器入口与释义文案适配最小窗口："+(english?"英文":"中文")+(dark?"深色":"浅色"));
            UpdateVerificationForm.Preview(this,Path.Combine(root,"verification","public-study-"+(dark?"dark":"light")+"-"+(english?"en":"zh")+".png"));
            workspaceTabs.SelectedIndex=1; usagePage.Dashboard.Refresh();
            check(usagePage.Dashboard.Width>=850 && usagePage.Dashboard.MapOnly && !AllControls(usagePage).Any(c=>c is Button || c is ModernComboBox),"地区地图适配主题、语言和最小窗口");
            UpdateVerificationForm.Preview(this,Path.Combine(root,"verification","public-map-"+(dark?"dark":"light")+"-"+(english?"en":"zh")+".png"));
        }
        ApplyLanguage("zh",false); workspaceTabs.SelectedIndex=0;
        await Seek(4.5); await mpv.WaitForVideo(); await Task.Delay(100); sentenceEdited=false; UpdateSentence(); query.Text="hello"; await Lookup(); await lookupSupplement;
        check(currentWord!=null && !string.IsNullOrWhiteSpace(currentWord.Meaning) && currentWord.Sentence.Contains("Hello"),"发布版实际暂停、同步字幕和离线查词正常");
        note.Text="发布版隔离测试笔记"; SaveNote();
        check(store.LoadMovie(movie.MoviePath).Words.Any(w=>w.Note=="发布版隔离测试笔记") && tabs.TabPages[1].Text.Contains("1"),"发布版保存笔记到 SQLite，本片生词计数更新到正确页签");
        string script=OcrRuntime.Script(root,Path.Combine(store.Root,"ocr-fixture"));
        check(File.Exists(script) && File.ReadAllText(script).Contains("Windows.Media.Ocr"),"OCR 从内嵌资源准备运行脚本，不依赖发布包中的源码目录");
        string other=Path.Combine(store.Root,"another-movie.mkv"); var isolated=store.LoadMovie(other);
        check(isolated.Words.Count==0 && store.IntegrityCheck()=="ok","不同电影学习记录仍然隔离，SQLite 完整性正常");
    }
}
}
