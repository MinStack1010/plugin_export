"""Extractor + command tests against the NX TEST DOUBLES (tests/fakes/fake_nx.py).

They verify the plugin logic (traversal, transforms, units, naming,
visibility rules, material grouping, error handling, files written) for the
functional scenarios 1-10 of the specification.  They do NOT prove that the
real NXOpen API behaves like the fakes - see README "Kiem thu trong NX".
"""

import os
import stat
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import test_support  # noqa: F401,E402
from test_support import NullLog, TempDir  # noqa: E402

import fake_nx as fx  # noqa: E402
from synode_nx_exporter import matrix as mx  # noqa: E402
from synode_nx_exporter.config import ExportOptions  # noqa: E402
from synode_nx_exporter.errors import NoActivePartError, UnsupportedModelError, EmptyGeometryError  # noqa: E402
from synode_nx_exporter.exporter import export_scene_to_glb  # noqa: E402
from synode_nx_exporter.nx.command import ExportCommand, SUCCESS_MESSAGE  # noqa: E402
from synode_nx_exporter.nx.extractor import NxSceneExtractor, NO_ACTIVE_PART_MESSAGE  # noqa: E402
from synode_nx_exporter.nx.ui import NxUi  # noqa: E402
from glb_reader import read_glb, world_matrices  # noqa: E402


# ------------------------------------------------------------------ fixtures
def single_part(nx, units=fx.Units.Millimeters, bodies=None):
    bodies = bodies if bodies is not None else [fx.box_body((100, 50, 20), name="BLOCK", color=2)]
    part = nx.register_part(fx.Part("block", units=units, bodies=bodies))
    nx.session.Parts.Display = part
    return part


def assembly(nx, columns_convention=False):
    """main_asm (mm)
         sub_a-1 (rot z 90, +200 x)   -> bolt-1, bolt-2 (same bolt part, different placement)
         plate-1 (inch part, 2 bodies, rot x -90)
         bracket-1 (mm)
         spacer (suppressed)
    """
    bolt = nx.register_part(fx.Part("bolt", bodies=[fx.box_body((10, 10, 40), color=2)]))
    sub_a = nx.register_part(fx.Part("sub_a"))
    plate_body2 = fx.box_body((1, 1, 1), offset=(0, 0, 1), name="TAB", color=3, translucency=40)
    plate_body2.faces[0].Color = 4   # one face with its own colour -> second material group
    plate = nx.register_part(fx.Part("plate", units=fx.Units.Inches,
                                     bodies=[fx.box_body((4, 2, 1), name="PLATE", color=2), plate_body2]))
    bracket = nx.register_part(fx.Part("bracket", bodies=[fx.box_body((30, 30, 10), color=1)]))
    spacer = nx.register_part(fx.Part("spacer", bodies=[fx.box_body((5, 5, 5))]))
    main = nx.register_part(fx.Part("main_asm"))
    root = fx.make_assembly(main)
    kw = {"columns_convention": columns_convention}
    a = fx.FakeComponent(sub_a, main, root, axes=fx.rot_z(90), origin=(200, 0, 0), **kw)
    fx.FakeComponent(bolt, main, a, origin=(0, 30, 0), **kw)
    fx.FakeComponent(bolt, main, a, axes=fx.rot_x(30), origin=(0, -30, 10), **kw)
    fx.FakeComponent(plate, main, root, axes=fx.rot_x(-90), origin=(0, 300, 50), **kw)
    fx.FakeComponent(bracket, main, root, origin=(-100, -100, 0), **kw)
    fx.FakeComponent(spacer, main, root, suppressed=True, **kw)
    nx.session.Parts.Display = main
    return main


def extract(nx, options=None, log=None):
    log = log or NullLog()
    extractor = NxSceneExtractor(nx.context(), options or ExportOptions(), log)
    return extractor.extract_display_part(), extractor, log


def node_by_name(scene, name):
    for node in scene.root.iter_nodes():
        if node.name == name:
            return node
    raise KeyError(name)


def world_of(scene, name):
    def walk(node, parent):
        world = mx.multiply(parent, node.local_transform)
        if node.name == name:
            return world
        for child in node.children:
            found = walk(child, world)
            if found is not None:
                return found
        return None
    return walk(scene.root, mx.IDENTITY)


def close(a, b, tol=1e-9):
    return all(abs(x - y) <= tol for x, y in zip(a, b))


# ------------------------------------------------------------------ extractor
class ExtractorScenarios(unittest.TestCase):
    def test_01_no_active_part(self):
        nx = fx.FakeNx()
        with self.assertRaises(NoActivePartError) as ctx:
            extract(nx)
        self.assertEqual(str(ctx.exception), NO_ACTIVE_PART_MESSAGE)

    def test_02_single_part_units_and_names(self):
        nx = fx.FakeNx()
        single_part(nx)
        scene, ex, _ = extract(nx)
        self.assertEqual(scene.root.name, "block")
        self.assertEqual(scene.root.local_transform, mx.IDENTITY)
        self.assertEqual([m.name for m in scene.root.meshes], ["block_Material_0"])
        mesh = scene.root.meshes[0]
        self.assertEqual(mesh.triangle_count, 12)
        self.assertEqual(mesh.vertex_count, 24)
        xs = [p[0] for p in mesh.positions]
        self.assertAlmostEqual(max(xs), 0.1, places=6)       # 100 mm -> 0.1 m
        self.assertEqual(mesh.material.name, "BLOCK_Material")
        self.assertEqual(mesh.material.base_color, (1.0, 0.0, 0.0, 1.0))
        # temporary facet model deleted, part never saved
        self.assertEqual(len(nx.facet.models), 0)
        self.assertFalse(nx.session.Parts.Display.saved)

    def test_02b_inch_part(self):
        nx = fx.FakeNx()
        single_part(nx, units=fx.Units.Inches, bodies=[fx.box_body((1, 1, 1))])
        scene, _, _ = extract(nx)
        self.assertAlmostEqual(max(p[2] for p in scene.root.meshes[0].positions), 0.0254, places=6)

    def test_03_assembly_hierarchy_and_suppressed(self):
        nx = fx.FakeNx()
        assembly(nx)
        scene, ex, log = extract(nx)
        self.assertEqual(scene.root.name, "main_asm")
        self.assertEqual([c.name for c in scene.root.children], ["sub_a-1", "plate-1", "bracket-1"])
        self.assertEqual(scene.root.meshes, [])
        self.assertTrue(any("Suppressed" in m for m in log.infos))
        self.assertEqual(ex.stats["components"], 5)

    def test_04_nested_assembly_names_and_transforms(self):
        nx = fx.FakeNx()
        main = assembly(nx)
        scene, _, _ = extract(nx)
        a = scene.root.children[0]
        self.assertEqual([c.name for c in a.children], ["sub_a-1/bolt-1", "sub_a-1/bolt-2"])
        self.assertEqual(a.meshes, [])  # sub-assembly part has no own bodies
        # every exported vertex, transformed by the node world matrix, equals the
        # occurrence position NX reports in the assembly (display part, metres)
        root = main.ComponentAssembly.RootComponent
        bolt2 = root.children[0].children[1]
        world = world_of(scene, "sub_a-1/bolt-2")
        local_pt = (0.01, 0.01, 0.04)  # bolt corner in metres (part coordinates)
        expected = tuple(c * 0.001 for c in fx._apply(bolt2.abs_axes, bolt2.abs_origin, (10, 10, 40)))
        self.assertTrue(close(mx.transform_point(world, local_pt), expected, 1e-9))
        # local transform of bolt-2 is relative to sub_a-1 (not absolute)
        self.assertTrue(close(a.children[1].local_transform[12:15], (0.0, -0.03, 0.01), 1e-12))

    def test_05_shared_part_instances(self):
        nx = fx.FakeNx()
        main = assembly(nx)
        scene, _, _ = extract(nx)
        b1 = node_by_name(scene, "sub_a-1/bolt-1")
        b2 = node_by_name(scene, "sub_a-1/bolt-2")
        bolt_body = main.ComponentAssembly.RootComponent.children[0].children[0].Prototype.Bodies[0]
        self.assertEqual(nx.facet.facet_calls.count(bolt_body.Tag), 1)   # tessellated once
        self.assertEqual(b1.meshes[0].positions, b2.meshes[0].positions)  # same part geometry
        self.assertIsNot(b1.meshes[0], b2.meshes[0])                       # one mesh per occurrence (SW)
        self.assertEqual(b1.meshes[0].share_key, b2.meshes[0].share_key)
        self.assertFalse(mx.nearly_equal(world_of(scene, b1.name), world_of(scene, b2.name), 1e-6))
        with TempDir() as tmp:
            path = os.path.join(tmp, "shared.glb")
            export_scene_to_glb(scene, path, ExportOptions(share_instance_meshes=True))
            doc = read_glb(path).gltf
            names = {n.get("name"): n for n in doc["nodes"]}
            self.assertEqual(names["sub_a-1/bolt-1"]["mesh"], names["sub_a-1/bolt-2"]["mesh"])

    def test_06_transforms_units_mixed(self):
        nx = fx.FakeNx()
        main = assembly(nx)
        scene, _, _ = extract(nx)
        plate = node_by_name(scene, "plate-1")
        # translation converted from display units (mm) to metres
        self.assertTrue(close(plate.local_transform[12:15], (0.0, 0.3, 0.05), 1e-12))
        # inch geometry converted to metres
        xs = [p[0] for p in plate.meshes[0].positions]
        self.assertAlmostEqual(max(xs), 4 * 0.0254, places=7)
        comp = main.ComponentAssembly.RootComponent.children[1]
        expected = tuple(c * 0.001 for c in fx._apply(comp.abs_axes, comp.abs_origin, (4 * 25.4, 2 * 25.4, 25.4)))
        self.assertTrue(close(mx.transform_point(world_of(scene, "plate-1"), (4 * 0.0254, 2 * 0.0254, 0.0254)),
                              expected, 1e-9))

    def test_06b_columns_convention_detected(self):
        nx = fx.FakeNx()
        main = assembly(nx, columns_convention=True)
        log = NullLog()
        scene, ex, _ = extract(nx, log=log)
        self.assertEqual(ex._convention, "columns")
        comp = main.ComponentAssembly.RootComponent.children[0]
        expected = tuple(c * 0.001 for c in fx._apply(comp.abs_axes, comp.abs_origin, (0, 0, 0)))
        self.assertTrue(close(world_of(scene, "sub_a-1")[12:15], expected, 1e-12))
        w = world_of(scene, "sub_a-1")
        self.assertTrue(close((w[0], w[1], w[2]), comp.abs_axes[0], 1e-12))

    def test_07_multi_body_and_material_groups(self):
        nx = fx.FakeNx()
        assembly(nx)
        scene, _, _ = extract(nx)
        plate = node_by_name(scene, "plate-1")
        # SW naming: node + "_Material_" + group index, index restarts per body
        self.assertEqual([m.name for m in plate.meshes],
                         ["plate-1_Material_0", "plate-1_Material_0", "plate-1_Material_1"])
        mats = [m.material for m in plate.meshes]
        self.assertEqual(mats[0].name, "PLATE_Material")
        self.assertEqual(mats[1].name, "TAB_Face_Material")   # first face of body 2 has its own colour
        self.assertEqual(mats[2].name, "TAB_Material")
        self.assertEqual(mats[2].alpha_mode, "BLEND")
        self.assertAlmostEqual(mats[2].base_color[3], 0.6)
        self.assertEqual(sum(m.triangle_count for m in plate.meshes[1:]), 12)

    def test_07b_visibility_rules(self):
        nx = fx.FakeNx()
        blanked = fx.box_body((1, 1, 1), name="BLANKED", blanked=True)
        hidden = fx.box_body((1, 1, 1), name="HIDDEN", layer=200)
        sheet = fx.box_body((1, 1, 1), name="SHEET", solid=False)
        visible = fx.box_body((2, 2, 2), name="VISIBLE")
        part = single_part(nx, bodies=[blanked, hidden, sheet, visible])
        part.Layers = fx.FakeLayers(hidden=[200])
        scene, ex, _ = extract(nx)
        self.assertEqual([m.material.name for m in scene.root.meshes], ["VISIBLE_Material"])
        self.assertEqual(ex.stats["skipped_bodies"], 1)

    def test_07c_reference_set_and_occurrence_colour(self):
        nx = fx.FakeNx()
        b1 = fx.box_body((1, 1, 1), name="IN", color=2)
        b2 = fx.box_body((1, 1, 1), name="OUT")
        part = nx.register_part(fx.Part("p", bodies=[b1, b2]))
        main = nx.register_part(fx.Part("main"))
        root = fx.make_assembly(main)
        comp = fx.FakeComponent(part, main, root, reference_set={b1.Tag})
        comp.color_override[b1.Tag] = 3      # assembly colour override on the occurrence
        nx.session.Parts.Display = main
        scene, _, _ = extract(nx)
        meshes = scene.root.children[0].meshes
        self.assertEqual(len(meshes), 1)
        self.assertEqual(meshes[0].material.base_color, (0.0, 0.0, 1.0, 1.0))

    def test_08_root_bodies_in_assembly_part(self):
        nx = fx.FakeNx()
        main = assembly(nx)
        main.Bodies.append(fx.box_body((1, 1, 1), name="WELD"))
        nx.register_part(main)
        scene, _, _ = extract(nx)
        self.assertEqual(len(scene.root.meshes), 1)
        scene, _, _ = extract(nx, ExportOptions(include_root_part_bodies=False))
        self.assertEqual(len(scene.root.meshes), 0)

    def test_10_unsupported_geometry(self):
        nx = fx.FakeNx()
        broken = fx.box_body((1, 1, 1), name="BROKEN", facet_error="facet failure")
        good = fx.box_body((1, 1, 1), name="GOOD")
        single_part(nx, bodies=[broken, good])
        scene, ex, log = extract(nx)
        self.assertEqual(len(scene.root.meshes), 1)
        self.assertEqual(ex.stats["failed_bodies"], 1)
        self.assertTrue(any("BROKEN" in w for w in log.warnings))

    def test_10b_occurrence_tessellation_fallback(self):
        nx = fx.FakeNx()
        nx.facet.allow_occurrence_facets = True
        body = fx.box_body((10, 10, 10), name="B", facet_error="prototype not faceted")
        part = nx.register_part(fx.Part("p", bodies=[body]))
        main = nx.register_part(fx.Part("main"))
        root = fx.make_assembly(main)
        fx.FakeComponent(part, main, root, axes=fx.rot_z(45), origin=(5, 6, 7))
        nx.session.Parts.Display = main
        scene, _, _ = extract(nx)
        mesh = scene.root.children[0].meshes[0]
        self.assertAlmostEqual(max(p[0] for p in mesh.positions), 0.01, places=9)  # back in part frame
        self.assertIsNone(mesh.share_key)

    def test_10c_only_unsupported_bodies(self):
        nx = fx.FakeNx()
        single_part(nx, bodies=[fx.box_body((1, 1, 1), solid=False)])
        scene, _, _ = extract(nx)
        with TempDir() as tmp:
            path = os.path.join(tmp, "x.glb")
            with self.assertRaises(EmptyGeometryError):
                export_scene_to_glb(scene, path, ExportOptions())
            self.assertFalse(os.path.exists(path))

    def test_10d_non_part_display(self):
        nx = fx.FakeNx()
        nx.session.Parts.Display = fx.BasePart()
        with self.assertRaises(UnsupportedModelError):
            extract(nx)

    def test_partially_loaded_part_is_loaded(self):
        nx = fx.FakeNx()
        part = single_part(nx)
        part.IsFullyLoaded = False
        extract(nx)
        self.assertEqual(part.load_calls, 1)


# ------------------------------------------------------------------ command
class CommandScenarios(unittest.TestCase):
    def make(self, nx, args, options=None):
        log = NullLog()
        ctx = nx.context()
        cmd = ExportCommand(ctx, options or ExportOptions(), log, NxUi(ctx, log))
        return cmd.run(args), log

    def messages(self, nx, kind):
        return [m for k, m in nx.message_box.shown if k == kind]

    def test_01_no_part_shows_error(self):
        nx = fx.FakeNx()
        result, _ = self.make(nx, [])
        self.assertIsNone(result)
        self.assertEqual(self.messages(nx, "Error"), [NO_ACTIVE_PART_MESSAGE])
        self.assertEqual(nx.uf.Ui.calls, [])   # no file dialog before the check

    def test_interactive_export_success(self):
        nx = fx.FakeNx()
        assembly(nx)
        with TempDir() as tmp:
            nx.uf.Ui.filename = os.path.join(tmp, "model")      # extension added like SW
            result, _ = self.make(nx, [])
            self.assertEqual(result, os.path.join(tmp, "model.glb"))
            self.assertEqual(self.messages(nx, "Information"), [SUCCESS_MESSAGE.format(result)])
            glb = read_glb(result)
            self.assertEqual(glb.gltf["nodes"][0]["name"], "main_asm")
            worlds = world_matrices(glb)
            self.assertEqual(len(worlds), len(glb.gltf["nodes"]))
            self.assertEqual(nx.uf.Ui.calls[0], "lock")
            self.assertEqual(nx.uf.Ui.calls[-1], "unlock")
            # undo mark set and removed, model untouched
            self.assertEqual([m[0] for m in nx.session.marks], ["set", "delete"])
            self.assertEqual(os.listdir(tmp), ["model.glb"])

    def test_cancel_writes_nothing(self):
        nx = fx.FakeNx()
        single_part(nx)
        nx.uf.Ui.response = 3   # UF_UI_CANCEL
        result, _ = self.make(nx, [])
        self.assertIsNone(result)
        self.assertEqual(nx.message_box.shown, [])

    def test_overwrite_declined(self):
        nx = fx.FakeNx()
        single_part(nx)
        with TempDir() as tmp:
            target = os.path.join(tmp, "a.glb")
            with open(target, "wb") as handle:
                handle.write(b"keep")
            nx.uf.Ui.filename = target
            nx.message_box.question_answer = 2
            self.assertIsNone(self.make(nx, [])[0])
            with open(target, "rb") as handle:
                self.assertEqual(handle.read(), b"keep")
            nx.message_box.question_answer = 1
            self.assertEqual(self.make(nx, [])[0], target)
            self.assertGreater(os.path.getsize(target), 100)

    def test_08_invalid_path(self):
        nx = fx.FakeNx()
        single_part(nx)
        for bad in (r"C:\out\bad|name.glb", r"C:\out\model.stl"):
            nx.message_box.shown.clear()
            self.assertIsNone(self.make(nx, [bad])[0])
            self.assertEqual(len(self.messages(nx, "Error")), 1)
        with TempDir() as tmp:
            nx.message_box.shown.clear()
            missing = os.path.join(tmp, "no_such_dir", "x.glb")
            self.assertIsNone(self.make(nx, [missing])[0])
            self.assertIn("does not exist", self.messages(nx, "Error")[0])
            self.assertEqual(nx.facet.facet_calls, [])   # failed before extraction

    def test_09_no_write_permission(self):
        nx = fx.FakeNx()
        single_part(nx)
        with TempDir() as tmp:
            target = os.path.join(tmp, "ro.glb")
            with open(target, "wb") as handle:
                handle.write(b"old")
            os.chmod(target, stat.S_IREAD)
            try:
                self.assertIsNone(self.make(nx, [target])[0])
                self.assertIn("read-only", self.messages(nx, "Error")[0])
            finally:
                os.chmod(target, stat.S_IWRITE | stat.S_IREAD)

    def test_10_empty_geometry_message(self):
        nx = fx.FakeNx()
        single_part(nx, bodies=[fx.box_body((1, 1, 1), facet_error="bad")])
        with TempDir() as tmp:
            target = os.path.join(tmp, "x.glb")
            self.assertIsNone(self.make(nx, [target])[0])
            self.assertIn("No GLB file was written", self.messages(nx, "Error")[0])
            self.assertEqual(os.listdir(tmp), [])

    def test_unexpected_exception_is_reported(self):
        nx = fx.FakeNx()
        part = single_part(nx)
        part.Bodies = None   # list(None) -> TypeError inside NX adapter -> logged, no crash
        part.ComponentAssembly = None
        with TempDir() as tmp:
            self.assertIsNone(self.make(nx, [os.path.join(tmp, "x.glb")])[0])
            self.assertEqual(len(self.messages(nx, "Error")), 1)

    def test_batch_mode_opens_part(self):
        nx = fx.FakeNx(interactive=False)
        part = single_part(nx)
        nx.session.Parts.Display = None
        nx.session.Parts.opened.append(part)
        with TempDir() as tmp:
            model = os.path.join(tmp, "block.prt")
            with open(model, "wb") as handle:
                handle.write(b"fake prt")
            part.FullPath = model
            out = os.path.join(tmp, "batch.glb")
            result, _ = self.make(nx, [model, out])
            self.assertEqual(result, out)
            self.assertTrue(any("GLB exported successfully" in line for line in nx.session.log_lines))


if __name__ == "__main__":
    unittest.main()
