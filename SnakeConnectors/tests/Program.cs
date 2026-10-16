using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.IO;
using Designer.Modules.SnakeConnectors;
using QSC.QSys.Design;
using QSC.QSys.Design.Mux;
using QSC.QSys.Extract;
using QSC.QSys.Tools;

static class Program
{
    static int passed;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); passed++; }
    static Pin Boundary(bool group=false) { var owner=group?new ChannelGroupPin():new ContainerPin(); var p=new Pin{Provider=owner}; owner.Pins.Add(p); return p; }
    static Pin Snake(Direction direction,int count=2) {
        var owner=new MuxBase(); var p=new Pin{Provider=owner,Direction=direction,WiringDomain=Domain.Mux}; owner.Pins.Add(p);
        for(uint i=0;i<count;i++) owner.Pins.Add(new Pin{Provider=owner,GraphicIndex=i,Direction=direction==Direction.Output?Direction.Input:Direction.Output}); return p;
    }
    [STAThread] static void Main(string[] args)
    {
        if(args.Length>0) {
            var directory=args[0]; AssemblyLoadContext.Default.Resolving+=(_,name)=>File.Exists(Path.Combine(directory,name.Name+".dll"))?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory,name.Name+".dll")):null;
            var host=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory,"Q-Sys Designer.dll"));
            _=new SnakeHooks(host);
            if(args.Length>1 && args[1]=="--models") { NativeProbe.Run(host); return; }
            Console.WriteLine("Actual Designer bundle, boundary, traversal and validation bindings resolve; no patches installed."); return;
        }
        var errors=new List<Exception>();
        var hooks=new SnakeHooks(Assembly.GetExecutingAssembly()) { Report=errors.Add }; var h=hooks.Install();
        var source=Snake(Direction.Output); var sink=Snake(Direction.Input); var boundary=Boundary();
        Check(WireTool.CanWireMux(source,boundary),"bundle can reach an empty generic connector");
        Check(!WireTool.CanWireMux(source,Boundary(true)),"typed Channel Group connector remains rejected");
        var typed=Boundary(); typed.WiringDomain=Domain.Audio;
        Check(!WireTool.CanWireMux(source,typed),"existing scalar boundary cannot be repurposed");
        var control=Boundary(); control.UserCreated=true;
        Check(!WireTool.CanWireMux(source,control),"exposed controls are not bundle connectors");
        DesignNet.Link(source,boundary);
        Check(UnknownPinHelper.Original(boundary)==Domain.Mux,"native propagation can retain a connected bundle domain");
        Check(UnknownPinHelper.Original(source)==Domain.Mux,"native Snake origin stays unchanged");
        Check(WireTool.CanWireMux(boundary,sink),"second end accepts an equal-channel Snake");
        Check(!WireTool.CanWireMux(boundary,Snake(Direction.Input,3)),"unequal channel counts rejected");
        Check(!WireTool.CanWireMux(boundary,Snake(Direction.Output)),"multiple bundle sources rejected");
        Check(!WireTool.CanWireMux(source,boundary),"new loops on the same bundle net rejected");
        var scalar=new Pin{Provider=new PinProvider(),WiringDomain=Domain.Audio};
        Check(!WireTool.CanWireMux(boundary,scalar),"bundle cannot attach to scalar endpoint");
        source.Provider.Pins[1].WiringDomain=Domain.Audio; sink.Provider.Pins[1].WiringDomain=Domain.Control;
        Check(!WireTool.CanWireMux(boundary,sink),"native per-channel compatibility remains required");
        sink.Provider.Pins[1].WiringDomain=Domain.Audio; WireTool.ValidNet=false;
        Check(!WireTool.CanWireMux(boundary,sink),"native net validation cannot be bypassed"); WireTool.ValidNet=true;
        var media=new Pin{Provider=new PinProvider(),WiringDomain=Domain.Mediacast};
        Check(!WireTool.CanWire(source.Provider.Pins[1],media),"later special-domain channel wiring across a bundle boundary is rejected");
        source.Provider.Pins[1].WiringDomain=Domain.Mediacast;
        Check(!WireTool.CanWireMux(boundary,sink),"special-domain bundles remain outside candidate scope");
        source.Provider.Pins[1].WiringDomain=Domain.Audio;
        DesignNet.Link(boundary,sink);
        Check(!WireTool.CanWireMux(boundary,Snake(Direction.Input)),"unreviewed bundle fanout rejected");
        DesignNet.Clear();
        Check(UnknownPinHelper.Original(boundary)==Domain.Unknown,"disconnect restores native generic type");
        var before=WireTool.NativeCalls; WireTool.CanWireMux(source,sink);
        Check(WireTool.NativeCalls==before+1,"ordinary Snake-to-Snake wiring stays native");
        Check(errors.Count==0,"no reflection or hook failures");
        h.UnpatchAll(SnakeHooks.PatchId);
        Check(!WireTool.CanWireMux(source,boundary),"unpatch restores native rejection");
        Console.WriteLine($"{passed} Snake connector harness checks passed.");
    }
}
namespace QSC.QSys.Design
{
    public enum Domain { Unknown, Mux, Audio, Control, Mediacast }
    public enum Direction { Input, Output }
    public class PinProvider { public List<Pin> Pins {get;}=[]; }
    public class Container : PinProvider { }
    public class ContainerPin : PinProvider { public bool AllowSoftTyping=true; }
    public class ChannelGroupPin : ContainerPin { }
    public class Pin { public PinProvider Provider=null!; public Direction Direction; public bool UserCreated; public Domain WiringDomain {get;set;} public uint GraphicIndex {get;set;} }
    public static class UnknownPinHelper {
        [MethodImpl(MethodImplOptions.NoInlining)] private static Domain GetOriginalDomain(Pin pin)=>pin.Provider is Mux.MuxBase mux&&ReferenceEquals(mux.MuxedPin,pin)?Domain.Mux:Domain.Unknown;
        [MethodImpl(MethodImplOptions.NoInlining)] public static Domain Original(Pin pin)=>GetOriginalDomain(pin);
    }
}
namespace QSC.QSys.Design.Mux {
    public enum ConnectionRules { Default, IgnoreMux }
    public class MuxBase : PinProvider { public Pin MuxedPin=>Pins[0]; }
}
namespace QSC.QSys.Extract
{
    public static class DesignNet {
        private static readonly Dictionary<Pin,HashSet<Pin>> links=[];
        public static void Link(Pin a,Pin b) { if(!links.ContainsKey(a)) links[a]=[]; if(!links.ContainsKey(b)) links[b]=[]; links[a].Add(b); links[b].Add(a); }
        public static void Clear()=>links.Clear();
        public static HashSet<Pin> GetConnectedPins(Pin pin,ConnectionRules rules) { var result=new HashSet<Pin>(); var queue=new Stack<Pin>(); queue.Push(pin); while(queue.TryPop(out var p)) if(result.Add(p)&&links.TryGetValue(p,out var next)) foreach(var n in next) queue.Push(n); return result; }
    }
}
namespace QSC.QSys.Tools
{
    public static class WireTool {
        public static int NativeCalls; public static bool ValidNet=true;
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool CanWireMux(Pin a,Pin b) { NativeCalls++; return a.Provider is MuxBase&&b.Provider is MuxBase; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool CanWire(Pin a,Pin b)=>true;
        public static bool AreCompatibleToWired(Pin a,Pin b)=>a.WiringDomain==Domain.Unknown||b.WiringDomain==Domain.Unknown||a.WiringDomain==b.WiringDomain;
        public static bool CheckNet(HashSet<Pin> pins)=>ValidNet;
    }
}
