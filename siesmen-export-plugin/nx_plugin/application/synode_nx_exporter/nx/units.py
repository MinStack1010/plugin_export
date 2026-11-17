"""Part unit handling (NX part units -> metres)."""

INCH = 0.0254


class UnitResolver:
    def __init__(self, ctx, log):
        self.ctx = ctx
        self.log = log
        units = ctx.nx.BasePart.Units
        self._map = []
        for name, factor in (("Millimeters", 0.001), ("Inches", INCH), ("Meters", 1.0), ("Micrometers", 1e-6)):
            member = getattr(units, name, None)
            if member is not None:
                self._map.append((member, factor))
        self._cache = {}

    def scale_to_meters(self, part):
        tag = getattr(part, "Tag", id(part))
        if tag in self._cache:
            return self._cache[tag]
        factor = None
        try:
            unit = part.PartUnits
            for member, value in self._map:
                if unit == member:
                    factor = value
                    break
        except Exception as exc:
            self.log.warning("Cannot read PartUnits of '{}': {}".format(_leaf(part), exc))
        if factor is None:
            factor = self._uf_units(part)
        self._cache[tag] = factor
        return factor

    def _uf_units(self, part):
        # UF_PART_ask_units: UF_PART_METRIC (mm) / UF_PART_ENGLISH (inch) / UF_PART_MICRO / UF_PART_SI
        try:
            value = self.ctx.uf.Part.AskUnits(part.Tag)
            mapping = {
                self.ctx.uf_constant("UF_PART_METRIC", 1): 0.001,
                self.ctx.uf_constant("UF_PART_ENGLISH", 2): INCH,
                self.ctx.uf_constant("UF_PART_MICRO", 3): 1e-6,
                self.ctx.uf_constant("UF_PART_SI", 4): 1.0,
            }
            if value in mapping:
                return mapping[value]
        except Exception as exc:
            self.log.warning("UF_PART_ask_units failed for '{}': {}".format(_leaf(part), exc))
        self.log.warning("Unknown units for part '{}'; millimetres assumed.".format(_leaf(part)))
        return 0.001


def _leaf(part):
    try:
        return part.Leaf
    except Exception:
        return "?"
