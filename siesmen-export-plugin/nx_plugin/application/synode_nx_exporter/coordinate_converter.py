"""Port of CoordinateConverter.ConvertSolidWorksSceneToGltf.

The SW converter scales positions by ExportOptions.UnitScale (1.0) and
renormalises normals; it performs no axis conversion.  Differences:
* translations are scaled too (SW leaves them unscaled, which is only
  correct because its UnitScale is always 1);
* optional Z-up -> Y-up conversion (disabled by default for parity), applied
  as a rotation of the root node so geometry stays untouched.
"""

import math
import struct

from . import matrix
from .errors import ExportError
from .scene import ExtractedScene, ExtractedNode, ExtractedMesh


def convert_scene(source, options):
    if source is None:
        raise ExportError("The extracted scene is missing.")
    if source.root is None:
        raise ExportError("The extracted scene has no root node.")
    if not (options.unit_scale > 0):
        raise ExportError("unit_scale must be greater than zero.")
    root = _convert_node(source.root, options.unit_scale)
    if options.z_up_to_y_up:
        root.local_transform = matrix.multiply(matrix.Z_UP_TO_Y_UP, root.local_transform)
    return ExtractedScene(root=root)


def _convert_node(source, scale):
    node = ExtractedNode(name=source.name,
                         local_transform=matrix.with_scaled_translation(source.local_transform, scale))
    for mesh in source.meshes:
        node.meshes.append(_convert_mesh(mesh, scale))
    for child in source.children:
        node.children.append(_convert_node(child, scale))
    return node


def _f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def normalize_f32(n):
    """Emulates System.Numerics.Vector3.Normalize (single precision, v / |v|).

    Every operation is rounded to float32 exactly like the SW converter, so
    the written normals are bit-identical to the SOLIDWORKS pipeline output.
    """
    x, y, z = _f32(n[0]), _f32(n[1]), _f32(n[2])
    length_sq = _f32(_f32(_f32(x * x) + _f32(y * y)) + _f32(z * z))
    length = _f32(math.sqrt(length_sq))
    if length == 0.0 or not math.isfinite(length):
        return None
    return (_f32(x / length), _f32(y / length), _f32(z / length))


def _convert_mesh(source, scale):
    if scale == 1.0:
        positions = list(source.positions)
    else:
        positions = [(p[0] * scale, p[1] * scale, p[2] * scale) for p in source.positions]
    normals = []
    for n in source.normals:
        un = normalize_f32(n)
        normals.append(un if un is not None else n)
    return ExtractedMesh(name=source.name, positions=positions, normals=normals,
                         indices=list(source.indices), material=source.material,
                         share_key=source.share_key)
