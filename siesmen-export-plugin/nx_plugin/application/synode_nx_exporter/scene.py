"""Intermediate scene model (port of SW ExtractedScene/Node/Mesh/Material).

Units: metres.  Matrices: 16 floats, glTF column-major order (identical to
the memory layout of a System.Numerics.Matrix4x4 used by the SW plugin).
"""

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

from . import matrix

ALPHA_OPAQUE = "OPAQUE"
ALPHA_BLEND = "BLEND"
ALPHA_MASK = "MASK"


@dataclass
class ExtractedMaterial:
    name: Optional[str] = None
    base_color: Tuple[float, float, float, float] = (0.8, 0.8, 0.8, 1.0)  # linear RGBA
    metallic: float = 0.0
    roughness: float = 0.0
    alpha_mode: str = ALPHA_OPAQUE


@dataclass
class ExtractedMesh:
    name: Optional[str] = None
    positions: List[Tuple[float, float, float]] = field(default_factory=list)
    normals: List[Tuple[float, float, float]] = field(default_factory=list)
    indices: List[int] = field(default_factory=list)
    material: Optional[ExtractedMaterial] = None
    # Identifies identical geometry+material (used only when meshes are shared).
    share_key: Optional[object] = None

    @property
    def vertex_count(self):
        return len(self.positions)

    @property
    def triangle_count(self):
        return len(self.indices) // 3


@dataclass
class ExtractedNode:
    name: Optional[str] = None
    local_transform: Tuple[float, ...] = matrix.IDENTITY
    meshes: List[ExtractedMesh] = field(default_factory=list)
    children: List["ExtractedNode"] = field(default_factory=list)

    def iter_nodes(self):
        yield self
        for child in self.children:
            yield from child.iter_nodes()


@dataclass
class ExtractedScene:
    root: Optional[ExtractedNode] = None

    def triangle_count(self):
        if self.root is None:
            return 0
        return sum(m.triangle_count for n in self.root.iter_nodes() for m in n.meshes)

    def node_count(self):
        return 0 if self.root is None else sum(1 for _ in self.root.iter_nodes())

    def mesh_count(self):
        return 0 if self.root is None else sum(len(n.meshes) for n in self.root.iter_nodes())
