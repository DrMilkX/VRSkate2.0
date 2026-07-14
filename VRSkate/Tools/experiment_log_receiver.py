"""
Minimal local receiver for ExperimentLogger.cs uploads.

Run this on the Windows machine while it's on the same Wi-Fi network as the
Quest headset. The headset POSTs the CSV log to this server whenever a
session ends (normally or prematurely); this script just writes the bytes
it receives to disk.

No third-party dependencies - only the Python standard library.

Usage:
    python experiment_log_receiver.py [--port 8000] [--out received_logs]

Then in ExperimentLogger's Inspector, set Upload Url to:
    http://<this-machine's-LAN-IP>:8000/upload
(Find the IP with `ipconfig` on Windows - use the Wi-Fi adapter's IPv4 address.)
"""

import argparse
import os
import re
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# Only allow simple filenames (letters, digits, dot, dash, underscore) to avoid
# writing outside the output directory via a crafted X-Filename header.
SAFE_FILENAME_RE = re.compile(r"^[A-Za-z0-9._-]+$")


def make_handler(output_dir: str):
    class UploadHandler(BaseHTTPRequestHandler):
        def do_POST(self):
            if self.path != "/upload":
                self.send_error(404, "Unknown endpoint")
                return

            content_length = int(self.headers.get("Content-Length", 0))
            if content_length <= 0:
                self.send_error(400, "Empty body")
                return

            body = self.rfile.read(content_length)

            filename = self.headers.get("X-Filename", "").strip()
            if not filename or not SAFE_FILENAME_RE.match(filename):
                filename = f"upload_{datetime.now():%Y%m%d_%H%M%S}.csv"

            path = os.path.join(output_dir, filename)
            with open(path, "wb") as f:
                f.write(body)

            print(f"[{datetime.now():%Y-%m-%d %H:%M:%S}] Received {len(body)} bytes -> {path}")

            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, format, *args):
            pass  # suppress default request logging; we print our own line above

    return UploadHandler


def main():
    parser = argparse.ArgumentParser(description="Receive ExperimentLogger CSV uploads from the Quest headset.")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--out", default="received_logs")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)

    server = ThreadingHTTPServer(("0.0.0.0", args.port), make_handler(os.path.abspath(args.out)))
    print(f"Listening on port {args.port}, saving files to {os.path.abspath(args.out)}")
    print("Set ExperimentLogger's Upload Url to http://<this-machine-IP>:%d/upload" % args.port)
    print("Press Ctrl+C to stop.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
