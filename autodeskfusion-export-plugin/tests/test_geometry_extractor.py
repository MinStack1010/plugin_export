"""Tests for the Fusion geometry extractor using fake Fusion model objects.

The fakes reproduce only the documented API surface used by the extractor
(collections with count/item, Matrix3D.getCell, Occurrence.transform2 /
createForAssemblyContext, BRepBody / BRepFace appearances and mesh managers).
They do not prove the behaviour of the real Fusion API.
"""

import unittest

import support

adsk = support.install_fake_adsk()

from glb_reader import Glb  # noqa: E402

from synode_glb.core.coordinate_converter import ConversionOptions, convert_fusion_scene_to_gltf  # noqa: E402
from synode_glb.core.errors import ExportError  # noqa: E402
from synode_glb.core.gltf_writer import build_glb  # noqa: E402
from synode_glb.fusion.geometry_extractor import FusionGeometryExtractor  # noqa: E402

SRC = adsk.core.AppearanceSourceTypes


# --------------------------------------------------------------------- fakes
class Collection:
    def __init__(self, items=()):
        self._items = list(items)

    @property
    def count(self):
        return len(self._items)

    def item(self, index):
        return self._items[index]


class Matrix3D:
    def __init__(self, cells):
        self.cells = cells

    def getCell(self, row, col):
        return self.cells[row * 4 + col]


def translation(x, y, z):
    return Matrix3D((1.0, 0, 0, x, 0, 1.0, 0, y, 0, 0, 1.0, z, 0, 0, 0, 1.0))


class TriangleMesh:
    def __init__(self, coords, normals, indices):
        self.nodeCoordinatesAsDouble = coords
        self.normalVectorsAsDouble = normals
        self.nodeIndices = indices


class MeshManager:
    def __init__(self, owner):
        self._owner = owner

    def createMeshCalculator(self):
        owner = self._owner

        class Calculator:
            def setQuality(self, quality):
                owner.quality = quality

            def calculate(self):
                owner.tessellations += 1
                return TriangleMesh(*owner.raw())
        return Calculator()


class Appearance:
    def __init__(self, name, rgba, roughness=0.3):
        self.name = name
        self.id = "id-" + name
        self.color = adsk.core.Color(*rgba)
        self.roughness = roughness
        self.appearanceProperties = None
        self.hasTexture = False


def quad(origin, u, v, normal):
    """Two triangles (CCW with respect to normal)."""
    o = origin
    pts = [o, tuple(o[i] + u[i] for i in range(3)), tuple(o[i] + u[i] + v[i] for i in range(3)),
           tuple(o[i] + v[i] for i in range(3))]
    coords = [c for p in pts for c in p]
    return coords, list(normal) * 4, [0, 1, 2, 0, 2, 3]


class Face:
    def __init__(self, raw, appearance=None):
        self._raw = raw
        self.face_appearance = appearance
        self.tessellations = 0
        self.quality = None
        self.meshManager = MeshManager(self)

    def raw(self):
        return [list(x) for x in self._raw]

    # Native (root component) faces report their own appearance like BRepFace does.
    @property
    def appearance(self):
        return self.face_appearance if self.face_appearance is not None else self.body.appearance

    @property
    def appearanceSourceType(self):
        return SRC.FaceAppearanceSource if self.face_appearance is not None else SRC.BodyAppearanceSource


class FaceProxy:
    def __init__(self, appearance, source):
        self.appearance = appearance
        self.appearanceSourceType = source


class Body:
    def __init__(self, name, faces, appearance=None, solid=True, visible=True):
        self.name = name
        self._faces = faces
        for face in faces:
            face.body = self
        self.faces = Collection(faces)
        self.appearance = appearance
        self.isSolid = solid
        self.isLightBulbOn = visible
        self.tessellations = 0
        self.quality = None
        self.meshManager = MeshManager(self)

    def raw(self):
        coords, normals, indices = [], [], []
        for face in self._faces:
            c, n, i = face.raw()
            offset = len(coords) // 3
            coords += c
            normals += n
            indices += [k + offset for k in i]
        return coords, normals, indices

    def createForAssemblyContext(self, occurrence_proxy):
        return BodyProxy(self, occurrence_proxy.path)


class BodyProxy:
    def __init__(self, body, path):
        override = next((o.appearance_override for o in path if o.appearance_override is not None), None)
        faces = []
        for face in body._faces:
            if face.face_appearance is not None:
                faces.append(FaceProxy(face.face_appearance, SRC.FaceAppearanceSource))
            elif override is not None:
                faces.append(FaceProxy(override, SRC.OccurrenceAppearanceSource))
            else:
                faces.append(FaceProxy(body.appearance, SRC.BodyAppearanceSource))
        self.faces = Collection(faces)


class Component:
    _next_id = 0

    def __init__(self, name, bodies=(), occurrences=(), mesh_bodies=()):
        Component._next_id += 1
        self.id = "component-{}".format(Component._next_id)
        self.name = name
        self.bRepBodies = Collection(bodies)
        self.occurrences = Collection(occurrences)
        self.meshBodies = Collection(mesh_bodies)


class Occurrence:
    def __init__(self, name, component, matrix=None, appearance_override=None):
        self.name = name
        self.component = component
        self.transform2 = matrix or translation(0, 0, 0)
        self.appearance_override = appearance_override

    @property
    def path(self):
        return [self]

    def createForAssemblyContext(self, parent_proxy):
        return OccurrenceProxy(parent_proxy.path + [self])


class OccurrenceProxy:
    def __init__(self, path):
        self.path = path


class Design:
    def __init__(self, root):
        self.rootComponent = root


class Named:
    def __init__(self, name):
        self.name = name


GREY = Appearance("Steel", (128, 128, 128, 255))
BLUE = Appearance("Blue", (0, 0, 255, 255))
RED = Appearance("Red", (255, 0, 0, 255))


def box_faces(face_appearance=None):
    """Two faces of a 2 cm box. Both quads are wound against their normals,
    so the extractor's winding correction is exercised."""
    bottom = Face(quad((0, 0, 0), (2, 0, 0), (0, 2, 0), (0, 0, -1)))
    side = Face(quad((0, 0, 0), (0, 2, 0), (0, 0, 2), (-1, 0, 0)), face_appearance)
    return [bottom, side]


# --------------------------------------------------------------------- tests
class GeometryExtractorTests(unittest.TestCase):
    def test_part_document(self):
        body = Body("Body1", box_faces(), GREY)
        root = Component("Root", bodies=[body])
        extractor = FusionGeometryExtractor("high")
        scene = extractor.extract(Design(root), "Bracket v1")
        self.assertEqual(scene.root.name, "Bracket v1")
        self.assertEqual(len(scene.root.meshes), 1)
        mesh = scene.root.meshes[0]
        self.assertEqual(mesh.name, "Bracket v1_Material_0")
        self.assertEqual(mesh.material.name, "Body1_Material")
        self.assertEqual(mesh.triangle_count, 4)
        self.assertEqual(body.tessellations, 1)
        self.assertEqual(body.quality, adsk.fusion.TriangleMeshQualityOptions.HighQualityTriangleMesh)
        self.assertEqual(max(mesh.positions), 2.0)  # centimetres, unscaled at this stage

    def test_triangle_winding_follows_normals(self):
        body = Body("Body1", box_faces(), GREY)
        scene = FusionGeometryExtractor().extract(Design(Component("Root", bodies=[body])), "D")
        mesh = scene.root.meshes[0]
        p, n = mesh.positions, mesh.normals
        for t in range(0, len(mesh.indices), 3):
            a, b, c = mesh.indices[t:t + 3]
            u = [p[3 * b + k] - p[3 * a + k] for k in range(3)]
            v = [p[3 * c + k] - p[3 * a + k] for k in range(3)]
            g = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
            self.assertGreater(sum(g[k] * n[3 * a + k] for k in range(3)), 0)

    def test_nested_assembly_local_transforms(self):
        bolt = Component("Bolt", bodies=[Body("Body1", box_faces(), GREY)])
        sub = Component("Sub", occurrences=[Occurrence("Bolt:1", bolt, translation(0, 0, 5))])
        root = Component("Root", occurrences=[Occurrence("Sub:1", sub, translation(10, 0, 0))])
        extractor = FusionGeometryExtractor()
        scene = extractor.extract(Design(root), "Asm")
        sub_node = scene.root.children[0]
        bolt_node = sub_node.children[0]
        self.assertEqual((sub_node.name, bolt_node.name), ("Sub:1", "Bolt:1"))
        self.assertEqual(sub_node.local_transform[3], 10)
        self.assertEqual(bolt_node.local_transform[3], 0)   # local, not world
        self.assertEqual(bolt_node.local_transform[11], 5)
        self.assertEqual(sub_node.meshes, [])
        self.assertEqual(bolt_node.meshes[0].name, "Bolt:1_Material_0")
        self.assertEqual(extractor.report.occurrences, 2)

    def test_reused_component_tessellated_once(self):
        body = Body("Body1", box_faces(), GREY)
        bolt = Component("Bolt", bodies=[body])
        root = Component("Root", occurrences=[
            Occurrence("Bolt:1", bolt, translation(0, 0, 0)),
            Occurrence("Bolt:2", bolt, translation(5, 0, 0)),
        ])
        extractor = FusionGeometryExtractor()
        scene = extractor.extract(Design(root), "Asm")
        self.assertEqual([c.name for c in scene.root.children], ["Bolt:1", "Bolt:2"])
        self.assertEqual(body.tessellations, 1)
        self.assertEqual(extractor.report.body_instances, 2)
        m1, m2 = scene.root.children[0].meshes[0], scene.root.children[1].meshes[0]
        self.assertIsNot(m1, m2)
        self.assertEqual(m1.positions, m2.positions)

    def test_per_face_materials(self):
        body = Body("Body1", box_faces(BLUE), GREY)
        part = Component("Part", bodies=[body])
        root = Component("Root", occurrences=[Occurrence("Part:1", part)])
        scene = FusionGeometryExtractor().extract(Design(root), "Asm")
        meshes = scene.root.children[0].meshes
        self.assertEqual([m.name for m in meshes], ["Part:1_Material_0", "Part:1_Material_1"])
        self.assertEqual([m.material.name for m in meshes], ["Body1_Material", "Body1_Face_Material"])
        self.assertEqual(meshes[1].material.base_colour, (0.0, 0.0, 1.0, 1.0))
        self.assertEqual(body.tessellations, 0)  # faces are tessellated individually
        self.assertEqual([f.tessellations for f in body._faces], [1, 1])
        self.assertEqual([m.triangle_count for m in meshes], [2, 2])

    def test_equivalent_appearances_grouped(self):
        same_as_grey = Appearance("Steel copy", (128, 128, 128, 255))
        body = Body("Body1", box_faces(same_as_grey), GREY)
        scene = FusionGeometryExtractor().extract(Design(Component("Root", bodies=[body])), "D")
        self.assertEqual(len(scene.root.meshes), 1)
        self.assertEqual(scene.root.meshes[0].triangle_count, 4)

    def test_occurrence_appearance_override(self):
        bolt = Component("Bolt", bodies=[Body("Body1", box_faces(), GREY)])
        root = Component("Root", occurrences=[
            Occurrence("Bolt:1", bolt),
            Occurrence("Bolt:2", bolt, appearance_override=RED),
        ])
        scene = FusionGeometryExtractor().extract(Design(root), "Asm")
        c1 = scene.root.children[0].meshes[0].material.base_colour
        c2 = scene.root.children[1].meshes[0].material.base_colour
        self.assertAlmostEqual(c1[0], c1[2])
        self.assertEqual(c2, (1.0, 0.0, 0.0, 1.0))

    def test_nested_override_through_parent_occurrence(self):
        bolt = Component("Bolt", bodies=[Body("Body1", box_faces(), GREY)])
        sub = Component("Sub", occurrences=[Occurrence("Bolt:1", bolt)])
        root = Component("Root", occurrences=[Occurrence("Sub:1", sub, appearance_override=RED)])
        scene = FusionGeometryExtractor().extract(Design(root), "Asm")
        self.assertEqual(scene.root.children[0].children[0].meshes[0].material.base_colour, (1.0, 0.0, 0.0, 1.0))

    def test_skipped_bodies_are_reported(self):
        root = Component("Root", bodies=[
            Body("Visible", box_faces(), GREY),
            Body("Hidden", box_faces(), GREY, visible=False),
            Body("Surface", box_faces(), GREY, solid=False),
        ], mesh_bodies=[Named("Scan")])
        extractor = FusionGeometryExtractor()
        scene = extractor.extract(Design(root), "D")
        self.assertEqual(len(scene.root.meshes), 1)
        self.assertEqual(extractor.report.skipped_hidden_bodies, ["D/Hidden"])
        self.assertEqual(extractor.report.skipped_surface_bodies, ["D/Surface"])
        self.assertEqual(extractor.report.skipped_mesh_bodies, ["D/Scan"])
        self.assertEqual(len(extractor.report.warnings()), 3)

    def test_degenerate_triangle_removed(self):
        coords = [0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 0, 0]
        normals = [0, 0, 1] * 4
        face = Face((coords, normals, [0, 1, 2, 0, 1, 3]))  # second triangle is collinear
        extractor = FusionGeometryExtractor()
        scene = extractor.extract(Design(Component("Root", bodies=[Body("B", [face], GREY)])), "D")
        self.assertEqual(scene.root.meshes[0].indices, [0, 1, 2])
        self.assertEqual(extractor.report.degenerate_triangles, 1)

    def test_clockwise_triangle_flipped(self):
        face = Face(([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, -1] * 3, [0, 1, 2]))
        scene = FusionGeometryExtractor().extract(Design(Component("Root", bodies=[Body("B", [face], GREY)])), "D")
        self.assertEqual(scene.root.meshes[0].indices, [0, 2, 1])

    def test_zero_normal_repaired(self):
        face = Face(([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1, 0, 0, 0, 0, 0, 1], [0, 1, 2]))
        extractor = FusionGeometryExtractor()
        scene = extractor.extract(Design(Component("Root", bodies=[Body("B", [face], GREY)])), "D")
        self.assertEqual(scene.root.meshes[0].normals[3:6], [0.0, 0.0, 1.0])
        self.assertEqual(extractor.report.repaired_normals, 1)

    def test_missing_appearance_uses_default_material(self):
        body = Body("Body1", box_faces(), None)
        scene = FusionGeometryExtractor().extract(Design(Component("Root", bodies=[body])), "D")
        self.assertIsNone(scene.root.meshes[0].material)
        glb = Glb(build_glb(scene))
        self.assertEqual(glb.json["materials"][0]["name"], "Material")

    def test_errors(self):
        with self.assertRaises(ExportError):
            FusionGeometryExtractor().extract(None)
        with self.assertRaises(ExportError):
            FusionGeometryExtractor().extract(Design(None))
        with self.assertRaises(ValueError):
            FusionGeometryExtractor("ultra")

    def test_invalid_mesh_from_fusion(self):
        face = Face(([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1] * 3, [0, 1, 7]))
        with self.assertRaises(ExportError):
            FusionGeometryExtractor().extract(Design(Component("Root", bodies=[Body("B", [face], GREY)])), "D")

    def test_progress_callback(self):
        calls = []
        bolt = Component("Bolt", bodies=[Body("Body1", box_faces(), GREY)])
        root = Component("Root", occurrences=[Occurrence("Bolt:1", bolt), Occurrence("Bolt:2", bolt)])
        FusionGeometryExtractor(progress=lambda d, t, label: calls.append((d, t, label))).extract(Design(root), "A")
        self.assertEqual(calls, [(0, 2, "Bolt:1/Body1"), (1, 2, "Bolt:2/Body1")])

    def test_full_pipeline_to_glb(self):
        bolt = Component("Bolt", bodies=[Body("Body1", box_faces(BLUE), GREY)])
        sub = Component("Sub", occurrences=[Occurrence("Bolt:1", bolt, translation(0, 0, 5))])
        root = Component("Root", bodies=[Body("Base", box_faces(), GREY)],
                         occurrences=[Occurrence("Sub:1", sub, translation(10, 0, 0))])
        scene = FusionGeometryExtractor().extract(Design(root), "Asm v2")
        gltf_scene = convert_fusion_scene_to_gltf(scene, ConversionOptions(0.01, False))
        glb = Glb(build_glb(gltf_scene))
        glb.validate()
        self.assertEqual(glb.json["nodes"][0]["name"], "Asm v2")
        i_bolt, bolt_node = glb.node_by_name("Bolt:1")
        world = [p for child in bolt_node["children"] for p in glb.world_positions(child)]
        xs = [p[0] for p in world]
        zs = [p[2] for p in world]
        self.assertAlmostEqual(min(xs), 0.1, places=6)   # 10 cm -> 0.1 m
        self.assertAlmostEqual(max(zs), 0.07, places=6)  # 5 cm + 2 cm
        self.assertEqual(sorted(m["name"] for m in glb.json["materials"]),
                         ["Base_Material", "Body1_Face_Material", "Body1_Material"])


if __name__ == "__main__":
    unittest.main()
