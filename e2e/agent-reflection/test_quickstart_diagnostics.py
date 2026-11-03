"""The harness reports what failed and never a credential; the IdP key copy works for a uid-1001 runner.

Values below are synthetic test data, never real credentials.
"""
import base64
import http.server
import importlib.util
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from unittest import mock

from quickstart_diagnostics import (DETAIL_TAIL_CHARS, MAX_ERROR_BODY_BYTES, REDACTED, CommandFailed, DetailedError,
                                    Redactor, describe_failure, publish_report, read_error_body, redact_json, scrub_tree)

HERE = Path(__file__).resolve().parent
SCRIPT = HERE / 'run-quickstart.py'
PLAYWRIGHT_DIR = HERE.parent / 'playwright'
ENV = {'HONUA_CONSOLE_E2E_SERVER_IMAGE': 'server', 'HONUA_CONSOLE_E2E_CONSOLE_IMAGE': 'console',
       'HONUA_REFLECTION_EXECUTOR_IMAGE': 'executor'}
SAMPLE = 'Zq7' + 'x' * 30 + 'END'
OTHER_SAMPLE = 'Kv' + 'y' * 38


def load_harness():
    spec = importlib.util.spec_from_file_location('run_quickstart', SCRIPT)
    module = importlib.util.module_from_spec(spec)
    with mock.patch.dict(os.environ, ENV):
        spec.loader.exec_module(module)
    return module


def redactor_with(*values):
    redactor = Redactor()
    redactor.add(*values)
    return redactor


def fragments(value, size=6):
    return [value[i:i + size] for i in range(0, len(value) - size + 1)]


class RedactorTests(unittest.TestCase):
    def test_removes_every_learned_value_longest_first(self):
        redactor = redactor_with(SAMPLE, SAMPLE[:20], 'short', None)
        self.assertEqual(redactor.redact(f'a {SAMPLE} b {SAMPLE[:20]} c short'), f'a {REDACTED} b {REDACTED} c short')

    def test_learns_an_install_from_secret_files_and_secret_named_env_lines(self):
        with tempfile.TemporaryDirectory() as temp:
            install = Path(temp)
            (install / 'secrets').mkdir()
            (install / 'secrets' / 'honua_admin_password').write_text(SAMPLE + '\n')
            (install / '.env').write_text(f'HONUA_HTTP_PORT=18090\nHONUA_COMPOSE_PROJECT_DIRECTORY={temp}\n'
                                          f'POSTGRES_PASSWORD="{OTHER_SAMPLE}"\n')
            redactor = Redactor()
            redactor.add_install_secrets(install)
            self.assertEqual(redactor.redact(f'{SAMPLE} {OTHER_SAMPLE} {temp} 18090'),
                             f'{REDACTED} {REDACTED} {temp} 18090')

    def test_a_missing_install_is_not_an_error(self):
        redactor = Redactor()
        redactor.add_install_secrets('/nonexistent/agent-reflection-install')
        self.assertEqual(redactor.redact('unchanged'), 'unchanged')


class FailureTextTests(unittest.TestCase):
    def test_command_failure_names_the_command_code_and_stderr_tail(self):
        stderr = ''.join(f'line {n}\n' for n in range(100)).encode() + b'container honua is unhealthy\n'
        error = CommandFailed('docker compose up', 1, stderr)
        self.assertEqual(str(error), 'docker compose up failed with exit code 1')
        text = describe_failure(error, Redactor())
        self.assertIn('docker compose up failed with exit code 1\n', text)
        self.assertIn('container honua is unhealthy', text)
        self.assertNotIn('line 0\n', text)

    def test_a_secret_across_the_character_bound_is_redacted_before_the_cut(self):
        for offset in range(1, len(SAMPLE)):
            # The bound falls inside the sample: `offset` of its characters lie past the cut.
            stderr = 'p' * (DETAIL_TAIL_CHARS * 2) + SAMPLE[:-offset] + SAMPLE[-offset:] + 'q' * (DETAIL_TAIL_CHARS - offset)
            text = describe_failure(CommandFailed('probe', 1, stderr.encode()), redactor_with(SAMPLE))
            for piece in fragments(SAMPLE):
                self.assertNotIn(piece, text, offset)

    def test_a_secret_on_the_first_kept_line_is_redacted_whole(self):
        stderr = '\n'.join(['early'] * 30 + [f'token {SAMPLE}'] + ['late'] * 19)
        text = describe_failure(CommandFailed('probe', 2, stderr), redactor_with(SAMPLE))
        self.assertIn(f'token {REDACTED}', text)
        self.assertNotIn(SAMPLE[-6:], text)

    def test_an_admin_error_body_is_read_whole_or_omitted_never_cut(self):
        whole = read_error_body(io.BytesIO(b'{"detail":"' + SAMPLE.encode() + b'"}'))
        text = describe_failure(DetailedError('Admin setup /api returned HTTP 403', whole), redactor_with(SAMPLE))
        self.assertIn('HTTP 403\n{"detail":"[redacted]"}', text)
        large = b'x' * (MAX_ERROR_BODY_BYTES - 10) + SAMPLE.encode()
        self.assertEqual(read_error_body(io.BytesIO(large)), f'(body omitted: over {MAX_ERROR_BODY_BYTES} bytes)')

    def test_a_failure_before_the_harness_reads_secrets_is_still_redacted(self):
        with tempfile.TemporaryDirectory() as temp:
            (Path(temp) / 'secrets').mkdir()
            (Path(temp) / 'secrets' / 'honua_master_key').write_text(SAMPLE)
            error = CommandFailed('quickstart.py', 1, f'initializer echoed {SAMPLE}'.encode())
            redactor = Redactor()
            redactor.add_install_secrets(temp)
            self.assertNotIn(SAMPLE, describe_failure(error, redactor))

    def test_describe_names_type_message_and_harness_line_and_redacts(self):
        try:
            exec(compile(f'raise PermissionError(13, "Permission denied {SAMPLE}")', str(SCRIPT), 'exec'))
        except PermissionError as error:
            text = describe_failure(error, redactor_with(SAMPLE))
        self.assertTrue(text.startswith('PermissionError: [Errno 13] Permission denied '), text)
        self.assertIn('[at run-quickstart.py:1 in <module>]', text)
        self.assertNotIn(SAMPLE, text)

    def test_key_errors_say_which_key(self):
        self.assertEqual(describe_failure(KeyError('data'), Redactor()), "KeyError: missing key 'data'")


class EvidenceTests(unittest.TestCase):
    def test_json_report_strings_and_attachment_bodies_are_redacted(self):
        text_body = base64.b64encode(f'progress {SAMPLE}'.encode()).decode()
        binary_body = base64.b64encode(b'\x89PNG\xff\xfe' + SAMPLE.encode()).decode()
        clean_body = base64.b64encode(b'{"qualificationComplete":true}').decode()
        report = {'suites': [{'specs': [{'tests': [{'results': [{
            'errors': [{'message': f'fill("{SAMPLE}")'}], 'stdout': [{'text': f'key {SAMPLE}'}],
            'attachments': [{'name': 'a', 'contentType': 'application/json', 'body': text_body},
                            {'name': 'b', 'contentType': 'image/png', 'body': binary_body},
                            {'name': 'c', 'contentType': 'application/json', 'body': clean_body}]}]}]}]}]}
        redacted = redact_json(report, redactor_with(SAMPLE))
        result = redacted['suites'][0]['specs'][0]['tests'][0]['results'][0]
        self.assertNotIn(SAMPLE, json.dumps(redacted))
        self.assertEqual(result['errors'][0]['message'], f'fill("{REDACTED}")')
        self.assertEqual(base64.b64decode(result['attachments'][0]['body']).decode(), f'progress {REDACTED}')
        self.assertEqual(result['attachments'][1]['body'], '')
        self.assertEqual(result['attachments'][2]['body'], clean_body)

    def test_a_value_with_json_escapes_is_redacted_in_the_report(self):
        value = 'quote"and\\slash-' + 'z' * 10
        with tempfile.TemporaryDirectory() as temp:
            raw, published = Path(temp, 'raw.json'), Path(temp, 'out/report.json')
            raw.write_text(json.dumps({'message': value}) + '\n{truncated')
            self.assertTrue(publish_report(raw, published, redactor_with(value)))
            self.assertFalse(raw.exists())
            self.assertNotIn(json.dumps(value)[1:-1], published.read_text())

    def test_output_directory_text_is_redacted_and_binary_with_a_secret_removed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'case').mkdir()
            (root / 'case' / 'error-context.md').write_text(f'- textbox: {SAMPLE}')
            (root / 'case' / 'trace.bin').write_bytes(b'\xff\xfe' + SAMPLE.encode())
            (root / 'case' / 'shot.png').write_bytes(b'\x89PNG clean')
            scrub_tree(root, redactor_with(SAMPLE))
            self.assertEqual((root / 'case' / 'error-context.md').read_text(), f'- textbox: {REDACTED}')
            self.assertFalse((root / 'case' / 'trace.bin').exists())
            self.assertEqual((root / 'case' / 'shot.png').read_bytes(), b'\x89PNG clean')


FAKE_PLAYWRIGHT = '''#!{python}
import json, os, pathlib, sys
value = os.environ['HONUA_REFLECTION_CUSTOMER_PASSWORD']
print('  - fill("' + value + '")', flush=True)
print('stderr ' + value, file=sys.stderr, flush=True)
pathlib.Path(os.environ['PLAYWRIGHT_JSON_OUTPUT_FILE']).write_text(json.dumps({{'errors': [{{'message': value}}]}}))
out = pathlib.Path('test-results/agent-reflection')
out.mkdir(parents=True)
(out / 'error-context.md').write_text('password ' + value)
sys.exit(1)
'''


class HarnessTests(unittest.TestCase):
    def test_idp_key_is_copied_by_root_to_keycloak_uid_without_the_runner_reading_it(self):
        harness = load_harness()
        with tempfile.TemporaryDirectory() as temp:
            source, target = Path(temp, 'certs'), Path(temp, 'install')
            source.mkdir(), target.mkdir()
            (source / 'kc.crt').write_text('certificate')
            (source / 'kc.key').write_text('private key')
            os.chmod(source / 'kc.key', 0)  # The testbed step leaves it unreadable to the CI runner.
            with mock.patch.object(harness, 'run') as run:
                harness.install_idp_certs(source, target)
            self.assertEqual((target / 'kc.crt').read_text(), 'certificate')
            self.assertFalse((target / 'kc.key').exists())
            run.assert_called_once_with(['sudo', 'install', '-o', '1000', '-g', '0', '-m', '0640',
                                         str(source / 'kc.key'), str(target / 'kc.key')], label='sudo install kc.key')

    def test_run_raises_command_failed_with_stderr(self):
        harness = load_harness()
        with self.assertRaises(CommandFailed) as caught:
            harness.run([sys.executable, '-c', 'import sys; sys.stderr.write("boom detail"); sys.exit(3)'], label='probe')
        self.assertEqual(caught.exception.returncode, 3)
        self.assertEqual(caught.exception.detail, 'boom detail')

    def test_playwright_output_report_and_results_are_redacted(self):
        harness = load_harness()
        harness.REDACTOR.add(SAMPLE)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'node_modules/.bin').mkdir(parents=True)
            fake = root / 'node_modules/.bin/playwright'
            fake.write_text(FAKE_PLAYWRIGHT.format(python=sys.executable))
            fake.chmod(0o755)
            out = io.StringIO()
            code = harness.run_playwright(dict(os.environ, HONUA_REFLECTION_CUSTOMER_PASSWORD=SAMPLE), playwright_dir=root,
                                          raw_report=root / 'private.json', report=root / 'artifacts/results.json', out=out)
            self.assertEqual(code, 1)
            self.assertIn(f'fill("{REDACTED}")', out.getvalue())
            self.assertIn(f'stderr {REDACTED}', out.getvalue())
            self.assertFalse((root / 'private.json').exists())
            for path in (root / 'artifacts/results.json', root / 'test-results/agent-reflection/error-context.md'):
                self.assertNotIn(SAMPLE, path.read_text())
            self.assertNotIn(SAMPLE, out.getvalue())

    @unittest.skipUnless((PLAYWRIGHT_DIR / 'node_modules/.bin/playwright').exists() and shutil.which('node'),
                         'needs e2e/playwright npm ci and Chromium')
    def test_a_real_failed_password_fill_leaks_nothing(self):
        # A real Playwright failure on the customer login: the password field cannot be filled (a checkbox
        # fails at once), so the call log names the fill and its value. Nothing shown or published may contain it.
        page = (b'<!doctype html><title>login</title><input id="username">'
                b'<input id="password" type="checkbox"><button id="kc-login">go</button>')

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200)
                self.send_header('Content-Type', 'text/html')
                self.end_headers()
                self.wfile.write(page)

            def log_message(self, *args):
                pass

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        url = f'http://127.0.0.1:{server.server_address[1]}'
        harness = load_harness()
        harness.REDACTOR.add(SAMPLE, OTHER_SAMPLE)
        env = dict(os.environ, HONUA_REFLECTION_CONSOLE_URL=url, HONUA_REFLECTION_MANAGER_URL=url,
                   HONUA_REFLECTION_SERVER_URL=url, HONUA_REFLECTION_MANAGER_PROPOSER=OTHER_SAMPLE,
                   HONUA_REFLECTION_MANAGER_APPROVER=OTHER_SAMPLE, HONUA_REFLECTION_SERVER_PROPOSER=OTHER_SAMPLE,
                   HONUA_REFLECTION_SERVER_APPROVER=OTHER_SAMPLE, HONUA_REFLECTION_MANAGER_ACTOR='apikey:api-key:a',
                   HONUA_REFLECTION_SERVER_ACTOR='apikey:api-key:b', HONUA_REFLECTION_COMPOSE_DIR='/nonexistent',
                   HONUA_REFLECTION_CONNECTION_NAME='agent-reflection-db', HONUA_REFLECTION_CUSTOMER_USER='customer',
                   HONUA_REFLECTION_CUSTOMER_PASSWORD=SAMPLE, HONUA_CONSOLE_E2E_CONSOLE_IMAGE='console')
        try:
            with tempfile.TemporaryDirectory() as temp:
                out = io.StringIO()
                report = Path(temp, 'results.json')
                code = harness.run_playwright(env, raw_report=Path(temp, 'raw.json'), report=report, out=out)
                self.assertNotEqual(code, 0)
                self.assertIn('fill(', out.getvalue())
                self.assertIn(REDACTED, out.getvalue())
                self.assertNotIn(SAMPLE, out.getvalue())
                self.assertNotIn(SAMPLE, report.read_text())
                for path in (PLAYWRIGHT_DIR / 'test-results').rglob('*'):
                    if path.is_file():
                        self.assertNotIn(SAMPLE.encode(), path.read_bytes(), path)
        finally:
            server.shutdown()
            server.server_close()

    def test_script_failure_prints_type_message_and_line(self):
        result = subprocess.run([sys.executable, str(SCRIPT), 'neither'], env=dict(os.environ, **ENV),
                                capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 1)
        self.assertRegex(result.stderr.strip(),
                         r'^Agent quickstart journey failed: RuntimeError: Specify run or clean '
                         r'\[at run-quickstart\.py:\d+ in main\]$')


if __name__ == '__main__':
    unittest.main()
