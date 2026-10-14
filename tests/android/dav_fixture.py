"""Loopback-only DAV fixture for the production Android APK. Never used by the product."""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--port', type=int, required=True)
p.add_argument('--root', required=True)
p.add_argument('--etag-mode', choices=['strong', 'none', 'reject-wildcard'], default='strong')
args = p.parse_args()
root = Path(args.root)
root.mkdir(parents=True, exist_ok=True)
file = root / 'resume-studio.sync.json'

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if not file.exists():
            self.send_error(404)
            return
        data = file.read_bytes()
        self.send_response(200)
        if args.etag_mode == 'strong':
            self.send_header('ETag', '"' + hashlib.sha256(data).hexdigest() + '"')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_PUT(self):
        if file.exists():
            tag = '"' + hashlib.sha256(file.read_bytes()).hexdigest() + '"'
            match = self.headers.get('If-Match')
            if (self.headers.get('If-None-Match') == '*' or
                (args.etag_mode == 'strong' and match != tag) or
                (args.etag_mode == 'reject-wildcard' and match == '*')):
                # Some Android HttpURLConnection builds report a truncated error body
                # when BaseHTTPRequestHandler closes its generated 412 response.
                self.send_response(412)
                self.send_header('Content-Length', '0')
                self.end_headers()
                return
        elif self.headers.get('If-None-Match') != '*':
            self.send_error(412)
            return
        size = int(self.headers.get('Content-Length', '0'))
        if size > 64 * 1024 * 1024:
            self.send_error(413)
            return
        file.write_bytes(self.rfile.read(size))
        self.send_response(201)
        self.send_header('Content-Length', '0')
        self.end_headers()

HTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
