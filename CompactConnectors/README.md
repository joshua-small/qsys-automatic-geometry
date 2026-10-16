# Compact Connectors

An independent source candidate for 10-unit Container and Channel Group connectors. It centers their pins and label ink at 5 units, preserves existing positions and widths, and uses 10-unit spacing for newly generated connectors. Component Auto Width is optional.

Connector dragging reuses Designer's **Wiring** snap domain, used by Signal Snakes and wire breakpoints. The native engine supplies actual pin centers, tolerance and gesture handling. Mixed selections follow the native rule: Wiring applies only when every selected object requests that domain.

For ordinary object snapping, an open component drawer adds the two bottom corners of the main body as targets. Full drawer corners remain available. Wiring targets remain native pin centers. Closed drawers have no extra body targets.

[Better Packing](https://github.com/joshua-small/qsys-better-packing) supplies **Pack to Top Pins** and **Pack to Bottom Pins**. Traditional boundary packing retains its meaning, so it can still leave a 10-unit connector one unit from a component pin.

Removing the module restores native calculations on restart. Connector heights saved in a design may remain 10 units; removal is not a bulk geometry migration. Preserve originals when testing.

This candidate requires Mods Core and the exact inspected Designer 10.5 host. Package/catalog approval and native acceptance are pending. See [migration and verification](../README.md).
