// Luna Notify 0.9, opt-in local Systematic bridge. No outbound traffic or Firestore access.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Threading;

internal sealed partial class LunaApp
{
    private const int BridgeMaxBytes=262144;
    private const string ProductionOrigin="https://d5d8gka87mlca0b3tv6e.6brbn2wz.apigw.yandexcloud.net";
    private sealed class LocalTodo {internal string Id,Text,Client; internal DateTimeOffset At;}
    private readonly Dictionary<string,LocalTodo> localTasks=new Dictionary<string,LocalTodo>(StringComparer.Ordinal);
    private readonly HashSet<string> notifiedTasks=new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> notifiedMail=new HashSet<string>(StringComparer.Ordinal);
    private string lockedEmployee="";
    private bool suspended,hadSnapshot;
    private DispatcherTimer localTaskTimer;

    private void InitializeLocalBridge() {
        localTaskTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};
        localTaskTimer.Tick+=(s,e)=>CheckDueTasks();
        localTaskTimer.Start();
    }
    private static string Safe(object v,int max) {
        string str=Convert.ToString(v,CultureInfo.InvariantCulture)??"";
        var output=new StringBuilder(Math.Min(str.Length,max));
        foreach(char c in str) { if(output.Length>=max)break;
            if(!char.IsControl(c))output.Append(c);
            else if(char.IsWhiteSpace(c))output.Append(' ');
        }
        return output.ToString().Trim();
    }
    private static string Read(Dictionary<string,object> data,string field,int max) {
        object v;return data!=null && data.TryGetValue(field,out v)?Safe(v,max):"";
    }
    private static string ReadHeaders(Stream input) {
        var bytes=new MemoryStream();int tail=0;
        while(bytes.Length<8192) {
            int next=input.ReadByte();if(next<0)throw new IOException("Closed request");
            bytes.WriteByte((byte)next);tail=(tail<<8)|next;
            if(tail==0x0d0a0d0a)return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new IOException("Header size limit");
    }
    private static byte[] ReadBody(Stream input,int length) {
        byte[] bytes=new byte[length];int total=0;
        while(total<length) {
            int received=input.Read(bytes,total,length-total);
            if(received<=0)throw new IOException("Short request");
            total+=received;
        }
        return bytes;
    }
    private static void Reply(NetworkStream stream,int status,string json,string origin,bool allowed) {
        string phrase=status==200?"OK":status==204?"No Content":status==400?"Bad Request":
            status==403?"Forbidden":status==404?"Not Found":status==405?"Method Not Allowed":
            status==409?"Conflict":status==413?"Payload Too Large":status==429?"Too Many Requests":"Error";
        if(json=="")json=status==204?"":"{\"ok\":false}";
        string h="HTTP/1.1 "+status+" "+phrase+"\r\nContent-Type: application/json; charset=utf-8\r\n"+
            "Cache-Control: no-store\r\nConnection: close\r\n"+
            "Content-Length: "+Encoding.UTF8.GetByteCount(json)+"\r\n";
        if(allowed)h+="Access-Control-Allow-Origin: "+origin+"\r\n"+
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"+
            "Access-Control-Allow-Headers: Content-Type\r\n"+
            "Access-Control-Allow-Private-Network: true\r\nVary: Origin\r\n";
        byte[] b=Encoding.UTF8.GetBytes(h+"\r\n"+json);
        stream.Write(b,0,b.Length);stream.Flush();
    }
    private void HandleBridgeRequest(TcpClient client) {
        using(client) {
            client.ReceiveTimeout=2200;client.SendTimeout=2200;
            try {
                using(var stream=client.GetStream()) {
                    string[] lines=ReadHeaders(stream).Split(new[]{"\r\n"},StringSplitOptions.None);
                    string[] first=lines[0].Split(' ');
                    if(first.Length!=3||first[2]!="HTTP/1.1"){Reply(stream,400,"","",false);return;}
                    string method=first[0],path=first[1],origin="",host="",type="";
                    int size=0;bool foundSize=false;
                    for(int i=1;i<lines.Length;i++) {
                        string line=lines[i];if(line=="")break;
                        int sep=line.IndexOf(':');if(sep<1){Reply(stream,400,"","",false);return;}
                        string name=line.Substring(0,sep).Trim().ToLowerInvariant();
                        string value=line.Substring(sep+1).Trim();
                        if(name=="origin"){if(origin!=""){Reply(stream,400,"","",false);return;}origin=value;}
                        if(name=="host")host=value;
                        if(name=="content-type")type=value;
                        if(name=="content-length") {
                            if(foundSize||!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out size)||size<0||size>BridgeMaxBytes)
                            {Reply(stream,413,"","",false);return;}foundSize=true;
                        }
                        if(name=="transfer-encoding"){Reply(stream,400,"","",false);return;}
                    }
                    bool allowed=(origin==TrustedOrigin||origin==ProductionOrigin) && host=="127.0.0.1:58731";
                    if(!allowed){Reply(stream,403,"","",false);return;}
                    bool known=path==TestPath||path=="/v1/state"||path=="/v1/mail-notification"||
                        path=="/v1/health"||path=="/v1/pending-todos"||path=="/v1/todo-actions";
                    if(!known){Reply(stream,404,"",origin,true);return;}
                    if(method=="OPTIONS"){Reply(stream,204,"",origin,true);return;}
                    if(method=="GET") {
                        string result=path=="/v1/health"?"{\"ok\":true,\"version\":\"0.9\"}":
                            path=="/v1/pending-todos"?"{\"todos\":[]}":
                            path=="/v1/todo-actions"?"{\"actions\":[]}":"";
                        if(result!=""){Reply(stream,200,result,origin,true);return;}
                    }
                    if(method!="POST"){Reply(stream,405,"",origin,true);return;}
                    if(path==TestPath&&size==0) {
                        bool tooSoon;lock(gate) {
                            long now=DateTime.UtcNow.Ticks;tooSoon=now-lastTestTicks<TimeSpan.FromSeconds(2).Ticks;
                            if(!tooSoon)lastTestTicks=now;
                        }
                        if(tooSoon){Reply(stream,429,"",origin,true);return;}
                        app.Dispatcher.BeginInvoke(new Action(ShowTest));
                        Reply(stream,200,"{\"ok\":true}",origin,true);return;
                    }
                    if(path!="/v1/state"&&path!="/v1/mail-notification")
                    {Reply(stream,405,"",origin,true);return;}
                    if(!foundSize||size==0||!type.StartsWith("application/json",StringComparison.OrdinalIgnoreCase))
                    {Reply(stream,400,"",origin,true);return;}
                    string json=new UTF8Encoding(false,true).GetString(ReadBody(stream,size));
                    var parser=new JavaScriptSerializer{MaxJsonLength=BridgeMaxBytes};
                    var data=parser.DeserializeObject(json) as Dictionary<string,object>;
                    if(data==null){Reply(stream,400,"",origin,true);return;}
                    string employee=Read(data,"employeeName",150);
                    if(employee==""){Reply(stream,400,"",origin,true);return;}
                    bool match;lock(gate){
                        if(lockedEmployee=="")lockedEmployee=employee;
                        match=lockedEmployee==employee;suspended=!match;
                    }
                    if(!match) {
                        app.Dispatcher.BeginInvoke(new Action(ClearLocalTasks));
                        Reply(stream,409,"{\"ok\":false,\"reason\":\"profile_mismatch\"}",origin,true);return;
                    }
                    if(path=="/v1/state") {
                        var items=new Dictionary<string,LocalTodo>(StringComparer.Ordinal);
                        object raw;object[] rows=data.TryGetValue("todos",out raw)?raw as object[]:null;
                        if(rows!=null)foreach(object row in rows) {
                            if(items.Count>=120)break;
                            var obj=row as Dictionary<string,object>;if(obj==null)continue;
                            string id=Read(obj,"id",150),date=Read(obj,"remindAt",80);
                            DateTimeOffset when;
                            if(id==""||!DateTimeOffset.TryParse(date,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out when))continue;
                            items[id]=new LocalTodo{Id=id,At=when,Text=Read(obj,"actionText",190),Client=Read(obj,"clientName",80)};
                        }
                        app.Dispatcher.BeginInvoke(new Action(()=>AcceptLocalTasks(items)));
                    } else {
                        string slot=Read(data,"slotLabel",110),key=Read(data,"storageKey",170);
                        if(slot==""){Reply(stream,400,"",origin,true);return;}
                        app.Dispatcher.BeginInvoke(new Action(()=>{
                            string k=key!=""?key:employee+"|"+slot+"|"+DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                            if(notifiedMail.Add(k))AddLocalBubble("График почты","Ваше время работы с почтой: "+slot);
                        }));
                    }
                    Reply(stream,200,"{\"ok\":true}",origin,true);
                }
            } catch(Exception ex) {
                // Privacy: never write task bodies, names, authorization headers or JSON into logs.
                Report("LocalBridge",new IOException(ex.GetType().Name));
            }
        }
    }
    private void ClearLocalTasks() {localTasks.Clear();hadSnapshot=false;}
    private void AddLocalBubble(string title,string body) {
        if(exiting||queue.Count>=4)return;
        queue.Enqueue(new AlertItem{Id=Guid.NewGuid().ToString("N"),Title=title,Body=body,Source="Systematic"});
        Drain();
    }
    private static string Text(LocalTodo t) {
        string body=t.Text!=""?t.Text:"Проверьте задачу в Systematic.";
        return t.Client!=""?t.Client+": "+body:body;
    }
    private void AcceptLocalTasks(Dictionary<string,LocalTodo> rows) {
        if(suspended)return;
        bool initial=!hadSnapshot;hadSnapshot=true;
        var prior=new HashSet<string>(localTasks.Keys,StringComparer.Ordinal);
        localTasks.Clear();foreach(var x in rows)localTasks[x.Key]=x.Value;
        var now=DateTimeOffset.UtcNow;int shown=0;
        foreach(var t in rows.Values) {
            string dueKey=t.Id+"|"+t.At.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
            if(!notifiedTasks.Contains(dueKey)&&t.At<=now) {
                notifiedTasks.Add(dueKey);
                if(shown++<2)AddLocalBubble("Напоминание",Text(t));
            } else if(!initial && !prior.Contains(t.Id)) {
                if(notifiedTasks.Add("new|"+t.Id)&&shown++<2)AddLocalBubble("Новая задача",Text(t));
            }
        }
        if(notifiedTasks.Count>2500)notifiedTasks.Clear();
    }
    private void CheckDueTasks() {
        if(exiting||suspended||!hadSnapshot)return;
        var now=DateTimeOffset.UtcNow;int sent=0;
        foreach(var t in localTasks.Values) {
            if(t.At>now)continue;
            string key=t.Id+"|"+t.At.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
            if(!notifiedTasks.Add(key))continue;
            AddLocalBubble("Напоминание",Text(t));if(++sent==2)break;
        }
    }
}
