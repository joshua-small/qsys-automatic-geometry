using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Designer.Modules.SnakeConnectors;

internal sealed class SnakeHooks
{
    internal const string PatchId = "qsys.mods.snake-connectors";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private readonly Type container, connector, mux, pinType, pinSet;
    private readonly FieldInfo provider, direction, userCreated, softTyping;
    private readonly PropertyInfo domain, pins, muxPin, index;
    private readonly MethodInfo connected, canWire, canWireAny, originalDomain, compatible, checkNet;
    private readonly object ignoreMux, defaultRules;
    private readonly int muxValue;
    private static SnakeHooks? active;
    internal Action<Exception>? Report;

    internal SnakeHooks(Assembly host)
    {
        Type T(string name) => host.GetType("QSC.QSys." + name, true)!;
        FieldInfo F(Type t, string name) => t.GetField(name, All) ?? throw new MissingFieldException(t.FullName, name);
        PropertyInfo P(Type t, string name) => t.GetProperty(name, All) ?? throw new MissingMemberException(t.FullName, name);
        MethodInfo M(Type t, string name, params Type[] args) => t.GetMethod(name, All, null, args, null) ?? throw new MissingMethodException(t.FullName, name);
        container=T("Design.Container"); connector=T("Design.ContainerPin"); mux=T("Design.Mux.MuxBase"); pinType=T("Design.Pin");
        pinSet=typeof(HashSet<>).MakeGenericType(pinType);
        provider=F(pinType,"Provider"); direction=F(pinType,"Direction"); userCreated=F(pinType,"UserCreated"); softTyping=F(connector,"AllowSoftTyping");
        domain=P(pinType,"WiringDomain"); pins=P(T("Design.PinProvider"),"Pins"); muxPin=P(mux,"MuxedPin"); index=P(pinType,"GraphicIndex");
        var rules=T("Design.Mux.ConnectionRules"); ignoreMux=Enum.Parse(rules,"IgnoreMux"); defaultRules=Enum.Parse(rules,"Default");
        connected=M(T("Extract.DesignNet"),"GetConnectedPins",pinType,rules);
        var tool=T("Tools.WireTool");
        canWire=M(tool,"CanWireMux",pinType,pinType); compatible=M(tool,"AreCompatibleToWired",pinType,pinType);
        canWireAny=M(tool,"CanWire",pinType,pinType);
        checkNet=M(tool,"CheckNet",pinSet);
        originalDomain=M(T("Design.UnknownPinHelper"),"GetOriginalDomain",pinType);
        if(Enum.GetUnderlyingType(domain.PropertyType)!=typeof(int) || originalDomain.ReturnType!=domain.PropertyType)
            throw new InvalidOperationException("Unexpected signal-domain storage.");
        muxValue=Convert.ToInt32(Enum.Parse(domain.PropertyType,"Mux"));
    }

    internal Harmony Install()
    {
        foreach(var method in new[]{canWire,canWireAny,originalDomain})
            if(Harmony.GetPatchInfo(method)?.Owners.Any()==true) throw new InvalidOperationException("Snake compatibility is already patched.");
        active=this; var harmony=new Harmony(PatchId);
        try
        {
            harmony.Patch(canWire,prefix:new HarmonyMethod(typeof(SnakeHooks),nameof(CanWirePrefix)));
            harmony.Patch(canWireAny,postfix:new HarmonyMethod(typeof(SnakeHooks),nameof(GuardChannelDomain)));
            harmony.Patch(originalDomain,transpiler:new HarmonyMethod(typeof(SnakeHooks),nameof(OriginalDomainPatch)));
            return harmony;
        }
        catch { harmony.UnpatchAll(PatchId); active=null; throw; }
    }

    private object Owner(object pin) => provider.GetValue(pin)!;
    private string Domain(object pin) => domain.GetValue(pin)!.ToString()!;
    private string Direction(object pin) => direction.GetValue(pin)!.ToString()!;
    private bool Boundary(object pin)
    {
        var owner=Owner(pin);
        // Exact types exclude ChannelGroup, ChannelGroupPin, plugin pins and derived providers.
        return !(bool)userCreated.GetValue(pin)! && (owner.GetType()==container
            || owner.GetType()==connector && (bool)softTyping.GetValue(owner)!);
    }
    private bool Bundle(object pin) => mux.IsInstanceOfType(Owner(pin)) && ReferenceEquals(muxPin.GetValue(Owner(pin)),pin);
    private bool ScalarDomain(object pin) => Domain(pin) is "Unknown" or "Audio" or "Control";
    private object[] Net(object pin, bool ignore=true) => ((IEnumerable)connected.Invoke(null,[pin,ignore?ignoreMux:defaultRules])!).Cast<object>().ToArray();
    private object Set(IEnumerable<object> values)
    {
        var set=Activator.CreateInstance(pinSet)!; var add=pinSet.GetMethod("Add")!;
        foreach(var value in values) add.Invoke(set,[value]);
        return set;
    }

    private static bool CanWirePrefix(object __0, object __1, ref bool __result)
    {
        var h=active!;
        try
        {
            var left=h.Net(__0); var right=h.Net(__1);
            if(!left.Concat(right).Any(h.Boundary)) return true;
            // Only intercept the Snake-domain route. Individual scalar channel wiring stays native.
            __result=h.Validate(left,right); return false;
        }
        catch(Exception error) { h.Report?.Invoke(error); __result=false; return false; }
    }

    internal bool Validate(object[] left, object[] right)
    {
        if(left.Intersect(right,ReferenceEqualityComparer.Instance).Any()) return false; // No new bundle loops.
        var net=left.Concat(right).Distinct(ReferenceEqualityComparer.Instance).ToArray();
        if(net.Length>1024 || net.Any(p=>!Boundary(p)&&!Bundle(p))) return false;
        if(net.Where(Boundary).Any(p=>Domain(p) is not ("Unknown" or "Mux"))) return false;
        var bundles=net.Where(Bundle).ToArray();
        if(bundles.Length==0 || bundles.Count(p=>Direction(p)=="Output")>1 || bundles.Count(p=>Direction(p)=="Input")>1) return false;
        var channels=bundles.Select(b=>((IEnumerable)pins.GetValue(Owner(b))!).Cast<object>()
            .Where(p=>!ReferenceEquals(p,b)).OrderBy(p=>Convert.ToUInt32(index.GetValue(p))).ToArray()).ToArray();
        int count=channels[0].Length;
        if(count is <1 or >256 || channels.Any(c=>c.Length!=count || !c.Select(p=>Convert.ToUInt32(index.GetValue(p))).SequenceEqual(Enumerable.Range(0,count).Select(i=>(uint)i)))) return false;
        for(int i=0;i<count;i++)
        {
            var row=channels.Select(c=>c[i]).ToArray();
            for(int a=0;a<row.Length;a++) for(int b=a+1;b<row.Length;b++)
                if(compatible.Invoke(null,[row[a],row[b]]) is not true) return false;
            var all=row.SelectMany(p=>Net(p,false)).Distinct(ReferenceEqualityComparer.Instance).ToArray();
            // MediaCast and device-specific graph rules require their own acceptance work.
            if(all.Any(p=>!ScalarDomain(p))) return false;
            if(checkNet.Invoke(null,[Set(all)]) is not true) return false;
        }
        return true;
    }

    private static void GuardChannelDomain(object __0, object __1, ref bool __result)
    {
        if(!__result) return;
        var h=active!;
        try
        {
            var net=h.Net(__0,false).Concat(h.Net(__1,false)).Distinct(ReferenceEqualityComparer.Instance).ToArray();
            // Bundle endpoints themselves are validated by CanWireMux; this guards later
            // scalar connections to a channel already routed through a Container bundle.
            if(net.Any(h.Bundle) || net.All(h.ScalarDomain)) return;
            foreach(var pin in net.Where(p=>h.mux.IsInstanceOfType(h.Owner(p))))
                if(h.muxPin.GetValue(h.Owner(pin)) is { } bundle && h.Net(bundle).Any(h.Boundary))
                { __result=false; return; }
        }
        catch(Exception error) { h.Report?.Invoke(error); __result=false; }
    }

    private static bool HasBundleOrigin(object pin)
    {
        var h=active!;
        try
        {
            if(!h.Boundary(pin)) return false;
            var net=h.Net(pin);
            return net.Length<=1024 && net.All(p=>h.Bundle(p)||(h.Boundary(p)&&h.Domain(p) is ("Unknown" or "Mux"))) && net.Any(h.Bundle);
        }
        catch(Exception error) { h.Report?.Invoke(error); return false; }
    }

    private static IEnumerable<CodeInstruction> OriginalDomainPatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
        if(code.Count==0) throw new InvalidOperationException("Empty native original-domain method.");
        var original=generator.DefineLabel(); code[0].labels.Add(original);
        yield return new(OpCodes.Ldarg_0);
        yield return new(OpCodes.Call,typeof(SnakeHooks).GetMethod(nameof(HasBundleOrigin),All)!);
        yield return new(OpCodes.Brfalse,original);
        yield return new(OpCodes.Ldc_I4,active!.muxValue);
        yield return new(OpCodes.Ret);
        foreach(var instruction in code) yield return instruction;
    }
}
