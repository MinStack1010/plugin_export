"""TEST DOUBLES for the parts of NXOpen / NXOpen.UF used by the exporter.

!!! These classes are NOT Siemens NX.  They imitate only the documented
!!! signatures the plugin calls (see synode_nx_exporter/nx/*.py docstrings) so
!!! that traversal, transforms, units, naming, visibility, material grouping
!!! and error handling can be unit tested outside NX.  Passing these tests does
!!! not prove compatibility with a real NX session.

Geometry model of the fake:
* a FakeBody is a list of FakeFace; each face holds triangles in the units of
  its owning part (position triples + one normal per vertex);
* FakeUFFacet.FacetSolid(prototype body tag) returns those triangles
  (like UF_FACET on a prototype solid: part coordinates, face tags =
  prototype face tags);
* a FakeComponent has a local placement relative to its parent component;
  GetPosition() returns the ABSOLUTE placement in display-part units
  (rotation rows = axes, as assumed by the extractor; ``columns_convention``
  returns the transposed matrix to test the runtime convention check);
* component.FindOccurrence(obj) returns occurrence objects with their own tags,
  colours (overridable) and edges whose vertices are in display-part units.
"""

import itertools
import math
import types

_tags = itertools.count(1000)
OBJECTS = {}   # tag -> fake object (prototypes and occurrences)


def new_tag(obj=None):
    tag = next(_tags)
    if obj is not None:
        OBJECTS[tag] = obj
    return tag


# --------------------------------------------------------------------- enums
class _Enum:
    def __init__(self, name):
        self.name = name

    def __repr__(self):
        return "<{}>".format(self.name)


class Units:
    Millimeters = _Enum("Millimeters")
    Inches = _Enum("Inches")
    Meters = _Enum("Meters")
    Micrometers = _Enum("Micrometers")
    Mix = _Enum("Mix")


UNIT_SCALE = {Units.Millimeters: 0.001, Units.Inches: 0.0254, Units.Meters: 1.0, Units.Micrometers: 1e-6}


class Point3d:
    def __init__(self, x, y, z):
        self.X, self.Y, self.Z = float(x), float(y), float(z)


class Matrix3x3:
    def __init__(self, rows):
        (self.Xx, self.Xy, self.Xz), (self.Yx, self.Yy, self.Yz), (self.Zx, self.Zy, self.Zz) = rows


class Rgb:
    def __init__(self, r, g, b):
        self.R, self.G, self.B = r, g, b


# ------------------------------------------------------------- small math
def _apply(axes, origin, p):
    """origin + p.x * X + p.y * Y + p.z * Z (axes = rows)."""
    return tuple(origin[i] + p[0] * axes[0][i] + p[1] * axes[1][i] + p[2] * axes[2][i] for i in range(3))


def _rotate(axes, v):
    return tuple(v[0] * axes[0][i] + v[1] * axes[1][i] + v[2] * axes[2][i] for i in range(3))


def rot_z(deg):
    a = math.radians(deg)
    return ((math.cos(a), math.sin(a), 0.0), (-math.sin(a), math.cos(a), 0.0), (0.0, 0.0, 1.0))


def rot_x(deg):
    a = math.radians(deg)
    return ((1.0, 0.0, 0.0), (0.0, math.cos(a), math.sin(a)), (0.0, -math.sin(a), math.cos(a)))


IDENTITY_AXES = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))


# ------------------------------------------------------------- geometry
_CUBE = [
    ((1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
    ((-1, 0, 0), [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)]),
    ((0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
    ((0, -1, 0), [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)]),
    ((0, 0, 1), [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]),
    ((0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)]),
]


class FakeEdge:
    def __init__(self, v0, v1):
        self.Tag = new_tag(self)
        self._v = (v0, v1)

    def GetVertices(self):
        return Point3d(*self._v[0]), Point3d(*self._v[1])


class FakeFace:
    def __init__(self, triangles, color=None, translucency=0):
        self.Tag = new_tag(self)
        self.triangles = triangles   # [((p0,p1,p2),(n0,n1,n2))] part units
        self.Color = color
        self.translucency = translucency
        self.body = None


class FakeBody:
    def __init__(self, faces, name="", color=1, solid=True, blanked=False, layer=1,
                 translucency=0, facet_error=None):
        self.Tag = new_tag(self)
        self.faces = faces
        self.Name = name
        self.Color = color
        self.IsSolidBody = solid
        self.IsBlanked = blanked
        self.Layer = layer
        self.translucency = translucency
        self.facet_error = facet_error
        for f in faces:
            f.body = self
            if f.Color is None:
                f.Color = color
        p = faces[0].triangles[0][0]
        self._edges = [FakeEdge(p[0], p[1]), FakeEdge(p[1], p[2])]

    def GetFaces(self):
        return list(self.faces)

    def GetEdges(self):
        return list(self._edges)


def box_body(size, offset=(0, 0, 0), **kw):
    faces = []
    for normal, quad in _CUBE:
        q = [tuple(offset[i] + c[i] * size[i] for i in range(3)) for c in quad]
        faces.append(FakeFace([((q[0], q[1], q[2]), (normal,) * 3), ((q[0], q[2], q[3]), (normal,) * 3)]))
    return FakeBody(faces, **kw)


# ------------------------------------------------------------- parts
class FakeColors:
    TABLE = {1: (0.8, 0.8, 0.8), 2: (1.0, 0.0, 0.0), 3: (0.0, 0.0, 1.0), 4: (0.0, 1.0, 0.0)}

    def Find(self, index):
        rgb = self.TABLE[index]
        return types.SimpleNamespace(GetRgb=lambda: Rgb(*rgb))


class FakeLayers:
    def __init__(self, hidden=()):
        self.hidden = set(hidden)

    def GetState(self, layer):
        return Layer.State.Hidden if layer in self.hidden else Layer.State.Selectable


class Layer:
    class State:
        Selectable = _Enum("Selectable")
        Hidden = _Enum("Hidden")


class FakeComponentAssembly:
    def __init__(self, root):
        self.RootComponent = root


class BasePart:
    Units = Units


class Part(BasePart):
    def __init__(self, leaf, units=Units.Millimeters, bodies=(), full_path=None, fully_loaded=True):
        self.Tag = new_tag(self)
        self.Leaf = leaf
        self.Name = leaf
        self.FullPath = full_path or "C:\\models\\{}.prt".format(leaf)
        self.PartUnits = units
        self.Bodies = list(bodies)
        self.Colors = FakeColors()
        self.Layers = FakeLayers()
        self.ComponentAssembly = FakeComponentAssembly(None)
        self.IsFullyLoaded = fully_loaded
        self.load_calls = 0
        self.saved = False

    @property
    def scale(self):
        return UNIT_SCALE[self.PartUnits]

    def LoadThisPartFully(self):
        self.load_calls += 1
        self.IsFullyLoaded = True

    def Save(self, *a):  # must never be called by the exporter
        self.saved = True


class _Occurrence:
    """Occurrence of a prototype object inside one component."""

    def __init__(self, proto, component):
        self.Tag = new_tag(self)
        self.proto = proto
        self.component = component
        self.Color = component.color_override.get(proto.Tag, getattr(proto, "Color", None))
        body = getattr(proto, "body", None)
        if (proto.Tag not in component.color_override and body is not None
                and body.Tag in component.color_override and proto.Color == body.Color):
            # a body-level occurrence colour override also shows on faces that
            # carry the body colour (models NX display behaviour in tests only)
            self.Color = component.color_override[body.Tag]
        self.IsBlanked = getattr(proto, "IsBlanked", False)
        self.Layer = getattr(proto, "Layer", 1)
        self.translucency = getattr(proto, "translucency", 0)

    def GetVertices(self):
        comp = self.component
        p0, p1 = self.proto.GetVertices()
        ratio = comp.Prototype.scale / comp.display.scale
        out = []
        for p in (p0, p1):
            out.append(Point3d(*_apply(comp.abs_axes, comp.abs_origin, (p.X * ratio, p.Y * ratio, p.Z * ratio))))
        return tuple(out)


class FakeComponent:
    def __init__(self, prototype, display, parent=None, axes=IDENTITY_AXES, origin=(0, 0, 0),
                 suppressed=False, display_name=None, columns_convention=False, reference_set=None):
        self.Tag = new_tag(self)
        self.Prototype = prototype
        self.display = display
        self.parent = parent
        self.local_axes = axes
        self.local_origin = tuple(float(c) for c in origin)  # display units, parent frame
        self.IsSuppressed = suppressed
        self.DisplayName = display_name or (prototype.Leaf if prototype else "unloaded")
        self.Name = self.DisplayName.upper()
        self.columns_convention = columns_convention
        self.children = []
        self.color_override = {}
        self.reference_set = reference_set  # None = all bodies, else set of body tags
        self._occ = {}
        if parent is not None:
            parent.children.append(self)

    @property
    def abs_axes(self):
        if self.parent is None or self.parent.is_root:
            return tuple(self.local_axes)
        base = self.parent.abs_axes
        return tuple(_rotate(base, a) for a in self.local_axes)

    @property
    def abs_origin(self):
        if self.parent is None or self.parent.is_root:
            return self.local_origin
        return _apply(self.parent.abs_axes, self.parent.abs_origin, self.local_origin)

    is_root = False

    def GetChildren(self):
        return list(self.children)

    def GetPosition(self):
        axes = self.abs_axes
        rows = axes if not self.columns_convention else tuple(zip(*axes))
        return Point3d(*self.abs_origin), Matrix3x3(rows)

    def FindOccurrence(self, obj):
        if isinstance(obj, FakeBody) and self.reference_set is not None and obj.Tag not in self.reference_set:
            return None
        occ = self._occ.get(obj.Tag)
        if occ is None:
            occ = _Occurrence(obj, self)
            self._occ[obj.Tag] = occ
        return occ


class FakeRootComponent(FakeComponent):
    is_root = True

    def __init__(self, display):
        super().__init__(display, display, None)

    @property
    def abs_axes(self):
        return IDENTITY_AXES

    @property
    def abs_origin(self):
        return (0.0, 0.0, 0.0)


def make_assembly(display):
    root = FakeRootComponent(display)
    display.ComponentAssembly = FakeComponentAssembly(root)
    return root


# ------------------------------------------------------------- UF
class FakeUFFacet:
    def __init__(self, registry):
        self.registry = registry   # tag -> prototype FakeBody
        self.models = {}
        self.deleted = []
        self.facet_calls = []
        self.allow_occurrence_facets = False

    def AskDefaultParameters(self):
        return types.SimpleNamespace(MaxFacetEdges=4, StoreFaceTags=False)

    def FacetSolid(self, tag, params):
        self.facet_calls.append(tag)
        body = self.registry.get(tag)
        occurrence = None
        if body is None and self.allow_occurrence_facets:
            occurrence = OBJECTS.get(tag)
            if isinstance(occurrence, _Occurrence) and isinstance(occurrence.proto, FakeBody):
                body = occurrence.proto
            else:
                occurrence = None
        if body is None:
            raise RuntimeError("Invalid object type (fake: only prototype bodies can be faceted)")
        if body.facet_error and occurrence is None:
            raise RuntimeError(body.facet_error)
        facets = []
        for face in body.faces:
            for pts, nrms in face.triangles:
                if occurrence is not None:  # absolute coordinates, display units
                    comp = occurrence.component
                    ratio = comp.Prototype.scale / comp.display.scale
                    pts = tuple(_apply(comp.abs_axes, comp.abs_origin, tuple(c * ratio for c in p)) for p in pts)
                    nrms = tuple(_rotate(comp.abs_axes, n) for n in nrms)
                facets.append((face.Tag, pts, nrms))
        model = new_tag()
        self.models[model] = facets
        return model

    def CycleFacets(self, model, facet_id):
        facets = self.models[model]
        nxt = facet_id + 1
        return nxt if nxt < len(facets) else -1

    def AskVerticesOfFacet(self, model, facet_id):
        return 3, [list(p) for p in self.models[model][facet_id][1]]

    def AskNormalsOfFacet(self, model, facet_id):
        return 3, [list(n) for n in self.models[model][facet_id][2]]

    def AskSolidFaceOfFacet(self, model, facet_id):
        return self.models[model][facet_id][0]


class FakeUFObj:
    def __init__(self, facet, registry):
        self.facet = facet
        self.registry = registry

    def DeleteObject(self, tag):
        self.facet.deleted.append(tag)
        self.facet.models.pop(tag, None)

    def AskTranslucency(self, tag):
        obj = OBJECTS.get(tag)
        return getattr(obj, "translucency", 0)


class FakeUFUi:
    def __init__(self):
        self.response = 2
        self.filename = ""
        self.calls = []

    def LockUgAccess(self, value):
        self.calls.append("lock")

    def UnlockUgAccess(self, value):
        self.calls.append("unlock")

    def CreateFilebox(self, prompt, title, filter_string, default_name):
        self.calls.append(("filebox", filter_string, default_name))
        return filter_string, self.filename, self.response


class NXMessageBox:
    class DialogType:
        Error = "Error"
        Warning = "Warning"
        Information = "Information"
        Question = "Question"


class FakeMessageBox:
    def __init__(self):
        self.shown = []
        self.question_answer = 1

    def Show(self, title, kind, message):
        self.shown.append((kind, message))
        return self.question_answer if kind == "Question" else 0


class Session:
    class MarkVisibility:
        Invisible = "Invisible"
        Visible = "Visible"


class FakeParts:
    def __init__(self):
        self.Display = None
        self.opened = []

    def OpenBaseDisplay(self, path):
        for part in self.opened:
            if part.FullPath.lower() == path.lower():
                self.Display = part
                return part, None
        raise RuntimeError("File not found (fake)")


class FakeSession:
    def __init__(self):
        self.Parts = FakeParts()
        self.marks = []
        self.log_lines = []
        self.LogFile = types.SimpleNamespace(WriteLine=self.log_lines.append)

    def SetUndoMark(self, visibility, name):
        self.marks.append(("set", visibility, name))
        return len(self.marks)

    def DeleteUndoMark(self, mark, name):
        self.marks.append(("delete", mark))


class FakeNx:
    """Stands in for both the NXOpen module and an NxContext."""

    def __init__(self, interactive=True):
        self.registry = {}
        self.module = types.SimpleNamespace(Part=Part, BasePart=BasePart, Layer=Layer, Session=Session,
                                            NXMessageBox=NXMessageBox)
        self.facet = FakeUFFacet(self.registry)
        self.uf = types.SimpleNamespace(Facet=self.facet, Obj=FakeUFObj(self.facet, self.registry),
                                        Ui=FakeUFUi(), Part=types.SimpleNamespace(AskUnits=lambda tag: 1),
                                        Disp=None)
        self.session = FakeSession()
        self.message_box = FakeMessageBox()
        self.ui = types.SimpleNamespace(NXMessageBox=self.message_box) if interactive else None

    def register_part(self, part):
        for body in part.Bodies:
            self.registry[body.Tag] = body
            for face in body.faces:
                self.registry[face.Tag] = face
        return part

    def context(self):
        from synode_nx_exporter.nx import NxContext
        nx_uf = types.SimpleNamespace(UFConstants=types.SimpleNamespace(
            UF_FACET_NULL_FACET_ID=-1, UF_UI_OK=2, UF_UI_FROM_CUSTOM=1))
        ctx = NxContext(self.module, nx_uf, self.session, self.uf, self.ui)
        return ctx
