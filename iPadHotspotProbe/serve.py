#!/usr/bin/env python3
"""Bounded HTTP listener for the temporary iPad hotspot probe."""
import argparse
import socketserver
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

class LocalHTTPServer(ThreadingHTTPServer):
    # Avoid reverse-DNS lookup of the private hotspot address.
    def server_bind(self):
        socketserver.TCPServer.server_bind(self)
        self.server_name = self.server_address[0]
        self.server_port = self.server_address[1]

class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print("HTTP " + (fmt % args), flush=True)

    def do_GET(self):
        print(f"GET peer={self.client_address[0]}:{self.client_address[1]} path={self.path}", flush=True)
        if self.path != "/health":
            self.send_error(404)
            return
        body = b"iPadHotspotProbe mac-ok\n"
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        length = min(int(self.headers.get("Content-Length", "0")), 8192)
        body = self.rfile.read(length)
        print(f"POST peer={self.client_address[0]}:{self.client_address[1]} path={self.path} body={body.decode('utf-8', 'replace')}", flush=True)
        response = b"report-ok\n"
        self.send_response(200)
        self.send_header("Content-Length", str(len(response)))
        self.end_headers()
        self.wfile.write(response)

    def do_PUT(self):
        self.send_error(405)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="172.20.10.4")
    parser.add_argument("--port", type=int, default=48123)
    args = parser.parse_args()
    server = LocalHTTPServer((args.host, args.port), Handler)
    print(f"LISTEN {args.host}:{args.port}", flush=True)
    try:
        server.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        print("STOPPED", flush=True)
