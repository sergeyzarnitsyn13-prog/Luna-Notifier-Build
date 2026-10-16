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

internal sealed class LunaBubble
{
    private readonly Window window;
    private readonly Grid root;
    private readonly ScaleTransform scale;
    private readonly Ellipse ambientGlow;
    private readonly WpfPath halo;
    private readonly TextBlock title;
    private readonly TextBlock text;
    private readonly Button dismiss;
    private readonly Action onDismiss;
    private readonly DispatcherTimer timer;
    private DispatcherTimer settleTimer;
    private bool closing;

    internal LunaBubble(LunaMoon moon, AlertItem item, Action closed) {
        onDismiss=closed;
        using(var sr=new StreamReader(Embed.Get("SpeechBubbleXaml")))
            window=(Window)XamlReader.Parse(sr.ReadToEnd());
        window.MaxHeight=320;
        root=(Grid)window.FindName("BubbleRoot");
        scale=(ScaleTransform)root.RenderTransform;
        ambientGlow=(Ellipse)window.FindName("AmbientGlow");
        halo=(WpfPath)window.FindName("MoonHalo");
        title=(TextBlock)window.FindName("TitleText");
        text=(TextBlock)window.FindName("TaskText");
        dismiss=(Button)window.FindName("DismissButton");
        text.Text=(item.Title.Length>0 ? item.Title+"\n" : "")+item.Body;
        dismiss.Click+=(s,e)=>Close();
        timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(10) };
        timer.Tick+=(s,e)=>Close();
        window.Closed+=(s,e)=>{
            timer.Stop();
            if(settleTimer!=null)settleTimer.Stop();
            onDismiss();
        };
        window.Loaded+=(s,e)=>Position();
        moon.Pulse();
    }

    private void Position() {
        // Position with WPF DIPs only. WinForms pixels break at 125% scaling.
        window.UpdateLayout();
        var work=SystemParameters.WorkArea;
        double ww=window.Width;
        double wh=Math.Max(80,window.ActualHeight>1?window.ActualHeight:140);
        const double gap=18;
        window.Left=Math.Max(work.Left+gap,work.Right-ww-gap);
        window.Top=Math.Max(work.Top+gap,work.Bottom-wh-gap);
        double rw=Math.Max(80,ww-32);
        double rh=Math.Max(70,wh-32);
        ((WpfPath)window.FindName("BubbleOutline")).Data=Outline(rw,rh);
    }

    // Calm continuous rounded contour; no pointed tail without a tray icon.
    private static Geometry Outline(double w,double h) {
        var g=new RectangleGeometry(new Rect(0,0,w,h),20,20);
        g.Freeze();
        return g;
    }

    private static DoubleAnimation Fade(double from,double to,int milliseconds,int delay) {
        return new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds)) {
            BeginTime=TimeSpan.FromMilliseconds(delay),
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},
            FillBehavior=FillBehavior.HoldEnd
        };
    }
    private static void MaterializeText(UIElement element,int delay,int duration) {
        element.Opacity=0;
        element.BeginAnimation(UIElement.OpacityProperty,Fade(0,1,duration,delay));
    }

    internal void ShowAnimated() {
        root.Opacity=0;
        root.Effect=new BlurEffect { Radius=9, RenderingBias=RenderingBias.Performance };
        scale.ScaleX=.94;
        scale.ScaleY=.94;
        ambientGlow.Opacity=0;
        halo.Opacity=.24;
        title.Opacity=0;
        text.Opacity=0;
        dismiss.Opacity=0;
        window.Show();

        // Phase 1: light emerges from empty space, then fades behind the solid card.
        var bloom=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.HoldEnd };
        bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        bloom.KeyFrames.Add(new EasingDoubleKeyFrame(.72,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(135)),
            new CubicEase{EasingMode=EasingMode.EaseOut}));
        bloom.KeyFrames.Add(new EasingDoubleKeyFrame(.20,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)),
            new CubicEase{EasingMode=EasingMode.EaseInOut}));
        bloom.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(570))));
        ambientGlow.BeginAnimation(UIElement.OpacityProperty,bloom);

        // Phase 2: white card condenses from a soft glow (no bounce or flying panel).
        root.BeginAnimation(UIElement.OpacityProperty,Fade(0,1,300,100));
        var settle=new CubicEase{EasingMode=EasingMode.EaseOut};
        var grow=new DoubleAnimation(.94,1,TimeSpan.FromMilliseconds(460)) {
            BeginTime=TimeSpan.FromMilliseconds(85), EasingFunction=settle
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow.Clone());
        ((BlurEffect)root.Effect).BeginAnimation(BlurEffect.RadiusProperty,Fade(9,0,400,100));

        // Phase 3: the content resolves only once the cloud becomes legible.
        MaterializeText(title,180,185);
        MaterializeText(text,260,215);
        MaterializeText(dismiss,345,150);

        // One restrained halo pulse, then a stable resting glow.
        var glow=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.HoldEnd };
        glow.KeyFrames.Add(new LinearDoubleKeyFrame(.24,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(.92,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350)),
            new CubicEase{EasingMode=EasingMode.EaseOut}));
        glow.KeyFrames.Add(new EasingDoubleKeyFrame(.68,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(630)),
            new CubicEase{EasingMode=EasingMode.EaseInOut}));
        halo.BeginAnimation(UIElement.OpacityProperty,glow);

        // Make the resting state deterministic even when WPF skips animation frames.
        settleTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(730) };
        settleTimer.Tick+=(s,e)=>{
            settleTimer.Stop();
            if(closing || !window.IsVisible)return;
            root.BeginAnimation(UIElement.OpacityProperty,null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            ((BlurEffect)root.Effect).BeginAnimation(BlurEffect.RadiusProperty,null);
            ambientGlow.BeginAnimation(UIElement.OpacityProperty,null);
            halo.BeginAnimation(UIElement.OpacityProperty,null);
            foreach(var el in new UIElement[]{title,text,dismiss})
                el.BeginAnimation(UIElement.OpacityProperty,null);
            scale.ScaleX=1;scale.ScaleY=1;root.Opacity=1;
            ((BlurEffect)root.Effect).Radius=0;
            ambientGlow.Opacity=0;halo.Opacity=.68;
            title.Opacity=1;text.Opacity=1;dismiss.Opacity=1;
        };
        settleTimer.Start();
        timer.Start();
    }

    internal void Close() {
        if(closing)return;
        closing=true;
        timer.Stop();
        if(settleTimer!=null)settleTimer.Stop();
        root.BeginAnimation(UIElement.OpacityProperty,null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        var ease=new CubicEase{EasingMode=EasingMode.EaseIn};
        var shrink=new DoubleAnimation(scale.ScaleX,.97,TimeSpan.FromMilliseconds(225)){EasingFunction=ease};
        shrink.Completed+=(s,e)=>window.Close();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,shrink.Clone());
        root.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(root.Opacity,0,TimeSpan.FromMilliseconds(205)){EasingFunction=ease});
    }
}
