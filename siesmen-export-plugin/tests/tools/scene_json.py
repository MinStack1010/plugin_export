"""Serialize an ExtractedScene to the JSON format read by tests/reference_sw."""

import json


def _material(m):
    if m is None:
        return None
    return {"name": m.name, "baseColor": list(m.base_color), "metallic": m.metallic,
            "roughness": m.roughness, "alphaMode": m.alpha_mode}


def _mesh(m):
    return {"name": m.name, "positions": [list(p) for p in m.positions],
            "normals": [list(n) for n in m.normals], "indices": list(m.indices),
            "material": _material(m.material)}


def _node(n):
    return {"name": n.name, "matrix": list(n.local_transform),
            "meshes": [_mesh(m) for m in n.meshes], "children": [_node(c) for c in n.children]}


def scene_to_json(scene):
    return json.dumps({"root": _node(scene.root)})


def write_scene_json(scene, path):
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(scene_to_json(scene))
