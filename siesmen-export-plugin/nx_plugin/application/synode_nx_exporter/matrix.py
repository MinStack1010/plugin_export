"""4x4 affine matrix helpers.

Matrices are tuples of 16 floats in glTF column-major order:
``m[col * 4 + row]``.  Columns 0..2 are the X/Y/Z axes of the local frame
expressed in the parent frame, column 3 is the translation.  This is the same
memory layout as System.Numerics.Matrix4x4 (row-vector convention: M11..M13 =
X axis, M41..M43 = translation) used by the SOLIDWORKS plugin, so matrices
can be compared element by element.
"""

import math

IDENTITY = (1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0)


def get(m, row, col):
    return m[col * 4 + row]


def from_axes(x_axis, y_axis, z_axis, origin):
    return (float(x_axis[0]), float(x_axis[1]), float(x_axis[2]), 0.0,
            float(y_axis[0]), float(y_axis[1]), float(y_axis[2]), 0.0,
            float(z_axis[0]), float(z_axis[1]), float(z_axis[2]), 0.0,
            float(origin[0]), float(origin[1]), float(origin[2]), 1.0)


def from_rows_3x3(rows, origin):
    """Build a matrix whose rotation part has the given rows (rows[i][j] = R[i][j])."""
    return (rows[0][0], rows[1][0], rows[2][0], 0.0,
            rows[0][1], rows[1][1], rows[2][1], 0.0,
            rows[0][2], rows[1][2], rows[2][2], 0.0,
            float(origin[0]), float(origin[1]), float(origin[2]), 1.0)


def multiply(a, b):
    """Return a * b (apply b first, then a) for column vectors."""
    out = [0.0] * 16
    for col in range(4):
        for row in range(4):
            out[col * 4 + row] = (a[row] * b[col * 4] + a[4 + row] * b[col * 4 + 1]
                                  + a[8 + row] * b[col * 4 + 2] + a[12 + row] * b[col * 4 + 3])
    return tuple(out)


def determinant3(m):
    a, b, c = m[0], m[4], m[8]
    d, e, f = m[1], m[5], m[9]
    g, h, i = m[2], m[6], m[10]
    return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)


def inverse_affine(m):
    """Invert an affine matrix (last row 0,0,0,1).  Raises ValueError if singular."""
    a, b, c = m[0], m[4], m[8]
    d, e, f = m[1], m[5], m[9]
    g, h, i = m[2], m[6], m[10]
    det = determinant3(m)
    if abs(det) < 1e-300:
        raise ValueError("Matrix is singular and cannot be inverted.")
    inv = 1.0 / det
    r00 = (e * i - f * h) * inv
    r01 = (c * h - b * i) * inv
    r02 = (b * f - c * e) * inv
    r10 = (f * g - d * i) * inv
    r11 = (a * i - c * g) * inv
    r12 = (c * d - a * f) * inv
    r20 = (d * h - e * g) * inv
    r21 = (b * g - a * h) * inv
    r22 = (a * e - b * d) * inv
    tx, ty, tz = m[12], m[13], m[14]
    return (r00, r10, r20, 0.0,
            r01, r11, r21, 0.0,
            r02, r12, r22, 0.0,
            -(r00 * tx + r01 * ty + r02 * tz),
            -(r10 * tx + r11 * ty + r12 * tz),
            -(r20 * tx + r21 * ty + r22 * tz),
            1.0)


def transform_point(m, p):
    x, y, z = p
    return (m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14])


def transform_vector(m, v):
    x, y, z = v
    return (m[0] * x + m[4] * y + m[8] * z,
            m[1] * x + m[5] * y + m[9] * z,
            m[2] * x + m[6] * y + m[10] * z)


def is_identity(m, tolerance=0.0):
    return all(abs(a - b) <= tolerance for a, b in zip(m, IDENTITY))


def nearly_equal(a, b, tolerance=1e-9):
    return all(abs(x - y) <= tolerance for x, y in zip(a, b))


def transpose_rotation(m):
    return (m[0], m[4], m[8], 0.0,
            m[1], m[5], m[9], 0.0,
            m[2], m[6], m[10], 0.0,
            m[12], m[13], m[14], 1.0)


def with_scaled_translation(m, factor):
    out = list(m)
    out[12] *= factor
    out[13] *= factor
    out[14] *= factor
    return tuple(out)


# Rotation of -90 deg about X: maps CAD Z-up to glTF Y-up ((x, y, z) -> (x, z, -y)).
Z_UP_TO_Y_UP = (1.0, 0.0, 0.0, 0.0,
                0.0, 0.0, -1.0, 0.0,
                0.0, 1.0, 0.0, 0.0,
                0.0, 0.0, 0.0, 1.0)


def decompose(m):
    """Decompose an affine matrix into (translation, rotation quaternion xyzw, scale).

    Returns None when the matrix contains shear (not representable as TRS).
    """
    cols = [(m[0], m[1], m[2]), (m[4], m[5], m[6]), (m[8], m[9], m[10])]
    sx, sy, sz = (math.sqrt(c[0] ** 2 + c[1] ** 2 + c[2] ** 2) for c in cols)
    if min(sx, sy, sz) < 1e-12:
        return None
    if determinant3(m) < 0:
        sx = -sx
    r = [[cols[0][0] / sx, cols[1][0] / sy, cols[2][0] / sz],
         [cols[0][1] / sx, cols[1][1] / sy, cols[2][1] / sz],
         [cols[0][2] / sx, cols[1][2] / sy, cols[2][2] / sz]]
    # orthogonality check
    for i in range(3):
        for j in range(i + 1, 3):
            dot = r[0][i] * r[0][j] + r[1][i] * r[1][j] + r[2][i] * r[2][j]
            if abs(dot) > 1e-5:
                return None
    trace = r[0][0] + r[1][1] + r[2][2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2
        w = 0.25 * s
        x = (r[2][1] - r[1][2]) / s
        y = (r[0][2] - r[2][0]) / s
        z = (r[1][0] - r[0][1]) / s
    elif r[0][0] > r[1][1] and r[0][0] > r[2][2]:
        s = math.sqrt(1.0 + r[0][0] - r[1][1] - r[2][2]) * 2
        w = (r[2][1] - r[1][2]) / s
        x = 0.25 * s
        y = (r[0][1] + r[1][0]) / s
        z = (r[0][2] + r[2][0]) / s
    elif r[1][1] > r[2][2]:
        s = math.sqrt(1.0 + r[1][1] - r[0][0] - r[2][2]) * 2
        w = (r[0][2] - r[2][0]) / s
        x = (r[0][1] + r[1][0]) / s
        y = 0.25 * s
        z = (r[1][2] + r[2][1]) / s
    else:
        s = math.sqrt(1.0 + r[2][2] - r[0][0] - r[1][1]) * 2
        w = (r[1][0] - r[0][1]) / s
        x = (r[0][2] + r[2][0]) / s
        y = (r[1][2] + r[2][1]) / s
        z = 0.25 * s
    norm = math.sqrt(x * x + y * y + z * z + w * w)
    return (m[12], m[13], m[14]), (x / norm, y / norm, z / norm, w / norm), (sx, sy, sz)


def compose(translation, rotation, scale):
    x, y, z, w = rotation
    sx, sy, sz = scale
    r00 = 1 - 2 * (y * y + z * z)
    r01 = 2 * (x * y - z * w)
    r02 = 2 * (x * z + y * w)
    r10 = 2 * (x * y + z * w)
    r11 = 1 - 2 * (x * x + z * z)
    r12 = 2 * (y * z - x * w)
    r20 = 2 * (x * z - y * w)
    r21 = 2 * (y * z + x * w)
    r22 = 1 - 2 * (x * x + y * y)
    return (r00 * sx, r10 * sx, r20 * sx, 0.0,
            r01 * sy, r11 * sy, r21 * sy, 0.0,
            r02 * sz, r12 * sz, r22 * sz, 0.0,
            translation[0], translation[1], translation[2], 1.0)
