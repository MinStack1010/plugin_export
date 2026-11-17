"""Display colours / translucency of NX bodies and faces -> glTF materials.

NX has no PBR render material equivalent to SOLIDWORKS DSPBR accessible
through NX Open for every object, so the displayed colour (colour table
index -> RGB) and translucency (0..100 %) are used.  Metallic/roughness use
the SW writer defaults (configurable).
"""

from ..materials import material_from_display_color, materials_equivalent


class AppearanceResolver:
    def __init__(self, ctx, display_part, options, log):
        self.ctx = ctx
        self.display_part = display_part
        self.options = options
        self.log = log
        self._rgb_cache = {}

    def rgb(self, color_index):
        if color_index in self._rgb_cache:
            return self._rgb_cache[color_index]
        rgb = None
        try:
            c = self.display_part.Colors.Find(int(color_index)).GetRgb()
            rgb = (float(c.R), float(c.G), float(c.B))
        except Exception as exc:
            try:
                model = self.ctx.uf_constant("UF_DISP_rgb_model", 0)
                result = self.ctx.uf.Disp.AskColor(int(color_index), model)
                values = result[-1] if isinstance(result, tuple) else result
                rgb = tuple(float(v) for v in list(values)[:3])
            except Exception as exc2:
                self.log.warning("Colour {} could not be resolved ({}; {}); grey used.".format(color_index, exc, exc2))
        if rgb is None or len(rgb) != 3:
            rgb = (0.8, 0.8, 0.8)
        if max(rgb) > 1.0:  # defensive: 0..255 values
            rgb = tuple(v / 255.0 for v in rgb)
        self._rgb_cache[color_index] = rgb
        return rgb

    def translucency(self, obj):
        try:
            return int(self.ctx.uf.Obj.AskTranslucency(obj.Tag))
        except Exception:
            return 0

    @staticmethod
    def color_index(obj):
        try:
            return int(obj.Color)
        except Exception:
            return None

    def _material(self, name, color_index, translucency):
        rgb = self.rgb(color_index) if color_index is not None else (0.8, 0.8, 0.8)
        return material_from_display_color(name, rgb, translucency,
                                           self.options.default_metallic, self.options.default_roughness)

    def body_material(self, body, body_name):
        """SW: body render material named "<body>_Material"."""
        return self._material(body_name + "_Material", self.color_index(body), self.translucency(body))

    def face_material(self, face, body, body_name, body_material):
        """SW: face render material "<body>_Face_Material" when the face differs."""
        if face is None:
            return body_material
        face_color = self.color_index(face)
        body_translucency = self.translucency(body)
        # Assumption (not verified in a live NX session): NX translucency is
        # normally set on the body; a face reporting 0 inherits the body value.
        face_translucency = self.translucency(face) or body_translucency
        if face_color == self.color_index(body) and face_translucency == body_translucency:
            return body_material
        material = self._material(body_name + "_Face_Material", face_color, face_translucency)
        if materials_equivalent(material, body_material):
            return body_material
        return material
