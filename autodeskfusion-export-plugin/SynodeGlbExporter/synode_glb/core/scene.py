"""CAD-independent scene model.

This mirrors the data model of the SolidWorks plugin (ExtractedScene, ExtractedNode,
ExtractedMesh, ExtractedMaterial) so that the same extraction -> conversion -> GLB
pipeline can be used.

Conventions
-----------
* Matrices are 16-element tuples in row-major order using the column-vector
  convention (p' = M * p), i.e. the translation is stored at indices 3, 7 and 11.
  This is the same layout as ``adsk.core.Matrix3D.getCell(row, col)``.
* Positions / normals are flat lists ``[x0, y0, z0, x1, y1, z1, ...]``.
* Indices are a flat list of vertex indices, three per triangle.
* Colours are linear RGBA floats in the range 0..1 (glTF baseColorFactor).
"""

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

IDENTITY: Tuple[float, ...] = (
    1.0, 0.0, 0.0, 0.0,
    0.0, 1.0, 0.0, 0.0,
    0.0, 0.0, 1.0, 0.0,
    0.0, 0.0, 0.0, 1.0,
)


class AlphaMode:
    OPAQUE = "OPAQUE"
    BLEND = "BLEND"
    MASK = "MASK"


@dataclass
class ExtractedMaterial:
    name: Optional[str] = None
    base_colour: Tuple[float, float, float, float] = (0.8, 0.8, 0.8, 1.0)
    metallic: float = 0.0
    roughness: float = 0.0
    alpha: str = AlphaMode.OPAQUE


@dataclass
class ExtractedMesh:
    name: Optional[str] = None
    positions: List[float] = field(default_factory=list)
    normals: List[float] = field(default_factory=list)
    indices: List[int] = field(default_factory=list)
    material: Optional[ExtractedMaterial] = None

    @property
    def vertex_count(self) -> int:
        return len(self.positions) // 3

    @property
    def triangle_count(self) -> int:
        return len(self.indices) // 3


@dataclass
class ExtractedNode:
    name: Optional[str] = None
    local_transform: Tuple[float, ...] = IDENTITY
    meshes: List[ExtractedMesh] = field(default_factory=list)
    children: List["ExtractedNode"] = field(default_factory=list)


@dataclass
class ExtractedScene:
    root: Optional[ExtractedNode] = None


def validate_mesh(mesh: ExtractedMesh) -> None:
    """Same checks as SolidWorksGeometryExtractor.ValidateExtractedMesh."""
    from .errors import ExportError

    if mesh.vertex_count == 0:
        raise ExportError("Mesh '{}' has no vertices.".format(mesh.name))
    if mesh.triangle_count == 0:
        raise ExportError("Mesh '{}' has no triangles.".format(mesh.name))
    if len(mesh.positions) % 3 != 0 or len(mesh.indices) % 3 != 0:
        raise ExportError("Mesh '{}' has malformed position or index arrays.".format(mesh.name))
    if len(mesh.normals) != len(mesh.positions):
        raise ExportError("Mesh '{}' has mismatched position/normal count.".format(mesh.name))
    count = mesh.vertex_count
    for index in mesh.indices:
        if index < 0 or index >= count:
            raise ExportError("Mesh '{}' has invalid triangle index: {}.".format(mesh.name, index))


def scene_statistics(scene: ExtractedScene) -> dict:
    """Counts nodes, meshes, vertices and triangles of a scene."""
    stats = {"nodes": 0, "meshes": 0, "vertices": 0, "triangles": 0}
    if scene is None or scene.root is None:
        return stats
    stack = [scene.root]
    while stack:
        node = stack.pop()
        stats["nodes"] += 1
        for mesh in node.meshes:
            if mesh.vertex_count and mesh.triangle_count:
                stats["meshes"] += 1
                stats["vertices"] += mesh.vertex_count
                stats["triangles"] += mesh.triangle_count
        stack.extend(node.children)
    return stats
