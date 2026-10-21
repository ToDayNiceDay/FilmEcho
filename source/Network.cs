using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Win32;
namespace FilmEcho {
static class Network {
    public static readonly string LocalProxy=DetectProxy();
    static string DetectProxy() {
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) {
                string value=Convert.ToString(key?.GetValue("ProxyServer"));
                if(!value.StartsWith("127.0.0.1:") && !value.StartsWith("localhost:")) return "";
                Uri uri; if(!Uri.TryCreate("http://"+value,UriKind.Absolute,out uri)) return "";
                using(var tcp=new TcpClient()) { if(tcp.ConnectAsync(uri.Host,uri.Port).Wait(250) && tcp.Connected) return uri.AbsoluteUri.TrimEnd('/'); }
            }
        } catch { }
        return "";
    }
    public static HttpClient CreateClient() {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        var handler=new HttpClientHandler(); if(LocalProxy.Length>0) { handler.Proxy=new WebProxy(LocalProxy); handler.UseProxy=true; }
        var client=new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(12) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("FilmEcho/1.0"); return client;
    }
}
}
