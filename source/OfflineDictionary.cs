using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
namespace FilmEcho {
public sealed class OfflineEntry {
    public string Word,Phonetic,Chinese,English,Exchange;
}
public sealed class OfflineDictionary {
    readonly Dictionary<string,OfflineEntry> entries=new Dictionary<string,OfflineEntry>(StringComparer.OrdinalIgnoreCase);
    readonly Task loading;
    public int Count { get { return entries.Count; } }
    public OfflineDictionary(string path) { loading=Task.Run(()=>Load(path)); }
    void Load(string path) {
        using(var file=File.OpenRead(path)) using(var gzip=new GZipStream(file,CompressionMode.Decompress)) using(var reader=new StreamReader(gzip,Encoding.UTF8)) {
            string line;
            while((line=reader.ReadLine())!=null) { var p=line.Split('\t'); if(p.Length<5) continue; entries[p[0]]=new OfflineEntry { Word=p[0],Phonetic=p[1],Chinese=p[2].Replace("\\n","\r\n"),English=p[3].Replace("\\n","\r\n"),Exchange=p[4] }; }
        }
    }
    public async Task<OfflineEntry> Find(string word) {
        await loading; OfflineEntry entry;
        string key=word.Trim().Replace('’','\'');
        if(entries.TryGetValue(key,out entry)) return entry;
        return null;
    }
}
}
