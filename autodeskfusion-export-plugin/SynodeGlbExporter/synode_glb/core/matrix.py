"""Small 4x4 matrix helpers (row-major storage, column-vector convention)."""

import math
from typing import Optional, Sequence, Tuple

from .scene import IDENTITY

Matrix = Tuple[float, ...]


def multiply(a: Sequence[float], b: Sequence[float]) -> Matrix:
    """Returns a * b."""
    result = [0.0] * 16
    for r in range(4):
        for c in range(4):
            result[r * 4 + c] = (
                a[r * 4 + 0] * b[0 * 4 + c]
                + a[r * 4 + 1] * b[1 * 4 + c]
                + a[r * 4 + 2] * b[2 * 4 + c]
                + a[r * 4 + 3] * b[3 * 4 + c]
            )
    return tuple(result)


def transpose(m: Sequence[float]) -> Matrix:
    return tuple(m[c * 4 + r] for r in range(4) for c in range(4))


def invert(m: Sequence[float]) -> Matrix:
    """General 4x4 inverse (Gauss-Jordan). Raises ValueError if singular."""
    a = [list(m[r * 4:(r + 1) * 4]) + [1.0 if r == c else 0.0 for c in range(4)] for r in range(4)]
    for col in range(4):
        pivot = max(range(col, 4), key=lambda r: abs(a[r][col]))
        if abs(a[pivot][col]) < 1e-12:
            raise ValueError("Matrix is singular and cannot be inverted.")
        a[col], a[pivot] = a[pivot], a[col]
        p = a[col][col]
        a[col] = [v / p for v in a[col]]
        for r in range(4):
            if r != col:
                f = a[r][col]
                if f != 0.0:
                    a[r] = [rv - f * cv for rv, cv in zip(a[r], a[col])]
    return tuple(a[r][4 + c] for r in range(4) for c in range(4))


def is_identity(m: Sequence[float], tol: float = 1e-9) -> bool:
    return all(abs(m[i] - IDENTITY[i]) <= tol for i in range(16))


def transform_point(m: Sequence[float], x: float, y: float, z: float) -> Tuple[float, float, float]:
    return (
        m[0] * x + m[1] * y + m[2] * z + m[3],
        m[4] * x + m[5] * y + m[6] * z + m[7],
        m[8] * x + m[9] * y + m[10] * z + m[11],
    )


def transform_vector(m: Sequence[float], x: float, y: float, z: float) -> Tuple[float, float, float]:
    return (
        m[0] * x + m[1] * y + m[2] * z,
        m[4] * x + m[5] * y + m[6] * z,
        m[8] * x + m[9] * y + m[10] * z,
    )


def to_gltf_column_major(m: Sequence[float]) -> list:
    """glTF stores node.matrix in column-major order."""
    return [float(m[r * 4 + c]) for c in range(4) for r in range(4)]


def decompose_trs(m: Sequence[float], tol: float = 1e-6) -> Optional[tuple]:
    """Decomposes an affine matrix into (translation, rotation quaternion xyzw, scale).

    Returns None when the matrix is not a rotation + positive scale + translation
    (projective row, shear, mirroring or zero scale). Callers should then fall back
    to writing a full matrix.
    """
    if abs(m[12]) > tol or abs(m[13]) > tol or abs(m[14]) > tol or abs(m[15] - 1.0) > tol:
        return None

    cols = [(m[0 + c], m[4 + c], m[8 + c]) for c in range(3)]
    scales = [math.sqrt(x * x + y * y + z * z) for x, y, z in cols]
    if any(s < 1e-12 for s in scales):
        return None

    r = [[cols[c][row] / scales[c] for c in range(3)] for row in range(3)]

    # Orthonormality check (rejects shear).
    for i in range(3):
        for j in range(3):
            dot = r[0][i] * r[0][j] + r[1][i] * r[1][j] + r[2][i] * r[2][j]
            if abs(dot - (1.0 if i == j else 0.0)) > 1e-5:
                return None

    det = (
        r[0][0] * (r[1][1] * r[2][2] - r[1][2] * r[2][1])
        - r[0][1] * (r[1][0] * r[2][2] - r[1][2] * r[2][0])
        + r[0][2] * (r[1][0] * r[2][1] - r[1][1] * r[2][0])
    )
    if det < 0:
        return None

    trace = r[0][0] + r[1][1] + r[2][2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2.0
        qw = 0.25 * s
        qx = (r[2][1] - r[1][2]) / s
        qy = (r[0][2] - r[2][0]) / s
        qz = (r[1][0] - r[0][1]) / s
    elif r[0][0] > r[1][1] and r[0][0] > r[2][2]:
        s = math.sqrt(1.0 + r[0][0] - r[1][1] - r[2][2]) * 2.0
        qw = (r[2][1] - r[1][2]) / s
        qx = 0.25 * s
        qy = (r[0][1] + r[1][0]) / s
        qz = (r[0][2] + r[2][0]) / s
    elif r[1][1] > r[2][2]:
        s = math.sqrt(1.0 + r[1][1] - r[0][0] - r[2][2]) * 2.0
        qw = (r[0][2] - r[2][0]) / s
        qx = (r[0][1] + r[1][0]) / s
        qy = 0.25 * s
        qz = (r[1][2] + r[2][1]) / s
    else:
        s = math.sqrt(1.0 + r[2][2] - r[0][0] - r[1][1]) * 2.0
        qw = (r[1][0] - r[0][1]) / s
        qx = (r[0][2] + r[2][0]) / s
        qy = (r[1][2] + r[2][1]) / s
        qz = 0.25 * s

    n = math.sqrt(qx * qx + qy * qy + qz * qz + qw * qw)
    quat = (qx / n, qy / n, qz / n, qw / n)
    translation = (m[3], m[7], m[11])
    return translation, quat, tuple(scales)
