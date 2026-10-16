using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.IO;
using Designer.Modules.SnakeConnectors;

// Runs in this disposable console process, never in Designer. No objects are attached,
// no designs loaded/saved, and all model values use fields to avoid global undo/setter effects.
internal static class NativeProbe
{
    internal static void Run(Assembly host)
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(host.Location))).ToLowerInvariant()!="0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60") throw new Exception("Unsupported probe host.");
        Type T(string n)=>host.GetType("QSC.QSys."+n,true)!;
        object New(string n)=>Activator.CreateInstance(T(n))!;
        object P(object o,string n)=>o.GetType().GetProperty(n,flags)!.GetValue(o)!;
        void F(object o,string n,object v)=>o.GetType().GetField(n,flags)!.SetValue(o,v);
        void E(object o,string n,string v) { var f=o.GetType().GetField(n,flags)!; f.SetValue(o,Enum.Parse(f.FieldType,v)); }
        object Pin(object owner,string direction,string domain,uint index=0)
        {
            var p=New("Design.Pin"); F(p,"Provider",owner); E(p,"Direction",direction); E(p,"Domain",domain); F(p,"_GraphicIndex",index);
            var pins=P(owner,"Pins"); pins.GetType().GetMethod("Add",[T("Design.Pin")])!.Invoke(pins,[p]); return p;
        }
        var can=T("Tools.WireTool").GetMethod("CanWire",flags,null,[T("Design.Pin"),T("Design.Pin")],null)!;
        void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS native model: "+name); }
        foreach(var snake in new[]{"Muxer","Demuxer"})
        foreach(var boundary in new[]{"Container","ContainerPin","ChannelGroupPin"})
        foreach(var domain in boundary=="ChannelGroupPin"?new[]{"Audio","Control"}:new[]{"Unknown","Audio","Control"})
        {
            var owner=New("Design.Mux."+snake); bool input=snake=="Muxer";
            Pin(owner,input?"Output":"Input","Mux");
            var channel=Pin(owner,input?"Input":"Output","Unknown");
            var connector=New("Design."+boundary);
            var target=Pin(connector,input?"Output":"Input",domain);
            bool allowed=(bool)can.Invoke(null,[channel,target])!;
            Console.WriteLine($"Native individual channel {snake} -> {boundary} ({domain}): {allowed}");
            Check(allowed,$"{snake} channel to {boundary} {domain}");
        }
        Console.WriteLine("16 unattached native individual-channel cases passed before installing any hooks.");
        object Bundle(string kind)
        {
            var owner=New("Design.Mux."+kind); bool source=kind=="Muxer";
            var bundle=Pin(owner,source?"Output":"Input","Mux");
            Pin(owner,source?"Input":"Output","Unknown"); return bundle;
        }
        void Link(object a,object b)
        {
            var wire=New("Design.Wire"); F(wire,"_PinA",a); F(wire,"_PinB",b);
            foreach(var p in new[]{a,b}) { var wires=P(p,"Wires"); wires.GetType().GetMethod("Add",[T("Design.Wire")])!.Invoke(wires,[wire]); }
        }
        var sourceBundle=Bundle("Muxer"); var sinkBundle=Bundle("Demuxer");
        var outer=Pin(New("Design.Container"),"Input","Unknown");
        var innerOwner=New("Design.ContainerPin"); F(innerOwner,"AllowSoftTyping",true);
        var inner=Pin(innerOwner,"Output","Unknown"); Link(outer,inner);
        Check(can.Invoke(null,[sourceBundle,outer]) is false,"bundle boundary rejected before hooks");
        var errors=new List<Exception>(); var hooks=new SnakeHooks(host) { Report=errors.Add }; var harmony=hooks.Install();
        try
        {
            Check(can.Invoke(null,[sourceBundle,outer]) is true,"bundle boundary accepted by isolated hooks");
            Link(sourceBundle,outer);
            var original=T("Design.UnknownPinHelper").GetMethod("GetOriginalDomain",flags)!;
            Check(original.Invoke(null,[inner])!.ToString()=="Mux","bundle origin traverses native Container jumper");
            E(outer,"Domain","Mux"); E(inner,"Domain","Mux");
            Check(can.Invoke(null,[inner,sinkBundle]) is true,"bundle can leave the Container through its paired connector");
            Link(inner,sinkBundle);
            var getPins=T("Extract.DesignNet").GetMethod("GetConnectedPins",flags,null,[T("Design.Pin"),T("Design.Mux.ConnectionRules")],null)!;
            // Provider is a field on native Pin.
            var sourceOwner=sourceBundle.GetType().GetField("Provider")!.GetValue(sourceBundle)!;
            var sinkOwner=sinkBundle.GetType().GetField("Provider")!.GetValue(sinkBundle)!;
            var a=((IEnumerable)P(sourceOwner,"Pins")).Cast<object>().Single(p=>!ReferenceEquals(p,sourceBundle));
            var b=((IEnumerable)P(sinkOwner,"Pins")).Cast<object>().Single(p=>!ReferenceEquals(p,sinkBundle));
            var net=((IEnumerable)getPins.Invoke(null,[a,Enum.Parse(T("Design.Mux.ConnectionRules"),"Default")])!).Cast<object>().ToArray();
            Check(net.Contains(b),"native extraction finds matching channel across Container bundle");
            Check(errors.Count==0,"native bundle hooks report no failures");
        }
        finally { harmony.UnpatchAll(SnakeHooks.PatchId); }
        Console.WriteLine("Native model checks run in this console process only; Designer, save/reopen, undo and hardware remain untested.");
    }
}
