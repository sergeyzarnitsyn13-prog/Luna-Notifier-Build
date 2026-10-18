// Luna Notify: per-user background notification preview and local fixed-text Systematic test.
// This program receives only a fixed test signal over 127.0.0.1. No outbound traffic.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfPath = System.Windows.Shapes.Path;

internal sealed class AlertItem { internal string Id, Title, Body, Source; }

internal sealed class LunaApp : IDisposable
{
    private const int Port = 58731;
    private const string TestPath = "/luna/test";
    private const string TrustedOrigin = "https://d5d1foolkgopgtskjhcv.d8epmcec.apigw.yandexcloud.net";
    private readonly Application app;
    private readonly Mutex mutex;
    private readonly LunaMoon moon;
    private readonly Queue<AlertItem> queue = new Queue<AlertItem>();
    private readonly object gate = new object();
    private TcpListener listener;
    private LunaBubble current;
    private bool exiting;
    private long lastTestTicks;
    private DispatcherTimer launchPreviewTimer;
    private bool ownsMutex;

    [STAThread]
    public static int Main(string[] args) {
        bool showPreview = Array.IndexOf(args ?? new string[0], "--preview") >= 0;
        bool acquired;
        var mutex = new Mutex(true, @"Local\RusklimatIM.LunaOwnerTest.v1", out acquired);
        if (!acquired) {
            mutex.Dispose();
            if (showPreview) SendLocalPreview();
            return 0;
        }
        try {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var client = new LunaApp(app, mutex, showPreview);
            try {app.Run();} finally {client.Dispose();}
            return 0;
        } catch (Exception ex) {
            try { File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "luna-owner-test-error.log"), DateTime.Now.ToString("s") + " Startup: " + ex + Environment.NewLine); } catch { }
            mutex.Dispose(); return 1;
        }
    }
    private LunaApp(Application application, Mutex instanceMutex, bool showPreview) {
        app=application;
        mutex=instanceMutex;
        ownsMutex=true;
        moon=new LunaMoon();
        try {
            // TcpListener binds to loopback only, does not need admin, firewall rules or URL ACL.
            listener=new TcpListener(IPAddress.Loopback,Port);
            listener.Start(2);
            var thread=new Thread(AcceptLoop) { IsBackground=true, Name="LunaTestLoopback" };
            thread.Start();
        } catch (Exception ex) {
            if (listener!=null) {try {listener.Stop();} catch {}}
            listener=null;
            Report("Listener",ex);
        }
        // First installation preview only; Windows logon never produces an unsolicited demo.
        if (showPreview) {
            launchPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            launchPreviewTimer.Tick += (s,e) => { launchPreviewTimer.Stop(); ShowTest(); };
            launchPreviewTimer.Start();
        }
    }
    private void ShowTest() {
        if (exiting) return;
        if (queue.Count>=4) return;
        queue.Enqueue(new AlertItem {Id=Guid.NewGuid().ToString("N"),Title="",Body="Тестовое уведомление из Systematic.",Source="Тест"});
        Drain();
    }
    private void Drain() {
        if(exiting || current!=null || queue.Count==0)return;
        try {
            current=new LunaBubble(moon,queue.Dequeue(),()=>{
                current=null;
                app.Dispatcher.BeginInvoke(new Action(Drain));
            });
            current.ShowAnimated();
        } catch(Exception ex) {
            current=null;
            Report("Bubble",ex);
        }
    }
    private void AcceptLoop() {
        while(!exiting && listener!=null) {
            TcpClient client=null;
            try {client=listener.AcceptTcpClient();}
            catch (SocketException) {if (exiting) break; continue;}
            catch (ObjectDisposedException) {break;}
            if (client==null)continue;
            ThreadPool.QueueUserWorkItem(s=>HandleClient((TcpClient)s),client);
        }
    }
    private void HandleClient(TcpClient client) {
        using(client) {
            client.ReceiveTimeout=2200;client.SendTimeout=2200;
            try {
                using(var stream=client.GetStream()) {
                    // Plain-text local HTTP, never exposed on a network interface.
                    using(var reader=new StreamReader(stream,new UTF8Encoding(false),false,1024,true)) {
                        var start=reader.ReadLine();
                        if(start==null || start.Length>160) {Write(stream,400,false,false);return;}
                        var isOptions=start=="OPTIONS /luna/test HTTP/1.1";
                        var isPost=start=="POST /luna/test HTTP/1.1";
                        if(!isOptions && !isPost) {Write(stream,404,false,false);return;}
                        string origin=null,host=null,contentLength=null;
                        int total=0;
                        for(int i=0;i<40;i++) {
                            var line=reader.ReadLine();
                            if(line==null) return;
                            total+=line.Length;
                            if(line.Length>1000 || total>4096) {Write(stream,431,false,false);return;}
                            if(line.Length==0)break;
                            var colon=line.IndexOf(':');
                            if(colon<1)continue;
                            var name=line.Substring(0,colon).Trim().ToLowerInvariant();
                            var value=line.Substring(colon+1).Trim();
                            if(name=="origin")origin=value;
                            if(name=="host")host=value;
                            if(name=="content-length")contentLength=value;
                        }
                        // The browser is not trusted as employee identity in general, this is ONLY
                        // a local, fixed-text visual test. It does not handle tasks or account data.
                        bool valid = origin == TrustedOrigin && host == "127.0.0.1:58731" &&
                                     (contentLength==null || contentLength=="0");
                        if(!valid) {Write(stream,403,false,false);return;}
                        if(isOptions) {Write(stream,204,true,true);return;}
                        bool tooSoon;
                        lock(gate) {
                            var now=DateTime.UtcNow.Ticks;
                            tooSoon=now-lastTestTicks<TimeSpan.FromSeconds(2).Ticks;
                            if(!tooSoon)lastTestTicks=now;
                        }
                        if(tooSoon) {Write(stream,429,true,false);return;}
                        app.Dispatcher.BeginInvoke(new Action(ShowTest));
                        Write(stream,200,true,false);
                    }
                }
            } catch (IOException) {} catch (ObjectDisposedException) {}
            catch (Exception) {} // no secrets or personal data to log
        }
    }
    private static void Write(NetworkStream stream,int code,bool allowed,bool preflight) {
        string status=code==200?"200 OK":code==204?"204 No Content":code==403?"403 Forbidden":
            code==429?"429 Too Many Requests":code==431?"431 Request Header Fields Too Large":"404 Not Found";
        string body=code==204?"":"{\"ok\":"+(code==200?"true":"false")+"}";
        string headers="HTTP/1.1 "+status+"\r\n"+
            "Content-Type: application/json; charset=utf-8\r\n"+
            "Cache-Control: no-store\r\n"+
            "Connection: close\r\n"+
            "Content-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n";
        if(allowed) {
            headers+="Access-Control-Allow-Origin: "+TrustedOrigin+"\r\n"+
                "Access-Control-Allow-Methods: POST, OPTIONS\r\n"+
                "Access-Control-Allow-Headers: Content-Type\r\n"+
                "Access-Control-Allow-Private-Network: true\r\n"+
                "Vary: Origin\r\n";
        }
        headers+="\r\n";
        byte[] bytes=Encoding.UTF8.GetBytes(headers+body);
        stream.Write(bytes,0,bytes.Length);stream.Flush();
    }
    private static void Report(string where,Exception ex) {
        try {
            string dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rusklimat IM", "Luna Notify");
            Directory.CreateDirectory(dir);
            File.AppendAllText(System.IO.Path.Combine(dir,"luna-errors.log"), DateTime.Now.ToString("s")+" "+where+": "+ex+Environment.NewLine);
        } catch { }
    }
    private static void SendLocalPreview() {
        try {
            using(var c = new TcpClient()) {
                c.Connect(IPAddress.Loopback,Port);
                using(var s = c.GetStream()) {
                    var req = "POST "+TestPath+" HTTP/1.1\r\nHost: 127.0.0.1:58731\r\nOrigin: "+TrustedOrigin+"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    byte[] bytes=Encoding.UTF8.GetBytes(req);
                    s.Write(bytes,0,bytes.Length);
                    s.Flush();
                }
            }
        } catch (Exception ex) { Report("Preview",ex); }
    }
    private void Exit() {
        if(exiting)return;
        exiting=true;
        if(launchPreviewTimer!=null)launchPreviewTimer.Stop();
        try {if(listener!=null)listener.Stop();}catch{}
        if(current!=null)current.Close();
        app.Shutdown();
    }
    public void Dispose() {
        if (launchPreviewTimer != null) launchPreviewTimer.Stop();
        if (listener != null) { try { listener.Stop(); } catch {} }
        if (ownsMutex) { try { mutex.ReleaseMutex(); } catch {} ownsMutex=false; }
        mutex.Dispose();
    }
}

internal static class Embed
{
    internal static Stream Get(string name) {
        var value=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if(value==null)throw new InvalidOperationException("Embedded resource missing: "+name);
        return value;
    }
}

// One tray moon, no secondary floating moon and no idle windows.
// When the user clicks the tray icon the origin is exact. Before the first click,
// default to Windows taskbar notification area, with all four taskbar orientations handled.
// Notification anchor is calculated entirely in WPF device-independent pixels.
internal sealed class LunaMoon { internal void Pulse() {} }

// Animation ported from Luna-MoonGlow-v10-PORTABLE-NOBUILD/source/Harness.cs
// DisplaySpeech() and PopScale(). Same WPF keyframes and easing; only tail removed.
internal sealed class LunaBubble
{
    private readonly Window window;
    private readonly Grid root;
    private readonly ScaleTransform scale;
    private readonly TextBlock text;
    private readonly Button dismiss;
    private readonly Action onDismiss;
    private readonly DispatcherTimer timer;
    private bool closing;

    internal LunaBubble(LunaMoon moon, AlertItem item, Action closed) {
        onDismiss=closed;
        using(var sr=new StreamReader(Embed.Get("SpeechBubbleXaml")))
            window=(Window)XamlReader.Parse(sr.ReadToEnd());
        window.MaxHeight=320;
        root=(Grid)window.FindName("BubbleRoot");
        scale=(ScaleTransform)root.RenderTransform;
        text=(TextBlock)window.FindName("TaskText");
        dismiss=(Button)window.FindName("DismissButton");
        text.Text=(item.Title.Length>0 ? item.Title+"\n" : "")+item.Body;
        dismiss.Click+=(s,e)=>Close();
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};
        timer.Tick+=(s,e)=>Close();
        window.Closed+=(s,e)=>{timer.Stop();onDismiss();};
        window.Loaded+=(s,e)=>Position();
    }
    private void Position() {
        // Keep DPI-safe WPF working-area positioning from verified Luna Notify.
        window.UpdateLayout();
        var area=SystemParameters.WorkArea;
        double ww=window.Width;
        double wh=Math.Max(80,window.ActualHeight>1?window.ActualHeight:140);
        const double margin=18;
        window.Left=Math.Max(area.Left+margin,area.Right-ww-margin);
        window.Top=Math.Max(area.Top+margin,area.Bottom-wh-margin);
        double rw=Math.Max(80,ww-32),rh=Math.Max(70,wh-32);
        var geometry=new RectangleGeometry(new Rect(0,0,rw,rh),20,20);
        geometry.Freeze(); // no tail, only original rounded body
        ((WpfPath)window.FindName("BubbleOutline")).Data=geometry;
    }

    // EXACT original v10 keyframes: 8% -> 101.8% (280ms) -> 100% (420ms).
    private static void PopScale(ScaleTransform target,DependencyProperty property) {
        var animation=new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.08,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.018,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
            new CubicEase{EasingMode=EasingMode.EaseOut}));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)),
            new CubicEase{EasingMode=EasingMode.EaseInOut}));
        target.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private static void Fade(UIElement target,double to,int durationMs,Action completed=null) {
        var animation=new DoubleAnimation(target.Opacity,to,TimeSpan.FromMilliseconds(durationMs)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}
        };
        if(completed!=null)animation.Completed+=(s,e)=>completed();
        target.BeginAnimation(UIElement.OpacityProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
    private static void Tween(Animatable target,DependencyProperty property,double from,double to,int durationMs) {
        var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(durationMs)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}
        };
        target.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);
    }
    internal void ShowAnimated() {
        closing=false;
        root.Opacity=0;
        scale.ScaleX=.08;
        scale.ScaleY=.08;
        window.Show();
        // EXACT original v10 DisplaySpeech() invocation.
        PopScale(scale,ScaleTransform.ScaleXProperty);
        PopScale(scale,ScaleTransform.ScaleYProperty);
        Fade(root,1,160);
        timer.Start();
    }
    internal void Close() {
        if(closing)return;
        closing=true;
        timer.Stop();
        // EXACT v10 CloseBubble() motion; no new visual effects.
        Tween(scale,ScaleTransform.ScaleXProperty,scale.ScaleX,.82,150);
        Tween(scale,ScaleTransform.ScaleYProperty,scale.ScaleY,.82,150);
        Fade(root,0,150,()=>window.Close());
    }
}
