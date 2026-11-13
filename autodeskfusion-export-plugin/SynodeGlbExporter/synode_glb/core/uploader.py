"""Multipart upload of a GLB file (port of GlbUploader.cs, stdlib only).

Request contract kept from the SolidWorks plugin:
* HTTP POST multipart/form-data to the configured URL,
* part "file": the GLB, content type model/gltf-binary, original file name,
* part "api_key": the configured key (only when not empty),
* any non-2xx response is reported as "HTTP <code>\\n\\n<body>".
"""

import os
import urllib.error
import urllib.parse
import urllib.request
import uuid

from .errors import UploadError

USER_AGENT = "Mozilla/5.0 FusionGLBExporter/1.0"


def validate_upload_url(upload_url: str) -> str:
    url = (upload_url or "").strip()
    if not url:
        raise UploadError("No upload URL is configured. Set 'upload_url' in config.json.")
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        raise UploadError("Invalid upload URL '{}'. Only http:// and https:// URLs are supported.".format(url))
    return url


def _quote(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"').replace("\r", " ").replace("\n", " ")


def build_multipart_body(file_name: str, file_bytes: bytes, api_key: str, boundary: str) -> bytes:
    crlf = b"\r\n"
    parts = [
        b"--" + boundary.encode("ascii"),
        'Content-Disposition: form-data; name="file"; filename="{}"'.format(_quote(file_name)).encode("utf-8"),
        b"Content-Type: model/gltf-binary",
        b"",
        file_bytes,
    ]
    if api_key and api_key.strip():
        parts += [
            b"--" + boundary.encode("ascii"),
            b'Content-Disposition: form-data; name="api_key"',
            b"Content-Type: text/plain; charset=utf-8",
            b"",
            api_key.encode("utf-8"),
        ]
    parts += [b"--" + boundary.encode("ascii") + b"--", b""]
    return crlf.join(parts)


def upload_glb(file_path: str, upload_url: str, api_key: str, timeout: float = 120.0) -> str:
    """Uploads the file and returns the response body (decoded text)."""
    if not os.path.isfile(file_path):
        raise UploadError("GLB file not found: {}".format(file_path))
    url = validate_upload_url(upload_url)

    with open(file_path, "rb") as handle:
        file_bytes = handle.read()

    boundary = "----SynodeFusion" + uuid.uuid4().hex
    body = build_multipart_body(os.path.basename(file_path), file_bytes, api_key, boundary)
    request = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={
            "Content-Type": "multipart/form-data; boundary=" + boundary,
            "Content-Length": str(len(body)),
            "User-Agent": USER_AGENT,
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        try:
            detail = exc.read().decode("utf-8", errors="replace")
        except Exception:
            detail = ""
        raise UploadError("HTTP {}\n\n{}".format(exc.code, detail))
    except urllib.error.URLError as exc:
        raise UploadError("Upload to '{}' failed: {}".format(url, exc.reason))
    except OSError as exc:
        raise UploadError("Upload to '{}' failed: {}".format(url, exc))
