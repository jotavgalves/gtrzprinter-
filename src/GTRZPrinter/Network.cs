using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Text.Json;

namespace GTRZPrinter
{
    public sealed class DiscoveryService:IDisposable
    {
        readonly AppConfig C;UdpClient U;Thread T;volatile bool Run;
        public DiscoveryService(AppConfig c){C=c;}
        public void Start(){if(Run)return;U=new UdpClient(C.DiscoveryPort);U.EnableBroadcast=true;Run=true;T=new Thread(Loop){IsBackground=true,Name="GTRZ-Discovery"};T.Start();Log.Info("Discovery UDP "+C.DiscoveryPort);}
        void Loop(){while(Run){try{IPEndPoint ep=new IPEndPoint(IPAddress.Any,0);byte[]d=U.Receive(ref ep);if(Encoding.UTF8.GetString(d)=="GTRZ_PRINTER_DISCOVER_V1"){string j="{\"computer\":\""+Environment.MachineName+"\",\"ip\":\""+AppConfig.LocalIp()+"\",\"ipp\":"+C.IppPort+",\"api\":"+C.ApiPort+"}";byte[]b=Encoding.UTF8.GetBytes(j);U.Send(b,b.Length,ep);}}catch{if(!Run)return;}}}
        public static string Discover(int port,int timeout){
            try{using(var u=new UdpClient()){u.EnableBroadcast=true;u.Client.ReceiveTimeout=timeout;byte[]q=Encoding.UTF8.GetBytes("GTRZ_PRINTER_DISCOVER_V1");u.Send(q,q.Length,new IPEndPoint(IPAddress.Broadcast,port));IPEndPoint ep=new IPEndPoint(IPAddress.Any,0);byte[]a=u.Receive(ref ep);var x=JsonSerializer.Deserialize<DiscoveryInfo>(Encoding.UTF8.GetString(a), new JsonSerializerOptions{IncludeFields=true});return x==null?null:x.ip;}}catch{return null;}
        }
        public void Dispose(){Run=false;try{if(U!=null)U.Close();}catch{}try{if(T!=null&&T.IsAlive)T.Join(1000);}catch{}}
    }
    public sealed class DiscoveryInfo{public string computer;public string ip;public int ipp;public int api;}

    public sealed class Heartbeat{public string computer="",version="";public bool printerInstalled;}

    public sealed class ApiServer:IDisposable
    {
        readonly AppConfig C;readonly ClientRegistry Clients;readonly object PrintLock;HttpListener L;Thread T;volatile bool Run;long Req,Ok,Fail;
        readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> Done=new System.Collections.Concurrent.ConcurrentDictionary<string,DateTime>();
        public long Requests{get{return Interlocked.Read(ref Req);}}public long Printed{get{return Interlocked.Read(ref Ok);}}public long Failed{get{return Interlocked.Read(ref Fail);}}
        public ApiServer(AppConfig c,ClientRegistry clients,object printLock){C=c;Clients=clients;PrintLock=printLock;}
        public void Start(){if(Run)return;L=new HttpListener();L.Prefixes.Add("http://+:"+C.ApiPort+"/");L.Start();Run=true;T=new Thread(Loop){IsBackground=true,Name="GTRZ-API"};T.Start();Log.Info("API online "+C.ApiPort);}
        void Loop(){while(Run){try{var x=L.GetContext();ThreadPool.QueueUserWorkItem(_=>Handle(x));}catch{if(!Run)return;}}}
        static string Esc(string s){return(s??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");}
        static byte[] Read(HttpListenerRequest r,int max){using(var m=new MemoryStream()){byte[]b=new byte[8192];int total=0;for(;;){int n=r.InputStream.Read(b,0,b.Length);if(n<=0)break;total+=n;if(total>max)throw new InvalidDataException("Payload excede limite.");m.Write(b,0,n);}return m.ToArray();}}
        static void Json(HttpListenerContext x,int status,string j){byte[]b=Encoding.UTF8.GetBytes(j);x.Response.StatusCode=status;x.Response.ContentType="application/json; charset=utf-8";x.Response.Headers["Access-Control-Allow-Origin"]="*";x.Response.ContentLength64=b.Length;x.Response.OutputStream.Write(b,0,b.Length);x.Response.Close();}
        void Handle(HttpListenerContext x){
            Interlocked.Increment(ref Req);
            try{
                string path=x.Request.Url.AbsolutePath.ToLowerInvariant(),method=x.Request.HttpMethod.ToUpperInvariant(),ip=x.Request.RemoteEndPoint.Address.ToString();
                if(method=="GET"&&(path=="/"||path=="/health")){string pe;bool po=RawPrinter.CanOpen(C.LocalPrinterQueue,out pe);Json(x,200,"{\"ok\":true,\"computer\":\""+Environment.MachineName+"\",\"printerOpen\":"+(po?"true":"false")+",\"printed\":"+Printed+",\"failed\":"+Failed+"}");return;}
                if(method!="POST"){Json(x,405,"{\"ok\":false}");return;}
                if((x.Request.Headers["X-GTRZ-Print-Key"]??"")!=C.ApiKey){Json(x,401,"{\"ok\":false,\"error\":\"chave\"}");return;}
                if(path=="/heartbeat"){
                    var h=JsonSerializer.Deserialize<Heartbeat>(Encoding.UTF8.GetString(Read(x.Request,65536)), new JsonSerializerOptions{IncludeFields=true})??new Heartbeat();
                    Clients.Touch(h.computer,ip,"heartbeat",h.version,h.printerInstalled);Json(x,200,"{\"ok\":true}");return;
                }
                if(path=="/print80"||path=="/print"){
                    string id=x.Request.Headers["X-GTRZ-Job-Id"];if(String.IsNullOrWhiteSpace(id))id=Guid.NewGuid().ToString("N");DateTime when;
                    if(Done.TryGetValue(id,out when)){Json(x,200,"{\"ok\":true,\"duplicate\":true}");return;}
                    string text=Encoding.UTF8.GetString(Read(x.Request,1024*1024));bool cut=C.CutAfterJob;string ch=x.Request.Headers["X-GTRZ-Cut"];if(ch=="0"||ch=="false")cut=false;if(ch=="1"||ch=="true")cut=true;
                    byte[]data=Receipt.TextBytes(text,C,cut);string err;bool ok;lock(PrintLock)ok=RawPrinter.Send(C.LocalPrinterQueue,data,"GTRZ API "+id,out err);Clients.Touch("",ip,"API","",true);
                    if(!ok){Interlocked.Increment(ref Fail);Log.Error("API print "+err);Json(x,500,"{\"ok\":false,\"error\":\""+Esc(err)+"\"}");return;}
                    Done[id]=DateTime.Now;Interlocked.Increment(ref Ok);Log.Info("API print OK "+id+" de "+ip);Json(x,200,"{\"ok\":true,\"printed\":true}");return;
                }
                Json(x,404,"{\"ok\":false}");
            }catch(Exception ex){Interlocked.Increment(ref Fail);Log.Error("API "+ex.Message);try{Json(x,500,"{\"ok\":false,\"error\":\""+Esc(ex.Message)+"\"}");}catch{}}
        }
        public void Dispose(){Run=false;try{if(L!=null)L.Stop();}catch{}try{if(L!=null)L.Close();}catch{}try{if(T!=null&&T.IsAlive)T.Join(1000);}catch{}}
    }

    public sealed class ClientAgent:IDisposable
    {
        readonly AppConfig C;Thread T;volatile bool Run;
        public bool Online;public bool PrinterInstalled;public int Latency;public DateTime LastSuccess=DateTime.MinValue;public DateTime LastPrinterCheck=DateTime.MinValue;public string LastError="";
        public ClientAgent(AppConfig c){C=c;}
        public void Start(){if(Run)return;Run=true;T=new Thread(Loop){IsBackground=true,Name="GTRZ-Client"};T.Start();}
        void Loop(){int fails=0;while(Run){
            if(C.AutoDiscover && (String.IsNullOrWhiteSpace(C.ServerAddress) || C.ServerAddress==AppConfig.LocalIp())) {
                string initial=DiscoveryService.Discover(C.DiscoveryPort,1400);
                if(!String.IsNullOrWhiteSpace(initial) && initial!=AppConfig.LocalIp()) {
                    Log.Info("Servidor descoberto automaticamente: "+initial);
                    C.ServerAddress=initial;
                    C.Save();
                }
            }
            try{
            if(LastPrinterCheck==DateTime.MinValue || (DateTime.Now-LastPrinterCheck).TotalSeconds>=15) {
                PrinterInstalled=Proc.PrinterInstalled(C);
                LastPrinterCheck=DateTime.Now;
            }
            bool installed=PrinterInstalled;DateTime start=DateTime.Now;string j="{\"computer\":\""+Environment.MachineName+"\",\"version\":\""+C.Version+"\",\"printerInstalled\":"+(installed?"true":"false")+"}";
            var r=(HttpWebRequest)WebRequest.Create("http://"+C.ServerAddress+":"+C.ApiPort+"/heartbeat");r.Method="POST";r.Timeout=1800;r.ContentType="application/json";r.Headers["X-GTRZ-Print-Key"]=C.ApiKey;byte[]b=Encoding.UTF8.GetBytes(j);r.ContentLength=b.Length;using(Stream s=r.GetRequestStream())s.Write(b,0,b.Length);using(var resp=(HttpWebResponse)r.GetResponse())Online=resp.StatusCode==HttpStatusCode.OK;
            Latency=(int)(DateTime.Now-start).TotalMilliseconds;LastSuccess=DateTime.Now;LastError="";fails=0;
        }catch(Exception ex){Online=false;LastError=ex.Message;fails++;if(C.AutoDiscover&&fails>=2){string found=DiscoveryService.Discover(C.DiscoveryPort,1200);if(!String.IsNullOrWhiteSpace(found)&&found!=C.ServerAddress){Log.Info("Servidor redescoberto "+C.ServerAddress+" -> "+found);C.ServerAddress=found;C.Save();fails=0;}}}
            int wait=Math.Max(1,C.HeartbeatSeconds)*1000;for(int i=0;i<wait/100&&Run;i++)Thread.Sleep(100);
        }}
        public void Dispose(){Run=false;try{if(T!=null&&T.IsAlive)T.Join(1200);}catch{}}
    }
}
