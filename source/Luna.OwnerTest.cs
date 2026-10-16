// Luna Owner-only loopback TEST (Systematic 3.57) — NOT a production delivery client.
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
using Forms = System.Windows.Forms;
using WpfPath = System.Windows.Shapes.Path;

internal sealed class AlertItem { internal string Id, Title, Body, Source; }

internal sealed class LunaApp : IDisposable
{
    private const int Port = 58731;
    private const string TestPath = "/luna/test";
    private const string TrustedOrigin = "https://d5d1foolkgopgtskjhcv.d8epmcec.apigw.yandexcloud.net";
    private readonly Application app;
    private readonly Mutex mutex;
    private readonly Forms.NotifyIcon tray;
    private readonly LunaMoon moon;
    private readonly Queue<AlertItem> queue = new Queue<AlertItem>();
    private readonly object gate = new object();
    private TcpListener listener;
    private LunaBubble current;
    private bool exiting;
    private long lastTestTicks;

    [STAThread]
    public static int Main() {
        bool acquired;
        var mutex = new Mutex(true, @"Local\RusklimatIM.LunaOwnerTest.v1", out acquired);
        if (!acquired) {mutex.Dispose(); return 0;}
        try {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var client = new LunaApp(app, mutex);
            try {app.Run();} finally {client.Dispose();}
            return 0;
        } catch { mutex.Dispose(); return 1; }
    }
    private LunaApp(Application application, Mutex instanceMutex) {
        app=application;
        mutex=instanceMutex;
        moon=new LunaMoon();
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Показать тестовый бабл",null,(s,e)=>ShowTest());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход",null,(s,e)=>Exit());
        var icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        tray=new Forms.NotifyIcon {
            Icon=icon ?? System.Drawing.SystemIcons.Information,
            Text="Luna · Тест Systematic", Visible=true, ContextMenuStrip=menu
        };
        tray.MouseUp+=(s,e)=>moon.SetAnchor(Forms.Cursor.Position);
        tray.DoubleClick+=(s,e)=>{moon.SetAnchor(Forms.Cursor.Position);ShowTest();};
        try {
            // TcpListener binds to loopback only, does not need admin, firewall rules or URL ACL.
            listener=new TcpListener(IPAddress.Loopback,Port);
            listener.Start(2);
            var thread=new Thread(AcceptLoop) { IsBackground=true, Name="LunaTestLoopback" };
            thread.Start();
        } catch (Exception) {
            if (listener!=null) {try {listener.Stop();} catch {}}
            listener=null;
            tray.ShowBalloonTip(4000,"Luna","Порт тестового подключения занят. Локальный бабл доступен из трея.",Forms.ToolTipIcon.Warning);
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
        current=new LunaBubble(moon,queue.Dequeue(),()=>{
            current=null;
            app.Dispatcher.BeginInvoke(new Action(Drain));
        });
        current.ShowAnimated();
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
    private void Exit() {
        if(exiting)return;
        exiting=true;
        try {if(listener!=null)listener.Stop();}catch{}
        tray.Visible=false;
        if(current!=null)current.Close();
        app.Shutdown();
    }
    public void Dispose() {
        try {tray.Dispose();}catch{}
        try {mutex.ReleaseMutex();}catch{}
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
internal sealed class LunaMoon
{
    private bool hasAnchor;
    private System.Drawing.Point anchor;
    internal double Left {get; private set;}
    internal double Top {get; private set;}
    internal double Width {get {return 24;} }
    internal double Height {get {return 24;} }
    internal LunaMoon() { Update(); }
    internal void SetAnchor(System.Drawing.Point position) {
        anchor=position;hasAnchor=true;Update();
    }
    internal void Update() {
        if (hasAnchor) { Left=anchor.X-Width/2;Top=anchor.Y-Height/2;return; }
        var screen=Forms.Screen.PrimaryScreen;
        var w=screen.WorkingArea;var b=screen.Bounds;
        if (w.Bottom < b.Bottom) { Left=w.Right-94;Top=w.Bottom+8; }
        else if (w.Top > b.Top) { Left=w.Right-94;Top=b.Top+9; }
        else if (w.Right < b.Right) { Left=w.Right+8;Top=w.Bottom-65; }
        else if (w.Left > b.Left) { Left=b.Left+8;Top=w.Bottom-65; }
        else { Left=w.Right-94;Top=w.Bottom-14; }
    }
    internal void Pulse() { /* Tray Luna remains the only moon; bubble itself animates from it. */ }
}

internal sealed class LunaBubble
{
    private readonly Window window;
    private readonly Grid root;
    private readonly ScaleTransform scale;
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
        var text=(TextBlock)window.FindName("TaskText");
        text.Text=(item.Title.Length>0 ? item.Title+"\n" : "")+item.Body;
        ((Button)window.FindName("DismissButton")).Click+=(s,e)=>Close();
        window.Closed+=(s,e)=> {timer.Stop();onDismiss();};
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};
        timer.Tick+=(s,e)=>Close();
        window.Loaded+=(s,e)=>Position(moon);
        moon.Pulse();
    }
    private void Position(LunaMoon moon) {
        moon.Update();
        window.UpdateLayout();
        double ww=window.Width;
        double wh=window.ActualHeight>1?window.ActualHeight:220;
        double rw=Math.Max(80,ww-32),rh=Math.Max(80,wh-32);
        double anchorX=moon.Left+moon.Width*.53;
        double anchorY=moon.Top+16;
        var work=SystemParameters.WorkArea;
        window.Left=Math.Max(work.Left+8,Math.Min(work.Right-ww-8,anchorX-ww+72));
        window.Top=Math.Max(work.Top+8,anchorY-rh-18);
        double tip=Math.Min(rw-34,Math.Max(34,anchorX-window.Left-16));
        root.RenderTransformOrigin=new Point(tip/rw,(rh-2)/rh);
        ((WpfPath)window.FindName("BubbleOutline")).Data=Outline(rw,rh,tip);
    }
    private static Geometry Outline(double w,double h,double tip) {
        const double rad=19;
        double bottom=h-12,neck=Math.Min(w-rad-12,Math.Max(rad+12,tip-5));
        var g=new StreamGeometry();
        using(var c=g.Open()) {
            c.BeginFigure(new Point(rad,0),true,true);
            c.LineTo(new Point(w-rad,0),true,false);c.QuadraticBezierTo(new Point(w,0),new Point(w,rad),true,false);
            c.LineTo(new Point(w,bottom-rad),true,false);c.QuadraticBezierTo(new Point(w,bottom),new Point(w-rad,bottom),true,false);
            c.LineTo(new Point(neck+12,bottom),true,false);
            c.BezierTo(new Point(neck+6,bottom),new Point(tip+3,h-3),new Point(tip,h),true,false);
            c.BezierTo(new Point(tip-3,h-3),new Point(neck-6,bottom),new Point(neck-12,bottom),true,false);
            c.LineTo(new Point(rad,bottom),true,false);c.QuadraticBezierTo(new Point(0,bottom),new Point(0,bottom-rad),true,false);
            c.LineTo(new Point(0,rad),true,false);c.QuadraticBezierTo(new Point(0,0),new Point(rad,0),true,false);
        } g.Freeze();return g;
    }
    internal void ShowAnimated() {
        root.Opacity=0;scale.ScaleX=.08;scale.ScaleY=.08;
        window.Show();
        var elastic=new BackEase {EasingMode=EasingMode.EaseOut,Amplitude=.26};
        var ax=new DoubleAnimation(.08,1,TimeSpan.FromMilliseconds(270)){EasingFunction=elastic};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,ax);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,ax.Clone());
        root.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        timer.Start();
    }
    internal void Close() {
        if(closing)return;closing=true;timer.Stop();
        var ax=new DoubleAnimation(1,.08,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseIn}};
        ax.Completed+=(s,e)=>window.Close();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,ax);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,ax.Clone());
        root.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(165)));
    }
}
