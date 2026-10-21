using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#if !PUBLIC_RELEASE
using Microsoft.Web.WebView2.Core;
#endif

namespace FilmEcho {
sealed class ThemePalette {
    public Color Background,Surface,Side,Field,Text,Muted,Accent,Button,Border,Error,Primary,PrimaryText,Selection;
    public static ThemePalette For(bool dark) {
        return dark ? new ThemePalette {
            Background=Color.FromArgb(18,21,25),Surface=Color.FromArgb(25,30,35),Side=Color.FromArgb(22,27,32),Field=Color.FromArgb(31,37,43),
            Text=Color.FromArgb(221,227,232),Muted=Color.FromArgb(156,172,181),Accent=Color.FromArgb(124,206,184),Button=Color.FromArgb(35,43,49),Border=Color.FromArgb(68,83,91),
            Error=Color.FromArgb(241,155,136),Primary=Color.FromArgb(49,111,94),PrimaryText=Color.FromArgb(240,247,243),Selection=Color.FromArgb(45,80,71)
        } : new ThemePalette {
            Background=Color.FromArgb(244,246,242),Surface=Color.White,Side=Color.FromArgb(235,240,234),Field=Color.White,
            Text=Color.FromArgb(29,43,48),Muted=Color.FromArgb(91,107,106),Accent=Color.FromArgb(30,103,92),Button=Color.White,Border=Color.FromArgb(207,218,213),
            Error=Color.FromArgb(163,57,44),Primary=Color.FromArgb(30,103,92),PrimaryText=Color.White,Selection=Color.FromArgb(212,234,222)
        };
    }
}
sealed class ThemeRole { public bool Accent,Muted,Side,Surface,NativeHooked; }
sealed class ThemePage : Panel {
    public bool UseVisualStyleBackColor { get; set; }
    public ThemePage(string text) { Text=text; }
}
sealed class ThemeTabs : ContainerControl {
    public int HeaderItemWidth { get; set; }
    public Color HeaderBackground=Color.FromArgb(18,21,25),HeaderSelected=Color.FromArgb(45,80,71),HeaderText=Color.FromArgb(221,227,232),HeaderBorder=Color.FromArgb(68,83,91);
    public sealed class PageCollection : Collection<ThemePage> {
        readonly ThemeTabs owner; internal PageCollection(ThemeTabs tabs) { owner=tabs; }
        public void AddRange(ThemePage[] pages) { foreach(var page in pages) Add(page); }
        protected override void InsertItem(int index,ThemePage page) { base.InsertItem(index,page); page.Dock=DockStyle.Fill; page.Visible=false; owner.pageHost.Controls.Add(page); page.TextChanged+=(s,e)=>owner.Invalidate(); if(owner.selected<0) owner.SelectedIndex=0; owner.Invalidate(); }
    }
    readonly Panel body=new Panel { Dock=DockStyle.Fill }; int selected=-1,hover=-1;
    readonly Panel pageHost=new Panel { Dock=DockStyle.Fill };
    public readonly Panel SharedHeader=new Panel { Dock=DockStyle.Top,Height=0 };
    public PageCollection TabPages { get; private set; }
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex {
        get { return selected; }
        set { if(value<0 || value>=TabPages.Count || value==selected) return; if(selected>=0) TabPages[selected].Visible=false; selected=value; TabPages[value].Visible=true; TabPages[value].BringToFront(); Invalidate(); SelectedIndexChanged?.Invoke(this,EventArgs.Empty); }
    }
    public ThemePage SelectedTab { get { return selected>=0?TabPages[selected]:null; } }
    public ThemeTabs() { TabPages=new PageCollection(this); Padding=new Padding(0,43,0,0); Controls.Add(body); body.Controls.Add(pageHost); body.Controls.Add(SharedHeader); SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); TabStop=true; AccessibleRole=AccessibleRole.PageTabList; }
    Rectangle Header(int index) { int w=Math.Max(1,(Width-8)/Math.Max(1,TabPages.Count)); if(HeaderItemWidth>0) w=Math.Min(w,HeaderItemWidth); return new Rectangle(4+index*w,4,w-3,32); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); ModernPaint.Card(e.Graphics,new Rectangle(1,1,Math.Max(1,Width-3),38),HeaderBackground,Color.Transparent,9);
        for(int i=0;i<TabPages.Count;i++) {
            var r=Header(i); if(i==selected || i==hover) ModernPaint.Card(e.Graphics,r,i==selected?HeaderSelected:ModernPaint.Mix(HeaderBackground,HeaderSelected,.4f),Color.Transparent,7);
            TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,HeaderText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e) { int next=-1; for(int i=0;i<TabPages.Count;i++) if(Header(i).Contains(e.Location)) next=i; if(next!=hover) { hover=next; Cursor=hover<0?Cursors.Default:Cursors.Hand; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=-1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) for(int i=0;i<TabPages.Count;i++) if(Header(i).Contains(e.Location)) { Focus(); SelectedIndex=i; break; } base.OnMouseDown(e); }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) { if(Focused && (key==Keys.Left || key==Keys.Right)) { SelectedIndex=(selected+(key==Keys.Right?1:TabPages.Count-1))%TabPages.Count; return true; } if(key==(Keys.Control|Keys.Tab) || key==(Keys.Control|Keys.Shift|Keys.Tab)) { SelectedIndex=(selected+(key==(Keys.Control|Keys.Tab)?1:TabPages.Count-1))%TabPages.Count; return true; } return base.ProcessCmdKey(ref msg,key); }
}
public sealed partial class MainForm {
    readonly ModernComboBox themeChoice=new ModernComboBox();
    ThemePalette theme=ThemePalette.For(false);
    bool themeApplied,changingTheme,statusIsError;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd,string subAppName,string subIdList);
    void RememberThemeRoles(Control control) {
        if(!(control.Tag is ThemeRole)) control.Tag=new ThemeRole {
            Accent=control.ForeColor.ToArgb()==Color.FromArgb(30,103,92).ToArgb(),
            Muted=control.ForeColor.ToArgb()==Color.FromArgb(91,107,106).ToArgb(),
            Side=control.BackColor.ToArgb()==Color.FromArgb(235,240,234).ToArgb(),
            Surface=control.BackColor.ToArgb()==Color.White.ToArgb() || control.BackColor.ToArgb()==Color.FromArgb(247,249,244).ToArgb()
        };
        foreach(Control child in control.Controls) RememberThemeRoles(child);
    }
    void ApplyTheme(string choice,bool save) {
        bool dark=choice!="light";
        RememberThemeRoles(this);
        changingTheme=true; SuspendLayout();
        try {
            settings.Theme=dark?"dark":"light"; theme=ThemePalette.For(dark); accent=theme.Accent; ink=theme.Text;
            StyleThemedControl(this); themeApplied=true;
            themeChoice.SelectedIndex=dark?0:1;
            ApplyWindowTheme(); ApplyBrowserTheme();
            Invalidate(true);
        } finally { ResumeLayout(true); changingTheme=false; }
        if(save) {
            // Store only the preference. Do not touch movie progress or playback.
            try { store.SaveSettings(settings); Status(dark?"已切换深色主题；下次打开会记住":"已切换浅色主题；下次打开会记住"); }
            catch(Exception ex) { Status("主题已切换，偏好保存失败："+ex.Message,true); }
        }
    }
    void StyleThemedControl(Control c) {
        var role=c.Tag as ThemeRole;
        if(role==null) { RememberThemeRoles(c); role=(ThemeRole)c.Tag; }
        if(c==video || c==emptyVideo) return;
        Color bg=role.Side?theme.Side:role.Surface?theme.Surface:theme.Background;
        if(c is TextBoxBase || c is ListBox || c is ModernComboBox || c is ModernField) bg=theme.Field;
        if(c is ThemePage) { ((ThemePage)c).UseVisualStyleBackColor=false; bg=theme.Surface; }
        c.BackColor=bg; c.ForeColor=role.Accent?theme.Accent:role.Muted?theme.Muted:theme.Text;
        if(c is Button) {
            var button=(Button)c; button.UseVisualStyleBackColor=false;
            bool primaryButton=c==pauseButton || c==lookupButton;
            button.BackColor=primaryButton?theme.Primary:theme.Button; button.ForeColor=primaryButton?theme.PrimaryText:theme.Text;
            button.FlatAppearance.BorderColor=theme.Border; button.FlatAppearance.MouseOverBackColor=theme.Selection; button.FlatAppearance.MouseDownBackColor=theme.Primary;
        }
        if(c is ModernComboBox) ((ModernComboBox)c).SetPalette(theme);
        if(c is ModernButton) ((ModernButton)c).Palette=theme;
        if(c is UpdateIndicator) ((UpdateIndicator)c).Palette=theme;
        if(c is ModernSlider) ((ModernSlider)c).Palette=theme;
        if(c is ModernToggle) ((ModernToggle)c).Palette=theme;
        if(c is ModernField) ((ModernField)c).Palette=theme;
        if(c is ThemeTabs) { var strip=(ThemeTabs)c; strip.HeaderBackground=theme.Side; strip.HeaderSelected=theme.Selection; strip.HeaderText=theme.Text; strip.HeaderBorder=theme.Border; }
        if(c==usagePage) { usagePage.SetAppearance(theme,EnglishUi); return; }
        if(c==status) c.ForeColor=statusIsError?theme.Error:theme.Accent;
        if(c.IsHandleCreated && (c is TextBoxBase || c is ListBox)) ApplyNativeControlTheme(c);
        else if(!c.IsHandleCreated && !role.NativeHooked) { role.NativeHooked=true; c.HandleCreated+=(s,e)=>ApplyNativeControlTheme(c); }
        foreach(Control child in c.Controls) StyleThemedControl(child);
    }
    void ApplyNativeControlTheme(Control c) {
        if(c.IsHandleCreated && (c is TextBoxBase || c is ListBox)) {
            try { SetWindowTheme(c.Handle,settings.Theme=="dark"?"DarkMode_Explorer":"Explorer",null); } catch { }
        }
    }
    void ApplyWindowTheme() { if(IsHandleCreated) { try { int dark=settings.Theme=="dark"?1:0; DwmSetWindowAttribute(Handle,20,ref dark,4); } catch { } } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyWindowTheme(); }
    void ApplyBrowserTheme() {
#if !PUBLIC_RELEASE
        browser.DefaultBackgroundColor=theme.Surface;
        if(browser.CoreWebView2!=null) browser.CoreWebView2.Profile.PreferredColorScheme=settings.Theme=="dark"?CoreWebView2PreferredColorScheme.Dark:CoreWebView2PreferredColorScheme.Light;
#endif
    }
    void DrawWordRecord(DrawItemEventArgs e) {
        if(e.Index<0 || e.Index>=history.Items.Count) return;
        var word=history.Items[e.Index] as WordRecord; if(word==null) return;
        using(var brush=new SolidBrush(theme.Field)) e.Graphics.FillRectangle(brush,e.Bounds);
        bool selected=(e.State&DrawItemState.Selected)!=0;
        var card=new Rectangle(e.Bounds.X+3,e.Bounds.Y+3,Math.Max(1,e.Bounds.Width-8),e.Bounds.Height-6);
        ModernPaint.Card(e.Graphics,card,selected?theme.Selection:theme.Field,selected?theme.Accent:Color.Transparent,7);
        var title=new Rectangle(card.X+10,card.Y+6,Math.Max(1,card.Width-108),23);
        using(var font=new Font(history.Font.FontFamily,10.5f,FontStyle.Bold)) TextRenderer.DrawText(e.Graphics,word.Word,font,title,theme.Text,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics,WordRecord.Clock(word.Seconds),history.Font,new Rectangle(card.Right-88,card.Y+8,78,20),theme.Muted,TextFormatFlags.Right|TextFormatFlags.SingleLine);
        string context=string.IsNullOrWhiteSpace(word.Sentence)?word.Meaning:word.Sentence; context=context.Replace("\r"," ").Replace("\n"," ");
        TextRenderer.DrawText(e.Graphics,context,history.Font,new Rectangle(card.X+10,card.Y+31,Math.Max(1,card.Width-20),20),theme.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    }
}
}
