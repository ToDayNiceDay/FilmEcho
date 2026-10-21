using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
internal sealed class UpdateIndicator : Button {
    public ThemePalette Palette=ThemePalette.For(false);
    public bool Available,Checking;
    public UpdateIndicator() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); FlatStyle=FlatStyle.Flat; Cursor=Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent?.BackColor??Palette.Background); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        ModernPaint.Card(e.Graphics,ModernPaint.Box(this),Available?Palette.Primary:Palette.Button,Focused?Palette.Accent:Palette.Border);
        Color color=Available?Palette.PrimaryText:Palette.Text;
        using(var pen=new Pen(color,1.7f)) {
            e.Graphics.DrawArc(pen,10,8,18,18,30,290);
            e.Graphics.DrawLines(pen,new[]{new Point(24,7),new Point(28,12),new Point(22,13)});
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(32,0,Width-34,Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
        if(Available) { using(var dot=new SolidBrush(Color.FromArgb(255,201,82))) e.Graphics.FillEllipse(dot,Width-12,3,8,8); using(var outline=new Pen(Palette.Primary,1)) e.Graphics.DrawEllipse(outline,Width-12,3,8,8); }
    }
}
internal static class UpdateMessages {
    public static string Error(Exception ex,bool english) {
        var problem=ex as UpdateException; string code=problem?.Code;
        switch(code) {
            case "source": return english?"The update source is invalid. Please contact the publisher.":"更新源配置无效，请联系发布者。";
            case "key": return english?"The publisher's verification key is not configured.":"发布者尚未配置更新验证公钥。";
            case "signature": return english?"The update signature could not be verified. Nothing was installed.":"更新签名验证失败，未安装任何文件。";
            case "manifest": return english?"The release is missing a valid Windows update package.":"此版本缺少有效的 Windows 更新包或更新清单。";
            case "release": return english?"No public stable release was found. Please check again later.":"尚未找到可公开访问的正式版本，请稍后再检查。";
            case "limit": return english?"The update service has limited requests. Please try again later.":"更新服务暂时限制了请求，请稍后重试。";
            case "architecture": return english?"This update is not a Windows x64 application.":"更新包中的程序不是 Windows x64 版本。";
            case "hash": return english?"The update package is incomplete or damaged. Please download it again.":"更新包不完整或已损坏，请重新下载。";
            case "path": return english?"The update contains an unsafe file path and was rejected.":"更新包包含不安全的文件路径，已拒绝更新。";
            case "version": return english?"The update version does not match, or it is not newer.":"更新版本不匹配，或版本并未高于当前版本。";
            case "space": return english?"There is not enough disk space for this update.":"磁盘剩余空间不足，无法准备更新。";
            case "updater": return english?"The updater is missing. Please download the full application package.":"更新器文件缺失，请下载完整程序包。";
            case "pending": return english?"An earlier update needs recovery. Restart the application first.":"上次更新尚待恢复，请先重新启动程序。";
            case "locked": return english?"Some application files are in use or cannot be replaced.":"部分程序文件正在使用或无法替换，请检查写入权限。";
            case "rollback": return english?"Recovery is incomplete. Keep the .updates folder and restart for recovery.":"旧版本恢复尚未完成，请保留 .updates 文件夹并重启以恢复。";
        }
        if(ex is OperationCanceledException) return english?"The request timed out or was cancelled. You can try again.":"请求超时或已取消，可以重试。";
        if(ex is HttpRequestException) return english?"Could not reach the update service. Check your network and try again.":"暂时无法连接更新服务，请检查网络后重试。";
        if(ex is UnauthorizedAccessException || ex is IOException) return english?"Could not write update files. Check folder permissions, disk space and files in use.":"无法写入更新文件，请检查目录权限、磁盘空间和文件占用。";
        return english?"The update could not be completed. Please try again later.":"更新未能完成，请稍后重试。";
    }
}
internal sealed class UpdateDialog : Form {
    readonly UpdateService service;
    readonly bool english;
    readonly Action changed;
    readonly Func<string,bool> install;
    readonly Label description=new Label(),state=new Label();
    readonly RichTextBox notes=new RichTextBox();
    readonly ProgressBar progress=new ProgressBar();
    readonly ModernButton primary=new ModernButton(),check=new ModernButton(),close=new ModernButton();
    CancellationTokenSource operation;
    UpdateOffer offer;
    string job;
    bool busy,wantClose;
    readonly string setupError;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    public UpdateDialog(UpdateService updater,ThemePalette palette,bool en,Icon icon,Action onChanged,Func<string,bool> onInstall,string error=null) {
        service=updater; english=en; changed=onChanged; install=onInstall; setupError=error;
        Text=en?"FilmEcho · Updates":"FilmEcho · 软件更新"; Icon=icon; Font=new Font("Microsoft YaHei UI",10); BackColor=palette.Background; ForeColor=palette.Text;
        Size=new Size(670,540); MinimumSize=new Size(580,450); StartPosition=FormStartPosition.CenterParent; ShowInTaskbar=false; MinimizeBox=false; MaximizeBox=false;
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(22,18,22,16) };
        foreach(int height in new[]{48,32,66}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text=en?"Software updates":"软件更新",Font=new Font(Font.FontFamily,17,FontStyle.Bold),ForeColor=palette.Accent,Dock=DockStyle.Fill },0,0);
        description.Dock=DockStyle.Fill; state.Dock=DockStyle.Fill; layout.Controls.Add(description,0,1); layout.Controls.Add(state,0,2);
        notes.Dock=DockStyle.Fill; notes.ReadOnly=true; notes.DetectUrls=false; notes.BorderStyle=BorderStyle.None; notes.BackColor=palette.Field; notes.ForeColor=palette.Text; notes.Font=Font; layout.Controls.Add(notes,0,3);
        progress.Dock=DockStyle.Fill; progress.Margin=new Padding(0,8,0,2); progress.Visible=false; layout.Controls.Add(progress,0,4);
        var footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,9,0,0) }; layout.Controls.Add(footer,0,5);
        foreach(var b in new[]{close,primary,check}) { b.Size=new Size(146,34); b.Margin=new Padding(4,0,0,0); b.Palette=palette; b.BackColor=palette.Button; b.ForeColor=palette.Text; footer.Controls.Add(b); }
        close.Width=92; primary.BackColor=palette.Primary; primary.ForeColor=palette.PrimaryText;
        check.Text=en?"Check again":"检查更新"; close.Text=en?"Close":"关闭";
        primary.Click+=async(s,e)=> { if(job!=null) { if(install(job)) { job=null; Close(); } } else if(offer!=null) await Download(); else await Check(); };
        check.Click+=async(s,e)=>await Check(); close.Click+=(s,e)=> { if(busy) operation?.Cancel(); else Close(); };
        FormClosing+=(s,e)=> { if(busy) { wantClose=true; operation?.Cancel(); e.Cancel=true; } };
        Shown+=async(s,e)=> { Render(); if(service?.Source.Enabled==true && service.Offer==null && setupError==null) await Check(); };
        offer=service?.Offer;
        HandleCreated+=(s,e)=> { try { int dark=palette.Background.GetBrightness()<.3?1:0; DwmSetWindowAttribute(Handle,20,ref dark,4); } catch { } };
    }
    void Render() {
        bool enabled=service?.Source.Enabled==true && setupError==null;
        description.Text=(english?"Current version: ":"当前版本：")+AppInfo.Version+(offer!=null?(english?"    New version: ":"    新版本：")+offer.Manifest.Version:"");
        state.Text=setupError??(!enabled?(english?"The update service is not enabled yet. You can keep using this version.":"更新服务尚未启用，可以继续使用当前版本。"):
            job!=null?(english?"Ready to update. Your current notes and progress will be saved before restarting.":"更新已准备好。重启前会保存当前笔记和播放进度。"):
            offer!=null?(english?"A new version is available. Download it now and choose when to restart.":"发现新版本。可先下载，再选择何时重启更新。"):(english?"Check for a newer version.":"可以检查是否有新版本。"));
        notes.Text=offer!=null?(offer.Notes+"\r\n\r\n"+(english?"Download size: ":"下载大小：")+(offer.Manifest.Size/1048576d).ToString("0.0")+" MB"):
            (english?"Updates keep your movie words, notes, preferences and playback progress. You can cancel a download or update later.":"更新会保留电影生词、笔记、设置和播放进度。可取消下载，也可稍后再更新。");
        primary.Text=job!=null?(english?"Restart & update":"重启并更新"):offer!=null?(english?"Download update":"下载更新"):(english?"Check updates":"检查更新");
        primary.Enabled=check.Enabled=enabled && !busy; check.Enabled&=job==null;
        primary.Visible=enabled; check.Visible=enabled && offer!=null;
    }
    void SetBusy(bool value) { busy=value; primary.Enabled=!value && service?.Source.Enabled==true && setupError==null; check.Enabled=primary.Enabled && job==null; close.Text=value?(english?"Cancel":"取消"):(english?"Close":"关闭"); }
    async Task Check() {
        if(busy || service==null || !service.Source.Enabled) return;
        operation=new CancellationTokenSource(); SetBusy(true); state.Text=english?"Checking for updates…":"正在检查更新…";
        try { offer=await service.Check(true,operation.Token); changed(); Render(); if(offer==null) state.Text=english?"You are using the latest version.":"当前已是最新版本。"; }
        catch(Exception ex) { state.Text=UpdateMessages.Error(ex,english); }
        finally { operation.Dispose(); operation=null; SetBusy(false); if(wantClose) Close(); }
    }
    async Task Download() {
        operation=new CancellationTokenSource(); SetBusy(true); progress.Value=0; progress.Visible=true;
        state.Text=english?"Downloading and verifying the update…":"正在下载并验证更新…";
        try { job=await service.Download(offer,new Progress<int>(n=> { if(!IsDisposed) { progress.Value=Math.Max(0,Math.Min(100,n)); state.Text=(english?"Downloading / verifying: ":"下载 / 验证：")+n+"%"; } }),operation.Token); Render(); }
        catch(Exception ex) { state.Text=UpdateMessages.Error(ex,english); }
        finally { operation.Dispose(); operation=null; SetBusy(false); if(wantClose) Close(); }
    }
    protected override void Dispose(bool disposing) {
        if(disposing) { operation?.Cancel(); if(job!=null) try { Directory.Delete(job,true); } catch { } }
        base.Dispose(disposing);
    }
}
public sealed partial class MainForm {
    readonly UpdateIndicator updateButton=new UpdateIndicator { Width=142,Height=34,Margin=new Padding(6,0,0,0) };
    readonly System.Windows.Forms.Timer updateTimer=new System.Windows.Forms.Timer { Interval=3600000 };
    readonly CancellationTokenSource updateLifetime=new CancellationTokenSource();
    UpdateService updates;
    string updateSetupError;
    bool checkingUpdates;
    string installingJob;
    void SetupUpdates() {
        updateButton.Click+=(s,e)=> { using(var dialog=new UpdateDialog(updates,theme,EnglishUi,Icon,RefreshUpdateButton,BeginUpdate,updateSetupError)) dialog.ShowDialog(this); };
        if(!verificationMode) {
            try { updates=new UpdateService(root,AppInfo.Version,UpdateSource.Load(root),Path.Combine(store.Root,"update-check.json")); }
            catch(Exception ex) { updateSetupError=UpdateMessages.Error(ex,EnglishUi); }
            updateTimer.Tick+=async(s,e)=>await CheckUpdates(); updateTimer.Start(); Shown+=async(s,e)=>await CheckUpdates();
        }
        RefreshUpdateButton();
        FormClosed+=(s,e)=> { if(installingJob!=null) File.WriteAllText(Path.Combine(installingJob,"apply.ready"),"ready"); };
    }
    async Task CheckUpdates() {
        if(checkingUpdates || updates==null || !updates.Source.Enabled || closing) return;
        checkingUpdates=true; RefreshUpdateButton();
        try { await updates.Check(false,updateLifetime.Token); }
        catch(Exception ex) { try { File.AppendAllText(Path.Combine(root,"logs","update.log"),DateTime.UtcNow.ToString("o")+" "+ex.GetType().Name+": "+ex.Message+Environment.NewLine); } catch { } }
        finally { checkingUpdates=false; if(!closing && !IsDisposed) RefreshUpdateButton(); }
    }
    void RefreshUpdateButton() {
        updateButton.Available=updates?.Offer!=null; updateButton.Checking=checkingUpdates;
        updateButton.Text=EnglishUi?(checkingUpdates?"Checking":updateButton.Available?"New version":"Update"):(checkingUpdates?"检查中":updateButton.Available?"新版本":"更新");
        updateButton.AccessibleName=EnglishUi?(updateButton.Available?"A new version is available":"Check for updates"):(updateButton.Available?"发现新版本，点击查看":"检查软件更新");
        updateButton.Palette=theme; updateButton.Invalidate();
    }
    bool BeginUpdate(string job) {
        if(loading) { MessageBox.Show(EnglishUi?"Wait for the movie to finish loading before updating.":"请等待电影加载完成后再更新。",Text); return false; }
        try {
            if(currentWord!=null && movie!=null && movie.Words.Contains(currentWord)) currentWord.Note=note.Text;
            if(!Save(true)) return false;
            var plan=UpdateJson.Read<UpdatePlan>(File.ReadAllText(Path.Combine(job,"plan.json"))); plan.English=EnglishUi; UpdateJson.Write(Path.Combine(job,"plan.json"),plan);
            var info=new ProcessStartInfo(Path.Combine(job,"FilmEcho.Updater.exe"),"--apply \""+Path.Combine(job,"plan.json")+"\"") { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=job };
            using(var process=Process.Start(info)) { }
            installingJob=job;
            // Let the modal dialog relinquish ownership of the prepared job first.
            BeginInvoke(new Action(()=> { Close(); if(!closing) { File.WriteAllText(Path.Combine(job,"abort"),"cancelled"); installingJob=null; } }));
            return true;
        } catch(Exception ex) { installingJob=null; MessageBox.Show(UpdateMessages.Error(ex,EnglishUi),EnglishUi?"Update failed":"更新失败",MessageBoxButtons.OK,MessageBoxIcon.Error); return false; }
    }
    void StopUpdates() { updateTimer.Stop(); updateTimer.Dispose(); updateLifetime.Cancel(); updates?.Dispose(); }
}
}
