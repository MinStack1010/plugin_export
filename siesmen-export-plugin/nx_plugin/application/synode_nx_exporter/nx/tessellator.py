"""Body tessellation with UF_FACET (NXOpen.UF.Facet).

Verified signatures (NXOpen_UF.pyd docstrings, NX 2606):
  AskDefaultParameters() -> Parameters
  FacetSolid(solidEntity: Tag, parameters) -> model Tag   [ASSEMBLIES MODULE license]
  CycleFacets(model, facetId) -> next facetId (UF_FACET_NULL_FACET_ID ends)
  AskVerticesOfFacet(model, facetId) -> (numVertices, vertices)
  AskNormalsOfFacet(model, facetId) -> (numVertices, normals)
  AskSolidFaceOfFacet(model, facetId) -> prototype solid face Tag
The facet model is a temporary object; it is always deleted with
UF_OBJ_delete_object so the part is left as it was.
"""

import math

from ..errors import TessellationError


def _points(values, count):
    """Accept [[x,y,z], ...] or a flat [x,y,z,x,y,z,...] list."""
    if values is None:
        return []
    values = list(values)
    if values and isinstance(values[0], (list, tuple)):
        pts = [tuple(float(c) for c in v[:3]) for v in values]
    elif values and hasattr(values[0], "X"):
        pts = [(float(v.X), float(v.Y), float(v.Z)) for v in values]
    else:
        flat = [float(v) for v in values]
        pts = [tuple(flat[i:i + 3]) for i in range(0, len(flat) - 2, 3)]
    if count is not None and count >= 0:
        pts = pts[:count]
    return pts


class Tessellator:
    def __init__(self, ctx, options, log):
        self.ctx = ctx
        self.options = options
        self.log = log
        self._null_facet = ctx.uf_constant("UF_FACET_NULL_FACET_ID", -1)
        self._params_cache = {}

    def _parameters(self, units_to_m):
        key = round(units_to_m, 12)
        if key in self._params_cache:
            return self._params_cache[key]
        facet = self.ctx.uf.Facet
        try:
            params = facet.AskDefaultParameters()
        except Exception as exc:
            self.log.warning("UF_FACET_ask_default_parameters failed ({}); NX defaults used.".format(exc))
            self._params_cache[key] = None
            return None
        settings = {"MaxFacetEdges": 3, "StoreFaceTags": True}
        if self.options.chord_tolerance_mm is not None:
            tol = self.options.chord_tolerance_mm * 0.001 / units_to_m  # mm -> part units
            settings.update({"SpecifySurfaceTolerance": True, "SurfaceDistTolerance": tol,
                             "SpecifyCurveTolerance": True, "CurveDistTolerance": tol})
        if self.options.angular_tolerance_deg is not None:
            ang = math.radians(self.options.angular_tolerance_deg)
            settings.update({"SpecifySurfaceTolerance": True, "SurfaceAngularTolerance": ang,
                             "SpecifyCurveTolerance": True, "CurveAngularTolerance": ang})
        for name, value in settings.items():
            try:
                setattr(params, name, value)
            except Exception as exc:
                self.log.warning("Cannot set facet parameter {}: {}".format(name, exc))
        self._params_cache[key] = params
        return params

    def facet_body(self, body_tag, units_to_m):
        """Tessellate one body.

        Returns a list of triangles ``(face_tag, (p0, p1, p2), (n0, n1, n2))``
        with positions multiplied by ``units_to_m`` (coordinates of the
        body's own space).  Raises TessellationError.
        """
        facet = self.ctx.uf.Facet
        params = self._parameters(units_to_m)
        try:
            model = facet.FacetSolid(body_tag, params)
        except Exception as exc:
            raise TessellationError("UF_FACET_facet_solid failed: {}".format(exc))
        if not model:
            raise TessellationError("UF_FACET_facet_solid returned no facet model.")
        triangles = []
        try:
            facet_id = self._null_facet
            guard = 0
            while True:
                facet_id = facet.CycleFacets(model, facet_id)
                if facet_id == self._null_facet or facet_id is None:
                    break
                guard += 1
                if guard > 50000000:
                    raise TessellationError("Facet cycling did not terminate.")
                count, vertices = facet.AskVerticesOfFacet(model, facet_id)
                ncount, normals = facet.AskNormalsOfFacet(model, facet_id)
                face_tag = facet.AskSolidFaceOfFacet(model, facet_id)
                pts = _points(vertices, count)
                nrm = _points(normals, ncount)
                if len(pts) < 3:
                    continue
                if len(nrm) != len(pts):
                    nrm = [None] * len(pts)
                pts = [(p[0] * units_to_m, p[1] * units_to_m, p[2] * units_to_m) for p in pts]
                # MaxFacetEdges = 3 gives triangles; fan-split defensively otherwise.
                for k in range(1, len(pts) - 1):
                    triangles.append((face_tag, (pts[0], pts[k], pts[k + 1]), (nrm[0], nrm[k], nrm[k + 1])))
        except TessellationError:
            raise
        except Exception as exc:
            raise TessellationError("Reading the facet model failed: {}".format(exc))
        finally:
            try:
                self.ctx.uf.Obj.DeleteObject(model)
            except Exception as exc:
                self.log.warning("Could not delete temporary facet model: {}".format(exc))
        if not triangles:
            raise TessellationError("The body produced no facets.")
        return triangles
