using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FilmEcho {
static class ModernPaint {
    public static GraphicsPath Round(RectangleF r,float radius) {
        var path=new GraphicsPath(); float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
        path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
    public static Color Mix(Color a,Color b,float t) { t=Math.Max(0,Math.Min(1,t)); return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t)); }
    public static void Card(Graphics g,RectangleF rect,Color fill,Color border,float radius=8) {
        if(rect.Width<=0 || rect.Height<=0) return;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Round(rect,radius)) { using(var b=new SolidBrush(fill)) g.FillPath(b,p); using(var pen=new Pen(border)) g.DrawPath(pen,p); }
    }
    public static Rectangle Box(Control c) { return new Rectangle(1,1,Math.Max(0,c.Width-3),Math.Max(0,c.Height-3)); }
}
// Timers run only while a transition is active, and are disposed with their control.
sealed class HoverMotion : IDisposable {
    readonly Control owner; readonly Timer timer=new Timer { Interval=16 }; readonly Stopwatch time=new Stopwatch();
    float start,target; public float Value { get; private set; }
    public HoverMotion(Control control) { owner=control; timer.Tick+=(s,e)=> { float t=Math.Min(1,(float)time.Elapsed.TotalMilliseconds/130); Value=start+(target-start)*(1-(1-t)*(1-t)); owner.Invalidate(); if(t>=1) timer.Stop(); }; }
    public void To(float value) { start=Value; target=value; time.Restart(); timer.Start(); }
    public void Dispose() { timer.Dispose(); }
}
sealed class ModernButton : Button {
    public ThemePalette Palette=ThemePalette.For(true); readonly HoverMotion hover; bool down;
    public ModernButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); hover=new HoverMotion(this); Cursor=Cursors.Hand; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; TabStop=true; }
    protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0); down=false; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down=e.Button==MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent==null?Palette.Background:Parent.BackColor);
        bool primary=BackColor.ToArgb()==Palette.Primary.ToArgb();
        Color fill=ModernPaint.Mix(BackColor,primary?Palette.Accent:Palette.Selection,down?.45f:hover.Value*.55f);
        ModernPaint.Card(e.Graphics,ModernPaint.Box(this),Enabled?fill:Palette.Field,Focused?Palette.Accent:primary?fill:Palette.Border);
        var r=ClientRectangle; if(down) r.Offset(0,1);
        TextRenderer.DrawText(e.Graphics,Text,Font,r,Enabled?ForeColor:Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if(disposing) hover.Dispose(); base.Dispose(disposing); }
}
sealed class ModernComboBox : Control {
    public sealed class ItemCollection : Collection<object> {
        readonly ModernComboBox owner; internal ItemCollection(ModernComboBox combo) { owner=combo; }
        public void AddRange(object[] values) { foreach(var value in values) Add(value); }
        protected override void ClearItems() { owner.CloseDropdown(); base.ClearItems(); owner.SelectedIndex=-1; owner.Invalidate(); }
        protected override void InsertItem(int index,object item) { base.InsertItem(index,item); if(owner.index>=index) owner.index++; owner.Invalidate(); }
        protected override void RemoveItem(int index) { owner.CloseDropdown(); base.RemoveItem(index); if(owner.index==index) owner.SelectedIndex=-1; else if(owner.index>index) owner.index--; owner.Invalidate(); }
        protected override void SetItem(int index,object item) { base.SetItem(index,item); owner.Invalidate(); }
    }
    int index=-1; readonly HoverMotion hover; readonly ToolTip tip=new ToolTip { InitialDelay=550,ReshowDelay=100,AutoPopDelay=10000 }; ToolStripDropDown popup; ChoiceSurface choices;
    public ThemePalette Palette=ThemePalette.For(true);
    public Func<object,string> DisplayText;
    public string EmptyText="选择…";
    public bool FullItemTooltip;
    public ItemCollection Items { get; private set; }
    public event EventHandler SelectedIndexChanged;
    public bool IsDropdownOpen { get { return popup!=null && popup.Visible; } }
    public int SelectedIndex {
        get { return index; }
        set { if(value<-1 || value>=Items.Count) throw new ArgumentOutOfRangeException("value"); if(index==value) return; index=value; RefreshDisplay(); AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1); SelectedIndexChanged?.Invoke(this,EventArgs.Empty); }
    }
    public object SelectedItem { get { return index<0?null:Items[index]; } set { SelectedIndex=Items.IndexOf(value); } }
    public string GetItemText(object value) { return DisplayText!=null?DisplayText(value):Convert.ToString(value); }
    public void RefreshDisplay() { base.Text=index<0?"":GetItemText(Items[index]); tip.SetToolTip(this,index<0?"":FullItemTooltip?Convert.ToString(Items[index]):base.Text); Invalidate(); }
    public ModernComboBox() { Items=new ItemCollection(this); Size=new Size(160,32); SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true); hover=new HoverMotion(this); Cursor=Cursors.Hand; AccessibleRole=AccessibleRole.ComboBox; TabStop=true; }
    public void SetPalette(ThemePalette p) { Palette=p; if(popup!=null) { popup.BackColor=p.Field; choices.Invalidate(); } Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Left && Enabled) { Focus(); if(IsDropdownOpen) CloseDropdown(); else OpenDropdown(); } }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { if(!Enabled) CloseDropdown(); Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent==null?Palette.Background:Parent.BackColor);
        ModernPaint.Card(e.Graphics,ModernPaint.Box(this),ModernPaint.Mix(Palette.Field,Palette.Selection,hover.Value*.4f),Focused||IsDropdownOpen?Palette.Accent:Palette.Border);
        var r=new Rectangle(11,1,Math.Max(0,Width-39),Height-3);
        TextRenderer.DrawText(e.Graphics,index<0?EmptyText:GetItemText(SelectedItem),Font,r,Enabled?Palette.Text:Palette.Muted,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        float x=Width-17,y=Height/2f; float direction=IsDropdownOpen?-1:1;
        using(var pen=new Pen(Enabled?Palette.Muted:Palette.Border,1.7f)) { pen.StartCap=pen.EndCap=LineCap.Round; e.Graphics.DrawLines(pen,new[]{new PointF(x-4,y-2*direction),new PointF(x,y+2*direction),new PointF(x+4,y-2*direction)}); }
    }
    public void OpenDropdown() {
        if(IsDropdownOpen || Items.Count==0 || !Enabled) return;
        if(popup!=null) { popup.Dispose(); popup=null; }
        var screen=Screen.FromControl(this).WorkingArea; int width=Math.Min(screen.Width-16,Math.Max(Width,Math.Min(620,Items.Cast<object>().Select(x=>TextRenderer.MeasureText(GetItemText(x),Font).Width+68).Max())));
        choices=new ChoiceSurface(this) { Size=new Size(width,Math.Min(Items.Count,8)*RowHeight+16),Font=Font };
        var host=new ToolStripControlHost(choices) { AutoSize=false,Size=choices.Size,Margin=Padding.Empty,Padding=Padding.Empty };
        popup=new ToolStripDropDown { AutoSize=false,Size=choices.Size,Padding=Padding.Empty,Margin=Padding.Empty,BackColor=Palette.Field,DropShadowEnabled=true,AutoClose=true };
        popup.Items.Add(host); popup.Closed+=(s,e)=> { Invalidate(); if(!IsDisposed && Visible && Enabled && (e.CloseReason==ToolStripDropDownCloseReason.CloseCalled || e.CloseReason==ToolStripDropDownCloseReason.Keyboard || e.CloseReason==ToolStripDropDownCloseReason.ItemClicked)) Focus(); };
        Point below=PointToScreen(new Point(0,Height+5)); int px=Math.Max(screen.Left+4,Math.Min(below.X,screen.Right-width-4));
        int py=below.Y+popup.Height<=screen.Bottom?below.Y:PointToScreen(Point.Empty).Y-popup.Height-5;
        popup.Show(new Point(px,Math.Max(screen.Top+4,py))); choices.Focus(); Invalidate();
    }
    public void CloseDropdown() { if(popup!=null) popup.Close(); }
    int RowHeight { get { return Math.Max(36,Font.Height+18); } }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) {
        if(IsDropdownOpen) return choices.RouteKey(key) || base.ProcessCmdKey(ref msg,key);
        if(key==Keys.Space || key==Keys.Enter || key==Keys.F4 || key==(Keys.Alt|Keys.Down)) { OpenDropdown(); return true; }
        if(key==Keys.Down || key==Keys.Up || key==Keys.Home || key==Keys.End) {
            if(Items.Count>0) SelectedIndex=key==Keys.Home?0:key==Keys.End?Items.Count-1:Math.Max(0,Math.Min(Items.Count-1,index+(key==Keys.Down?1:-1)));
            return true;
        }
        return base.ProcessCmdKey(ref msg,key);
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new ComboAccessible(this); }
    sealed class ComboAccessible : ControlAccessibleObject {
        readonly ModernComboBox combo; public ComboAccessible(ModernComboBox owner):base(owner) { combo=owner; }
        public override string Value { get { return combo.Text; } set { for(int i=0;i<combo.Items.Count;i++) if(combo.GetItemText(combo.Items[i])==value) { combo.SelectedIndex=i; break; } } }
        public override string DefaultAction { get { return combo.IsDropdownOpen?"收起选项":"展开选项"; } }
        public override AccessibleStates State { get { return base.State|(combo.IsDropdownOpen?AccessibleStates.Expanded:AccessibleStates.Collapsed); } }
        public override void DoDefaultAction() { if(combo.IsDropdownOpen) combo.CloseDropdown(); else combo.OpenDropdown(); }
    }
    protected override void Dispose(bool disposing) { if(disposing) { hover.Dispose(); tip.Dispose(); if(popup!=null) popup.Dispose(); } base.Dispose(disposing); }
    sealed class ChoiceSurface : Control {
        readonly ModernComboBox owner; int active,first; string typed=""; DateTime lastTyped;
        int VisibleRows { get { return Math.Max(1,(Height-16)/owner.RowHeight); } }
        public ChoiceSurface(ModernComboBox combo) { owner=combo; active=Math.Max(0,owner.SelectedIndex); SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Selectable,true); TabStop=true; Cursor=Cursors.Hand; AccessibleRole=AccessibleRole.List; EnsureVisible(); }
        void EnsureVisible() { if(active<first) first=active; if(active>=first+VisibleRows) first=active-VisibleRows+1; first=Math.Max(0,Math.Min(first,Math.Max(0,owner.Items.Count-VisibleRows))); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if(owner!=null) EnsureVisible(); }
        protected override void OnPaint(PaintEventArgs e) {
            var p=owner.Palette; e.Graphics.Clear(p.Field); ModernPaint.Card(e.Graphics,ModernPaint.Box(this),p.Field,p.Border,10);
            for(int i=first;i<Math.Min(owner.Items.Count,first+VisibleRows);i++) {
                var r=new Rectangle(7,8+(i-first)*owner.RowHeight,Width-14,owner.RowHeight-2);
                if(i==active || i==owner.SelectedIndex) ModernPaint.Card(e.Graphics,r,i==active?p.Selection:ModernPaint.Mix(p.Field,p.Selection,.5f),Color.Transparent,6);
                var text=new Rectangle(r.X+10,r.Y,r.Width-44,r.Height);
                TextRenderer.DrawText(e.Graphics,owner.GetItemText(owner.Items[i]),Font,text,p.Text,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                if(i==owner.SelectedIndex) using(var pen=new Pen(p.Accent,1.8f)) { float x=r.Right-23,y=r.Y+r.Height/2f; e.Graphics.DrawLines(pen,new[]{new PointF(x-4,y),new PointF(x-1,y+3),new PointF(x+5,y-4)}); }
            }
            if(owner.Items.Count>VisibleRows) {
                float h=(Height-20f)*VisibleRows/owner.Items.Count,y=10+(Height-20-h)*first/Math.Max(1,owner.Items.Count-VisibleRows);
                using(var b=new SolidBrush(p.Muted)) using(var path=ModernPaint.Round(new RectangleF(Width-5,y,2,h),1)) e.Graphics.FillPath(b,path);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { int row=(e.Y-8)/owner.RowHeight+first; if(e.Y>=8 && row<owner.Items.Count && row!=active) { active=row; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int row=(e.Y-8)/owner.RowHeight+first; if(e.Button==MouseButtons.Left && e.Y>=8 && row<owner.Items.Count && row<first+VisibleRows) { owner.SelectedIndex=row; owner.CloseDropdown(); } }
        protected override void OnMouseWheel(MouseEventArgs e) { first=Math.Max(0,Math.Min(Math.Max(0,owner.Items.Count-VisibleRows),first-Math.Sign(e.Delta)*3)); active=Math.Max(first,Math.Min(active,first+VisibleRows-1)); Invalidate(); base.OnMouseWheel(e); }
        public bool RouteKey(Keys key) {
            if(key==Keys.Escape) { owner.CloseDropdown(); return true; }
            if(key==Keys.Tab || key==(Keys.Shift|Keys.Tab)) { owner.CloseDropdown(); owner.FindForm()?.SelectNextControl(owner,key==Keys.Tab,true,true,true); return true; }
            if(key==Keys.Enter || key==Keys.Space) { owner.SelectedIndex=active; owner.CloseDropdown(); return true; }
            if(key==Keys.Down || key==Keys.Up || key==Keys.Home || key==Keys.End || key==Keys.PageDown || key==Keys.PageUp) {
                active=key==Keys.Home?0:key==Keys.End?owner.Items.Count-1:active+(key==Keys.Down?1:key==Keys.Up?-1:key==Keys.PageDown?VisibleRows:-VisibleRows);
                active=Math.Max(0,Math.Min(owner.Items.Count-1,active)); EnsureVisible(); Invalidate(); return true;
            }
            return false;
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys key) { return RouteKey(key)||base.ProcessCmdKey(ref msg,key); }
        protected override bool IsInputKey(Keys key) { return key==Keys.Up||key==Keys.Down||base.IsInputKey(key); }
        protected override void OnKeyPress(KeyPressEventArgs e) {
            if(!char.IsControl(e.KeyChar)) {
                if((DateTime.UtcNow-lastTyped).TotalSeconds>1) typed=""; typed+=e.KeyChar; lastTyped=DateTime.UtcNow;
                for(int i=0;i<owner.Items.Count;i++) if(owner.GetItemText(owner.Items[i]).StartsWith(typed,StringComparison.CurrentCultureIgnoreCase)) { active=i; EnsureVisible(); Invalidate(); break; }
                e.Handled=true;
            }
            base.OnKeyPress(e);
        }
    }
}
sealed class ModernSlider : Control {
    int minimum,maximum=100,value; bool dragging,hover;
    public ThemePalette Palette=ThemePalette.For(true);
    public int Minimum { get { return minimum; } set { minimum=value; Value=this.value; } }
    public int Maximum { get { return maximum; } set { maximum=value; Value=this.value; } }
    public int Value { get { return value; } set { int v=Math.Max(minimum,Math.Min(maximum,value)); if(this.value==v) return; this.value=v; Invalidate(); ValueChanged?.Invoke(this,EventArgs.Empty); } }
    public event EventHandler ValueChanged;
    public ModernSlider() { Size=new Size(160,32); SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true); Cursor=Cursors.Hand; TabStop=true; AccessibleRole=AccessibleRole.Slider; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); float y=Height/2f,x=10+(Width-20f)*(value-minimum)/Math.Max(1,maximum-minimum); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=new Pen(Palette.Border,4)) { p.StartCap=p.EndCap=LineCap.Round; e.Graphics.DrawLine(p,10,y,Math.Max(10,Width-10),y); }
        if(x>10) using(var p=new Pen(Palette.Accent,4)) { p.StartCap=p.EndCap=LineCap.Round; e.Graphics.DrawLine(p,10,y,x,y); }
        if(hover||Focused||dragging) using(var b=new SolidBrush(Color.FromArgb(40,Palette.Accent))) e.Graphics.FillEllipse(b,x-10,y-10,20,20);
        using(var b=new SolidBrush(Enabled?Palette.Accent:Palette.Muted)) e.Graphics.FillEllipse(b,x-5.5f,y-5.5f,11,11);
    }
    void SetFromX(int x) { Value=minimum+(int)Math.Round(Math.Max(0,Math.Min(1,(x-10.0)/Math.Max(1,Width-20)))*(maximum-minimum)); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Focus(); dragging=true; Capture=true; } base.OnMouseDown(e); if(dragging) SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if(dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if(dragging) SetFromX(e.X); dragging=false; Capture=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { dragging=false; base.OnMouseCaptureChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys key) { return key==Keys.Left||key==Keys.Right||key==Keys.Home||key==Keys.End||base.IsInputKey(key); }
    protected override void OnKeyDown(KeyEventArgs e) { int step=Math.Max(1,(maximum-minimum)/100); if(e.KeyCode==Keys.Left) Value-=step; else if(e.KeyCode==Keys.Right) Value+=step; else if(e.KeyCode==Keys.Home) Value=minimum; else if(e.KeyCode==Keys.End) Value=maximum; else { base.OnKeyDown(e); return; } e.Handled=true; base.OnKeyDown(e); }
}
sealed class ModernToggle : CheckBox {
    readonly HoverMotion motion; public ThemePalette Palette=ThemePalette.For(true);
    public ModernToggle() { motion=new HoverMotion(this); SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true); Cursor=Cursors.Hand; }
    public override Size GetPreferredSize(Size proposedSize) { var size=TextRenderer.MeasureText(Text,Font); return new Size(size.Width+46,Math.Max(26,size.Height+8)); }
    protected override void OnCheckedChanged(EventArgs e) { motion.To(Checked?1:0); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); float y=(Height-18)/2f;
        ModernPaint.Card(e.Graphics,new RectangleF(1,y,32,18),ModernPaint.Mix(Palette.Border,Palette.Primary,motion.Value),Focused?Palette.Accent:Color.Transparent,9);
        using(var b=new SolidBrush(Checked?Palette.PrimaryText:Palette.Text)) e.Graphics.FillEllipse(b,4+14*motion.Value,y+3,12,12);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(42,0,Math.Max(0,Width-42),Height),Enabled?Palette.Text:Palette.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if(disposing) motion.Dispose(); base.Dispose(disposing); }
}
sealed class ModernField : Panel {
    public ThemePalette Palette=ThemePalette.For(true); public readonly TextBoxBase Editor;
    public ModernField(TextBoxBase editor) {
        Editor=editor; Dock=editor.Dock; Margin=editor.Margin; Padding=new Padding(10,5,10,5);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        editor.BorderStyle=BorderStyle.None; editor.Dock=DockStyle.Fill; editor.Margin=Padding.Empty; Controls.Add(editor);
        var single=editor as TextBox;
        if(single!=null && !single.Multiline) { single.AutoSize=false; single.Dock=DockStyle.None; MinimumSize=new Size(0,single.Font.Height+18); single.FontChanged+=(s,e)=>PerformLayout(); }
        editor.GotFocus+=(s,e)=>Invalidate(); editor.LostFocus+=(s,e)=>Invalidate();
    }
    protected override void OnLayout(LayoutEventArgs e) {
        base.OnLayout(e);
        var single=Editor as TextBox;
        if(single!=null && !single.Multiline) {
            int height=Math.Max(single.PreferredHeight,single.Font.Height+6);
            MinimumSize=new Size(0,height+12);
            single.Bounds=new Rectangle(Padding.Left,Math.Max(4,(ClientSize.Height-height)/2),Math.Max(1,ClientSize.Width-Padding.Horizontal),height);
        }
    }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent==null?Palette.Background:Parent.BackColor); ModernPaint.Card(e.Graphics,ModernPaint.Box(this),Palette.Field,Editor.ContainsFocus?Palette.Accent:Palette.Border); }
}
}
