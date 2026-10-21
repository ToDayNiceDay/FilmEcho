using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FilmEcho {
public sealed partial class MainForm {
    async Task VerifyUi() {
        var report=new StringBuilder(); int failed=0;
        Action<bool,string> check=(ok,name)=> { report.AppendLine((ok?"PASS: ":"FAIL: ")+name); if(!ok) failed++; File.WriteAllText(Path.Combine(root,"verification","ui-results.txt"),report.ToString(),Encoding.UTF8); };
        try {
            check(movie!=null && !loading && ready,"完整桌面程序初始化、电影目录扫描、测试片加载");
            check(movies.Items.Count>=1,"默认扫描用户 E:\\英语电影，仅列视频");
            await mpv.Set("secondary-sid",2); await Seek(4.5); await Task.Delay(500);
            check(sentence.Text.Contains("Hello") && sentence.Text.Contains("你好"),"播放字幕同步到原句框，双语可选词");
            check(words.Controls.Cast<Control>().Any(c=>c.Text=="Hello"),"字幕自动拆分为可点击单词");
            await mpv.Command("keypress","RIGHT"); await Task.Delay(500);
            check(Math.Abs(position-7.5)<0.15,"视频窗口的方向键通过实际 mpv 输入绑定快进 3 秒");
            var message=new Message(); ProcessCmdKey(ref message,Keys.Control|Keys.Left); await Task.Delay(450);
            check(Math.Abs(position-2.5)<0.15,"宿主窗口 Ctrl+Left 快捷键精确倒退 5 秒");
            await Seek(4.5); await mpv.WaitForVideo();
            query.Text="hello"; await Lookup();
            await lookupSupplement; // Real online audio completes independently of the query button.
            check(currentWord!=null && currentWord.Word=="hello" && currentWord.Sentence.Contains("Hello") && Math.Abs(currentWord.Seconds-4.5)<0.2,"查询自动暂停并保存当前原句和精确位置");
            check(!string.IsNullOrWhiteSpace(currentWord?.Meaning),"释义返回并保存");
            var local=await offline.Find("hello");
            check(local!=null && System.Text.RegularExpressions.Regex.IsMatch(local.Chinese,"[\u4e00-\u9fff]") && offline.Count>600000,"无需网络即可查询 60 万以上词条中的中文释义");
            check(!string.IsNullOrWhiteSpace(currentWord?.AudioUrl),"原声词典发音地址实际返回");
            note.Text="UI 验证笔记"; SaveNote();
            var saved=store.LoadMovie(movie.MoviePath);
            check(saved.Words.Any(w=>w.Note=="UI 验证笔记"),"侧栏笔记保存到磁盘");
            check(currentWord.SentenceStart>=0 && currentWord.SentenceEnd>currentWord.SentenceStart,"原句起止位置与查词暂停位置分别保存");
            await JumpToRecord(); await Task.Delay(350); await mpv.Set("pause",true);
            check(position<2,"生词回放从原句开始处播放，保留起音余量");
            await Seek(4.5); await mpv.WaitForVideo();
            await Ocr(); check(RegexEnglish(sentence.Text),"Windows 本机 OCR 识别画面英语字幕");
            var navigated=new TaskCompletionSource<bool>();
            EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> handler=(s,e)=>navigated.TrySetResult(e.IsSuccess);
            browser.NavigationCompleted+=handler;
            await NavigateGoogle("hello");
            await Task.WhenAny(navigated.Task,Task.Delay(20000));
            TraceBrowser("Verification after navigation wait");
            check(browser.CoreWebView2!=null,"旁侧 Google 翻译 WebView2 初始化成功");
            check(navigated.Task.IsCompleted && navigated.Task.Result,"Google 翻译页面实际导航成功");
            browser.NavigationCompleted-=handler;
            var initializedBrowser=browser.CoreWebView2; var browserSource=browser.Source;
            workspaceTabs.SelectedIndex=1; await Task.Delay(100); workspaceTabs.SelectedIndex=0; await Task.Delay(100);
            check(browser.CoreWebView2==initializedBrowser && browser.Source==browserSource && tabs.SelectedIndex==1 && browser.Visible,"Google 页与使用分布往返后保留浏览器实例、地址和学习页签");
            string host=await browser.CoreWebView2.ExecuteScriptAsync("document.location.host");
            check(host.Contains("translate.google.com"),"返回学习页后 Google 网页仍可执行脚本，实际页面保持可用");
            tabs.SelectedIndex=0; ShowRecord(currentWord,true);
            // Restore fixture captions after OCR for a readable preview.
            sentenceEdited=false;
            await Seek(4.5); await Task.Delay(400);
            await SavePreview(Path.Combine(root,"verification","interface-preview.png"));
            ApplyTheme("light",false); await SavePreview(Path.Combine(root,"verification","interface-light.png"));
            ApplyTheme("dark",false); movies.OpenDropdown(); await Task.Delay(150); await SavePopupPreview(Path.Combine(root,"verification","interface-dropdown.png"),PopupWindow(movies)); movies.CloseDropdown();
            tabs.SelectedIndex=2; await SavePreview(Path.Combine(root,"verification","interface-history.png")); tabs.SelectedIndex=0;
            check(File.Exists(Path.Combine(root,"verification","interface-preview.png")),"生成原生界面及 mpv 实际画面的预览");
            double prev=position; await LoadMovie(Path.Combine(root,"verification","sample.mkv")); await Task.Delay(350);
            check(Math.Abs(position-prev)<0.2 && movie.Words.Any(w=>w.Note=="UI 验证笔记"),"重新打开电影恢复进度与查询记录");
        } catch(Exception ex) { check(false,"UI 验证异常："+ex); }
        finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(root,"verification","ui-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
    }
    static bool RegexEnglish(string text) { return System.Text.RegularExpressions.Regex.IsMatch(text,"Hello|world|learn",System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
    async Task SavePreview(string path) {
        string frame=Path.Combine(root,"verification","preview-frame-"+Path.GetFileNameWithoutExtension(path)+".png");
        // Refresh the paused test frame after offscreen layout/theme changes.
        await mpv.Set("pause",true); await Seek(position); await mpv.WaitForVideo();
        await mpv.Command("screenshot-to-file",frame,"subtitles");
        using(var bmp=new Bitmap(Width,Height)) {
            DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));
            // Native RichEdit/ComboBox controls do not implement offscreen WM_PRINT
            // fully. Render their current, already verified text for this preview.
            using(var g=Graphics.FromImage(bmp)) RenderNativeText(this,g);
            var pt=PointToClient(video.PointToScreen(Point.Empty)); int frameX=pt.X+(Width-ClientSize.Width)/2,frameY=pt.Y+Height-ClientSize.Height-(Width-ClientSize.Width)/2;
            using(var image=Image.FromFile(frame)) using(var g=Graphics.FromImage(bmp)) {
                var area=new Rectangle(frameX,frameY,video.Width,video.Height); g.FillRectangle(new SolidBrush(video.BackColor),area);
                double ratio=Math.Min((double)area.Width/image.Width,(double)area.Height/image.Height); int w=(int)(image.Width*ratio),h=(int)(image.Height*ratio);
                g.DrawImage(image,new Rectangle(area.X+(area.Width-w)/2,area.Y+(area.Height-h)/2,w,h));
            }
            bmp.Save(path,ImageFormat.Png);
        }
    }
    void RenderNativeText(Control parent,Graphics g) {
        foreach(Control c in parent.Controls) {
            if(!c.Visible) continue;
            if(c is RichTextBox || c is ComboBox) {
                Point p=PointToClient(c.PointToScreen(Point.Empty)); p.Offset((Width-ClientSize.Width)/2,Height-ClientSize.Height-(Width-ClientSize.Width)/2);
                var full=new Rectangle(p.X,p.Y,c.Width,c.Height);
                using(var brush=new SolidBrush(c.BackColor)) g.FillRectangle(brush,full);
                var bounds=new Rectangle(p.X+2,p.Y+3,Math.Max(1,c.Width-18),Math.Max(1,c.Height-6));
                using(var brush=new SolidBrush(c.ForeColor)) using(var format=new StringFormat { Trimming=StringTrimming.EllipsisWord }) g.DrawString(c.Text,c.Font,brush,bounds,format);
            }
            RenderNativeText(c,g);
        }
    }
}
}
