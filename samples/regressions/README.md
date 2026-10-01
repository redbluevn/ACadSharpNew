# Synthetic regression fixtures

`dimassoc-apparent-intersection-AC1021.dwg` was generated from three synthetic
lines, one linear dimension and a DIMASSOC with two ApparentIntersection snaps.
The snaps refer to distinct main lines and the same intersection line. The DXF
was opened, audited with zero errors and saved as DWG 2007 by AutoCAD 2027.
It contains no client drawing data. SHA-256:
`6886e0eb1166ee1a2462572d4edbea57d0d22d2af52067c2bd0b2790f5e682b7`.

This native DWG exercises the conditional 332/74/92 tail after the snap point.
The previous reader failed on the second reference with ReadBitDouble; the
regression checks the resolved object graph without depending on handles.
