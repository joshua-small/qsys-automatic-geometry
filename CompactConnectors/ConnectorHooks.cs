using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Media;
using HarmonyLib;

namespace Designer.Modules.CompactConnectors;

internal sealed class ConnectorBindings
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    internal readonly Type GraphicProvider, ModelConnector;
    internal readonly FieldInfo ModelSize;
    internal readonly PropertyInfo ProviderModel;
    internal readonly MethodInfo ConnectorCreatePins, ConnectorRedraw, ConnectorAttach, ContainerCreate, ChannelCreate;

    internal ConnectorBindings(Assembly host)
    {
        Type T(string name) => host.GetType("QSC.QSys." + name, throwOnError: true)!;
        FieldInfo F(Type type, string name) => type.GetField(name, Instance) ?? throw new MissingFieldException(type.FullName, name);
        PropertyInfo P(Type type, string name) => type.GetProperty(name, Instance) ?? throw new MissingMemberException(type.FullName, name);
        MethodInfo M(Type type, string name) => type.GetMethod(name, Instance | BindingFlags.Static | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException(type.FullName, name);
        GraphicProvider = T("Graphic.PinProvider");
        ModelConnector = T("Design.ContainerPin");
        ModelSize = F(T("Design.Object"), "_Size");
        ProviderModel = P(GraphicProvider, "InnerPinProvider");
        ConnectorCreatePins = M(T("Graphic.ContainerPin"), "_CreatePins");
        ConnectorRedraw = M(T("Graphic.ContainerPin"), "Redraw");
        ConnectorAttach = M(ModelConnector, "_Attach");
        ContainerCreate = M(T("Design.ContainerHelper"), "CreatePins");
        ChannelCreate = M(T("Design.ChannelGroupHelper"), "CreateInternalPin");
        if (ModelSize.FieldType != typeof(Size))
            throw new InvalidOperationException("Unexpected host geometry field types.");
    }
}

internal static class ConnectorHooks
{
    internal const string PatchId = "qsys.mods.compact-connectors.geometry";
    internal static ConnectorBindings? Host;
    internal static Action<string, object>? Report;
    private static int connectorReports, failures;

    internal static Harmony Install(ConnectorBindings host)
    {
        Host = host;
        var harmony = new Harmony(PatchId);
        var targets = new[] { host.ConnectorAttach, host.ConnectorCreatePins, host.ConnectorRedraw, host.ContainerCreate, host.ChannelCreate };
        foreach (var target in targets)
            if (Harmony.GetPatchInfo(target)?.Owners.Any() == true)
                throw new InvalidOperationException("Another runtime patch already changes " + target.DeclaringType?.FullName + "." + target.Name);
        try
        {
            // Check the two creation helpers before installing any patches.
            foreach (var target in new[] { host.ContainerCreate, host.ChannelCreate })
                _ = TenUnitCreation(PatchProcessor.GetOriginalInstructions(target)).ToList();
            _ = CenterConnectorPins(PatchProcessor.GetOriginalInstructions(host.ConnectorCreatePins)).ToList();
            _ = CenterConnectorText(PatchProcessor.GetOriginalInstructions(host.ConnectorRedraw)).ToList();
            harmony.Patch(host.ConnectorAttach, prefix: Hook(nameof(NormalizeConnectorModel)));
            harmony.Patch(host.ConnectorCreatePins, prefix: Hook(nameof(NormalizeConnectorGraphic)), transpiler: Hook(nameof(CenterConnectorPins)));
            harmony.Patch(host.ConnectorRedraw, transpiler: Hook(nameof(CenterConnectorText)));
            harmony.Patch(host.ContainerCreate, transpiler: Hook(nameof(TenUnitCreation)));
            harmony.Patch(host.ChannelCreate, transpiler: Hook(nameof(TenUnitCreation)));
            return harmony;
        }
        catch
        {
            harmony.UnpatchAll(PatchId);
            Host = null;
            throw;
        }
    }

    private static HarmonyMethod Hook(string name) => new(typeof(ConnectorHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!);

    internal static void NormalizeConnectorGraphic(object __instance)
    {
        try { NormalizeConnectorModel(Host!.ProviderModel.GetValue(__instance)!); }
        catch (Exception error) { Failure("connector-graphic", error); }
    }

    internal static void NormalizeConnectorModel(object __instance)
    {
        try
        {
            var h = Host!;
            if (!h.ModelConnector.IsInstanceOfType(__instance)) return;
            var size = (Size)h.ModelSize.GetValue(__instance)!;
            if (size.IsEmpty || !double.IsFinite(size.Width) || size.Width <= 0 || size.Height == 10.0) return;
            // This is a derived geometry normalization at attach/create time, not an editing command.
            // Preserve width and Position; avoid inserting an undo operation during deserialization.
            h.ModelSize.SetValue(__instance, new Size(size.Width, 10.0));
            if (connectorReports++ < 20) Report?.Invoke("connector-height-adjusted", new { previous = size.Height, height = 10.0 });
        }
        catch (Exception error) { Failure("connector-model", error); }
    }

    internal static IEnumerable<CodeInstruction> TenUnitCreation(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(x => new CodeInstruction(x)).ToList();
        var matches = code.Where(x => x.LoadsConstant(12L)).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("Expected exactly one 12-unit connector creation constant, found " + matches.Count);
        // The inspected local variable supplies both Size.Height and new-connector vertical pitch.
        matches[0].opcode = OpCodes.Ldc_I4_S;
        matches[0].operand = (sbyte)10;
        return code;
    }

    internal static IEnumerable<CodeInstruction> CenterConnectorPins(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(x => new CodeInstruction(x)).ToList();
        var constructor = typeof(Point).GetConstructor([typeof(double), typeof(double)])!;
        var helper = typeof(ConnectorHooks).GetMethod(nameof(CenteredConnectorPin), BindingFlags.NonPublic | BindingFlags.Static)!;
        int count = 0;
        for (int i = code.Count - 1; i >= 0; i--)
        {
            if (code[i].opcode != OpCodes.Newobj || !Equals(code[i].operand, constructor)) continue;
            // Native input/output centers use y=6 for a 12-unit body. Change the
            // model center before AddPin and native positioning/wire updates run.
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
            count++;
        }
        if (count != 2) throw new InvalidOperationException("Expected two connector pin-center constructions, found " + count);
        return code;
    }

    private static Point CenteredConnectorPin(Point original) =>
        new(original.X, original.Y + (10.0 - 12.0) / 2.0);

    internal static IEnumerable<CodeInstruction> CenterConnectorText(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(x => new CodeInstruction(x)).ToList();
        var draw = typeof(DrawingContext).GetMethod(nameof(DrawingContext.DrawText), [typeof(FormattedText), typeof(Point)])!;
        var helper = typeof(ConnectorHooks).GetMethod(nameof(DrawCenteredConnectorText), BindingFlags.NonPublic | BindingFlags.Static)!;
        int count = 0;
        for (int i = code.Count - 1; i >= 0; i--)
        {
            if (!code[i].Calls(draw)) continue;
            // Leave native FormattedText, size 10, brush and horizontal alignment
            // untouched. Preserve branch labels on the first replacement instruction.
            var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(code[i].labels);
            loadThis.blocks.AddRange(code[i].blocks);
            code[i] = loadThis;
            code.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
            count++;
        }
        if (count != 2) throw new InvalidOperationException("Expected two connector label DrawText calls, found " + count);
        return code;
    }

    private static void DrawCenteredConnectorText(DrawingContext context, FormattedText text, Point origin, object graphic)
    {
        Point adjusted = origin;
        try
        {
            var h = Host!;
            var size = (Size)h.ModelSize.GetValue(h.ProviderModel.GetValue(graphic)!)!;
            // Measure ink only; DrawText still renders the original glyphs using
            // normal WPF text rendering. No scaling or font-size substitution.
            Rect ink = text.BuildGeometry(new Point()).Bounds;
            if (!ink.IsEmpty && double.IsFinite(ink.Top) && double.IsFinite(ink.Height))
                adjusted.Y = (size.Height - ink.Height) / 2.0 - ink.Top;
        }
        catch (Exception error) { Failure("connector-text", error); }
        context.DrawText(text, adjusted);
    }

    private static void Failure(string operation, Exception error)
    {
        if (failures++ < 10) Report?.Invoke("geometry-error", new { operation, error = error.GetBaseException().Message });
    }
}
