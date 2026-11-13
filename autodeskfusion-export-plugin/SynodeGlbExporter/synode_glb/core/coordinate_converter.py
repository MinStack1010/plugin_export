"""Converts an extracted Fusion scene into the glTF output space.

SolidWorks reports geometry in metres and is Y-up, which already matches glTF, so
the SolidWorks CoordinateConverter only applied UnitScale (= 1). Fusion's API
always reports lengths in centimetres, so here the scale (default 0.01) is
applied to vertex positions *and* to node translations. Optionally a Z-up design
is rotated into glTF's Y-up convention.

The axis rotation is applied by conjugating every local transform
(L' = R * L * R^-1) and rotating every vertex/normal by R. This keeps the root
node transform identical to the source (identity) while producing the same world
positions as a single rotation on the root.
"""

import math
from dataclasses import dataclass

from . import matrix as mx
from .scene import ExtractedMesh, ExtractedNode, ExtractedScene

# Rotation of -90 degrees about X: (x, y, z) -> (x, z, -y). Maps +Z (up) to +Y.
Z_UP_TO_Y_UP = (
    1.0, 0.0, 0.0, 0.0,
    0.0, 0.0, 1.0, 0.0,
    0.0, -1.0, 0.0, 0.0,
    0.0, 0.0, 0.0, 1.0,
)


@dataclass
class ConversionOptions:
    # Multiplier from Fusion internal units (cm) to output units. 0.01 => metres.
    unit_scale: float = 0.01
    convert_z_up_to_y_up: bool = False


def convert_fusion_scene_to_gltf(source: ExtractedScene, options: ConversionOptions) -> ExtractedScene:
    if source is None:
        raise ValueError("source must not be None")
    if options is None:
        raise ValueError("options must not be None")
    if source.root is None:
        raise ValueError("The extracted scene has no root node.")
    if not (options.unit_scale > 0) or math.isinf(options.unit_scale):
        raise ValueError("unit_scale must be a positive finite number.")

    rotation = Z_UP_TO_Y_UP if options.convert_z_up_to_y_up else None
    return ExtractedScene(root=_convert_node(source.root, options.unit_scale, rotation))


def _convert_transform(m, scale, rotation):
    if rotation is not None:
        m = mx.multiply(mx.multiply(rotation, m), mx.transpose(rotation))
    m = list(m)
    m[3] *= scale
    m[7] *= scale
    m[11] *= scale
    return tuple(m)


def _convert_node(source: ExtractedNode, scale: float, rotation) -> ExtractedNode:
    node = ExtractedNode(
        name=source.name,
        local_transform=_convert_transform(source.local_transform, scale, rotation),
    )
    for mesh in source.meshes:
        node.meshes.append(_convert_mesh(mesh, scale, rotation))
    for child in source.children:
        node.children.append(_convert_node(child, scale, rotation))
    return node


def _convert_mesh(source: ExtractedMesh, scale: float, rotation) -> ExtractedMesh:
    mesh = ExtractedMesh(name=source.name, material=source.material)
    p = source.positions
    n = source.normals
    positions = [0.0] * len(p)
    normals = [0.0] * len(n)

    if rotation is None:
        for i in range(len(p)):
            positions[i] = p[i] * scale
        for i in range(0, len(n), 3):
            x, y, z = n[i], n[i + 1], n[i + 2]
            length = math.sqrt(x * x + y * y + z * z) or 1.0
            normals[i], normals[i + 1], normals[i + 2] = x / length, y / length, z / length
    else:
        for i in range(0, len(p), 3):
            x, y, z = mx.transform_vector(rotation, p[i], p[i + 1], p[i + 2])
            positions[i], positions[i + 1], positions[i + 2] = x * scale, y * scale, z * scale
        for i in range(0, len(n), 3):
            x, y, z = mx.transform_vector(rotation, n[i], n[i + 1], n[i + 2])
            length = math.sqrt(x * x + y * y + z * z) or 1.0
            normals[i], normals[i + 1], normals[i + 2] = x / length, y / length, z / length

    mesh.positions = positions
    mesh.normals = normals
    mesh.indices = list(source.indices)
    return mesh
