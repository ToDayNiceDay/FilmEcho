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
    async Task VerifyUsage() {
        int failed=0; var report=new StringBuilder();
        Action<bool,string> check=(ok,name)=> { if(!ok) failed++; report.AppendLine((ok?"PASS: ":"FAIL: ")+name); };
        try { await VerifyUsagePage(check); }
        catch(Exception ex) { check(false,"使用分布验证异常："+ex); }
        finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(root,"verification","usage-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
    }
    async Task VerifyUsagePage(Action<bool,string> check) {
        check(workspaceTabs.TabPages.Count==2 && workspaceTabs.SelectedIndex==0 && video.Visible,"顶层增加学习与使用分布分页，默认仍显示学习界面");
        await Seek(4.5); await mpv.WaitForVideo();
        double before=Json.Num(await mpv.Get("time-pos")); bool oldPause=Convert.ToBoolean(await mpv.Get("pause"));
        query.Text="usage draft"; note.Text="未保存的笔记"; sentence.Text="Unsaved sentence draft";
        string stored=Json.Encode(store.LoadMovie(movie.MoviePath));
        TestMouse(workspaceTabs,"OnMouseDown",240,17); await Task.Delay(100);
        check(workspaceTabs.SelectedIndex==1 && usagePage.Visible && !video.Visible,"点击顶层页签打开独立全宽使用分布页");
        check(!usagePage.HasData && !usagePage.PreviewMode,"没有统计服务或文件时显示无数据，未伪造零值或用户量");
        ApplyTheme("light",false); ApplyLanguage("zh",false); SaveUsagePreview("interface-usage-empty.png");
        usagePage.SetPreview(true); usagePage.Dashboard.Refresh();
        check(usagePage.HasData && usagePage.PreviewMode && usagePage.Dashboard.Snapshot.IsDemo && usagePage.Dashboard.Snapshot.Total(0)>0,"示例预览明确标记为示例，且提供可交互的地区数据");
        check(!File.Exists(Path.Combine(store.Root,"usage-summary.json")),"示例预览只在内存中生成，不写入实际统计汇总");
        usagePage.Metric.SelectedIndex=2;
        check(usagePage.Dashboard.Metric==2 && usagePage.Dashboard.Snapshot.Total(2)!=usagePage.Dashboard.Snapshot.Total(0),"切换统计指标使用独立计数，近30天活跃不复用启动次数");
        var geo=UsageGeography.Instance; var view=usagePage.Dashboard.MapView;
        check(geo.Regions.Count(r=>r.Code!="border")==34 && geo.VertexCount>20000 && view.Main.Values.All(p=>p.PointCount>0),"内嵌真实边界完整载入34个省级地区与南海边界，保留两万以上原始顶点");
        var islands=geo.Regions.First(r=>r.Code=="HI").SeaRings;
        // GDI+ closes AddPolygon itself and omits each source ring's repeated last point.
        check(islands.Count==133 && view.Sea["HI"].PointCount==islands.Sum(r=>r.Length-(r[0]==r[r.Length-1]?1:0)) && view.Sea["border"].PointCount>0,"南海附图完整保留海南133组岛屿轮廓及边界数据");
        check(view.Anchors.All(p=>view.Main[p.Key].IsVisible(p.Value)),"全部地区标签锚点位于对应实际区域内部");
        bool outside=false; var xinjiang=view.Main["XJ"]; var bounds=xinjiang.GetBounds();
        for(int row=1;row<20 && !outside;row++) for(int col=1;col<20;col++) {
            var point=Point.Round(new PointF(bounds.X+bounds.Width*col/20,bounds.Y+bounds.Height*row/20));
            if(!xinjiang.IsVisible(point) && !view.Labels["XJ"].Contains(point) && usagePage.Dashboard.HitRegion(point)!="XJ") { outside=true; break; }
        }
        check(outside && usagePage.Dashboard.HitRegion(new Point(1,1))=="","地图按真实多边形命中，区域外矩形空隙及概览卡片不误选地区");
        Point region=usagePage.Dashboard.RegionPoint("BJ");
        TestMouse(usagePage.Dashboard,"OnMouseDown",region.X,region.Y);
        check(region!=Point.Empty && usagePage.Dashboard.SelectedCode=="BJ","点击实际北京区域更新地区详情");
        var callout=view.Labels["SH"]; var calloutPoint=new Point(callout.X+callout.Width/2,callout.Y+callout.Height/2);
        TestMouse(usagePage.Dashboard,"OnMouseDown",calloutPoint.X,calloutPoint.Y);
        check(usagePage.Dashboard.SelectedCode=="SH","小区域引出标签可准确选中上海");
        TestKey(usagePage.Dashboard,Keys.Right);
        check(usagePage.Dashboard.SelectedCode!="BJ" && Math.Abs(Json.Num(await mpv.Get("time-pos"))-before)<.02,"分布图键盘导航改变地区，不触发电影跳转");
        usagePage.Metric.SelectedIndex=0; usagePage.Dashboard.SelectRegion("GD");
        ApplyTheme("light",false); ApplyLanguage("zh",false); SaveUsagePreview("interface-usage-light.png");
        ApplyTheme("dark",false); ApplyLanguage("zh",false); SaveUsagePreview("interface-usage-dark.png");
        check(usagePage.Dashboard.Palette==theme && !usagePage.Dashboard.English && workspaceTabs.TabPages[1].Text=="使用分布","使用分布的图形、按钮、页签跟随深色主题和中文");
        ApplyTheme("light",false); ApplyLanguage("en",false); SaveUsagePreview("interface-usage-light-english.png");
        check(usagePage.Dashboard.English && usagePage.Metric.GetItemText(usagePage.Metric.SelectedItem)=="Total launches" && workspaceTabs.TabPages[1].Text=="Usage","使用分布的指标、地区图和顶层页签支持英文切换");
        var previous=Size; Size=new Size(1100,730); await Task.Delay(100);
        check(ChildrenFit(usagePage.Metric.Parent) && usagePage.Dashboard.Width>=850 && usagePage.Dashboard.Height>=530 && !((ScrollableControl)usagePage.Dashboard.Parent).HorizontalScroll.Visible,"最小窗口工具行完整，长内容可纵向滚动且无多余横向滚动");
        SaveUsagePreview("interface-usage-small.png"); Size=previous; await Task.Delay(100);
        usagePage.SetPreview(false);
        check(!usagePage.HasData && !usagePage.PreviewMode,"退出示例恢复真实数据来源；没有汇总时恢复空状态");
        string folder=Path.Combine(root,"verification","usage-fixture"); Directory.CreateDirectory(folder); string file=Path.Combine(folder,"usage-summary.json");
        var sample=UsageSnapshot.Demo(); sample.IsDemo=false; string valid=Json.Encode(sample);
        using(var isolated=new UsagePage(file)) {
            isolated.ImportSummary(valid); var parsed=UsageSnapshot.Parse(File.ReadAllText(file));
            check(isolated.HasData && !isolated.PreviewMode && !parsed.IsDemo && parsed.Total(0)==sample.Total(0),"有效汇总导入、原子保存、重新解析与计数一致");
            string baseline=File.ReadAllText(file); bool rejected=false;
            try { isolated.ImportSummary(valid.Replace("\"Starts\":", "\"Starts\":-")); } catch(Exception) { rejected=true; }
            check(rejected && File.ReadAllText(file)==baseline,"负数统计导入被拒绝，原汇总不被覆盖");
            File.WriteAllText(file,"{broken",Encoding.UTF8); isolated.RefreshSnapshot();
            check(isolated.HasData && isolated.Dashboard.Snapshot.Total(0)==sample.Total(0) && File.ReadAllText(file)=="{broken","刷新遇到损坏文件保留已载入数据与原文件，不影响播放器");
        }
        bool duplicate=false,decimalCount=false,unknown=false,date=false,large=false;
        sample.Regions.Add(sample.Regions[0]); try { UsageSnapshot.Parse(Json.Encode(sample)); } catch(Exception) { duplicate=true; }
        sample.Regions.RemoveAt(sample.Regions.Count-1);
        string integerToken="\"Starts\":"+sample.Regions[0].Starts;
        try { UsageSnapshot.Parse(valid.Replace(integerToken,integerToken+".5")); } catch(Exception) { decimalCount=true; }
        try { UsageSnapshot.Parse(valid.Replace("\"Code\":\"XJ\"","\"Code\":\"invalid\"")); } catch(Exception) { unknown=true; }
        try { UsageSnapshot.Parse(valid.Replace("2026-09-11","2026-99-11")); } catch(Exception) { date=true; }
        try { UsageSnapshot.Parse(new string(' ',262145)); } catch(Exception) { large=true; }
        check(duplicate && decimalCount && unknown && date && large,"拒绝重复地区、小数计数、不支持的地区、无效日期和超大文件");
        check(UsageSnapshot.Parse(valid).Total(1)==sample.Regions.Sum(r=>r.ActiveToday) && sample.Regions.Any(r=>r.Code=="unknown"),"未知地区计入汇总总数，地区计数相加一致");
        ApplyLanguage("zh",false); ApplyTheme("dark",false);
        TestKey(this,Keys.Escape); await Task.Delay(100);
        check(workspaceTabs.SelectedIndex==0 && video.Visible && query.Text=="usage draft" && note.Text=="未保存的笔记" && sentence.Text=="Unsaved sentence draft","Esc 返回学习页，查询、笔记与原句草稿完整保留");
        check(Math.Abs(Json.Num(await mpv.Get("time-pos"))-before)<.02 && Convert.ToBoolean(await mpv.Get("pause"))==oldPause && Json.Encode(store.LoadMovie(movie.MoviePath))==stored,"分页、主题及语言切换保留真实 mpv 暂停位置和已保存的电影记录");
        query.Clear(); note.Clear(); sentenceEdited=false; UpdateSentence();
    }
    void SaveUsagePreview(string name) {
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"verification",name),ImageFormat.Png); }
    }
}
}
