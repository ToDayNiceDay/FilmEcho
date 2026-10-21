using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FilmEcho {
sealed class GeographicRegion {
    public string Code;
    public readonly List<PointF[]> MainRings=new List<PointF[]>();
    public readonly List<PointF[]> SeaRings=new List<PointF[]>();
    public PointF Label;
}
// The embedded original GeoJSON is retained unchanged. Rendering projects all rings,
// including holes and small islands; the sea inset is a second geographic viewport.
sealed class UsageGeography {
    public readonly List<GeographicRegion> Regions=new List<GeographicRegion>();
    public RectangleF MainBounds;
    public int VertexCount { get; private set; }
    static readonly Dictionary<string,string> Codes=new Dictionary<string,string> {
        {"110000","BJ"},{"120000","TJ"},{"130000","HE"},{"140000","SX"},{"150000","NM"},
        {"210000","LN"},{"220000","JL"},{"230000","HL"},{"310000","SH"},{"320000","JS"},
        {"330000","ZJ"},{"340000","AH"},{"350000","FJ"},{"360000","JX"},{"370000","SD"},
        {"410000","HA"},{"420000","HB"},{"430000","HN"},{"440000","GD"},{"450000","GX"},
        {"460000","HI"},{"500000","CQ"},{"510000","SC"},{"520000","GZ"},{"530000","YN"},
        {"540000","XZ"},{"610000","SN"},{"620000","GS"},{"630000","QH"},{"640000","NX"},
        {"650000","XJ"},{"710000","TW"},{"810000","HK"},{"820000","MO"},{"100000_JD","border"}
    };
    public static readonly UsageGeography Instance=Load();
    // Albers equal-area conic projection, central meridian 105 E, parallels 25/47 N.
    public static PointF Project(double longitude,double latitude) {
        double radians=Math.PI/180,phi1=25*radians,phi2=47*radians;
        double n=(Math.Sin(phi1)+Math.Sin(phi2))/2,c=Math.Pow(Math.Cos(phi1),2)+2*n*Math.Sin(phi1);
        double rho=Math.Sqrt(c-2*n*Math.Sin(latitude*radians))/n,theta=n*(longitude-105)*radians;
        return new PointF((float)(rho*Math.Sin(theta)),(float)(rho*Math.Cos(theta)));
    }
    static PointF[] Ring(object value) {
        return ((object[])value).Select(v=> { var coordinate=(object[])v; return new PointF((float)Json.Num(coordinate[0]),(float)Json.Num(coordinate[1])); }).ToArray();
    }
    static UsageGeography Load() {
        var result=new UsageGeography(); object data;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FilmEcho.ChinaProvinces.geojson")) {
            if(stream==null) throw new InvalidDataException("Embedded provincial boundary data is missing.");
            using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8)) data=Json.Parse(reader.ReadToEnd());
        }
        foreach(var feature in (object[])Json.Field(data,"features")) {
            var properties=Json.Field(feature,"properties"); string code;
            if(!Codes.TryGetValue(Json.Str(Json.Field(properties,"adcode")),out code)) throw new InvalidDataException("Unexpected boundary feature.");
            var region=new GeographicRegion { Code=code };
            var center=Json.Field(properties,"centroid") as object[] ?? Json.Field(properties,"center") as object[];
            if(center!=null) region.Label=Project(Json.Num(center[0]),Json.Num(center[1]));
            var geometry=Json.Field(feature,"geometry"); string type=Json.Str(Json.Field(geometry,"type"));
            var coordinates=(object[])Json.Field(geometry,"coordinates");
            var polygons=type=="Polygon"?new object[]{coordinates}:type=="MultiPolygon"?coordinates:null;
            if(polygons==null) throw new InvalidDataException("Unsupported boundary geometry.");
            foreach(object polygon in polygons) {
                var rings=((object[])polygon).Select(Ring).ToList(); if(rings.Count==0) continue;
                // Separate complete polygons rather than dropping or clipping their islands.
                bool mainland=rings[0].Max(p=>p.Y)>=18;
                foreach(var ring in rings) {
                    if(ring.Length<3) throw new InvalidDataException("Invalid boundary ring.");
                    result.VertexCount+=ring.Length;
                    if(mainland) region.MainRings.Add(ring.Select(p=>Project(p.X,p.Y)).ToArray());
                    if(ring.Max(p=>p.Y)>=3 && ring.Min(p=>p.Y)<=25.5 && ring.Max(p=>p.X)>=105 && ring.Min(p=>p.X)<=125)
                        region.SeaRings.Add(ring);
                }
            }
            result.Regions.Add(region);
        }
        if(result.Regions.Count(r=>r.Code!="border")!=34 || result.Regions.Select(r=>r.Code).Distinct().Count()!=35 || result.VertexCount<20000)
            throw new InvalidDataException("Incomplete provincial boundaries.");
        var points=result.Regions.Where(r=>r.Code!="border").SelectMany(r=>r.MainRings).SelectMany(r=>r).ToArray();
        float left=points.Min(p=>p.X),top=points.Min(p=>p.Y);
        result.MainBounds=new RectangleF(left,top,points.Max(p=>p.X)-left,points.Max(p=>p.Y)-top);
        return result;
    }
}

sealed class UsageMapView : IDisposable {
    public readonly Dictionary<string,GraphicsPath> Main=new Dictionary<string,GraphicsPath>();
    public readonly Dictionary<string,GraphicsPath> Sea=new Dictionary<string,GraphicsPath>();
    public readonly Dictionary<string,PointF> Anchors=new Dictionary<string,PointF>();
    public readonly Dictionary<string,Rectangle> Labels=new Dictionary<string,Rectangle>();
    public Rectangle Area,SeaFrame,SeaArea;
    public UsageMapView(Rectangle area) {
        Area=area; var geo=UsageGeography.Instance;
        float scale=Math.Min((area.Width-32)/geo.MainBounds.Width,(area.Height-16)/geo.MainBounds.Height);
        float x=area.X+(area.Width-geo.MainBounds.Width*scale)/2,y=area.Y+(area.Height-geo.MainBounds.Height*scale)/2;
        Func<PointF,PointF> project=p=>new PointF(x+(p.X-geo.MainBounds.Left)*scale,y+(p.Y-geo.MainBounds.Top)*scale);
        int insetHeight=Math.Min(125,area.Height/2),insetWidth=(int)(insetHeight*.70);
        SeaFrame=new Rectangle(area.Right-insetWidth-3,area.Bottom-insetHeight-2,insetWidth,insetHeight);
        SeaArea=new Rectangle(SeaFrame.X+5,SeaFrame.Y+22,SeaFrame.Width-10,SeaFrame.Height-28);
        // Preserve the same aspect ratio in the inset; clip the viewport, not source data.
        float seaScale=Math.Min(SeaArea.Width/(20f*(float)Math.Cos(14*Math.PI/180)),SeaArea.Height/22.5f);
        Func<PointF,PointF> projectSea=p=>new PointF(SeaArea.X+SeaArea.Width/2+(p.X-115)*(float)Math.Cos(14*Math.PI/180)*seaScale,SeaArea.Y+SeaArea.Height/2+(14.25f-p.Y)*seaScale);
        foreach(var region in geo.Regions) {
            var main=Path(region.MainRings,project); Main.Add(region.Code,main);
            Sea.Add(region.Code,Path(region.SeaRings,projectSea));
            if(region.Code=="border") continue;
            PointF anchor=project(region.Label);
            if(!main.IsVisible(anchor)) {
                var bounds=main.GetBounds(); bool found=false;
                for(int row=1;row<20 && !found;row++) for(int col=1;col<20;col++) {
                    var point=new PointF(bounds.X+bounds.Width*col/20,bounds.Y+bounds.Height*row/20);
                    if(main.IsVisible(point)) { anchor=point; found=true; break; }
                }
            }
            Anchors.Add(region.Code,anchor);
        }
    }
    static GraphicsPath Path(IEnumerable<PointF[]> rings,Func<PointF,PointF> project) {
        var result=new GraphicsPath(FillMode.Alternate);
        foreach(var ring in rings) result.AddPolygon(ring.Select(project).ToArray()); return result;
    }
    public string Hit(Point point) {
        if(SeaFrame.Contains(point)) {
            if(!SeaArea.Contains(point)) return "";
            foreach(var pair in Sea.Where(p=>p.Key!="border").OrderBy(p=>BoundsArea(p.Value))) if(pair.Value.PointCount>0 && pair.Value.IsVisible(point)) return pair.Key;
            return "";
        }
        foreach(var pair in Labels) if(pair.Value.Contains(point)) return pair.Key;
        foreach(var pair in Main.Where(p=>p.Key!="border").OrderBy(p=>BoundsArea(p.Value))) if(pair.Value.PointCount>0 && pair.Value.IsVisible(point)) return pair.Key;
        return "";
    }
    static float BoundsArea(GraphicsPath path) { if(path.PointCount==0) return float.MaxValue; var b=path.GetBounds(); return b.Width*b.Height; }
    public void Dispose() { foreach(var p in Main.Values) p.Dispose(); foreach(var p in Sea.Values) p.Dispose(); }
}
}
