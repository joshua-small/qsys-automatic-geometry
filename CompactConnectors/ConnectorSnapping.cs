using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using HarmonyLib;

namespace Designer.Modules.CompactConnectors;

// Reuse the native Wiring domain used by Signal Snakes and wire breakpoints.
// Default object snapping also gains the main-body bottom; full drawer bounds stay intact.
internal sealed class ConnectorSnapping
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type connector, component, provider;
    private readonly MethodInfo snapDomain, snapPoints;
    private readonly FieldInfo bodyHeight;
    private readonly PropertyInfo position, size;
    private readonly ConstructorInfo point;
    private readonly int wiring;
    private static ConnectorSnapping? active;
    private static int failures;

    internal ConnectorSnapping(Assembly host)
    {
        Type T(string name) => host.GetType("QSC.QSys." + name, true)!;
        connector = T("Graphic.ContainerPin"); component = T("Graphic.Component"); provider = T("Graphic.PinProvider");
        snapDomain = T("Graphic.Object").GetProperty("SnapDomain", Instance)!.GetMethod!;
        var domain = T("Tools.SnapTool+Domain");
        if (Enum.GetUnderlyingType(domain) != typeof(int)) throw new InvalidOperationException("Unexpected snap domain storage.");
        wiring = Convert.ToInt32(Enum.Parse(domain, "Wiring"));
        var toolPoint = T("Tools.ToolPoint");
        point = toolPoint.GetConstructor([typeof(Point)]) ?? throw new MissingMethodException("ToolPoint(Point)");
        snapPoints = provider.GetMethod("GetSnapPoints", Instance, null,
            [typeof(List<>).MakeGenericType(toolPoint).MakeByRefType(), domain], null) ?? throw new MissingMethodException("GetSnapPoints");
        bodyHeight = provider.GetField("_BuiltInHeight", Instance) ?? throw new MissingFieldException("_BuiltInHeight");
        position = provider.GetProperty("Position", Instance)!; size = provider.GetProperty("Size", Instance)!;
        foreach (var target in new[] { snapDomain, snapPoints })
            if (Harmony.GetPatchInfo(target)?.Owners.Any() == true) throw new InvalidOperationException("Snapping is already patched: " + target.Name);
    }

    internal void Install(Harmony harmony)
    {
        active = this;
        harmony.Patch(snapDomain, transpiler: new HarmonyMethod(typeof(ConnectorSnapping), nameof(ChooseSnapDomain)));
        harmony.Patch(snapPoints, postfix: new HarmonyMethod(typeof(ConnectorSnapping), nameof(AddBodyBottom)));
    }

    private static bool IsConnector(object instance) => active!.connector.IsInstanceOfType(instance);

    private static IEnumerable<CodeInstruction> ChooseSnapDomain(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.Select(c => new CodeInstruction(c)).ToList();
        if (code.Count == 0) throw new InvalidOperationException("Empty native snap-domain getter.");
        var original = generator.DefineLabel(); code[0].labels.Add(original);
        yield return new(OpCodes.Ldarg_0);
        yield return new(OpCodes.Call, typeof(ConnectorSnapping).GetMethod(nameof(IsConnector), BindingFlags.Static | BindingFlags.NonPublic)!);
        yield return new(OpCodes.Brfalse, original);
        yield return new(OpCodes.Ldc_I4, active!.wiring);
        yield return new(OpCodes.Ret);
        foreach (var instruction in code) yield return instruction;
    }

    private static void AddBodyBottom(object __instance, object[] __args)
    {
        try { AddBodyBottomCore(__instance, __args); }
        catch (Exception error)
        {
            if (failures++ < 5) ConnectorHooks.Report?.Invoke("snap-target-error", new { error = error.ToString() });
        }
    }

    private static void AddBodyBottomCore(object __instance, object[] __args)
    {
        var h = active!;
        // Wiring retains the host's exact pin centers and Default retains all four outer corners.
        if (__args[1].ToString() != "Default" || !h.component.IsInstanceOfType(__instance)) return;
        var height = (double)h.bodyHeight.GetValue(__instance)!;
        var drawn = (Size)h.size.GetValue(__instance)!;
        if (!double.IsFinite(height) || height <= 0 || height >= drawn.Height || !double.IsFinite(drawn.Width)) return;
        var origin = (Point)h.position.GetValue(__instance)!;
        var points = (IList)__args[0];
        points.Add(h.point.Invoke([new Point(origin.X, origin.Y + height)]));
        points.Add(h.point.Invoke([new Point(origin.X + drawn.Width, origin.Y + height)]));
    }
}
