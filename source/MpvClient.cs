using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilmEcho {
public sealed class MpvClient : IDisposable {
    public Process Process { get; private set; }
    NamedPipeClientStream pipe;
    StreamWriter writer;
    readonly SemaphoreSlim sendLock=new SemaphoreSlim(1,1);
    readonly ConcurrentDictionary<int,TaskCompletionSource<object>> pending=new ConcurrentDictionary<int,TaskCompletionSource<object>>();
    int sequence;
    int renderedFrames;
    bool disposed;
    public event Action<string,object> PropertyChanged;
    public event Action<string,Dictionary<string,object>> Event;
    public event Action<string> Failed;
    public async Task Start(string executable,IntPtr handle,string log,string input) {
        var name="FilmEcho-"+Guid.NewGuid().ToString("N");
        string args="--no-config --idle=yes --keep-open=yes --hwdec=no --hr-seek=yes --cache=yes --demuxer-max-bytes=128MiB --demuxer-max-back-bytes=64MiB --vo=gpu --gpu-api=d3d11 --osc=no --input-default-bindings=no --input-vo-keyboard=yes --input-cursor=yes --alang=en,eng --slang=en,eng --sub-auto=fuzzy --secondary-sub-visibility=yes --volume=80 --input-ipc-server="+name+" --log-file=\""+log+"\"";
        if(handle!=IntPtr.Zero) args+=" --wid="+handle.ToInt64();
        if(!string.IsNullOrEmpty(input)) args+=" --input-conf=\""+input+"\"";
        Process=System.Diagnostics.Process.Start(new ProcessStartInfo(executable,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable)});
        pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(12000);
        writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
        _=ReadLoop();
    }
    async Task ReadLoop() {
        try {
            using(var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true)) {
                while(!disposed) {
                    string line=await reader.ReadLineAsync(); if(line==null) break;
                    var d=Json.Map(Json.Parse(line));
                    if(d.ContainsKey("request_id")) {
                        TaskCompletionSource<object> t; int id=Convert.ToInt32(d["request_id"]);
                        if(pending.TryRemove(id,out t)) {
                            string error=Json.Str(Json.Field(d,"error"));
                            if(error=="success") t.TrySetResult(Json.Field(d,"data")); else t.TrySetException(new IOException(error));
                        }
                    } else if(d.ContainsKey("event")) {
                        string ev=Json.Str(d["event"]);
                        if(ev=="playback-restart") Interlocked.Increment(ref renderedFrames);
                        if(ev=="property-change") PropertyChanged?.Invoke(Json.Str(Json.Field(d,"name")),Json.Field(d,"data"));
                        Event?.Invoke(ev,d);
                    }
                }
            }
            if(!disposed) Failed?.Invoke("播放内核连接已关闭，请重新启动播放器。");
        } catch(Exception ex) { if(!disposed) Failed?.Invoke("播放内核："+ex.Message); }
        foreach(var pair in pending) { TaskCompletionSource<object> t; if(pending.TryRemove(pair.Key,out t)) t.TrySetException(new IOException("播放内核连接关闭")); }
    }
    public async Task<object> Command(params object[] command) {
        if(disposed || writer==null) throw new IOException("播放内核尚未准备好");
        int id=Interlocked.Increment(ref sequence);
        var t=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id]=t;
        try {
            await sendLock.WaitAsync();
            try { await writer.WriteLineAsync(Json.Encode(new{command=command,request_id=id})); }
            finally { sendLock.Release(); }
            if(await Task.WhenAny(t.Task,Task.Delay(10000))!=t.Task) throw new TimeoutException("播放命令超时");
            return await t.Task;
        } finally { TaskCompletionSource<object> ignored; pending.TryRemove(id,out ignored); }
    }
    public Task<object> Get(string name) { return Command("get_property",name); }
    public Task<object> Set(string name,object value) { return Command("set_property",name,value); }
    public async Task Observe(string name) { await Command("observe_property",Interlocked.Increment(ref sequence),name); }
    public async Task WaitForVideo() {
        for(int i=0;i<160;i++) {
            await Task.Delay(50);
            try {
                bool seeking=Convert.ToBoolean(await Get("seeking"));
                if(!seeking && Volatile.Read(ref renderedFrames)>0 && Json.Num(Json.Field(await Get("video-params"),"w"))>0) { await Task.Delay(40); return; }
            } catch(IOException) { }
        }
        throw new TimeoutException("等待视频帧解码完成超时");
    }
    public void Dispose() {
        disposed=true;
        try { pipe?.Dispose(); } catch { }
        try { if(Process!=null && !Process.HasExited) { Process.Kill(); Process.WaitForExit(2000); } } catch { }
        Process?.Dispose();
    }
}
}
