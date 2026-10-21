using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
#if !PUBLIC_RELEASE
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
#endif

namespace FilmEcho {
static class Program {
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException+=(s,e)=> { try { Directory.CreateDirectory(Path.Combine(Application.StartupPath,"logs")); File.AppendAllText(Path.Combine(Application.StartupPath,"logs","app-error.txt"),e.Exception+"\r\n"); } catch { } if(!args.Any(x=>x.EndsWith("-verify"))) MessageBox.Show(e.Exception.Message,"英语跟读播放器",MessageBoxButtons.OK,MessageBoxIcon.Error); };
        if(args.Contains("--update-verify")) { Application.Run(new UpdateVerificationForm()); return; }
        if(args.Contains("--release-verify")) { Application.Run(new PublicReleaseVerificationForm()); return; }
        if(!args.Any(x=>x.EndsWith("-verify")) && UpdateStartup.RecoverIfNeeded(Application.StartupPath)) return;
#if !PUBLIC_RELEASE
        if(args.Contains("--verify")) { Application.Run(new VerificationForm()); return; }
        if(args.Contains("--real-verify")) { Application.Run(new RealVerificationForm()); return; }
        if(args.Contains("--welcome-verify")) { Application.Run(new WelcomeVerificationForm()); return; }
        if(args.Contains("--ui-verify") || args.Contains("--controls-verify") || args.Contains("--usage-verify") || args.Contains("--focus-verify") || args.Contains("--lookup-verify")) {
            try { Application.Run(new MainForm(Path.Combine(Application.StartupPath,"verification","sample.mkv"),true,args.Contains("--controls-verify"),args.Contains("--usage-verify"),args.Contains("--focus-verify"),args.Contains("--lookup-verify"),args.Contains("--lookup-verify")?new HttpClient(new LookupFixtureHandler()):null)); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(Application.StartupPath,"verification","ui-error.txt"),ex.ToString()); Environment.ExitCode=1; }
            return;
        }
#endif
        bool created;
        using(var mutex=new Mutex(true,"Local\\FilmEcho-"+Store.Key(Application.StartupPath),out created)) {
            if(!created) { MessageBox.Show("播放器已经打开，请使用已打开的窗口。","英语跟读播放器"); return; }
            try { if(!StartupWelcome.ShowIfNeeded(Application.StartupPath)) return; }
            catch(Exception ex) { MessageBox.Show("启动设置无法读取："+ex.Message,"FilmEcho",MessageBoxButtons.OK,MessageBoxIcon.Error); return; }
            Application.Run(new MainForm(args.FirstOrDefault(x=>!x.StartsWith("--"))));
        }
    }
}
public sealed class TrackChoice {
    public string Id;
    public string Name;
    public string Lang;
    public string Codec;
    public override string ToString() { return Name; }
}
public sealed partial class MainForm : Form {
    readonly string root=Application.StartupPath;
    readonly Store store;
    readonly AppSettings settings;
    readonly MpvClient mpv=new MpvClient();
    readonly SemaphoreSlim loadGate=new SemaphoreSlim(1,1);
    readonly HttpClient http;
    readonly OfflineDictionary offline;
    readonly SplitContainer split=new SplitContainer();
    readonly VideoSurface video=new VideoSurface();
    readonly Label status=new Label();
    readonly Label clock=new Label();
    readonly Label movieTitle=new Label();
    readonly Label sentenceTitle=new Label();
    readonly ModernComboBox movies=new ModernComboBox();
    readonly ModernComboBox primary=new ModernComboBox(),secondary=new ModernComboBox(),audio=new ModernComboBox();
    readonly ModernComboBox speed=new ModernComboBox();
    readonly TextBox query=new TextBox();
    readonly TextBox sentence=new TextBox();
    readonly FlowLayoutPanel words=new FlowLayoutPanel();
    readonly RichTextBox definition=new RichTextBox();
    readonly ListBox history=new ListBox();
    readonly TextBox recordDetail=new TextBox();
    readonly TextBox note=new TextBox();
    readonly ModernSlider timeline=new ModernSlider();
    readonly ModernSlider volume=new ModernSlider();
    readonly ModernButton pauseButton=new ModernButton();
    readonly ModernButton lookupButton=new ModernButton();
    readonly ThemeTabs tabs=new ThemeTabs();
    readonly ThemeTabs workspaceTabs=new ThemeTabs { HeaderItemWidth=160 };
    UsagePage usagePage;
#if !PUBLIC_RELEASE
    readonly WebView2 browser=new WebView2();
#endif
    readonly ModernToggle loopSentence=new ModernToggle();
    readonly ModernToggle autoPause=new ModernToggle();
    readonly Label emptyVideo=new Label();
    readonly System.Windows.Forms.Timer saveTimer=new System.Windows.Forms.Timer { Interval=5000 };
    readonly System.Windows.Forms.Timer loopTimer=new System.Windows.Forms.Timer { Interval=70 };
    Color accent=Color.FromArgb(30,103,92);
    Color ink=Color.FromArgb(29,43,48);
    MovieData movie;
    WordRecord currentWord;
    TaskCompletionSource<bool> fileLoaded;
    string subText="",secondText="";
    double position,duration,subStart=-1,subEnd=-1,lastStart=-1,lastEnd=-1;
    double loopA=-1,loopB=-1;
    bool ready,loading,closing,paused=true,seeking,trackUpdating,fullscreen,loopSeeking,sentenceEdited,settingSentence,historyUpdating;
    int lookupVersion;
    CancellationTokenSource lookupCancellation;
    Task lookupSupplement=Task.CompletedTask;
    string savedSentence="";
    FormBorderStyle oldBorder;
    Rectangle oldBounds;
    FormWindowState oldState;
    dynamic voice;
    string launchMovie;
#if !PUBLIC_RELEASE
    Task browserInit;
#endif
    readonly bool verificationMode,controlsVerificationMode,usageVerificationMode,focusVerificationMode,lookupVerificationMode;
    public MainForm(string initialMovie,bool verify=false,bool controlsVerify=false,bool usageVerify=false,bool focusVerify=false,bool lookupVerify=false,HttpClient networkClient=null,bool updatePreview=false) {
        verificationMode=verify;
        controlsVerificationMode=controlsVerify;
        usageVerificationMode=usageVerify;
        focusVerificationMode=focusVerify;
        lookupVerificationMode=lookupVerify;
        http=networkClient??Network.CreateClient();
        store=new Store(Path.Combine(root,verify?(updatePreview?"verification\\update-ui-data-"+Guid.NewGuid().ToString("N"):lookupVerify?"verification\\lookup-ui-data-"+Guid.NewGuid().ToString("N"):"verification\\ui-data"):"data"));
        offline=new OfflineDictionary(Path.Combine(root,"vendor","dictionary","ecdict.tsv.gz"));
        try { settings=store.LoadSettings(); } catch(Exception ex) { settings=new AppSettings(); MessageBox.Show(ex.Message,"设置读取失败"); }
        launchMovie=initialMovie;
        Text="FilmEcho · 英语电影跟读"; Font=new Font("Microsoft YaHei UI",9F);
        BackColor=Color.FromArgb(244,246,242); ForeColor=ink;
        MinimumSize=new Size(1100,730); Size=new Size(1440,920); StartPosition=FormStartPosition.CenterScreen;
        try { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BuildUi();
#if !PUBLIC_RELEASE
        if(verify) {
            browser.HandleCreated+=(s,e)=>TraceBrowser("HandleCreated"); browser.HandleDestroyed+=(s,e)=>TraceBrowser("HandleDestroyed");
            browser.VisibleChanged+=(s,e)=>TraceBrowser("VisibleChanged");
            browser.CoreWebView2InitializationCompleted+=(s,e)=>TraceBrowser("InitializationCompleted success="+e.IsSuccess+" error="+e.InitializationException);
        }
#endif
        ApplyTheme(settings.Theme,false);
        CaptureLanguageLabels(this); ApplyLanguage(settings.Language,false);
        SetupUpdates();
        mpv.PropertyChanged+=(name,value)=>Ui(()=>Property(name,value));
        mpv.Event+=(name,value)=>Ui(()=>PlayerEvent(name,value));
        mpv.Failed+=message=>Ui(()=>Status(message,true));
        Shown+=async(s,e)=> { if(!updatePreview) await Initialize(); };
        FormClosing+=CloseApp;
        saveTimer.Tick+=(s,e)=>Save(); saveTimer.Start();
        loopTimer.Tick+=async(s,e)=>await CheckLoop(); loopTimer.Start();
        AllowDrop=true;
        DragEnter+=(s,e)=> { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
        DragDrop+=async(s,e)=> { var paths=(string[])e.Data.GetData(DataFormats.FileDrop); if(paths.Length>0) await LoadMovie(paths[0]); };
    }
    static Label LabelOf(string text,int width=0) { return new Label { Text=text,AutoSize=width==0,Width=width,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(5,8,5,5) }; }
    Button ButtonOf(string text,Action action,int width=0) {
        var b=new ModernButton { Text=text,AutoSize=width==0,Width=width,Height=34,BackColor=Color.White,ForeColor=ink,Margin=new Padding(3),TabStop=true };
        b.FlatAppearance.BorderColor=Color.FromArgb(207,218,213); b.Click+=(s,e)=>action(); if(themeApplied) StyleThemedControl(b); return b;
    }
    Button AsyncButton(string text,Func<Task> action,int width=0) { return ButtonOf(text,async()=> { try { await action(); } catch(Exception ex) { Status(ex.Message,true); } },width); }
    void StyleCombo(ModernComboBox c,int width) { c.Width=width; c.Height=32; c.Margin=new Padding(4,2,4,2); }
    void BuildUi() {
        var shell=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16,10,16,8) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,51)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute,47)); shell.RowStyles.Add(new RowStyle(SizeType.Percent,100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute,27)); Controls.Add(shell);
        var heading=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 }; heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,586)); heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var brand=new Label { Text="FilmEcho  /  电影跟读",Font=new Font(Font.FontFamily,20,FontStyle.Bold),ForeColor=accent,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft };
        heading.Controls.Add(brand,0,0);
        var appearance=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,5,0,0) };
        appearance.Controls.Add(updateButton);
        StyleCombo(themeChoice,108); themeChoice.Items.AddRange(new object[]{"深色 / 黑","浅色 / 白"});
        themeChoice.SelectedIndexChanged+=(s,e)=> { if(!changingTheme && themeChoice.SelectedIndex>=0) ApplyTheme(themeChoice.SelectedIndex==0?"dark":"light",true); };
        StyleCombo(languageChoice,116); languageChoice.AccessibleName="显示语言"; languageChoice.Items.AddRange(new object[]{"中文","English"});
        languageChoice.SelectedIndexChanged+=(s,e)=> { if(!changingLanguage && languageChoice.SelectedIndex>=0) ApplyLanguage(languageChoice.SelectedIndex==1?"en":"zh",true); };
        appearance.Controls.Add(languageChoice); appearance.Controls.Add(LabelOf("显示语言")); appearance.Controls.Add(themeChoice); appearance.Controls.Add(LabelOf("主题"));
        heading.Controls.Add(appearance,1,0); shell.Controls.Add(heading,0,0);
        var top=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false };
        top.Controls.Add(AsyncButton("打开电影",OpenDialog,100)); StyleCombo(movies,360); movies.DisplayText=value=>Path.GetFileName(Convert.ToString(value)); movies.FullItemTooltip=true; movies.AccessibleName="电影列表"; top.Controls.Add(movies);
        top.Controls.Add(AsyncButton("播放选中",async()=> { if(movies.SelectedItem is string) await LoadMovie((string)movies.SelectedItem); },92));
        top.Controls.Add(ButtonOf("电影目录",ChangeLibrary,92)); top.Controls.Add(AsyncButton("载入字幕",AddSubtitle,92));
        top.Controls.Add(ButtonOf("使用说明",ShowHelp,92)); shell.Controls.Add(top,0,1);
#if PUBLIC_RELEASE
        top.Controls.Add(ButtonOf("打开浏览器",OpenDefaultBrowser,140));
#endif
        split.Size=new Size(1300,750); split.Dock=DockStyle.Fill; split.FixedPanel=FixedPanel.Panel2; split.Panel1MinSize=580; split.Panel2MinSize=370; split.SplitterWidth=10;
        workspaceTabs.Dock=DockStyle.Fill;
        var learning=new ThemePage("跟读学习"); var distribution=new ThemePage("使用分布");
        learning.Controls.Add(split); workspaceTabs.TabPages.AddRange(new[]{learning,distribution});
        usagePage=new UsagePage(Path.Combine(store.Root,"usage-summary.json")) { Dock=DockStyle.Fill };
        distribution.Controls.Add(usagePage); shell.Controls.Add(workspaceTabs,0,2);
        workspaceTabs.SelectedIndexChanged+=(s,e)=> { if(workspaceTabs.SelectedIndex==1) usagePage.RefreshSnapshot(); };
        Load+= (s,e)=> { split.SplitterDistance=Math.Max(580,split.Width-430); };
        BuildPlayer(); BuildStudy();
        status.Dock=DockStyle.Fill; status.Text="正在准备播放器…"; status.ForeColor=accent; status.TextAlign=ContentAlignment.MiddleLeft; shell.Controls.Add(status,0,3);
    }
    void BuildPlayer() {
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,43)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,49)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,168)); split.Panel1.Controls.Add(layout);
        movieTitle.Dock=DockStyle.Fill; movieTitle.Font=new Font(Font.FontFamily,10,FontStyle.Bold); movieTitle.Text="从 E:\\英语电影 选择一部电影，或者拖入视频"; movieTitle.AutoEllipsis=true; movieTitle.TextAlign=ContentAlignment.MiddleLeft; layout.Controls.Add(movieTitle,0,0);
        video.Dock=DockStyle.Fill; video.BackColor=Color.FromArgb(16,21,24); layout.Controls.Add(video,0,1);
        // These cover the empty host only; native mpv child clicks arrive via IPC.
        video.MouseDown+=(s,e)=> { if(e.Button==MouseButtons.Left) video.TakePlaybackFocus(); };
        emptyVideo.MouseDown+=(s,e)=> { if(e.Button==MouseButtons.Left) video.TakePlaybackFocus(); };
        emptyVideo.Text="打开电影，开始今天的跟读\n\nSpace 暂停  ·  ← / → 回退和快进  ·  R 重听本句";
        emptyVideo.ForeColor=Color.FromArgb(174,192,190); emptyVideo.Dock=DockStyle.Fill; emptyVideo.TextAlign=ContentAlignment.MiddleCenter; emptyVideo.Font=new Font(Font.FontFamily,13); video.Controls.Add(emptyVideo);
        var timelineRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 }; timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));
        timeline.Dock=DockStyle.Fill; timeline.Maximum=10000; timeline.AccessibleName="播放进度";
        timeline.MouseDown+=(s,e)=>seeking=true; timeline.MouseUp+=async(s,e)=> { try { if(ready && duration>0) await Seek(duration*timeline.Value/10000.0); } catch(Exception ex){ Status(ex.Message,true); } finally { seeking=false; } };
        timeline.MouseCaptureChanged+=(s,e)=> { if(!timeline.Capture) seeking=false; };
        timeline.KeyUp+=async(s,e)=> { if(ready && duration>0 && (e.KeyCode==Keys.Left || e.KeyCode==Keys.Right || e.KeyCode==Keys.Home || e.KeyCode==Keys.End)) await Safe(()=>Seek(duration*timeline.Value/10000.0)); };
        clock.Dock=DockStyle.Fill; clock.Text="00:00:00 / 00:00:00"; clock.TextAlign=ContentAlignment.MiddleCenter; timelineRow.Controls.Add(timeline,0,0); timelineRow.Controls.Add(clock,1,0); layout.Controls.Add(timelineRow,0,2);
        var controls=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty };
        pauseButton.Text="▶ 播放"; pauseButton.Width=88; pauseButton.Height=34; pauseButton.BackColor=accent; pauseButton.ForeColor=Color.White; pauseButton.Margin=new Padding(3); pauseButton.Click+=async(s,e)=>await Safe(TogglePause); controls.Controls.Add(pauseButton);
        controls.Controls.Add(AsyncButton("−5秒",()=>Jump(-5),59)); controls.Controls.Add(AsyncButton("−3秒",()=>Jump(-3),59)); controls.Controls.Add(AsyncButton("重听本句 R",Replay,112)); controls.Controls.Add(AsyncButton("+3秒",()=>Jump(3),59)); controls.Controls.Add(AsyncButton("+5秒",()=>Jump(5),59)); controls.Controls.Add(ButtonOf("全屏 F",ToggleFullscreen,82));
        StyleCombo(speed,77); speed.Items.AddRange(new object[]{"0.5×","0.75×","0.85×","1.0×","1.1×","1.25×","1.5×","2.0×"}); speed.SelectedItem=settings.Speed.ToString("0.0#",System.Globalization.CultureInfo.InvariantCulture)+"×"; if(speed.SelectedIndex<0) speed.SelectedIndex=3;
        speed.SelectedIndexChanged+=async(s,e)=> { double rate=double.Parse(speed.Text.Replace("×",""),System.Globalization.CultureInfo.InvariantCulture); settings.Speed=rate; if(ready) await Safe(async()=> { await mpv.Set("speed",rate); }); }; controls.Controls.Add(speed); layout.Controls.Add(controls,0,3);
        var options=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty };
        loopSentence.Text="本句循环"; loopSentence.AutoSize=true; loopSentence.Margin=new Padding(5,9,12,0); loopSentence.CheckedChanged+=async(s,e)=> { if(loopSentence.Checked) await Safe(SetSentenceLoop); else { loopA=loopB=-1; } }; options.Controls.Add(loopSentence);
        autoPause.Text="句尾自动暂停"; autoPause.AutoSize=true; autoPause.Margin=new Padding(5,9,12,0); options.Controls.Add(autoPause);
        options.Controls.Add(LabelOf("音量")); volume.Minimum=0; volume.Maximum=100; volume.Value=Math.Min(100,Math.Max(0,settings.Volume)); volume.Width=100; volume.Height=33; volume.AccessibleName="音量"; volume.ValueChanged+=async(s,e)=> { settings.Volume=volume.Value; if(ready) await Safe(async()=>await mpv.Set("volume",volume.Value)); }; options.Controls.Add(volume);
        options.Controls.Add(LabelOf("方向键步长")); var step=new ModernComboBox(); StyleCombo(step,72); step.AccessibleName="方向键步长"; step.Items.AddRange(new object[]{"3秒","5秒"}); step.SelectedIndex=settings.JumpSeconds==5?1:0; step.SelectedIndexChanged+=(s,e)=>settings.JumpSeconds=step.SelectedIndex==0?3:5; options.Controls.Add(step); layout.Controls.Add(options,0,4);
        var tracks=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty }; tracks.Controls.Add(LabelOf("音轨")); StyleCombo(audio,145); audio.AccessibleName="音轨"; tracks.Controls.Add(audio); tracks.Controls.Add(LabelOf("字幕")); StyleCombo(primary,163); primary.AccessibleName="第一字幕"; tracks.Controls.Add(primary); tracks.Controls.Add(LabelOf("第二字幕")); StyleCombo(secondary,163); secondary.AccessibleName="第二字幕"; tracks.Controls.Add(secondary);
        bool adjustingRows=false;
        Action fitRows=()=> { if(adjustingRows) return; adjustingRows=true; try { var rows=new[]{controls,options,tracks}; var minimum=new[]{43,49,44}; for(int i=0;i<rows.Length;i++) { int needed=rows[i].Controls.Cast<Control>().Select(c=>c.Bottom+c.Margin.Bottom+2).DefaultIfEmpty(minimum[i]).Max(); layout.RowStyles[i+3].Height=Math.Max(minimum[i],needed); } } finally { adjustingRows=false; } };
        layout.SizeChanged+=(s,e)=>fitRows(); controls.Layout+=(s,e)=>fitRows(); options.Layout+=(s,e)=>fitRows(); tracks.Layout+=(s,e)=>fitRows();
        audio.SelectedIndexChanged+=async(s,e)=>await TrackChanged(audio,"aid"); primary.SelectedIndexChanged+=async(s,e)=>await TrackChanged(primary,"sid"); secondary.SelectedIndexChanged+=async(s,e)=>await TrackChanged(secondary,"secondary-sid"); layout.Controls.Add(tracks,0,5);
        var captions=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,BackColor=Color.White,Padding=new Padding(8,0,8,6) }; captions.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); captions.RowStyles.Add(new RowStyle(SizeType.Absolute,69)); captions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var captionHead=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty }; captionHead.RowStyles.Add(new RowStyle(SizeType.Percent,100)); captionHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); captionHead.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,144)); sentenceTitle.Text="原句 · 暂停后选择单词，或手动补充"; sentenceTitle.Dock=DockStyle.Fill; sentenceTitle.TextAlign=ContentAlignment.MiddleLeft; sentenceTitle.ForeColor=accent;
        captionHead.Controls.Add(sentenceTitle,0,0); captionHead.Controls.Add(AsyncButton("识别画面字幕 O",Ocr,140),1,0); captions.Controls.Add(captionHead,0,0);
        sentence.Multiline=true; sentence.Dock=DockStyle.Fill; sentence.BorderStyle=BorderStyle.None; sentence.Font=new Font(Font.FontFamily,12); sentence.ScrollBars=ScrollBars.Vertical;
        sentence.TextChanged+=(s,e)=> { if(!settingSentence) { sentenceEdited=true; if(!loading) SetSentenceCaption("已编辑原句 · 查词使用此内容"); } BuildWordButtons(); }; sentence.MouseUp+=(s,e)=> { if(!string.IsNullOrWhiteSpace(sentence.SelectedText)) query.Text=sentence.SelectedText.Trim(); };
        captions.Controls.Add(sentence,0,1); words.Dock=DockStyle.Fill; words.AutoScroll=true; captions.Controls.Add(words,0,2); layout.Controls.Add(captions,0,6);
    }
    void BuildStudy() {
        var side=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=new Padding(4,4,4,0),BackColor=Color.FromArgb(235,240,234) };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); side.RowStyles.Add(new RowStyle(SizeType.Absolute,49)); side.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        tabs.Dock=DockStyle.Fill; tabs.Font=new Font(Font.FontFamily,9); split.Panel2.Controls.Add(tabs); tabs.SharedHeader.Height=120; tabs.SharedHeader.Controls.Add(side);
        side.Controls.Add(new Label { Text="词语与原句",Font=new Font(Font.FontFamily,15,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=accent },0,0);
        var queryRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty }; queryRow.RowStyles.Add(new RowStyle(SizeType.Percent,100)); queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,82));
        query.Font=new Font(Font.FontFamily,11); query.Dock=DockStyle.Fill; query.Margin=new Padding(0,2,5,2); query.MaxLength=200; query.AccessibleName="输入单词或短语";
        query.KeyDown+=async(s,e)=> { if(e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; await Lookup(); } }; queryRow.Controls.Add(new ModernField(query),0,0);
        lookupButton.Text="查词 ↵"; lookupButton.Dock=DockStyle.Fill; lookupButton.BackColor=accent; lookupButton.ForeColor=Color.White; lookupButton.FlatStyle=FlatStyle.Flat; lookupButton.Click+=async(s,e)=>await Lookup(); queryRow.Controls.Add(lookupButton,1,0); side.Controls.Add(queryRow,0,1);
        side.Controls.Add(new Label { Text="查询会暂停电影，并自动保存单词、原句和时间",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(91,107,106),Font=new Font(Font.FontFamily,8) },0,2);
        var dictionary=new ThemePage("释义 / 发音") { BackColor=Color.White,Padding=new Padding(9) }; var saved=new ThemePage("本片生词") { BackColor=Color.White,Padding=new Padding(7) };
#if PUBLIC_RELEASE
        tabs.TabPages.AddRange(new[]{dictionary,saved});
#else
        var google=new ThemePage("Google 翻译") { BackColor=Color.White }; tabs.TabPages.AddRange(new[]{dictionary,google,saved});
#endif
        var dictLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4 }; dictLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,43)); dictLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); dictLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); dictLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,39)); dictionary.Controls.Add(dictLayout);
        var voiceRow=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false }; voiceRow.Controls.Add(AsyncButton("原声词典发音",PlayWordAudio,128)); voiceRow.Controls.Add(ButtonOf("英语朗读",()=>Speak(query.Text),100)); voiceRow.Controls.Add(ButtonOf("朗读原句",()=>Speak(sentence.Text),100)); dictLayout.Controls.Add(voiceRow,0,0);
        definition.Dock=DockStyle.Fill; definition.ReadOnly=true; definition.BorderStyle=BorderStyle.None; definition.BackColor=Color.White; definition.Font=new Font(Font.FontFamily,11); definition.Text=UiStrings.Intro; dictLayout.Controls.Add(definition,0,1);
        note.Multiline=true; note.Dock=DockStyle.Fill; note.ScrollBars=ScrollBars.Vertical; note.AccessibleName="学习笔记"; dictLayout.Controls.Add(new ModernField(note),0,2);
        var noteRow=new FlowLayoutPanel { Dock=DockStyle.Fill }; noteRow.Controls.Add(ButtonOf("保存笔记",SaveNote,95)); noteRow.Controls.Add(ButtonOf("更新原句",()=> { if(currentWord!=null && movie!=null && movie.Words.Contains(currentWord)) { currentWord.Sentence=sentence.Text.Trim(); SaveNote(); } },95)); noteRow.Controls.Add(ButtonOf("电影位置",()=> { if(movie!=null) Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+movie.MoviePath+"\""){UseShellExecute=true}); },100)); dictLayout.Controls.Add(noteRow,0,3);
#if !PUBLIC_RELEASE
        var googleLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 }; googleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,37)); googleLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); google.Controls.Add(googleLayout);
        var browserRow=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false }; browserRow.Controls.Add(AsyncButton("翻译当前查询",()=>NavigateGoogle(query.Text),127)); browserRow.Controls.Add(AsyncButton("翻译原句",()=>NavigateGoogle(sentence.Text),103)); browserRow.Controls.Add(ButtonOf("在浏览器打开",()=>OpenGoogleExternal(),126)); googleLayout.Controls.Add(browserRow,0,0); browser.Dock=DockStyle.Fill; googleLayout.Controls.Add(browser,0,1);
        tabs.SelectedIndexChanged+=async(s,e)=> { if(tabs.SelectedTab==google) await Safe(async()=> { await EnsureBrowser(); if(browser.Source==null) await NavigateGoogle(query.Text); }); };
#endif
        var historyLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3 }; historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent,62)); historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent,38)); historyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); saved.Controls.Add(historyLayout);
        history.Dock=DockStyle.Fill; history.HorizontalScrollbar=true; history.BorderStyle=BorderStyle.None; history.Font=new Font(Font.FontFamily,10); history.SelectedIndexChanged+=(s,e)=> { if(!historyUpdating) ShowRecord(history.SelectedItem as WordRecord,false); }; history.DoubleClick+=async(s,e)=>await Safe(JumpToRecord); historyLayout.Controls.Add(history,0,0);
        history.HorizontalScrollbar=false; history.DrawMode=DrawMode.OwnerDrawFixed; history.ItemHeight=64; history.DrawItem+=(s,e)=>DrawWordRecord(e);
        recordDetail.Dock=DockStyle.Fill; recordDetail.Multiline=true; recordDetail.ReadOnly=true; recordDetail.ScrollBars=ScrollBars.Vertical; recordDetail.BorderStyle=BorderStyle.None; recordDetail.BackColor=Color.FromArgb(247,249,244); historyLayout.Controls.Add(recordDetail,0,1);
        var recordRow=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false }; recordRow.Controls.Add(AsyncButton("回到原句",JumpToRecord,109)); recordRow.Controls.Add(ButtonOf("导出 CSV",Export,108)); recordRow.Controls.Add(ButtonOf("记录目录",()=>Process.Start(new ProcessStartInfo(store.Root){UseShellExecute=true}),108)); historyLayout.Controls.Add(recordRow,0,2);
    }
    async Task Initialize() {
        try {
            Directory.CreateDirectory(Path.Combine(root,"logs"));
            await mpv.Start(Path.Combine(root,"vendor","mpv","mpv.exe"),video.Handle,Path.Combine(root,"logs","mpv-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log"),Path.Combine(root,AppInfo.PublicRelease?"runtime":"source","input.conf"));
            foreach(string p in new[]{"time-pos","duration","pause","sub-text","secondary-sub-text","sub-start/full","sub-end/full","track-list","eof-reached"}) await mpv.Observe(p);
            ready=true; await mpv.Set("volume",settings.Volume); await mpv.Set("speed",settings.Speed);
            ScanLibrary(); Status("已准备 · 软件解码 / 精确跳转 · Space 暂停，方向键 ±"+settings.JumpSeconds+" 秒");
            if(!string.IsNullOrEmpty(launchMovie) && File.Exists(launchMovie)) await LoadMovie(launchMovie);
            else if(!string.IsNullOrEmpty(settings.LastMovie) && File.Exists(settings.LastMovie)) await LoadMovie(settings.LastMovie);
#if !PUBLIC_RELEASE
            if(lookupVerificationMode) await VerifyLookup(); else if(focusVerificationMode) await VerifyVideoFocus(); else if(usageVerificationMode) await VerifyUsage(); else if(controlsVerificationMode) await VerifyControls(); else if(verificationMode) await VerifyUi();
#endif
        } catch(Exception ex) { Status("启动失败："+ex.Message,true); if(verificationMode) { File.WriteAllText(Path.Combine(root,"verification","ui-error.txt"),ex.ToString()); Environment.ExitCode=1; Close(); } else MessageBox.Show(ex.Message,"播放器启动失败"); }
    }
    void ScanLibrary() {
        movies.Items.Clear();
        try {
            if(Directory.Exists(settings.LibraryFolder)) {
                var extensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".mp4",".mkv",".avi",".mov",".wmv",".m4v",".ts",".webm",".m2ts"};
                foreach(var f in Directory.EnumerateFiles(settings.LibraryFolder,"*",SearchOption.AllDirectories).Where(x=>extensions.Contains(Path.GetExtension(x))).OrderBy(x=>x)) movies.Items.Add(f);
                if(movies.Items.Count>0) movies.SelectedIndex=0;
            }
        } catch(Exception ex) { Status("电影目录读取失败："+ex.Message,true); }
    }
    void ChangeLibrary() { using(var d=new FolderBrowserDialog { Description=T("选择电影目录"),SelectedPath=settings.LibraryFolder }) if(d.ShowDialog(this)==DialogResult.OK) { settings.LibraryFolder=d.SelectedPath; ScanLibrary(); Save(); } }
    async Task OpenDialog() { using(var d=new OpenFileDialog { Title=T("选择英语电影"),Filter=T("视频|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.ts;*.webm;*.m2ts|全部文件|*.*"),InitialDirectory=settings.LibraryFolder }) if(d.ShowDialog(this)==DialogResult.OK) await LoadMovie(d.FileName); }
    public async Task LoadMovie(string path) {
        workspaceTabs.SelectedIndex=0;
        if(!ready) { Status("播放内核尚未准备好",true); return; }
        if(!File.Exists(path)) { Status("电影文件不存在："+path,true); return; }
        await loadGate.WaitAsync();
        try {
            MovieData next=store.LoadMovie(path); // Protect damaged records before changing the current movie.
            if(!Save(true)) return;
            loading=true; lookupVersion++; lookupCancellation?.Cancel(); lookupButton.Enabled=true; currentWord=null; query.Clear(); note.Clear(); definition.Text=T("选择当前电影中的单词。");
            loopSentence.Checked=false; autoPause.Checked=false; loopA=loopB=-1;
            movie=next; position=0; duration=0; subText=secondText=""; subStart=subEnd=lastStart=lastEnd=-1; sentence.Clear(); sentenceEdited=false; savedSentence="";
            emptyVideo.Visible=false; movieTitle.Text=Path.GetFileName(path); settings.LastMovie=path; RefreshHistory();
            movies.SelectedItem=movies.Items.Cast<string>().FirstOrDefault(x=>string.Equals(Path.GetFullPath(x),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
            movies.EmptyText=movies.SelectedIndex<0?Path.GetFileName(path)+T(" · 外部文件"):T("选择电影…"); movies.Invalidate();
            fileLoaded=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await mpv.Set("pause",true); await mpv.Command("loadfile",Path.GetFullPath(path),"replace");
            if(await Task.WhenAny(fileLoaded.Task,Task.Delay(18000))!=fileLoaded.Task) throw new TimeoutException("电影加载超时，请查看 logs 中的播放日志");
            await fileLoaded.Task;
            foreach(var subtitle in movie.ExternalSubtitles.Where(File.Exists)) await mpv.Command("sub-add",subtitle,"auto");
            var tracks=await mpv.Get("track-list"); UpdateTracks(tracks);
            await RestoreTracks(tracks);
            duration=Json.Num(await mpv.Get("duration"));
            if(movie.Position>0 && movie.Position<duration-1) await Seek(movie.Position);
            await mpv.Set("speed",settings.Speed); await mpv.Set("pause",true);
            await mpv.WaitForVideo();
            position=Json.Num(await mpv.Get("time-pos")); loading=false;
            subText=Json.Str(await GetOptional("sub-text")); secondText=Json.Str(await GetOptional("secondary-sub-text"));
            subStart=lastStart=Json.Num(await GetOptional("sub-start/full")??-1); subEnd=lastEnd=Json.Num(await GetOptional("sub-end/full")??-1); UpdateSentence(); Save();
            Status("已打开，暂停待跟读 · "+movie.Words.Count+" 条生词记录 · 点击播放 / Space");
        } catch(Exception ex) { loading=false; Status("打开失败："+ex.Message,true); }
        finally { loadGate.Release(); }
    }
    async Task RestoreTracks(object value) {
        var list=(value as object[] ?? new object[0]).Select(Json.Map).ToList();
        string sid=movie.PrimarySubtitle;
        if(sid=="auto") {
            var bilingual=list.FirstOrDefault(t=>Json.Str(Json.Field(t,"type"))=="sub" && Regex.IsMatch(Json.Str(Json.Field(t,"title")),"中英|双语|bilingual|上英下中|上中下英",RegexOptions.IgnoreCase));
            if(bilingual!=null) sid=Json.Str(Json.Field(bilingual,"id"));
        }
        string aid=movie.AudioTrack;
        if(aid=="auto") { var en=list.FirstOrDefault(t=>Json.Str(Json.Field(t,"type"))=="audio" && Regex.IsMatch(Json.Str(Json.Field(t,"title")),"英语|英文|English",RegexOptions.IgnoreCase)); if(en!=null) aid=Json.Str(Json.Field(en,"id")); }
        await SetTrackIfValid("aid",aid,audio);
        await SetTrackIfValid("sid",sid,primary);
        await SetTrackIfValid("secondary-sid",movie.SecondarySubtitle,secondary);
        SelectCurrentTracks();
    }
    async Task SetTrackIfValid(string prop,string id,ModernComboBox combo) {
        if(id=="auto" || id=="no" || combo.Items.Cast<TrackChoice>().Any(x=>x.Id==id)) await mpv.Set(prop,id);
    }
    async void SelectCurrentTracks() {
        try { trackUpdating=true; SelectId(audio,Json.Str(await mpv.Get("aid"))); SelectId(primary,Json.Str(await mpv.Get("sid"))); SelectId(secondary,Json.Str(await mpv.Get("secondary-sid"))); }
        catch { } finally { trackUpdating=false; }
    }
    static void SelectId(ModernComboBox combo,string id) { foreach(TrackChoice t in combo.Items) if(t.Id==id || (id=="False" && t.Id=="no")) { combo.SelectedItem=t; return; } }
    void UpdateTracks(object value) {
        trackUpdating=true;
        string aid=(audio.SelectedItem as TrackChoice)?.Id??"auto",sid=(primary.SelectedItem as TrackChoice)?.Id??"auto",ss=(secondary.SelectedItem as TrackChoice)?.Id??"no";
        audio.Items.Clear(); primary.Items.Clear(); secondary.Items.Clear();
        audio.Items.Add(new TrackChoice { Id="auto",Name="自动（优先英语）" }); primary.Items.Add(new TrackChoice{Id="auto",Name="自动"}); primary.Items.Add(new TrackChoice{Id="no",Name="关闭字幕"}); secondary.Items.Add(new TrackChoice{Id="no",Name="关闭"});
        foreach(var item in value as object[] ?? new object[0]) {
            var t=Json.Map(item); string type=Json.Str(Json.Field(t,"type")); if(type!="audio" && type!="sub") continue;
            string lang=Json.Str(Json.Field(t,"lang")),title=Json.Str(Json.Field(t,"title")),codec=Json.Str(Json.Field(t,"codec")),id=Json.Str(Json.Field(t,"id"));
            var choice=new TrackChoice { Id=id,Lang=lang,Codec=codec,Name=id+" · "+(string.IsNullOrEmpty(title)?lang:title)+" ["+codec+"]" };
            if(type=="audio") audio.Items.Add(choice); else { primary.Items.Add(choice); secondary.Items.Add(choice); }
        }
        audio.SelectedIndex=0; primary.SelectedIndex=0; secondary.SelectedIndex=0; SelectId(audio,aid); SelectId(primary,sid); SelectId(secondary,ss); trackUpdating=false;
    }
    async Task TrackChanged(ModernComboBox combo,string property) {
        if(trackUpdating || !ready || loading || movie==null) return;
        var t=combo.SelectedItem as TrackChoice; if(t==null) return;
        await Safe(async()=> { await mpv.Set(property,t.Id); if(property=="aid") movie.AudioTrack=t.Id; else if(property=="sid") movie.PrimarySubtitle=t.Id; else movie.SecondarySubtitle=t.Id; Save(); });
    }
    async Task AddSubtitle() {
        if(movie==null) { Status("先打开电影，再载入字幕"); return; }
        using(var d=new OpenFileDialog { Title=T("载入 SRT / ASS / VTT / SUP 字幕"),Filter=T("字幕|*.srt;*.ass;*.ssa;*.vtt;*.sup;*.sub;*.idx|全部文件|*.*"),InitialDirectory=Path.GetDirectoryName(movie.MoviePath) })
        if(d.ShowDialog(this)==DialogResult.OK) { await mpv.Command("sub-add",d.FileName,"select"); if(!movie.ExternalSubtitles.Contains(d.FileName)) movie.ExternalSubtitles.Add(d.FileName); movie.PrimarySubtitle=Json.Str(await mpv.Get("sid")); UpdateTracks(await mpv.Get("track-list")); SelectCurrentTracks(); Save(); }
    }
    void Property(string name,object value) {
        if(closing) return;
        if(name=="time-pos") { if(value!=null) position=Json.Num(value); UpdateTime(); }
        else if(name=="duration") { duration=Json.Num(value); UpdateTime(); }
        else if(name=="pause") { paused=Convert.ToBoolean(value??true); pauseButton.Text=T(paused?"▶ 播放":"Ⅱ 暂停"); if(!paused) UpdateSentence(); }
        else if(name=="sub-text") { subText=Json.Str(value); UpdateSentence(); }
        else if(name=="secondary-sub-text") { secondText=Json.Str(value); UpdateSentence(); }
        else if(name=="sub-start/full") { subStart=value==null?-1:Json.Num(value); if(subStart>=0) lastStart=subStart; }
        else if(name=="sub-end/full") { subEnd=value==null?-1:Json.Num(value); if(subEnd>=0) lastEnd=subEnd; }
        else if(name=="track-list" && !loading) UpdateTracks(value);
        else if(name=="eof-reached" && value is bool && (bool)value) Status("电影已到结尾 · 可以回退继续练习");
    }
    void PlayerEvent(string name,Dictionary<string,object> value) {
        if(name=="file-loaded") fileLoaded?.TrySetResult(true);
        else if(name=="end-file" && Json.Str(Json.Field(value,"reason"))=="error") { fileLoaded?.TrySetException(new IOException("解码失败："+Json.Str(Json.Field(value,"file_error")))); Status("电影解码失败，请查看日志",true); }
        else if(name=="client-message") {
            var args=Json.Field(value,"args") as object[]; if(args==null || args.Length==0) return;
            switch(Json.Str(args[0])) {
                case "app-video-click": video.TakePlaybackFocus(); _=Safe(TogglePause); break;
                case "app-video-fullscreen": video.TakePlaybackFocus(); ToggleFullscreen(); break;
                case "app-replay": _=Safe(Replay); break;
                case "app-fullscreen": ToggleFullscreen(); break;
                case "app-lookup": query.Focus(); break;
                case "app-left": _=Safe(()=>Jump(-settings.JumpSeconds)); break;
                case "app-right": _=Safe(()=>Jump(settings.JumpSeconds)); break;
                case "app-ocr": _=Safe(Ocr); break;
                case "app-escape": if(fullscreen) ToggleFullscreen(); else pauseButton.Focus(); break;
            }
        }
    }
    void UpdateTime() { clock.Text=WordRecord.Clock(position)+" / "+WordRecord.Clock(duration); if(!seeking && duration>0) timeline.Value=Math.Max(0,Math.Min(10000,(int)(position/duration*10000))); }
    void UpdateSentence() {
        if(loading) return;
        string text=(subText+(!string.IsNullOrWhiteSpace(subText) && !string.IsNullOrWhiteSpace(secondText)?"\r\n":"")+secondText).Trim();
        if(sentenceEdited && paused) return;
        if(!string.IsNullOrWhiteSpace(text)) {
            if(!sentence.Focused) { settingSentence=true; try { sentence.Text=text; savedSentence=text; sentenceEdited=false; } finally { settingSentence=false; } }
            SetSentenceCaption("当前原句 · 点击单词查词 · "+WordRecord.Clock(position));
        } else if(!string.IsNullOrWhiteSpace(savedSentence)) SetSentenceCaption("最近一句（当前无文本字幕） · 可修改原句");
        else SetSentenceCaption("无文本字幕 · 图片 / 内嵌字幕可按 O 识别");
    }
    void BuildWordButtons() {
        words.SuspendLayout(); foreach(Control c in words.Controls) c.Dispose(); words.Controls.Clear();
        foreach(string w in Regex.Matches(sentence.Text,"[A-Za-z]+(?:['’][A-Za-z]+)*").Cast<Match>().Select(m=>m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(45)) {
            string word=w; var b=ButtonOf(word,async()=> { query.Text=word; await Lookup(); }); b.Height=27; b.Font=new Font(Font.FontFamily,9); b.Padding=Padding.Empty; b.Margin=new Padding(2); words.Controls.Add(b);
        }
        words.ResumeLayout();
    }
    async Task TogglePause() { if(ready && movie!=null) await mpv.Command("cycle","pause"); }
    async Task Seek(double seconds) { if(ready && movie!=null) { if(sentenceEdited && Math.Abs(seconds-position)>.08) { sentenceEdited=false; UpdateSentence(); } loopA=loopB=-1; loopSentence.Checked=false; await mpv.Command("seek",Math.Max(0,Math.Min(duration>0?duration-0.05:double.MaxValue,seconds)),"absolute+exact"); } }
    async Task Jump(double amount) { if(ready && movie!=null) { if(sentenceEdited) { sentenceEdited=false; UpdateSentence(); } loopSentence.Checked=false; loopA=loopB=-1; await mpv.Command("seek",amount,"relative+exact"); } }
    async Task Replay() {
        if(movie==null || !ready) return;
        double at=lastStart>=0 && position-lastStart<25 ? Math.Max(0,lastStart-0.12):Math.Max(0,position-settings.JumpSeconds);
        await mpv.Command("seek",at,"absolute+exact"); await mpv.Set("pause",false);
    }
    async Task SetSentenceLoop() {
        if(movie==null || lastStart<0 || lastEnd<=lastStart) { loopSentence.Checked=false; Status("本句循环需要文本字幕。内嵌或图片字幕可用 3/5 秒回退。"); return; }
        loopA=Math.Max(0,lastStart-0.12); loopB=lastEnd+0.12; await mpv.Command("seek",loopA,"absolute+exact"); await mpv.Set("pause",false);
    }
    async Task CheckLoop() {
        if(!ready || loading || movie==null || paused || loopSeeking || closing) return;
        try {
            if(loopSentence.Checked && loopA>=0 && loopB>loopA && position>=loopB) { loopSeeking=true; await mpv.Command("seek",loopA,"absolute+exact"); }
            else if(autoPause.Checked && lastEnd>0 && position>=lastEnd && position-lastEnd<1.2) { await mpv.Set("pause",true); }
        } catch(Exception ex) { Status(ex.Message,true); } finally { loopSeeking=false; }
    }
    async Task Lookup() {
        string word=query.Text.Trim(); if(word.Length==0) return;
        if(movie==null) { Status("先打开电影，查询才能关联原句和电影位置"); return; }
        int version=++lookupVersion; var target=movie; string context=sentence.Text.Trim(); double requestedPosition=position;
        double sentenceStart=context==savedSentence?lastStart:-1,sentenceEnd=context==savedSentence?lastEnd:-1;
        lookupCancellation?.Cancel();
        var cancellation=new CancellationTokenSource(); lookupCancellation=cancellation;
        lookupSupplement=Task.CompletedTask;
        bool supplementStarted=false;
        try {
            await mpv.Set("pause",true); await mpv.WaitForVideo(); double stoppedPosition=Json.Num(await mpv.Get("time-pos"));
            // If a movie changed while pausing, retain the original movie's click position.
            double at=ReferenceEquals(target,movie) && !loading?stoppedPosition:requestedPosition;
            if(closing) return;
            var record=target.Words.LastOrDefault(w=>w.Word.Equals(word,StringComparison.OrdinalIgnoreCase) && Math.Abs(w.Seconds-at)<1);
            var cached=target.Words.LastOrDefault(w=>w.Word.Equals(word,StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(w.Meaning));
            if(record==null) { record=new WordRecord { Word=word,Sentence=context,Seconds=at,SentenceStart=sentenceStart,SentenceEnd=sentenceEnd,Meaning=cached?.Meaning??"",Phonetic=cached?.Phonetic??"",AudioUrl=cached?.AudioUrl??"" }; target.Words.Add(record); }
            else if(record.SentenceStart<0 && record.Sentence==context && sentenceStart>=0) { record.SentenceStart=sentenceStart; record.SentenceEnd=sentenceEnd; }
            store.SaveRecord(target,record);
            bool active=version==lookupVersion && ReferenceEquals(target,movie);
            if(active) {
                if(query.Focused) pauseButton.Focus();
                currentWord=record; RefreshHistory(record); ShowRecord(record,true); tabs.SelectedIndex=0;
                Status("已记录 “"+word+"” 与 "+WordRecord.Clock(at)+" 的原句；正在查词…");
            }
            var local=cached!=null?null:await offline.Find(word);
            if(closing) return;
            if(local!=null) {
                record.Meaning=local.Chinese+"\r\n\r\n"+local.English+"\r\n\r\n[离线词典 ECDICT："+local.Word+"]"; record.Phonetic=local.Phonetic;
                store.SaveRecord(target,record);
            }
            RefreshLookupResult(target,record,version,record.Meaning.Length>0?"释义已保存 · 可继续查词，在线发音在后台补充":"原句已保存 · 可继续查词，在线释义在后台查询");
            if(cancellation.IsCancellationRequested || closing || record.AudioUrl.Length>0) return;
            cancellation.CancelAfter(TimeSpan.FromSeconds(6));
            lookupSupplement=CompleteLookup(target,record,version,cancellation);
            supplementStarted=true;
        } catch(Exception ex) { if(version==lookupVersion && !closing) Status("查询失败："+ex.Message+"；可在 Google 页继续查词",true); }
        finally { if(!supplementStarted) { if(ReferenceEquals(lookupCancellation,cancellation)) lookupCancellation=null; cancellation.Dispose(); } }
    }
    void RefreshLookupResult(MovieData target,WordRecord record,int version,string message) {
        if(closing || version!=lookupVersion || !ReferenceEquals(target,movie) || !ReferenceEquals(currentWord,record)) return;
        // A late audio/meaning result must not replace text being typed in the note editor.
        definition.Text=RecordDetail(record); recordDetail.Text=definition.Text; history.Invalidate(); Status(message);
    }
    async Task CompleteLookup(MovieData target,WordRecord record,int version,CancellationTokenSource cancellation) {
        try {
            var token=cancellation.Token;
            var translationTask=record.Meaning.Length>0?Task.FromResult(""):Translate(record.Word,token); var dictionaryTask=Dictionary(record.Word,token);
            await Task.WhenAll(translationTask,dictionaryTask);
            if(token.IsCancellationRequested || closing) return;
            var translated=translationTask.Result; var entry=dictionaryTask.Result;
            if(record.Meaning.Length==0) record.Meaning=(translated.Length>0?translated+"\r\n\r\n":"")+entry.Meaning;
            if(record.Phonetic.Length==0) record.Phonetic=entry.Phonetic; record.AudioUrl=entry.AudioUrl;
            store.SaveRecord(target,record);
            RefreshLookupResult(target,record,version,record.AudioUrl.Length>0?"查询与原句已保存 · 原声词典 / 英语朗读 / Google 翻译均可使用":"在线补充未返回发音 · 可继续查词或使用英语朗读");
        } catch(Exception ex) { if(version==lookupVersion && !closing) Status("查询失败："+ex.Message+"；可在 Google 页继续查词",true); }
        finally { if(!closing && cancellation.IsCancellationRequested) RefreshLookupResult(target,record,version,"在线补充已取消或超时 · 离线释义与原句保留，可继续查词"); if(ReferenceEquals(lookupCancellation,cancellation)) lookupCancellation=null; cancellation.Dispose(); }
    }
    async Task<string> GetLookupText(string uri,CancellationToken token) {
        using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseContentRead,token)) { response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync(); }
    }
    async Task<string> Translate(string text,CancellationToken token) {
        try {
            var data=Json.Parse(await GetLookupText("https://api.mymemory.translated.net/get?q="+Uri.EscapeDataString(text)+"&langpair=en%7Czh-CN",token));
            if(Json.Num(Json.Field(data,"responseStatus"))!=200 || Json.Field(data,"quotaFinished") is bool && (bool)Json.Field(data,"quotaFinished")) return "";
            return WebUtility.HtmlDecode(Json.Str(Json.Field(Json.Field(data,"responseData"),"translatedText")));
        } catch { return ""; }
    }
    async Task<WordRecord> Dictionary(string word,CancellationToken token) {
        var record=new WordRecord();
        if(!Regex.IsMatch(word,"^[A-Za-z]+(?:['’-][A-Za-z]+)*$")) return record;
        try {
            var entries=Json.Parse(await GetLookupText("https://api.dictionaryapi.dev/api/v2/entries/en/"+Uri.EscapeDataString(word),token)) as object[];
            if(entries==null || entries.Length==0) return record;
            var entry=entries[0]; record.Phonetic=Json.Str(Json.Field(entry,"phonetic"));
            foreach(var p in Json.Field(entry,"phonetics") as object[] ?? new object[0]) { if(record.Phonetic.Length==0) record.Phonetic=Json.Str(Json.Field(p,"text")); string a=Json.Str(Json.Field(p,"audio")); if(a.StartsWith("//")) a="https:"+a; if(a.StartsWith("https://",StringComparison.OrdinalIgnoreCase)) { record.AudioUrl=a; break; } }
            var sb=new StringBuilder();
            foreach(var meaning in (Json.Field(entry,"meanings") as object[] ?? new object[0]).Take(4)) {
                sb.AppendLine(Json.Str(Json.Field(meaning,"partOfSpeech")));
                foreach(var d in (Json.Field(meaning,"definitions") as object[] ?? new object[0]).Take(3)) { sb.AppendLine("• "+Json.Str(Json.Field(d,"definition"))); string example=Json.Str(Json.Field(d,"example")); if(example.Length>0) sb.AppendLine("例："+example); }
                sb.AppendLine();
            }
            record.Meaning=sb.ToString().Trim();
        } catch { }
        return record;
    }
    void ShowRecord(WordRecord record,bool main) {
        if(record==null) return;
        currentWord=record;
        string detail=RecordDetail(record);
        recordDetail.Text=detail;
        if(main) { definition.Text=detail; note.Text=record.Note; }
        else { query.Text=record.Word; definition.Text=detail; note.Text=record.Note; }
    }
    void RefreshHistory(WordRecord changed=null) {
        historyUpdating=true;
        try {
        if(changed!=null) {
            if(!history.Items.Contains(changed)) {
                int index=0; while(index<history.Items.Count) { var row=(WordRecord)history.Items[index]; int order=string.Compare(row.CreatedAt,changed.CreatedAt,StringComparison.Ordinal); if(order<0 || order==0 && row.Seconds<=changed.Seconds) break; index++; } history.Items.Insert(index,changed);
            } if(ReferenceEquals(changed,currentWord)) history.SelectedItem=changed; history.Invalidate();
        } else { history.BeginUpdate(); history.Items.Clear(); if(movie!=null) foreach(var record in movie.Words.OrderByDescending(w=>w.CreatedAt).ThenByDescending(w=>w.Seconds)) history.Items.Add(record); history.EndUpdate(); }
        tabs.TabPages[AppInfo.SavedTabIndex].Text=T("本片生词")+" ("+(movie?.Words.Count??0)+")";
        } finally { historyUpdating=false; }
    }
    void SaveNote() {
        if(currentWord==null || movie==null || !movie.Words.Contains(currentWord)) { Status("先查询或选择一条生词记录"); return; }
        currentWord.Note=note.Text;
        try { store.SaveRecord(movie,currentWord); RefreshHistory(currentWord); ShowRecord(currentWord,true); Status("笔记与原句已保存"); }
        catch(Exception ex) { Status("保存失败："+ex.Message+"。请保留播放器窗口，并检查目录写入权限。",true); }
    }
    async Task JumpToRecord() { var selected=history.SelectedItem as WordRecord ?? currentWord; if(selected!=null && movie!=null) { double at=selected.SentenceStart>=0?Math.Max(0,selected.SentenceStart-0.12):Math.Max(0,selected.Seconds-1); await Seek(at); sentence.Text=selected.Sentence; savedSentence=selected.Sentence; await mpv.Set("pause",false); pauseButton.Focus(); Status("回到原句："+selected.Word+" @ "+WordRecord.Clock(at)); } }
    async Task PlayWordAudio() {
        if(currentWord==null || string.IsNullOrWhiteSpace(currentWord.AudioUrl)) { Speak(query.Text); Status("此词没有词典音频，改用本机英语朗读"); return; }
        try {
            await mpv.Set("pause",true);
            Uri uri; if(!Uri.TryCreate(currentWord.AudioUrl,UriKind.Absolute,out uri) || uri.Scheme!="https") throw new IOException("无效的发音地址");
            string dir=Path.Combine(root,"data","audio"); Directory.CreateDirectory(dir); string path=Path.Combine(dir,Store.Hash(uri.AbsoluteUri)+".mp3");
            if(!File.Exists(path)) { byte[] bytes=await http.GetByteArrayAsync(uri); File.WriteAllBytes(path,bytes); }
            Process.Start(new ProcessStartInfo(Path.Combine(root,"vendor","mpv","mpv.exe"),"--no-config --no-video --really-quiet --audio-display=no -- \""+path+"\""){UseShellExecute=false,CreateNoWindow=true});
        } catch(Exception ex) { Status("词典发音失败，改用本机英语朗读："+ex.Message,true); Speak(query.Text); }
    }
    void Speak(string text) {
        if(string.IsNullOrWhiteSpace(text)) return;
        try {
            if(voice==null) {
                voice=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice"));
                var voices=voice.GetVoices(); bool found=false;
                for(int i=0;i<voices.Count;i++) { dynamic token=voices.Item(i); string language=Convert.ToString(token.GetAttribute("Language")); if(language.Split(';').Any(l=>l.Equals("409",StringComparison.OrdinalIgnoreCase)||l.Equals("809",StringComparison.OrdinalIgnoreCase))) { voice.Voice=token; found=true; break; } }
                if(!found) { voice=null; Status("本机未安装英语语音；请使用词典原声或 Google 的发音按钮",true); return; }
                voice.Rate=-1;
            }
            if(ready) _=Safe(async()=>await mpv.Set("pause",true));
            string english=string.Join(" ",Regex.Matches(text,"[A-Za-z]+(?:['’][A-Za-z]+)*[,.?!]?").Cast<Match>().Select(x=>x.Value));
            voice.Speak(english,3); // Async + purge preceding speech.
        } catch(Exception ex) { Status("英语朗读不可用："+ex.Message,true); }
    }
#if !PUBLIC_RELEASE
    async Task EnsureBrowser() {
        if(browser.CoreWebView2!=null) return;
        if(browserInit==null) browserInit=InitializeBrowser();
        try { await browserInit; } catch { browserInit=null; throw; }
    }
    async Task InitializeBrowser() {
        TraceBrowser("Initialize begin");
        var options=new CoreWebView2EnvironmentOptions();
        if(Network.LocalProxy.Length>0) options.AdditionalBrowserArguments="--proxy-server="+Network.LocalProxy;
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.Root,"webview"),options);
        TraceBrowser("Environment ready");
        await browser.EnsureCoreWebView2Async(env);
        TraceBrowser("Ensure complete");
        if(verificationMode) browser.CoreWebView2.ProcessFailed+=(s,e)=>TraceBrowser("ProcessFailed kind="+e.ProcessFailedKind+" reason="+e.Reason+" exit="+e.ExitCode);
        ApplyBrowserTheme();
        browser.CoreWebView2.Settings.AreDevToolsEnabled=false;
        browser.CoreWebView2.NewWindowRequested+=(s,e)=> { e.Handled=true; if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var u) && (u.Scheme=="https"||u.Scheme=="http")) Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true}); };
        browser.NavigationCompleted+=(s,e)=> { TraceBrowser("NavigationCompleted success="+e.IsSuccess+" error="+e.WebErrorStatus); if(!e.IsSuccess) { Status("Google 页面未能连接（"+e.WebErrorStatus+"）；离线词典与本地记录可继续使用",true); File.AppendAllText(Path.Combine(root,"logs","network.log"),DateTime.Now+" Google: "+e.WebErrorStatus+" Proxy="+Network.LocalProxy+"\r\n"); } };
    }
    string GoogleUrl(string text) { return "https://translate.google.com/?sl=en&tl=zh-CN&text="+Uri.EscapeDataString(text??"")+"&op=translate"; }
    async Task NavigateGoogle(string text) {
        workspaceTabs.SelectedIndex=0;
        await EnsureBrowser(); browser.Source=new Uri(GoogleUrl(text)); tabs.SelectedIndex=1;
        TraceBrowser("Navigation assigned");
    }
    void TraceBrowser(string stage) { if(verificationMode) File.AppendAllText(Path.Combine(root,"verification","browser-lifecycle.txt"),DateTime.Now.ToString("HH:mm:ss.fff")+" "+stage+" handle="+browser.IsHandleCreated+" core="+(browser.CoreWebView2!=null)+" visible="+browser.Visible+"\r\n"); }
    void OpenGoogleExternal() { Process.Start(new ProcessStartInfo(GoogleUrl(query.Text)){UseShellExecute=true}); }
#endif
    async Task Ocr() {
        if(movie==null || !ready) return;
        await mpv.Set("pause",true); await mpv.WaitForVideo(); double at=Json.Num(await mpv.Get("time-pos"));
        string dir=Path.Combine(root,"data","ocr"); Directory.CreateDirectory(dir);
        string png=Path.Combine(dir,"frame.png"),output=Path.Combine(dir,"result.txt");
        await mpv.Command("screenshot-to-file",png,"subtitles");
        Status("正在本机识别画面下方字幕…");
        var info=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
        info.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+OcrRuntime.Script(root,dir)+"\" -ImagePath \""+png+"\" -OutputPath \""+output+"\"";
        using(var p=Process.Start(info)) {
            var err=p.StandardError.ReadToEndAsync(); var stdout=p.StandardOutput.ReadToEndAsync();
            bool done=await Task.Run(()=>p.WaitForExit(20000));
            if(!done) { p.Kill(); throw new TimeoutException("字幕识别超时，请手动输入原句"); }
            if(p.ExitCode!=0) throw new IOException("字幕识别不可用："+await err+"；可在原句框手动输入");
            string text=File.ReadAllText(output,Encoding.UTF8).Trim();
            if(text.Length==0) { Status("没有识别到字幕；可手动输入原句",true); return; }
            sentence.Text=text; savedSentence=text; lastStart=at; lastEnd=-1; SetSentenceCaption("画面识别原句 · 请核对 · "+WordRecord.Clock(at)); Status("字幕已识别，请核对后点击单词查词；画面保存在 data\\ocr");
        }
    }
    void Export() { if(movie==null) return; using(var d=new SaveFileDialog { Title=T("导出本片生词"),Filter=T("CSV 表格|*.csv"),FileName=Path.GetFileNameWithoutExtension(movie.MoviePath)+T("-生词.csv"),InitialDirectory=root }) if(d.ShowDialog(this)==DialogResult.OK) { File.WriteAllText(d.FileName,Store.Csv(movie),new UTF8Encoding(true)); Status("已导出 "+movie.Words.Count+" 条记录："+d.FileName); } }
    bool Save(bool fullSnapshot=false) {
        try { if(movie!=null && !loading) { movie.Position=position; if(fullSnapshot) store.SaveMovie(movie); else store.SaveProgress(movie); } store.SaveSettings(settings); return true; }
        catch(Exception ex) { Status("保存失败："+ex.Message+"。请保留播放器窗口，并检查目录写入权限。",true); return false; }
    }
    void CloseApp(object sender,FormClosingEventArgs e) {
        if(!Save(true)) { e.Cancel=true; MessageBox.Show(T("记录未能保存，请检查目录写入权限后再关闭。可从本片生词页导出 CSV。"),T("保存失败")); return; }
        closing=true; lookupCancellation?.Cancel(); StopUpdates(); saveTimer.Stop(); loopTimer.Stop(); mpv.Dispose(); http.Dispose(); store.Dispose();
        try { if(voice!=null) voice.Speak("",3); } catch { }
    }
    void ToggleFullscreen() {
        if(!fullscreen) { oldBounds=Bounds; oldBorder=FormBorderStyle; oldState=WindowState; WindowState=FormWindowState.Normal; FormBorderStyle=FormBorderStyle.None; Bounds=Screen.FromControl(this).Bounds; fullscreen=true; }
        else { FormBorderStyle=oldBorder; Bounds=oldBounds; WindowState=oldState; fullscreen=false; }
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) {
        if(key==(Keys.Control|Keys.O)) { _=Safe(OpenDialog); return true; }
        if(key==(Keys.Control|Keys.F)) { workspaceTabs.SelectedIndex=0; query.Focus(); query.SelectAll(); return true; }
        var focusedModern=FocusedModernControl(this);
        if(focusedModern!=null && (key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || key==Keys.Home || key==Keys.End || key==Keys.PageUp || key==Keys.PageDown || key==(Keys.Control|Keys.Tab) || key==(Keys.Control|Keys.Shift|Keys.Tab) || focusedModern is ModernComboBox && (key==Keys.Space || key==Keys.Enter || key==Keys.F4 || key==(Keys.Alt|Keys.Down)))) return base.ProcessCmdKey(ref msg,key);
        if(key==Keys.Escape && fullscreen) { ToggleFullscreen(); return true; }
        if(workspaceTabs.SelectedIndex==1) {
            if(key==Keys.Escape) { workspaceTabs.SelectedIndex=0; pauseButton.Focus(); return true; }
            return base.ProcessCmdKey(ref msg,key);
        }
        if(key==Keys.Escape) { pauseButton.Focus(); return true; }
        if(ActiveControl is TextBoxBase || ContainsFocusedText(this)) return base.ProcessCmdKey(ref msg,key);
#if !PUBLIC_RELEASE
        if(browser.ContainsFocus) return base.ProcessCmdKey(ref msg,key);
#endif
        switch(key) {
            case Keys.Space: _=Safe(TogglePause); return true;
            case Keys.Left: _=Safe(()=>Jump(-settings.JumpSeconds)); return true;
            case Keys.Right: _=Safe(()=>Jump(settings.JumpSeconds)); return true;
            case Keys.Control|Keys.Left: _=Safe(()=>Jump(-5)); return true;
            case Keys.Control|Keys.Right: _=Safe(()=>Jump(5)); return true;
            case Keys.Shift|Keys.Left: _=Safe(()=>Jump(-3)); return true;
            case Keys.Shift|Keys.Right: _=Safe(()=>Jump(3)); return true;
            case Keys.R: _=Safe(Replay); return true;
            case Keys.F: ToggleFullscreen(); return true;
            case Keys.O: _=Safe(Ocr); return true;
        }
        return base.ProcessCmdKey(ref msg,key);
    }
    static bool ContainsFocusedText(Control c) { foreach(Control child in c.Controls) if(child is TextBoxBase && child.Focused || ContainsFocusedText(child)) return true; return false; }
    static Control FocusedModernControl(Control c) { foreach(Control child in c.Controls) { if((child is ModernComboBox || child is ModernSlider || child is ThemeTabs) && child.Focused) return child; var found=FocusedModernControl(child); if(found!=null) return found; } return null; }
    async Task Safe(Func<Task> action) { try { await action(); } catch(Exception ex) { Status(ex.Message,true); } }
    async Task<object> GetOptional(string property) { try { return await mpv.Get(property); } catch(IOException) { return null; } }
    void Ui(Action action) { if(closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch(InvalidOperationException) { } }
    void Status(string text,bool error=false) { statusSource=text; statusIsError=error; status.Text=T(text); status.ForeColor=error?theme.Error:accent; }
}
}
