using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace GTRZPrinter
{
    public sealed class IppRequest
    {
        public byte Major,Minor; public ushort Operation; public int RequestId,DocumentOffset;
        public readonly Dictionary<string,List<byte[]>> Attributes=new Dictionary<string,List<byte[]>>(StringComparer.OrdinalIgnoreCase);
        public string GetString(string n,string f){List<byte[]>v;if(!Attributes.TryGetValue(n,out v)||v.Count==0)return f;return Encoding.UTF8.GetString(v[0]);}
        public int GetInt(string n,int f){List<byte[]>v;if(!Attributes.TryGetValue(n,out v)||v.Count==0||v[0].Length!=4)return f;byte[]b=v[0];return(b[0]<<24)|(b[1]<<16)|(b[2]<<8)|b[3];}
    }

    public static class IppCodec
    {
        static ushort U16(byte[]d,int p){return(ushort)((d[p]<<8)|d[p+1]);}
        static int I32(byte[]d,int p){return(d[p]<<24)|(d[p+1]<<16)|(d[p+2]<<8)|d[p+3];}
        public static IppRequest Decode(byte[]d){
            if(d==null||d.Length<8)throw new InvalidDataException("Mensagem IPP curta.");
            var r=new IppRequest{Major=d[0],Minor=d[1],Operation=U16(d,2),RequestId=I32(d,4)};int p=8;string current="";
            while(p<d.Length){
                byte tag=d[p++];if(tag==0x03){r.DocumentOffset=p;return r;}
                if(tag>=0x01&&tag<=0x0F){current="";continue;}
                if(p+2>d.Length)throw new InvalidDataException("IPP truncado.");int nl=U16(d,p);p+=2;
                if(p+nl>d.Length)throw new InvalidDataException("IPP nome truncado.");
                string name=nl>0?Encoding.UTF8.GetString(d,p,nl):current;p+=nl;if(nl>0)current=name;
                if(p+2>d.Length)throw new InvalidDataException("IPP valor truncado.");int vl=U16(d,p);p+=2;
                if(p+vl>d.Length)throw new InvalidDataException("IPP valor truncado.");byte[]v=new byte[vl];if(vl>0)Buffer.BlockCopy(d,p,v,0,vl);p+=vl;
                if(!String.IsNullOrWhiteSpace(name)){List<byte[]>list;if(!r.Attributes.TryGetValue(name,out list)){list=new List<byte[]>();r.Attributes[name]=list;}list.Add(v);}
            }
            r.DocumentOffset=d.Length;return r;
        }
        public static byte[] Document(byte[]d,IppRequest r){int len=d.Length-r.DocumentOffset;if(len<=0)return new byte[0];byte[]x=new byte[len];Buffer.BlockCopy(d,r.DocumentOffset,x,0,len);return x;}
    }

    public sealed class IppWriter
    {
        readonly MemoryStream M=new MemoryStream();
        public IppWriter(byte maj,byte min,ushort status,int id){M.WriteByte(maj);M.WriteByte(min);U16(status);I32(id);}
        void U16(int v){M.WriteByte((byte)(v>>8));M.WriteByte((byte)v);}
        void I32(int v){M.WriteByte((byte)(v>>24));M.WriteByte((byte)(v>>16));M.WriteByte((byte)(v>>8));M.WriteByte((byte)v);}
        byte[] B32(int v){return new[]{(byte)(v>>24),(byte)(v>>16),(byte)(v>>8),(byte)v};}
        public void Group(byte t){M.WriteByte(t);}
        public void Attr(byte t,string n,byte[]v){byte[]nb=Encoding.UTF8.GetBytes(n??"");M.WriteByte(t);U16(nb.Length);if(nb.Length>0)M.Write(nb,0,nb.Length);U16(v==null?0:v.Length);if(v!=null&&v.Length>0)M.Write(v,0,v.Length);}
        public void Str(byte t,string n,string v){Attr(t,n,Encoding.UTF8.GetBytes(v??""));}
        public void Strs(byte t,string n,params string[]v){for(int i=0;v!=null&&i<v.Length;i++)Str(t,i==0?n:"",v[i]);}
        public void Int(byte t,string n,int v){Attr(t,n,B32(v));}
        public void Ints(byte t,string n,params int[]v){for(int i=0;v!=null&&i<v.Length;i++)Attr(t,i==0?n:"",B32(v[i]));}
        public void Bool(string n,bool v){Attr(0x22,n,new[]{(byte)(v?1:0)});}
        public void Range(string n,int min,int max){byte[]b=new byte[8];Buffer.BlockCopy(B32(min),0,b,0,4);Buffer.BlockCopy(B32(max),0,b,4,4);Attr(0x33,n,b);}
        public void Res(string n,int x,int y){byte[]b=new byte[9];Buffer.BlockCopy(B32(x),0,b,0,4);Buffer.BlockCopy(B32(y),0,b,4,4);b[8]=3;Attr(0x32,n,b);}
        void Member(string n){Str(0x4A,"",n);}
        void MInt(string n,int v){Member(n);Int(0x21,"",v);}
        void MKey(string n,string v){Member(n);Str(0x44,"",v);}
        public void MediaCol(string name,string media,int w,int h,int side){
            Attr(0x34,name,new byte[0]);Member("media-size");Attr(0x34,"",new byte[0]);MInt("x-dimension",w);MInt("y-dimension",h);Attr(0x37,"",new byte[0]);
            MKey("media-size-name",media);MInt("media-left-margin",side);MInt("media-right-margin",side);MInt("media-top-margin",0);MInt("media-bottom-margin",0);Attr(0x37,"",new byte[0]);
        }
        public byte[] Finish(){M.WriteByte(0x03);return M.ToArray();}
    }

    public static class Pwg
    {
        static uint BE(byte[]d,int p){return((uint)d[p]<<24)|((uint)d[p+1]<<16)|((uint)d[p+2]<<8)|d[p+3];}
        static byte[] Line(byte[]d,ref int p,int bytes,int valueSize){
            byte[]line=new byte[bytes];int o=0;
            while(o<bytes){
                if(p>=d.Length)throw new InvalidDataException("PWG truncado.");int c=d[p++];
                if(c<=127){int count=c+1;if(p+valueSize>d.Length)throw new InvalidDataException("PWG run truncado.");for(int i=0;i<count&&o<bytes;i++){int cp=Math.Min(valueSize,bytes-o);Buffer.BlockCopy(d,p,line,o,cp);o+=cp;}p+=valueSize;}
                else if(c>=129){int count=257-c,total=count*valueSize;if(p+total>d.Length)throw new InvalidDataException("PWG literal truncado.");int cp=Math.Min(total,bytes-o);Buffer.BlockCopy(d,p,line,o,cp);o+=cp;p+=total;}
            }
            return line;
        }
        static bool Black(byte[]r,int x,int width,int bpp,int cs,int y){
            if(x<0||x>=width)return false;
            if(bpp==1){bool one=(r[x>>3]&(0x80>>(x&7)))!=0;return cs==3?one:!one;}
            int gray;
            if(bpp==8){int v=r[x];gray=cs==3?255-v:v;}
            else if(bpp==24){int i=x*3;if(i+2>=r.Length)return false;gray=(299*r[i]+587*r[i+1]+114*r[i+2])/1000;}
            else if(bpp==32){int i=x*4;if(i+3>=r.Length)return false;if(cs==6){int ink=Math.Min(255,r[i+3]+((r[i]+r[i+1]+r[i+2])/6));gray=255-ink;}else gray=(299*r[i]+587*r[i+1]+114*r[i+2])/1000;}
            else throw new NotSupportedException("bitsPerPixel PWG: "+bpp);
            int[,]b={{0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5}};return gray<32+b[y&3,x&3]*12;
        }
        static byte[] Normalize(byte[]src,int sw,int bpp,int cs,int dw,int y){
            byte[]dst=new byte[(dw+7)/8];
            for(int x=0;x<dw;x++){int sx=(int)(((long)x*sw)/dw);if(sx>=sw)sx=sw-1;if(Black(src,sx,sw,bpp,cs,y))dst[x>>3]|=(byte)(0x80>>(x&7));}
            return dst;
        }
        static bool Ink(byte[]r){for(int i=0;i<r.Length;i++)if(r[i]!=0)return true;return false;}
        static void Raster(MemoryStream m,List<byte[]>rows,int width){
            int wb=(width+7)/8,o=0;while(o<rows.Count){int h=Math.Min(256,rows.Count-o);m.WriteByte(0x1D);m.WriteByte(0x76);m.WriteByte(0x30);m.WriteByte(0);m.WriteByte((byte)wb);m.WriteByte((byte)(wb>>8));m.WriteByte((byte)h);m.WriteByte((byte)(h>>8));for(int y=0;y<h;y++)m.Write(rows[o+y],0,rows[o+y].Length);o+=h;}
        }
        public static byte[] Convert(byte[]doc,AppConfig c,out string desc){
            if(doc==null||doc.Length<4||doc[0]!=0x52||doc[1]!=0x61||doc[2]!=0x53||doc[3]!=0x32)throw new InvalidDataException("Documento não é PWG Raster RaS2.");
            int p=4,pages=0;long useful=0;using(var esc=new MemoryStream()){esc.WriteByte(0x1B);esc.WriteByte(0x40);
                while(p<doc.Length){
                    if(p+1796>doc.Length)throw new InvalidDataException("Cabeçalho PWG incompleto.");int h=p;
                    int width=(int)BE(doc,h+372),height=(int)BE(doc,h+376),bpp=(int)BE(doc,h+388),bytes=(int)BE(doc,h+392),order=(int)BE(doc,h+396),cs=(int)BE(doc,h+400);
                    if(width<=0||height<=0||bytes<=0||width>10000||height>100000)throw new InvalidDataException("Dimensões PWG inválidas.");if(order!=0)throw new NotSupportedException("PWG não chunky.");
                    int valueSize=Math.Max(1,(bpp+7)/8);p+=1796;var rows=new List<byte[]>();int produced=0,y=0;
                    while(produced<height){if(p>=doc.Length)throw new InvalidDataException("PWG terminou antes da página.");int repeat=doc[p++]+1;byte[]line=Line(doc,ref p,bytes,valueSize);for(int r=0;r<repeat&&produced<height;r++){rows.Add(Normalize(line,width,bpp,cs,c.PrintWidthDots,y++));produced++;}}
                    if(c.TrimBlank){int last=-1;for(int i=rows.Count-1;i>=0;i--)if(Ink(rows[i])){last=i;break;}if(last<0)rows.Clear();else{int keep=Math.Min(rows.Count,last+1+Math.Max(0,c.BottomMarginDots));if(keep<rows.Count)rows.RemoveRange(keep,rows.Count-keep);}}
                    Raster(esc,rows,c.PrintWidthDots);esc.WriteByte(0x0A);esc.WriteByte(0x0A);pages++;useful+=rows.Count;
                }
                if(c.CutAfterJob){esc.WriteByte(0x0A);esc.WriteByte(0x0A);esc.WriteByte(0x0A);esc.WriteByte(0x1D);esc.WriteByte(0x56);esc.WriteByte(0);}
                desc=pages+" página(s), "+useful+" linhas, "+c.PrintWidthDots+" dots";return esc.ToArray();
            }
        }
    }

    public sealed class Job { public int Id; public string Name="",User=""; public int State=3; public string Error=""; }

    public sealed class IppServer:IDisposable
    {
        readonly AppConfig C;readonly ClientRegistry Clients;readonly object PrintLock;HttpListener L;Thread T;volatile bool Run;int Next=1000;
        readonly ConcurrentDictionary<int,Job> Jobs=new ConcurrentDictionary<int,Job>();long Req,Ok,Fail;
        public string LastOperation="",LastError="",LastRaster="";
        public long Requests{get{return Interlocked.Read(ref Req);}}public long Printed{get{return Interlocked.Read(ref Ok);}}public long Failed{get{return Interlocked.Read(ref Fail);}}public bool Running{get{return Run;}}
        public IppServer(AppConfig c,ClientRegistry clients,object printLock){C=c;Clients=clients;PrintLock=printLock;}
        string Uri{get{return"ipp://"+AppConfig.LocalIp()+":"+C.IppPort+"/ipp/print";}}
        public void Start(){if(Run)return;L=new HttpListener();L.Prefixes.Add("http://+:"+C.IppPort+"/");L.Start();Run=true;T=new Thread(Loop){IsBackground=true,Name="GTRZ-IPP"};T.Start();Log.Info("IPP online "+Uri);}
        void Loop(){while(Run){try{var x=L.GetContext();ThreadPool.QueueUserWorkItem(_=>Handle(x));}catch{if(!Run)return;}}}
        void Handle(HttpListenerContext x){
            try{
                if(x.Request.HttpMethod=="GET"){byte[]b=Encoding.UTF8.GetBytes("<html><body><h2>GTRZ POS-80</h2><p>IPP ONLINE</p></body></html>");x.Response.ContentType="text/html";x.Response.ContentLength64=b.Length;x.Response.OutputStream.Write(b,0,b.Length);x.Response.Close();return;}
                if(x.Request.HttpMethod!="POST"||!x.Request.Url.AbsolutePath.StartsWith("/ipp/print",StringComparison.OrdinalIgnoreCase)){x.Response.StatusCode=404;x.Response.Close();return;}
                byte[]body=Read(x.Request);Interlocked.Increment(ref Req);string ip=x.Request.RemoteEndPoint.Address.ToString();Clients.Touch("",ip,"IPP","",true);byte[]resp=Process(body,ip);
                x.Response.ContentType="application/ipp";x.Response.ContentLength64=resp.Length;x.Response.OutputStream.Write(resp,0,resp.Length);x.Response.Close();
            }catch(Exception ex){LastError=ex.Message;Interlocked.Increment(ref Fail);Log.Error("IPP HTTP "+ex.Message);try{x.Response.StatusCode=500;x.Response.Close();}catch{}}
        }
        byte[] Read(HttpListenerRequest r){using(var m=new MemoryStream()){byte[]b=new byte[16384];int total=0;for(;;){int n=r.InputStream.Read(b,0,b.Length);if(n<=0)break;total+=n;if(total>C.MaxDocumentBytes+262144)throw new InvalidDataException("IPP excede limite.");m.Write(b,0,n);}return m.ToArray();}}
        IppWriter Base(IppRequest r,ushort st){var w=new IppWriter(r.Major==0?(byte)2:r.Major,r.Minor,st,r.RequestId);w.Group(0x01);w.Str(0x47,"attributes-charset","utf-8");w.Str(0x48,"attributes-natural-language","pt-br");return w;}
        void PrinterAttrs(IppWriter w){
            string media="custom_gtrz-receipt_80x200mm";int side=Math.Max(0,((C.PaperWidthMm-C.PrintableWidthMm)*100)/2);w.Group(0x04);
            w.Str(0x45,"printer-uri-supported",Uri);w.Str(0x44,"uri-authentication-supported","none");w.Str(0x44,"uri-security-supported","none");w.Str(0x42,"printer-name",C.NetworkPrinterName);
            w.Str(0x41,"printer-info","GTRZ Printer - térmica 80mm");w.Str(0x41,"printer-location",Environment.MachineName);w.Str(0x41,"printer-make-and-model","GTRZ POS-80 IPP Bridge");
            w.Str(0x45,"printer-uuid","urn:uuid:9c4d08ce-8a31-4b40-a8e8-80f38a0b6310");w.Int(0x23,"printer-state",3);w.Str(0x44,"printer-state-reasons","none");w.Bool("printer-is-accepting-jobs",true);w.Int(0x21,"queued-job-count",0);
            w.Strs(0x44,"ipp-versions-supported","1.1","2.0");w.Ints(0x23,"operations-supported",2,4,5,6,8,9,10,11);w.Str(0x47,"charset-configured","utf-8");w.Str(0x47,"charset-supported","utf-8");w.Bool("multiple-document-jobs-supported",false);
            w.Str(0x49,"document-format-default","image/pwg-raster");w.Str(0x49,"document-format-preferred","image/pwg-raster");w.Str(0x49,"document-format-supported","image/pwg-raster");
            w.Int(0x21,"copies-default",1);w.Range("copies-supported",1,99);w.Str(0x44,"sides-default","one-sided");w.Str(0x44,"sides-supported","one-sided");w.Str(0x44,"print-color-mode-default","monochrome");w.Strs(0x44,"print-color-mode-supported","monochrome","bi-level");
            w.Res("printer-resolution-default",C.Dpi,C.Dpi);w.Res("printer-resolution-supported",C.Dpi,C.Dpi);w.Res("pwg-raster-document-resolution-supported",C.Dpi,C.Dpi);w.Strs(0x44,"pwg-raster-document-type-supported","black_1","sgray_8");
            w.Str(0x44,"media-default",media);w.Str(0x44,"media-ready",media);w.Str(0x44,"media-supported",media);w.MediaCol("media-col-default",media,C.PaperWidthMm*100,C.PageLengthMm*100,side);w.MediaCol("media-col-ready",media,C.PaperWidthMm*100,C.PageLengthMm*100,side);w.MediaCol("media-col-database",media,C.PaperWidthMm*100,C.PageLengthMm*100,side);
        }
        int Create(IppRequest r){int id=Interlocked.Increment(ref Next);Jobs[id]=new Job{Id=id,Name=r.GetString("job-name","Documento Windows"),User=r.GetString("requesting-user-name","Windows")};return id;}
        void JobAttrs(IppWriter w,Job j){w.Group(0x02);w.Int(0x21,"job-id",j.Id);w.Str(0x45,"job-uri",Uri+"/job/"+j.Id);w.Str(0x42,"job-name",j.Name);w.Str(0x42,"job-originating-user-name",j.User);w.Int(0x23,"job-state",j.State);w.Str(0x44,"job-state-reasons",j.State==9?"job-completed-successfully":j.State==8?"aborted-by-system":j.State==5?"job-printing":"job-incoming");}
        bool Print(byte[]doc,Job j,out string error){try{string desc;byte[]esc=Pwg.Convert(doc,C,out desc);LastRaster=desc;lock(PrintLock)return RawPrinter.Send(C.LocalPrinterQueue,esc,"GTRZ IPP #"+j.Id+" - "+j.Name,out error);}catch(Exception ex){error=ex.Message;return false;}}
        byte[] Process(byte[]d,string ip){
            IppRequest r=IppCodec.Decode(d);LastOperation="0x"+r.Operation.ToString("X4");
            if(r.Operation==11){LastOperation="Get-Printer-Attributes";var w=Base(r,0);PrinterAttrs(w);return w.Finish();}
            if(r.Operation==4){LastOperation="Validate-Job";return Base(r,0).Finish();}
            if(r.Operation==2){
                LastOperation="Print-Job";int id=Create(r);Job j=Jobs[id];j.State=5;string err;bool ok=Print(IppCodec.Document(d,r),j,out err);j.State=ok?9:8;j.Error=ok?"":err;
                if(ok){Interlocked.Increment(ref Ok);LastError="";Log.Info("IPP job "+id+" OK de "+ip+" | "+LastRaster);}else{Interlocked.Increment(ref Fail);LastError=err;Log.Error("IPP job "+id+" "+err);}
                var w=Base(r,ok?(ushort)0:(ushort)0x040A);JobAttrs(w,j);if(!ok)w.Str(0x41,"status-message",err);return w.Finish();
            }
            if(r.Operation==5){LastOperation="Create-Job";int id=Create(r);var w=Base(r,0);JobAttrs(w,Jobs[id]);return w.Finish();}
            if(r.Operation==6){
                LastOperation="Send-Document";int id=r.GetInt("job-id",-1);Job j;if(!Jobs.TryGetValue(id,out j))return Base(r,0x0406).Finish();j.State=5;string err;bool ok=Print(IppCodec.Document(d,r),j,out err);j.State=ok?9:8;
                if(ok)Interlocked.Increment(ref Ok);else{Interlocked.Increment(ref Fail);LastError=err;}var w=Base(r,ok?(ushort)0:(ushort)0x040A);JobAttrs(w,j);return w.Finish();
            }
            if(r.Operation==9){LastOperation="Get-Job-Attributes";int id=r.GetInt("job-id",-1);Job j;if(!Jobs.TryGetValue(id,out j))return Base(r,0x0406).Finish();var w=Base(r,0);JobAttrs(w,j);return w.Finish();}
            if(r.Operation==10){LastOperation="Get-Jobs";var w=Base(r,0);int n=0;foreach(Job j in Jobs.Values){JobAttrs(w,j);if(++n>=20)break;}return w.Finish();}
            if(r.Operation==8){LastOperation="Cancel-Job";int id=r.GetInt("job-id",-1);Job j;if(!Jobs.TryGetValue(id,out j))return Base(r,0x0406).Finish();if(j.State==3)j.State=7;var w=Base(r,0);JobAttrs(w,j);return w.Finish();}
            var uns=Base(r,0x0501);uns.Str(0x41,"status-message","Operação IPP não suportada");return uns.Finish();
        }
        public void Dispose(){Run=false;try{if(L!=null)L.Stop();}catch{}try{if(L!=null)L.Close();}catch{}try{if(T!=null&&T.IsAlive)T.Join(1000);}catch{}}
    }
}
