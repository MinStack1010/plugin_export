"""NX independent export pipeline: scene -> converter -> validation -> GLB file.

Mirrors ExportGlbCommand: Extract -> CoordinateConverter -> GltfSceneWriter.
"""

from dataclasses import dataclass, field
from typing import List

from .coordinate_converter import convert_scene
from .errors import EmptyGeometryError
from .gltf_writer import build_glb
from .mesh_builder import validate_mesh
from .output_path import write_atomic


@dataclass
class ExportResult:
    path: str
    node_count: int
    mesh_count: int
    triangle_count: int
    byte_count: int
    warnings: List[str] = field(default_factory=list)


def export_scene_to_glb(scene, output_path, options, log=None):
    converted = convert_scene(scene, options)
    for node in converted.root.iter_nodes():
        for mesh in node.meshes:
            validate_mesh(mesh)
    triangles = converted.triangle_count()
    if triangles == 0:
        raise EmptyGeometryError(
            "The model contains no exportable solid geometry (no visible solid bodies were found "
            "or none could be tessellated). No GLB file was written.")
    data = build_glb(converted, options)
    write_atomic(output_path, data)
    result = ExportResult(path=output_path, node_count=converted.node_count(),
                          mesh_count=converted.mesh_count(), triangle_count=triangles,
                          byte_count=len(data), warnings=list(log.warnings) if log else [])
    if log:
        log.info("GLB written: {} ({} bytes, {} nodes, {} meshes, {} triangles)".format(
            output_path, len(data), result.node_count, result.mesh_count, triangles))
    return result
