using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Windows.Forms;

namespace GTRZPrinter
{
    public sealed class AppConfig
    {
        public string Version = "2.0.0";
        public string Mode = "auto";
        public string LocalPrinterQueue = "POS-80";
        public string NetworkPrinterName = "GTRZ POS-80";
        public string ServerAddress = "192.168.1.187";
        public int IppPort = 631;
        public int ApiPort = 9101;
        public int DiscoveryPort = 9102;
        public string ApiKey = "GTRZ-PRINT-9101";
        public int PaperWidthMm = 80;
        public int PrintableWidthMm = 72;
        public int PageLengthMm = 200;
        public int PrintWidthDots = 576;
        public int Dpi = 203;
        public int Columns = 48;
        public string EncodingName = "cp850";
        public bool CutAfterJob = true;
        public bool TrimBlank = true;
        public int BottomMarginDots = 32;
        public int FeedLines = 3;
        public bool StartWithWindows = true;
        public bool MinimizeOnClose = true;
        public bool AutoDiscover = true;
        public int HeartbeatSeconds = 2;
        public int OfflineSeconds = 8;
        public int MaxDocumentBytes = 16777216;

        public static string Folder {
            get {
                string p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"GTRZ Printer");
                Directory.CreateDirectory(p); return p;
            }
        }
        public static string ConfigFile { get { return Path.Combine(Folder,"config.json"); } }
        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields=true, WriteIndented=true };
        public static string LogFolder {
            get { string p=Path.Combine(Folder,"logs"); Directory.CreateDirectory(p); return p; }
        }
        public static AppConfig Load() {
            try {
                if(File.Exists(ConfigFile)) {
                    var c=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile), JsonOptions);
                    if(c!=null) return c;
                }
            } catch {}
            var fresh=new AppConfig();
            fresh.Save(); return fresh;
        }
        public void Save() {
            File.WriteAllText(ConfigFile,JsonSerializer.Serialize(this, JsonOptions));
        }
        public static string LocalIp() {
            try {
                foreach(var ip in Dns.GetHostAddresses(Dns.GetHostName())) {
                    if(ip.AddressFamily!=AddressFamily.InterNetwork) continue;
                    string s=ip.ToString();
                    if(s.StartsWith("127.")||s.StartsWith("169.254.")) continue;
                    if(s.StartsWith("192.168.")||s.StartsWith("10.")||s.StartsWith("172.")) return s;
                }
            } catch {}
            return "127.0.0.1";
        }
    }

    public static class Log
    {
        static readonly object Sync=new object();
        public static readonly ConcurrentQueue<string> Lines=new ConcurrentQueue<string>();
        public static string FilePath { get { return Path.Combine(AppConfig.LogFolder,"gtrz-"+DateTime.Now.ToString("yyyyMMdd")+".log"); } }
        static void Write(string level,string text) {
            string line=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+"  "+level.PadRight(5)+"  "+text;
            Lines.Enqueue(line);
            try { lock(Sync) File.AppendAllText(FilePath,line+Environment.NewLine,Encoding.UTF8); } catch {}
        }
        public static void Info(string s){Write("INFO",s);}
        public static void Warn(string s){Write("WARN",s);}
        public static void Error(string s){Write("ERROR",s);}
    }

    public static class RawPrinter
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        class DOC_INFO_1 {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
        }
        [DllImport("winspool.drv",EntryPoint="OpenPrinterW",SetLastError=true,CharSet=CharSet.Unicode)]
        static extern bool OpenPrinter(string name,out IntPtr h,IntPtr def);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr h);
        [DllImport("winspool.drv",EntryPoint="StartDocPrinterW",SetLastError=true,CharSet=CharSet.Unicode)]
        static extern int StartDocPrinter(IntPtr h,int level,[In] DOC_INFO_1 di);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool StartPagePrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool EndPagePrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr h,IntPtr bytes,int count,out int written);

        public static bool CanOpen(string name,out string error) {
            IntPtr h; if(!OpenPrinter(name,out h,IntPtr.Zero)) {
                error=new Win32Exception(Marshal.GetLastWin32Error()).Message; return false;
            }
            ClosePrinter(h); error=""; return true;
        }
        public static bool IsLocalUsbPrinter(string name,out string detail) {
            detail="";
            try {
                string safe=(name??"").Replace("\\","\\\\").Replace("'","''");
                using(var search=new ManagementObjectSearcher(
                    "SELECT Name,PortName,Local,Network FROM Win32_Printer WHERE Name='"+safe+"'")) {
                    foreach(ManagementObject p in search.Get()) {
                        string port=Convert.ToString(p["PortName"]);
                        bool local=p["Local"]!=null && Convert.ToBoolean(p["Local"]);
                        bool network=p["Network"]!=null && Convert.ToBoolean(p["Network"]);
                        detail=port;
                        return local && !network && !String.IsNullOrWhiteSpace(port) &&
                               (port.StartsWith("USB",StringComparison.OrdinalIgnoreCase) ||
                                port.StartsWith("DOT4",StringComparison.OrdinalIgnoreCase));
                    }
                }
            } catch(Exception ex) {
                detail=ex.Message;
            }
            return false;
        }
        public static bool Send(string name,byte[] bytes,string docName,out string error) {
            IntPtr h=IntPtr.Zero,p=IntPtr.Zero; bool ds=false,ps=false;
            try {
                if(!OpenPrinter(name,out h,IntPtr.Zero)) { error="OpenPrinter: "+new Win32Exception(Marshal.GetLastWin32Error()).Message; return false; }
                var di=new DOC_INFO_1{pDocName=docName,pDataType="RAW",pOutputFile=null};
                if(StartDocPrinter(h,1,di)==0){error="StartDocPrinter: "+new Win32Exception(Marshal.GetLastWin32Error()).Message;return false;} ds=true;
                if(!StartPagePrinter(h)){error="StartPagePrinter: "+new Win32Exception(Marshal.GetLastWin32Error()).Message;return false;} ps=true;
                p=Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes,0,p,bytes.Length);
                int written; if(!WritePrinter(h,p,bytes.Length,out written)){error="WritePrinter: "+new Win32Exception(Marshal.GetLastWin32Error()).Message;return false;}
                if(written!=bytes.Length){error="Escreveu "+written+" de "+bytes.Length+" bytes.";return false;}
                error=""; return true;
            } finally {
                if(p!=IntPtr.Zero) Marshal.FreeCoTaskMem(p);
                if(ps&&h!=IntPtr.Zero) EndPagePrinter(h);
                if(ds&&h!=IntPtr.Zero) EndDocPrinter(h);
                if(h!=IntPtr.Zero) ClosePrinter(h);
            }
        }
    }

    public static class Receipt
    {
        public static string Wrap(string text,int width) {
            if(width<16) width=48;
            var sb=new StringBuilder();
            string normalized=(text??"").Replace("\r\n","\n").Replace("\r","\n");
            foreach(string original in normalized.Split('\n')) {
                string line=original.Replace("\t","    ");
                if(line.Length==0){sb.Append("\r\n");continue;}
                while(line.Length>width) {
                    int cut=width;
                    int idx=line.LastIndexOf(' ',Math.Min(width-1,line.Length-1));
                    if(idx>width/2) cut=idx;
                    sb.Append(line.Substring(0,cut).TrimEnd()).Append("\r\n");
                    line=line.Substring(cut).TrimStart();
                }
                sb.Append(line).Append("\r\n");
            }
            return sb.ToString();
        }
        static string Center(string s,int w){s=s??"";if(s.Length>=w)return s;return new string(' ',(w-s.Length)/2)+s;}
        public static string Test(AppConfig c) {
            int w=Math.Max(16,c.Columns);
            var sb=new StringBuilder();
            sb.Append(Center("GTRZ PRINTER",w)).Append("\r\n");
            sb.Append(Center("TESTE 80MM",w)).Append("\r\n");
            sb.Append(new string('=',w)).Append("\r\n");
            sb.Append("Fila: ").Append(c.LocalPrinterQueue).Append("\r\n");
            sb.Append("Papel: ").Append(c.PaperWidthMm).Append(" mm\r\n");
            sb.Append("Area util: ").Append(c.PrintableWidthMm).Append(" mm\r\n");
            sb.Append("Raster: ").Append(c.PrintWidthDots).Append(" dots @ ").Append(c.Dpi).Append(" dpi\r\n");
            sb.Append("Colunas: ").Append(c.Columns).Append("\r\n");
            sb.Append("Acentos: á é í ó ú ç ã õ\r\n");
            sb.Append("Data: ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")).Append("\r\n");
            sb.Append(new string('=',w)).Append("\r\n");
            sb.Append(Center("GTRZ PRINTER OK",w)).Append("\r\n");
            return sb.ToString();
        }
        public static byte[] TextBytes(string text,AppConfig c,bool cut) {
            Encoding enc; try{enc=Encoding.GetEncoding(c.EncodingName);}catch{enc=Encoding.GetEncoding(850);}
            byte[] body=enc.GetBytes(Wrap(text,c.Columns));
            int feed=Math.Max(0,c.FeedLines), extra=2+feed+(cut?6:0);
            byte[] result=new byte[body.Length+extra]; int p=0;
            result[p++]=0x1B; result[p++]=0x40;
            Buffer.BlockCopy(body,0,result,p,body.Length); p+=body.Length;
            for(int i=0;i<feed;i++) result[p++]=0x0A;
            if(cut){result[p++]=0x0A;result[p++]=0x0A;result[p++]=0x0A;result[p++]=0x1D;result[p++]=0x56;result[p++]=0x00;}
            return result;
        }
    }

    public sealed class ClientInfo
    {
        public string ComputerName="",Ip="",Via="",Version="";
        public bool PrinterInstalled;
        public DateTime LastSeen=DateTime.MinValue;
    }
    public sealed class ClientRegistry
    {
        readonly object Sync=new object();
        readonly Dictionary<string,ClientInfo> Map=new Dictionary<string,ClientInfo>(StringComparer.OrdinalIgnoreCase);
        public void Touch(string pc,string ip,string via,string ver,bool installed) {
            string key=String.IsNullOrWhiteSpace(pc)?ip:pc; if(String.IsNullOrWhiteSpace(key))return;
            lock(Sync) {
                ClientInfo c; if(!Map.TryGetValue(key,out c)){c=new ClientInfo();Map[key]=c;}
                if(!String.IsNullOrWhiteSpace(pc))c.ComputerName=pc;
                if(!String.IsNullOrWhiteSpace(ip))c.Ip=ip;
                if(!String.IsNullOrWhiteSpace(via))c.Via=via;
                if(!String.IsNullOrWhiteSpace(ver))c.Version=ver;
                c.PrinterInstalled=installed;c.LastSeen=DateTime.Now;
            }
        }
        public List<ClientInfo> Snapshot() {
            lock(Sync) return Map.Values.OrderByDescending(x=>x.LastSeen).Select(x=>new ClientInfo{
                ComputerName=x.ComputerName,Ip=x.Ip,Via=x.Via,Version=x.Version,PrinterInstalled=x.PrinterInstalled,LastSeen=x.LastSeen
            }).ToList();
        }
    }

    public static class PortGuard
    {
        static readonly string[] LegacyNames = { "GTRZ Printer", "GTRZPrinter", "powershell", "pwsh", "dotnet" };

        public static void ReleaseLegacyListeners(params int[] ports)
        {
            var wanted=new HashSet<int>(ports);
            var pids=new HashSet<int>();
            foreach(string proto in new[]{"tcp","udp"}) {
                try {
                    string o,e;
                    Proc.Run("netstat.exe","-ano -p "+proto,8000,out o,out e);
                    foreach(string raw in o.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
                        string line=raw.Trim();
                        if(!line.StartsWith(proto.ToUpperInvariant()+" ",StringComparison.OrdinalIgnoreCase))continue;
                        string[] parts=line.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
                        if(parts.Length<4)continue;
                        string local=parts[1];
                        int colon=local.LastIndexOf(':');
                        if(colon<0)continue;
                        int port; if(!Int32.TryParse(local.Substring(colon+1),out port)||!wanted.Contains(port))continue;
                        int pid; if(!Int32.TryParse(parts[parts.Length-1],out pid)||pid<=0||pid==Environment.ProcessId)continue;
                        pids.Add(pid);
                    }
                } catch(Exception ex) { Log.Warn("PortGuard netstat: "+ex.Message); }
            }

            foreach(int pid in pids) {
                try {
                    using(var p=Process.GetProcessById(pid)) {
                        string name=p.ProcessName;
                        bool legacy=LegacyNames.Any(x=>name.Equals(x,StringComparison.OrdinalIgnoreCase) || name.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0);
                        if(!legacy) throw new IOException("Porta necessária ocupada por "+name+" (PID "+pid+"). O GTRZ Printer não encerrou um processo que não reconhece.");
                        Log.Warn("Encerrando servidor legado "+name+" PID "+pid+" que ocupava porta GTRZ.");
                        p.Kill(true);
                        p.WaitForExit(3000);
                    }
                } catch(ArgumentException) { }
            }
            Thread.Sleep(250);
        }
    }

    public static class Proc
    {
        public static int Run(string file,string args,int timeout,out string stdout,out string stderr) {
            var psi=new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=Process.Start(psi)) {
                stdout=p.StandardOutput.ReadToEnd(); stderr=p.StandardError.ReadToEnd();
                if(!p.WaitForExit(timeout)){try{p.Kill();}catch{} throw new TimeoutException(file+" excedeu o tempo limite.");}
                return p.ExitCode;
            }
        }
        static void Netsh(string a){try{string o,e;Run("netsh.exe",a,10000,out o,out e);}catch{}}
        public static void Firewall(AppConfig c) {
            Netsh("advfirewall firewall delete rule name=\"GTRZ Printer IPP\"");
            Netsh("advfirewall firewall delete rule name=\"GTRZ Printer API\"");
            Netsh("advfirewall firewall delete rule name=\"GTRZ Printer Discovery\"");
            Netsh("advfirewall firewall add rule name=\"GTRZ Printer IPP\" dir=in action=allow protocol=TCP localport="+c.IppPort+" remoteip=LocalSubnet profile=any");
            Netsh("advfirewall firewall add rule name=\"GTRZ Printer API\" dir=in action=allow protocol=TCP localport="+c.ApiPort+" remoteip=LocalSubnet profile=any");
            Netsh("advfirewall firewall add rule name=\"GTRZ Printer Discovery\" dir=in action=allow protocol=UDP localport="+c.DiscoveryPort+" remoteip=LocalSubnet profile=any");
        }
        public static void AutoStart(bool on) {
            string exe=Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath, o,e;
            try{
                if(on) Run("schtasks.exe","/Create /TN \"GTRZ Printer\" /SC ONLOGON /RL HIGHEST /F /TR \"\\\""+exe+"\\\" --minimized\"",15000,out o,out e);
                else Run("schtasks.exe","/Delete /TN \"GTRZ Printer\" /F",10000,out o,out e);
            }catch{}
        }
        static string Ps(string s){return (s??"").Replace("'","''");}
        static string Arg(string s){return (s??"").Replace("\\","\\\\").Replace("\"","\\\"");}
        public static bool PrinterInstalled(AppConfig c) {
            try {
                using(var search=new ManagementObjectSearcher(
                    "SELECT Name,DriverName,PortName FROM Win32_Printer")) {
                    foreach(ManagementObject p in search.Get()) {
                        string name=Convert.ToString(p["Name"]);
                        string driver=Convert.ToString(p["DriverName"]);
                        string port=Convert.ToString(p["PortName"]);

                        if(String.Equals(name,c.NetworkPrinterName,StringComparison.OrdinalIgnoreCase))
                            return true;

                        if(!String.IsNullOrWhiteSpace(driver) &&
                           driver.IndexOf("IPP",StringComparison.OrdinalIgnoreCase)>=0 &&
                           !String.IsNullOrWhiteSpace(port) &&
                           !String.IsNullOrWhiteSpace(c.ServerAddress) &&
                           port.IndexOf(c.ServerAddress,StringComparison.OrdinalIgnoreCase)>=0)
                            return true;
                    }
                }
            } catch {}
            return false;
        }
        public static bool TcpOpen(string host,int port,int timeoutMs,out string error) {
            error="";
            try {
                using(var tcp=new TcpClient()) {
                    IAsyncResult ar=tcp.BeginConnect(host,port,null,null);
                    if(!ar.AsyncWaitHandle.WaitOne(timeoutMs)) {
                        error="Timeout ao conectar em "+host+":"+port;
                        return false;
                    }
                    tcp.EndConnect(ar);
                    return true;
                }
            } catch(Exception ex) {
                error=ex.Message;
                return false;
            }
        }
        public static void InstallIpp(AppConfig c) {
            string url="http://"+c.ServerAddress+":"+c.IppPort+"/ipp/print";
            string sc="$ErrorActionPreference='Stop';Add-Printer -IppURL '"+Ps(url)+"';Start-Sleep -Seconds 2;$p=Get-Printer|?{$_.DriverName -like '*IPP*' -and ($_.PortName -like '*"+Ps(c.ServerAddress)+"*' -or $_.Name -like '*GTRZ*')}|select -First 1;if($p -and $p.Name -ne '"+Ps(c.NetworkPrinterName)+"'){try{Rename-Printer -Name $p.Name -NewName '"+Ps(c.NetworkPrinterName)+"'}catch{}}";
            string o,e; int code=Run("powershell.exe","-NoProfile -ExecutionPolicy Bypass -Command \""+Arg(sc)+"\"",45000,out o,out e);
            if(code!=0) throw new InvalidOperationException(e+" "+o);
        }
        public static void RemoveIpp(AppConfig c) {
            string sc="$p=Get-Printer -ErrorAction SilentlyContinue|?{$_.Name -eq '"+Ps(c.NetworkPrinterName)+"' -or ($_.DriverName -like '*IPP*' -and $_.PortName -like '*"+Ps(c.ServerAddress)+"*')};$p|%{Remove-Printer -Name $_.Name}";
            string o,e;Run("powershell.exe","-NoProfile -ExecutionPolicy Bypass -Command \""+Arg(sc)+"\"",30000,out o,out e);
        }
    }
}
