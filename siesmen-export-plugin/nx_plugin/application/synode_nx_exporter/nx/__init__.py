"""Adapters for the Siemens NX Open (Python) API.

Every module in this sub-package receives an :class:`NxContext` instead of
importing NXOpen at module import time, so the logic can be exercised with
test doubles outside NX.  Inside NX use :func:`NxContext.from_running_nx`.
"""


class NxContext:
    """Bundle of the NXOpen modules and session objects used by the exporter."""

    def __init__(self, nxopen, nxopen_uf, session, uf_session, ui=None):
        self.nx = nxopen            # module NXOpen
        self.nx_uf = nxopen_uf      # module NXOpen.UF
        self.session = session      # NXOpen.Session
        self.uf = uf_session        # NXOpen.UF.UFSession
        self.ui = ui                # NXOpen.UI or None (batch mode)

    def uf_constant(self, name, default):
        constants = getattr(self.nx_uf, "UFConstants", None)
        return getattr(constants, name, default) if constants is not None else default

    @classmethod
    def from_running_nx(cls, need_ui=True):
        import NXOpen  # noqa: imported only inside Siemens NX
        import NXOpen.UF
        import NXOpen.Assemblies  # noqa: F401 (registers the Assemblies namespace)
        session = NXOpen.Session.GetSession()
        uf_session = NXOpen.UF.UFSession.GetUFSession()
        ui = None
        if need_ui:
            try:
                ui = NXOpen.UI.GetUI()
            except Exception:
                ui = None
        return cls(NXOpen, NXOpen.UF, session, uf_session, ui)
