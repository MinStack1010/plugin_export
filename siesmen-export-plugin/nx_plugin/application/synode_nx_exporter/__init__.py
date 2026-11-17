"""Synode NX GLB Exporter.

Siemens NX port of the "Synode GLB Exporter" SOLIDWORKS add-in
(PluginSynode).  The package is split into:

* NX independent core (pure Python, unit-testable outside NX):
  ``config``, ``errors``, ``logger``, ``scene``, ``matrix``, ``materials``,
  ``mesh_builder``, ``coordinate_converter``, ``gltf_writer``,
  ``output_path``.
* NX adapters (require the NXOpen Python runtime inside Siemens NX):
  ``synode_nx_exporter.nx``.
"""

__version__ = "1.0.0"
PRODUCT_NAME = "Synode NX GLB Exporter"
