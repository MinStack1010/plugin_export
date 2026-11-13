"""Tests for the multipart uploader against a local loopback HTTP server."""

import http.server
import os
import tempfile
import threading
import unittest

import support  # noqa: F401

from synode_glb.core.errors import UploadError
from synode_glb.core.uploader import USER_AGENT, build_multipart_body, upload_glb, validate_upload_url


class _Handler(http.server.BaseHTTPRequestHandler):
    status = 200
    received = {}

    def do_POST(self):
        length = int(self.headers["Content-Length"])
        _Handler.received = {
            "path": self.path,
            "content_type": self.headers["Content-Type"],
            "user_agent": self.headers["User-Agent"],
            "body": self.rfile.read(length),
        }
        payload = b"ok" if _Handler.status == 200 else b"server exploded"
        self.send_response(_Handler.status)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *args):
        pass


class UploaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        try:
            cls.server = http.server.HTTPServer(("127.0.0.1", 0), _Handler)
        except OSError as exc:
            raise unittest.SkipTest("cannot bind a loopback port: {}".format(exc))
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = "http://127.0.0.1:{}/upload".format(cls.server.server_address[1])

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()

    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.path = os.path.join(self.folder.name, "model.glb")
        with open(self.path, "wb") as handle:
            handle.write(b"glTF\x02\x00\x00\x00binary")

    def tearDown(self):
        self.folder.cleanup()

    def test_successful_upload_contract(self):
        _Handler.status = 200
        self.assertEqual(upload_glb(self.path, self.url, "API_KEY_DEV", 10), "ok")
        r = _Handler.received
        self.assertEqual(r["path"], "/upload")
        self.assertTrue(r["content_type"].startswith("multipart/form-data; boundary="))
        self.assertEqual(r["user_agent"], USER_AGENT)
        body = r["body"]
        self.assertIn(b'name="file"; filename="model.glb"', body)
        self.assertIn(b"Content-Type: model/gltf-binary", body)
        self.assertIn(b"glTF\x02\x00\x00\x00binary", body)
        self.assertIn(b'name="api_key"', body)
        self.assertIn(b"API_KEY_DEV", body)

    def test_http_error_message(self):
        _Handler.status = 500
        with self.assertRaises(UploadError) as ctx:
            upload_glb(self.path, self.url, "k", 10)
        self.assertEqual(str(ctx.exception), "HTTP 500\n\nserver exploded")

    def test_missing_file(self):
        with self.assertRaises(UploadError):
            upload_glb(os.path.join(self.folder.name, "nope.glb"), self.url, "k")

    def test_invalid_url(self):
        for url in ("", "ftp://host/x", "not a url"):
            with self.assertRaises(UploadError):
                validate_upload_url(url)

    def test_api_key_omitted_when_empty(self):
        body = build_multipart_body("a.glb", b"x", "", "B")
        self.assertNotIn(b"api_key", body)
        self.assertTrue(body.endswith(b"--B--\r\n"))


if __name__ == "__main__":
    unittest.main()
