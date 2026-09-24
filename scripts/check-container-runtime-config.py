#!/usr/bin/env python3
"""Verify runtime mode and authentication on the same packaged Console image."""

import json
import os
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file, code, message, headers, target):
        return None


def request(url, headers=None):
    opener = urllib.request.build_opener(NoRedirect)
    try:
        with opener.open(urllib.request.Request(url, headers=headers or {}), timeout=10) as response:
            return response.status, response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode("utf-8")


def verify(image):
    image_id = subprocess.check_output(["docker", "image", "inspect", image, "--format", "{{.Id}}"], text=True).strip()
    receipt = {"imageId": image_id, "modes": []}
    for mode in ("full", "witness"):
        edge_secret = secrets.token_hex(24)
        admin_key = secrets.token_hex(24)
        configuration = {
            "ASPNETCORE_ENVIRONMENT": "Production",
            "HONUA_CONSOLE_MODE": mode,
            "Honua__Console__Auth__Mode": "EdgeForwarded",
            "Honua__Console__Auth__EdgeForwarded__SharedSecret": edge_secret,
            "HONUA_SERVER_BASE_URL": "http://127.0.0.1:9",
            "HONUA_ADMIN_API_KEY": admin_key,
        }
        command = ["docker", "run", "--detach", "--publish", "127.0.0.1::8080"]
        for key in configuration:
            command += ["--env", key]
        command += [image]
        container = subprocess.check_output(command, env={**os.environ, **configuration}, text=True).strip()
        try:
            port = subprocess.check_output(["docker", "port", container, "8080/tcp"], text=True).strip().rsplit(":", 1)[1]
            base_url = f"http://127.0.0.1:{port}"
            for attempt in range(60):
                try:
                    status, payload = request(base_url + "/version.json")
                    if status == 200:
                        break
                except (OSError, urllib.error.URLError):
                    pass
                time.sleep(1)
            else:
                raise AssertionError(f"{mode} container did not become ready")

            version = json.loads(payload)
            assert version["commit"] == os.environ["GITHUB_SHA"], version
            status, _ = request(base_url + "/operate")
            assert status in (302, 401, 403), f"Anonymous {mode} route returned {status}"
            status, _ = request(base_url + "/operate", {
                "X-Honua-Edge-Auth": "wrong-secret", "X-Forwarded-User": "runtime-config-check"
            })
            assert status in (302, 401, 403), f"Untrusted edge {mode} route returned {status}"
            status, markup = request(base_url + "/operate", {
                "X-Honua-Edge-Auth": edge_secret, "X-Forwarded-User": "runtime-config-check"
            })
            assert status == 200, f"Authenticated {mode} route returned {status}"
            assert f'data-console-mode="{mode}"' in markup, f"Runtime mode {mode} not rendered"
            assert admin_key not in markup and edge_secret not in markup, "Runtime credential leaked into HTML"
            receipt["modes"].append({"mode": mode, "result": "pass", "commit": version["commit"]})
        except Exception:
            subprocess.run(["docker", "logs", container], check=False)
            raise
        finally:
            subprocess.run(["docker", "rm", "--force", container], check=False, stdout=subprocess.DEVNULL)
    print(json.dumps(receipt, indent=2))


if __name__ == "__main__":
    verify(sys.argv[1])
