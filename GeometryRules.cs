namespace Designer.Modules.AutomaticGeometry;

internal static class GeometryRules
{
    internal const double DefaultTextWidth = 54;
    internal const double PinLabelPadding = 2;
    internal const double ConnectorHeight = 10;

    internal static double TextWidth(IEnumerable<double> pinWidths)
    {
        double width = DefaultTextWidth;
        foreach (double measured in pinWidths)
        {
            if (!double.IsFinite(measured) || measured < 0)
                throw new ArgumentOutOfRangeException(nameof(pinWidths));
            width = Math.Max(width, Math.Ceiling(measured) + PinLabelPadding);
        }
        return width;
    }
}
