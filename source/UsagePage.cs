using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FilmEcho {
sealed class UsageRegion {
    public string Code,Chinese,English;
    public UsageRegion(string code,string chinese,string english) { Code=code; Chinese=chinese; English=english; }
    public static readonly UsageRegion[] All={
        new UsageRegion("XJ","新疆","Xinjiang"),new UsageRegion("XZ","西藏","Tibet"),
        new UsageRegion("QH","青海","Qinghai"),new UsageRegion("GS","甘肃","Gansu"),
        new UsageRegion("NX","宁夏","Ningxia"),new UsageRegion("SN","陕西","Shaanxi"),
        new UsageRegion("NM","内蒙古","Inner Mongolia"),new UsageRegion("HL","黑龙江","Heilongjiang"),
        new UsageRegion("JL","吉林","Jilin"),new UsageRegion("LN","辽宁","Liaoning"),
        new UsageRegion("BJ","北京","Beijing"),new UsageRegion("TJ","天津","Tianjin"),
        new UsageRegion("HE","河北","Hebei"),new UsageRegion("SX","山西","Shanxi"),
        new UsageRegion("SD","山东","Shandong"),new UsageRegion("HA","河南","Henan"),
        new UsageRegion("SC","四川","Sichuan"),new UsageRegion("CQ","重庆","Chongqing"),
        new UsageRegion("HB","湖北","Hubei"),new UsageRegion("AH","安徽","Anhui"),
        new UsageRegion("JS","江苏","Jiangsu"),new UsageRegion("SH","上海","Shanghai"),
        new UsageRegion("YN","云南","Yunnan"),new UsageRegion("GZ","贵州","Guizhou"),
        new UsageRegion("HN","湖南","Hunan"),new UsageRegion("JX","江西","Jiangxi"),
        new UsageRegion("ZJ","浙江","Zhejiang"),new UsageRegion("FJ","福建","Fujian"),
        new UsageRegion("GX","广西","Guangxi"),new UsageRegion("GD","广东","Guangdong"),
        new UsageRegion("TW","台湾","Taiwan"),new UsageRegion("HI","海南","Hainan"),
        new UsageRegion("MO","澳门","Macao"),new UsageRegion("HK","香港","Hong Kong")
    };
    public static string Name(string code,bool english) { var r=All.FirstOrDefault(x=>x.Code==code); return r==null?(english?"Unknown region":"未知地区"):(english?r.English:r.Chinese); }
}
sealed class UsageRegionCount {
    public string Code { get; set; }
    public long Starts { get; set; }
    public long ActiveToday { get; set; }
    public long Active30Days { get; set; }
    public long Value(int metric) { return metric==1?ActiveToday:metric==2?Active30Days:Starts; }
}
sealed class UsageSnapshot {
    public int SchemaVersion { get; set; }
    public bool IsDemo { get; set; }
    public string GeneratedAt { get; set; }
    public string FirstDate { get; set; }
    public string LastDate { get; set; }
    public List<UsageRegionCount> Regions { get; set; }
    public long Total(int metric) { return Regions.Sum(r=>r.Value(metric)); }
    public long Value(string code,int metric) { var r=Regions.FirstOrDefault(x=>x.Code==code); return r==null?0:r.Value(metric); }
    public static UsageSnapshot Parse(string json) {
        if(Encoding.UTF8.GetByteCount(json)>262144) throw new InvalidDataException("Summary exceeds 256 KiB.");
        var raw=Json.Map(Json.Parse(json)); var rows=Json.Field(raw,"Regions") as object[];
        if(rows==null || rows.Length>35 || !(Json.Field(raw,"IsDemo") is bool)) throw new InvalidDataException("Invalid summary fields.");
        foreach(var item in rows) {
            var row=Json.Map(item);
            foreach(string field in new[]{"Starts","ActiveToday","Active30Days"}) {
                object number=Json.Field(row,field);
                if(!(number is int) && !(number is long)) throw new InvalidDataException("Counts must be JSON integers.");
            }
        }
        var v=Json.Decode<UsageSnapshot>(json); DateTime first,last; DateTimeOffset generated;
        if(v==null || v.SchemaVersion!=1 || v.Regions==null || v.Regions.Count>35 ||
            !DateTime.TryParseExact(v.FirstDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first) ||
            !DateTime.TryParseExact(v.LastDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last) || first>last ||
            string.IsNullOrEmpty(v.GeneratedAt) || !(v.GeneratedAt.EndsWith("Z") || System.Text.RegularExpressions.Regex.IsMatch(v.GeneratedAt,@"[+-]\d{2}:\d{2}$")) ||
            !DateTimeOffset.TryParse(v.GeneratedAt,CultureInfo.InvariantCulture,DateTimeStyles.None,out generated)) throw new InvalidDataException("Invalid summary schema or date window.");
        var codes=new HashSet<string>(StringComparer.Ordinal);
        foreach(var r in v.Regions) {
            if(r==null || r.Code==null || !codes.Add(r.Code) || r.Code!="unknown" && !UsageRegion.All.Any(x=>x.Code==r.Code) ||
                r.Starts<0 || r.ActiveToday<0 || r.Active30Days<0 || r.Starts>1000000000000L || r.ActiveToday>1000000000000L || r.Active30Days>1000000000000L)
                throw new InvalidDataException("Invalid, duplicate or unsupported region count.");
        }
        return v;
    }
    public static UsageSnapshot Demo() {
        var v=new UsageSnapshot { SchemaVersion=1,IsDemo=true,GeneratedAt="2026-10-10T08:00:00Z",FirstDate="2026-09-11",LastDate="2026-10-10",Regions=new List<UsageRegionCount>() };
        for(int i=0;i<UsageRegion.All.Length;i++) {
            string code=UsageRegion.All[i].Code; long starts=80+((i*137+47)%780);
            if(new[]{"GD","SH","ZJ","JS","BJ","SC"}.Contains(code)) starts*=4;
            v.Regions.Add(new UsageRegionCount { Code=code,Starts=starts,ActiveToday=starts/35,Active30Days=starts/5 });
        }
        v.Regions.Add(new UsageRegionCount { Code="unknown",Starts=120,ActiveToday=3,Active30Days=20 }); return v;
    }
}

sealed class UsagePage : Panel {
    readonly string snapshotPath;
    readonly Label title=new Label(),subtitle=new Label(),state=new Label();
    readonly ModernButton demo=new ModernButton(),refresh=new ModernButton(),import=new ModernButton();
    public readonly ModernComboBox Metric=new ModernComboBox();
    public readonly UsageDashboard Dashboard=new UsageDashboard();
    readonly TableLayoutPanel layout=new TableLayoutPanel();
    readonly Panel heading=new Panel(),scroll=new Panel();
    readonly FlowLayoutPanel toolbar=new FlowLayoutPanel();
    ThemePalette palette=ThemePalette.For(true); bool english,preview; string error="";
    UsageSnapshot loaded;
    public bool PreviewMode { get { return preview; } }
    public bool HasData { get { return Dashboard.Snapshot!=null; } }
    public UsagePage(string path) {
        snapshotPath=path; AccessibleName="使用分布"; Padding=new Padding(20,14,20,14);
        if(AppInfo.PublicRelease) {
            scroll.Dock=DockStyle.Fill; scroll.AutoScroll=true; Controls.Add(scroll);
            Dashboard.MapOnly=true; Dashboard.Dock=DockStyle.Top; Dashboard.MinimumSize=new Size(850,430); Dashboard.Height=430; Dashboard.TabStop=true; scroll.Controls.Add(Dashboard);
            scroll.SizeChanged+=(s,e)=> { Dashboard.Height=Math.Max(430,scroll.ClientSize.Height); };
            SetAppearance(palette,false); RefreshSnapshot(); return;
        }
        layout.Dock=DockStyle.Fill; layout.ColumnCount=1; layout.RowCount=3;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Controls.Add(layout);
        heading.Dock=DockStyle.Fill; title.Font=new Font("Microsoft YaHei UI",21,FontStyle.Bold); title.AutoSize=false; title.Location=new Point(0,0); title.Size=new Size(440,42);
        subtitle.Location=new Point(0,44); subtitle.AutoSize=false; subtitle.Height=25; subtitle.Width=680;
        state.TextAlign=ContentAlignment.MiddleRight; state.Anchor=AnchorStyles.Top|AnchorStyles.Right; state.Height=32;
        heading.Controls.AddRange(new Control[]{title,subtitle,state}); heading.SizeChanged+=(s,e)=> { state.Bounds=new Rectangle(Math.Max(0,heading.Width-340),7,340,32); subtitle.Width=heading.Width; };
        layout.Controls.Add(heading,0,0);
        toolbar.Dock=DockStyle.Fill; toolbar.WrapContents=false; toolbar.Margin=Padding.Empty; layout.Controls.Add(toolbar,0,1);
        Metric.Width=220; Metric.Height=34; Metric.Margin=new Padding(0,0,14,0); Metric.Items.AddRange(new object[]{"累计启动次数","当日活跃安装数","近30天活跃安装数"}); Metric.SelectedIndex=0;
        Metric.SelectedIndexChanged+=(s,e)=> { Dashboard.Metric=Metric.SelectedIndex; Dashboard.RefreshContext(); };
        toolbar.Controls.Add(Metric);
        foreach(var button in new[]{demo,refresh,import}) { button.Size=new Size(124,34); button.Margin=new Padding(0,0,8,0); toolbar.Controls.Add(button); }
        demo.Click+=(s,e)=>SetPreview(!preview); refresh.Click+=(s,e)=> { preview=false; RefreshSnapshot(); };
        import.Click+=(s,e)=>ImportDialog();
        scroll.Dock=DockStyle.Fill; scroll.AutoScroll=true; scroll.Margin=Padding.Empty; layout.Controls.Add(scroll,0,2);
        Dashboard.Dock=DockStyle.Top; Dashboard.MinimumSize=new Size(850,530); Dashboard.Height=530; Dashboard.TabStop=true; scroll.Controls.Add(Dashboard);
        scroll.SizeChanged+=(s,e)=> { Dashboard.Height=Math.Max(530,scroll.ClientSize.Height); };
        SetAppearance(palette,false); RefreshSnapshot();
    }
    string L(string zh,string en) { return english?en:zh; }
    public void SetAppearance(ThemePalette theme,bool en) {
        palette=theme; english=en; BackColor=layout.BackColor=heading.BackColor=toolbar.BackColor=scroll.BackColor=theme.Background;
        title.ForeColor=theme.Text; subtitle.ForeColor=theme.Muted; state.ForeColor=error.Length>0?theme.Error:theme.Accent;
        title.Text=L("使用分布","Usage overview"); subtitle.Text=L("从地区分布，了解 FilmEcho 的使用情况。","Explore FilmEcho usage across regions.");
        Metric.DisplayText=value=> { string text=Convert.ToString(value); return !english?text:text=="累计启动次数"?"Total launches":text=="当日活跃安装数"?"Active installs · report day":"Active installs · 30 days"; };
        Metric.AccessibleName=L("统计指标","Usage metric"); Metric.EmptyText=L("选择指标","Select a metric"); Metric.SetPalette(theme); Metric.RefreshDisplay();
        demo.Text=preview?L("退出示例","Exit preview"):L("查看示例","Preview layout"); refresh.Text=L("刷新数据","Refresh"); import.Text=L("导入统计","Import summary");
        foreach(var b in new[]{demo,refresh,import}) { b.Palette=theme; b.BackColor=theme.Button; b.ForeColor=theme.Text; b.Invalidate(); }
        Dashboard.Palette=theme; Dashboard.English=english; Dashboard.BackColor=theme.Background;
        Dashboard.ReadFailed=error.Length>0;
        state.Text=error.Length>0?L("读取失败 · 原数据已保留","Load failed · previous data kept"):preview || loaded!=null && loaded.IsDemo?L("示例预览 · 非真实使用量","DEMO · not real usage"):loaded==null?L("尚未接入统计数据","No usage data connected"):L("已导入汇总 · 非实时数据","Imported summary · not live");
        Dashboard.RefreshContext();
    }
    public void SetPreview(bool value) { preview=value; error=""; Dashboard.Snapshot=preview?UsageSnapshot.Demo():loaded; SetAppearance(palette,english); }
    public void RefreshSnapshot() {
        if(preview) return;
        try {
            UsageSnapshot next=null;
            if(File.Exists(snapshotPath)) { if(new FileInfo(snapshotPath).Length>262144) throw new InvalidDataException(); next=UsageSnapshot.Parse(File.ReadAllText(snapshotPath,Encoding.UTF8)); }
            if(AppInfo.PublicRelease && next!=null && next.IsDemo) throw new InvalidDataException("Demo summaries are not shown in the public version.");
            loaded=next; Dashboard.Snapshot=loaded; error="";
        } catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException) { error=ex.GetType().Name; }
        SetAppearance(palette,english);
    }
    public void ImportSummary(string json) {
        var next=UsageSnapshot.Parse(json); // Validate before replacing an existing summary.
        Store.Write(snapshotPath,next); loaded=next; preview=false; error=""; Dashboard.Snapshot=next; SetAppearance(palette,english);
    }
    void ImportDialog() {
        using(var dialog=new OpenFileDialog { Title=L("导入地区统计汇总","Import regional usage summary"),Filter="JSON|*.json",CheckFileExists=true }) {
            if(dialog.ShowDialog(FindForm())!=DialogResult.OK) return;
            try { if(new FileInfo(dialog.FileName).Length>262144) throw new InvalidDataException(); ImportSummary(File.ReadAllText(dialog.FileName,Encoding.UTF8)); }
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException) {
                MessageBox.Show(FindForm(),L("无法导入。请检查文件格式、日期、地区代码和非负整数计数；原数据已保留。","Cannot import. Check the schema, dates, region codes and nonnegative integer counts. Previous data was kept."),L("使用分布","Usage"),MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        }
    }
}

sealed class UsageDashboard : Control {
    public ThemePalette Palette=ThemePalette.For(true); public bool English; public int Metric;
    public bool MapOnly,ReadFailed;
    public UsageSnapshot Snapshot;
    public string SelectedCode { get; private set; } = "GD";
    UsageMapView mapView;
    readonly ToolTip tip=new ToolTip(); string hovered="";
    public UsageDashboard() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true); AccessibleRole=AccessibleRole.Graphic; AccessibleName="地区边界地图"; }
    string L(string zh,string en) { return English?en:zh; }
    string Number(long value) { return value.ToString("N0",CultureInfo.InvariantCulture); }
    string Small(long v) { return v>=1000000?(v/1000000.0).ToString("0.#",CultureInfo.InvariantCulture)+"m":v>=10000?(v/1000.0).ToString("0.#",CultureInfo.InvariantCulture)+"k":Number(v); }
    string MetricName { get { return Metric==1?L("当日活跃安装数","Active installs · report day"):Metric==2?L("近30天活跃安装数","Active installs · 30 days"):L("累计启动次数","Total launches"); } }
    public void SelectRegion(string code) { if(!UsageRegion.All.Any(r=>r.Code==code)) return; SelectedCode=code; AccessibleDescription=UsageRegion.Name(code,English)+": "+(Snapshot==null?L("暂无数据","No data"):Number(Snapshot.Value(code,Metric))); Invalidate(); }
    public void RefreshContext() { hovered=""; tip.SetToolTip(this,""); AccessibleName=L("地区边界地图","Regional boundary map"); SelectRegion(SelectedCode); }
    public Point RegionPoint(string code) { return mapView==null?Point.Empty:Point.Round(mapView.Anchors[code]); }
    public string HitRegion(Point point) { return mapView==null?"":mapView.Hit(point); }
    public UsageMapView MapView { get { return mapView; } }
    void TextAt(Graphics g,string text,Rectangle bounds,float size,Color color,bool bold=false,TextFormatFlags align=TextFormatFlags.Left) {
        using(var f=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular)) TextRenderer.DrawText(g,text,f,bounds,color,align|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics; g.Clear(Palette.Background); g.SmoothingMode=SmoothingMode.AntiAlias;
        if(MapOnly) {
            var board=new Rectangle(0,0,Width,Height); ModernPaint.Card(g,board,Palette.Surface,Palette.Border,15);
            TextAt(g,L("地区分布","REGIONAL DISTRIBUTION"),new Rectangle(20,12,Width-250,30),12,Palette.Text,true);
            string marker=ReadFailed?L("统计数据读取失败","Could not load usage data"):Snapshot==null?L("尚未接入统计数据","No usage data connected"):L("地区累计启动次数","Launches by region");
            TextAt(g,marker,new Rectangle(Width-250,14,230,26),8.5f,ReadFailed?Palette.Error:Palette.Muted,false,TextFormatFlags.Right);
            TextAt(g,L("省级行政区边界 · 数据：阿里云 DataV","Provincial boundaries · Alibaba Cloud DataV"),new Rectangle(20,43,Width-40,22),8,Palette.Muted);
            DrawGeographicMap(g,board); return;
        }
        int gap=14,rightWidth=Math.Min(300,Width/3),chartHeight=Height-62;
        var chart=new Rectangle(0,0,Width-rightWidth-gap,chartHeight); int sideX=chart.Right+gap;
        Stat(g,new Rectangle(sideX,0,rightWidth,58),L("累计启动次数","Total launches"),Snapshot==null?"—":Number(Snapshot.Total(0)),L("累计事件","Launch events"));
        Stat(g,new Rectangle(sideX,68,rightWidth,58),L("统计日活跃安装数","Active installs"),Snapshot==null?"—":Number(Snapshot.Total(1)),L("按安装标识去重","Report day · distinct installs"));
        Stat(g,new Rectangle(sideX,136,rightWidth,58),L("覆盖地区","Regions reached"),Snapshot==null?"—":Number(Snapshot.Regions.Count(r=>r.Code!="unknown" && r.Value(Metric)>0)),L("当前指标","For selected metric"));
        var ranking=new Rectangle(sideX,204,rightWidth,chartHeight-204);
        ModernPaint.Card(g,chart,Palette.Surface,Palette.Border,15); ModernPaint.Card(g,ranking,Palette.Surface,Palette.Border,15);
        TextAt(g,L("地区分布","REGIONAL DISTRIBUTION"),new Rectangle(chart.X+20,chart.Y+12,chart.Width-180,30),12,Palette.Text,true);
        string source=Snapshot==null?L("省级地区","PROVINCE LEVEL"):(Snapshot.IsDemo?L("示例 · ","DEMO · "):"")+L("未定位 ","Unknown ")+Small(Snapshot.Value("unknown",Metric));
        TextAt(g,source,new Rectangle(chart.Right-188,chart.Y+14,168,26),8.5f,Palette.Accent,true,TextFormatFlags.Right);
        TextAt(g,L("省级行政区边界 · 数据：阿里云 DataV","Provincial boundaries · Alibaba Cloud DataV"),new Rectangle(chart.X+20,chart.Y+43,chart.Width-40,22),8,Palette.Muted);
        DrawGeographicMap(g,chart);
        DrawRanking(g,ranking);
        var footer=new Rectangle(0,Height-48,Width,45);
        if(Snapshot==null) {
            TextAt(g,L("暂无全国统计数据","No national usage data yet"),new Rectangle(0,footer.Y,Width,21),9,Palette.Text,true);
            TextAt(g,L("可查看示例布局；接入汇总后显示实际数据。当前页面不上传任何使用信息。","Preview the layout or import a summary. This page sends no usage information."),new Rectangle(0,footer.Y+23,Width,21),8.5f,Palette.Muted);
        } else {
            string window=Metric==1?Snapshot.LastDate:Metric==2?DateTime.ParseExact(Snapshot.LastDate,"yyyy-MM-dd",CultureInfo.InvariantCulture).AddDays(-29).ToString("yyyy-MM-dd")+" – "+Snapshot.LastDate:Snapshot.FirstDate+" – "+Snapshot.LastDate;
            TextAt(g,(Snapshot.IsDemo?L("示例 · ","DEMO · "):L("汇总 · ","SUMMARY · "))+MetricName+"  /  "+window+L("  · 北京时间","  · Asia/Shanghai"),new Rectangle(0,footer.Y,Width,21),9,Palette.Text,true);
            string updated=DateTimeOffset.Parse(Snapshot.GeneratedAt,CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);
            TextAt(g,L("地区按网络出口估算，安装数不等于人数。更新时间：","Regions estimate network exits; installs are not people. Updated: ")+updated,new Rectangle(0,footer.Y+23,Width,21),8.5f,Palette.Muted);
        }
    }
    void Stat(Graphics g,Rectangle r,string label,string value,string note) {
        ModernPaint.Card(g,r,Palette.Surface,Palette.Border,15);
        TextAt(g,label,new Rectangle(r.X+18,r.Y+9,r.Width-142,22),9,Palette.Text,true);
        TextAt(g,note,new Rectangle(r.X+18,r.Y+32,r.Width-34,18),7.5f,Palette.Muted);
        TextAt(g,value,new Rectangle(r.Right-140,r.Y+5,122,29),20,Palette.Accent,true,TextFormatFlags.Right);
    }
    Color RegionFill(string code,long max) {
        if(MapOnly && Snapshot==null) return Palette.Side;
        long count=Snapshot==null?0:Snapshot.Value(code,Metric);
        float strength=max<=0?0:(float)Math.Sqrt((double)count/max);
        return Snapshot==null || count==0?Palette.Field:ModernPaint.Mix(Palette.Selection,Palette.Primary,.2f+.8f*strength);
    }
    void PaintRegions(Graphics g,System.Collections.Generic.Dictionary<string,GraphicsPath> paths,long max) {
        foreach(var pair in paths) {
            if(pair.Value.PointCount==0) continue;
            if(pair.Key!="border") using(var brush=new SolidBrush(RegionFill(pair.Key,max))) g.FillPath(brush,pair.Value);
            using(var pen=new Pen(pair.Key=="border"?Palette.Muted:MapOnly?Palette.Border:Palette.Surface, pair.Key=="border"?1f:.8f)) g.DrawPath(pen,pair.Value);
        }
        foreach(string code in new[]{hovered,SelectedCode}.Where(c=>c.Length>0).Distinct()) {
            GraphicsPath path; if(!paths.TryGetValue(code,out path) || path.PointCount==0) continue;
            using(var pen=new Pen(Palette.Accent,code==SelectedCode?2f:1.3f)) g.DrawPath(pen,path);
        }
    }
    void DrawGeographicMap(Graphics g,Rectangle chart) {
        var area=new Rectangle(chart.X+16,chart.Y+70,chart.Width-32,chart.Height-110);
        if(mapView==null || mapView.Area!=area) { if(mapView!=null) mapView.Dispose(); mapView=new UsageMapView(area); }
        long max=Snapshot==null?0:UsageRegion.All.Max(r=>Snapshot.Value(r.Code,Metric));
        PaintRegions(g,mapView.Main,max); mapView.Labels.Clear();
        foreach(var region in UsageRegion.All) {
            var anchor=mapView.Anchors[region.Code]; Point offset=Point.Empty;
            switch(region.Code) { case "BJ": offset=new Point(27,-22); break; case "TJ": offset=new Point(39,1); break; case "SH": offset=new Point(32,0); break; case "HK": offset=new Point(27,12); break; case "MO": offset=new Point(-20,16); break; case "HI": offset=new Point(-28,3); break; }
            int labelWidth=English?27:region.Chinese.Length*13+8;
            var label=new Rectangle((int)anchor.X+offset.X-labelWidth/2,(int)anchor.Y+offset.Y-9,labelWidth,18);
            Color fill=RegionFill(region.Code,max),text=Snapshot==null || Snapshot.Value(region.Code,Metric)==0?Palette.Muted:fill.GetBrightness()<.53?Palette.PrimaryText:Palette.Text;
            if(offset!=Point.Empty) {
                using(var pen=new Pen(Palette.Muted,.8f)) g.DrawLine(pen,anchor,new PointF(label.X+label.Width/2,label.Y+label.Height/2));
                ModernPaint.Card(g,label,Palette.Surface,region.Code==SelectedCode?Palette.Accent:Palette.Border,4); text=Palette.Text;
            }
            mapView.Labels[region.Code]=label;
            TextAt(g,English?region.Code:region.Chinese,label,8,text,region.Code==SelectedCode,TextFormatFlags.HorizontalCenter);
        }
        ModernPaint.Card(g,mapView.SeaFrame,Palette.Surface,Palette.Border,5);
        TextAt(g,L("南海诸岛","South China Sea"),new Rectangle(mapView.SeaFrame.X+2,mapView.SeaFrame.Y+2,mapView.SeaFrame.Width-4,18),7,Palette.Muted,false,TextFormatFlags.HorizontalCenter);
        var saved=g.Save(); g.SetClip(mapView.SeaArea); PaintRegions(g,mapView.Sea,max); g.Restore(saved);
        int legendY=chart.Bottom-29;
        if(MapOnly && Snapshot==null) {
            TextAt(g,L("点击选择地区 · 悬停查看信息","Select a region · hover for information"),new Rectangle(chart.X+20,legendY,chart.Width-40,20),8,Palette.Muted); return;
        }
        TextAt(g,Snapshot==null?L("暂无数据","No data"):L("较少","Lower"),new Rectangle(chart.X+20,legendY,80,20),8,Palette.Muted);
        for(int i=0;i<5;i++) ModernPaint.Card(g,new Rectangle(chart.X+102+i*22,legendY+5,18,10),Snapshot==null?Palette.Field:ModernPaint.Mix(Palette.Selection,Palette.Primary,i/4f),Palette.Border,3);
        TextAt(g,L("较多","Higher"),new Rectangle(chart.X+220,legendY,65,20),8,Palette.Muted);
        TextAt(g,MapOnly?L("点击选择 · 悬停查看计数","Select · hover for counts"):L("点击地区查看详情","Click a region for details"),new Rectangle(chart.Right-210,legendY,190,20),8,Palette.Muted,false,TextFormatFlags.Right);
    }
    void DrawRanking(Graphics g,Rectangle panel) {
        TextAt(g,L("地区排行","REGION RANKING"),new Rectangle(panel.X+20,panel.Y+12,panel.Width-40,30),12,Palette.Text,true);
        TextAt(g,MetricName,new Rectangle(panel.X+20,panel.Y+43,panel.Width-40,22),8.5f,Palette.Muted);
        if(Snapshot==null || Snapshot.Total(Metric)==0) {
            TextAt(g,Snapshot==null?L("等待统计数据","Awaiting usage data"):L("此指标暂无活动","No activity for this metric"),new Rectangle(panel.X+20,panel.Y+95,panel.Width-40,30),12,Palette.Text,true);
            TextAt(g,L("有数据后显示地区与占比","Regions and shares appear here"),new Rectangle(panel.X+20,panel.Y+129,panel.Width-40,24),8.5f,Palette.Muted);
        } else {
            var list=Snapshot.Regions.Where(r=>r.Value(Metric)>0).OrderByDescending(r=>r.Value(Metric)).ThenBy(r=>r.Code,StringComparer.Ordinal).Take(5).ToList();
            for(int i=0;i<list.Count;i++) {
                var row=list[i]; int y=panel.Y+67+i*20; double share=(double)row.Value(Metric)/Snapshot.Total(Metric);
                TextAt(g,(i+1).ToString("00")+"  "+UsageRegion.Name(row.Code,English),new Rectangle(panel.X+20,y,panel.Width-130,23),9,Palette.Text,i==0);
                TextAt(g,Number(row.Value(Metric)),new Rectangle(panel.Right-105,y,85,23),9,Palette.Text,true,TextFormatFlags.Right);
                ModernPaint.Card(g,new Rectangle(panel.X+20,y+20,panel.Width-40,2),Palette.Field,Color.Transparent,1);
                ModernPaint.Card(g,new RectangleF(panel.X+20,y+20,(panel.Width-40)*(float)share,2),Palette.Accent,Color.Transparent,1);
            }
        }
        int detailY=panel.Bottom-87;
        using(var pen=new Pen(Palette.Border)) g.DrawLine(pen,panel.X+20,detailY-7,panel.Right-20,detailY-7);
        TextAt(g,L("选中地区","SELECTED REGION"),new Rectangle(panel.X+20,detailY,panel.Width-40,20),8,Palette.Muted);
        TextAt(g,UsageRegion.Name(SelectedCode,English),new Rectangle(panel.X+20,detailY+22,panel.Width-120,29),13,Palette.Text,true);
        TextAt(g,Snapshot==null?"—":Number(Snapshot.Value(SelectedCode,Metric)),new Rectangle(panel.Right-120,detailY+22,100,29),14,Palette.Accent,true,TextFormatFlags.Right);
        TextAt(g,Snapshot==null?L("等待数据","Awaiting data"):(Snapshot.Total(Metric)==0?"0.0%":((double)Snapshot.Value(SelectedCode,Metric)/Snapshot.Total(Metric)).ToString("P1",CultureInfo.InvariantCulture))+L(" · 当前指标占比"," · share of this metric"),new Rectangle(panel.X+20,detailY+54,panel.Width-40,20),8,Palette.Muted);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        string code=HitRegion(e.Location);
        if(hovered!=code) { hovered=code; Cursor=code.Length>0?Cursors.Hand:Cursors.Default; tip.SetToolTip(this,code.Length==0?"":UsageRegion.Name(code,English)+" · "+MetricName+": "+(Snapshot==null?L("暂无数据","No data"):Number(Snapshot.Value(code,Metric)))); Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hovered=""; tip.SetToolTip(this,""); Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left) { Focus(); SelectRegion(HitRegion(e.Location)); } base.OnMouseDown(e); }
    protected override bool ProcessCmdKey(ref Message msg,Keys key) {
        if(key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || key==Keys.Home || key==Keys.End) {
            int index=Array.FindIndex(UsageRegion.All,r=>r.Code==SelectedCode);
            index=key==Keys.Home?0:key==Keys.End?UsageRegion.All.Length-1:(index+(key==Keys.Left || key==Keys.Up?UsageRegion.All.Length-1:1))%UsageRegion.All.Length;
            SelectRegion(UsageRegion.All[index].Code); return true;
        }
        return base.ProcessCmdKey(ref msg,key);
    }
    protected override void Dispose(bool disposing) { if(disposing) { tip.Dispose(); if(mapView!=null) mapView.Dispose(); } base.Dispose(disposing); }
}
}
