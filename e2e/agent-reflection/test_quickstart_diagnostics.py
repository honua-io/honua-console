"""The harness reports what failed (type, message, line, stderr tail) and never a secret; the IdP key copy works as uid 1001."""
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from quickstart_diagnostics import REDACTED, CommandFailed, Redactor, describe_failure

HERE = Path(__file__).resolve().parent
SCRIPT = HERE / 'run-quickstart.py'
ENV = {'HONUA_CONSOLE_E2E_SERVER_IMAGE': 'server', 'HONUA_CONSOLE_E2E_CONSOLE_IMAGE': 'console',
       'HONUA_REFLECTION_EXECUTOR_IMAGE': 'executor'}
SECRET = 'Aa1!' + 's' * 60
OTHER = 'k' * 40


def load_harness():
    spec = importlib.util.spec_from_file_location('run_quickstart', SCRIPT)
    module = importlib.util.module_from_spec(spec)
    with mock.patch.dict(os.environ, ENV):
        spec.loader.exec_module(module)
    return module


class RedactorTests(unittest.TestCase):
    def test_removes_every_learned_value_longest_first(self):
        redactor = Redactor()
        redactor.add(SECRET, SECRET[:20], 'short', None)
        self.assertEqual(redactor.redact(f'a {SECRET} b {SECRET[:20]} c short'), f'a {REDACTED} b {REDACTED} c short')

    def test_learns_an_install_from_secret_files_and_secret_named_env_lines(self):
        with tempfile.TemporaryDirectory() as temp:
            install = Path(temp)
            (install / 'secrets').mkdir()
            (install / 'secrets' / 'honua_admin_password').write_text(SECRET + '\n')
            (install / '.env').write_text(f'HONUA_HTTP_PORT=18090\nHONUA_COMPOSE_PROJECT_DIRECTORY={temp}\n'
                                          f'POSTGRES_PASSWORD="{OTHER}"\n')
            redactor = Redactor()
            redactor.add_install_secrets(install)
            text = redactor.redact(f'{SECRET} {OTHER} {temp} 18090')
            self.assertEqual(text, f'{REDACTED} {REDACTED} {temp} 18090')

    def test_a_missing_install_is_not_an_error(self):
        redactor = Redactor()
        redactor.add_install_secrets('/nonexistent/agent-reflection-install')
        self.assertEqual(redactor.redact('unchanged'), 'unchanged')


class FailureTextTests(unittest.TestCase):
    def test_command_failure_names_the_command_code_and_stderr_tail(self):
        stderr = ''.join(f'line {n}\n' for n in range(100)).encode() + b'container honua is unhealthy\n'
        error = CommandFailed('docker compose up', 1, stderr)
        self.assertIn('docker compose up failed with exit code 1', str(error))
        self.assertIn('container honua is unhealthy', str(error))
        self.assertNotIn('line 0\n', str(error))

    def test_describe_names_type_message_and_harness_line_and_redacts(self):
        redactor = Redactor()
        redactor.add(SECRET)
        try:
            exec(compile(f'raise PermissionError(13, "Permission denied {SECRET}")', str(SCRIPT), 'exec'))
        except PermissionError as error:
            text = describe_failure(error, redactor)
        self.assertTrue(text.startswith('PermissionError: [Errno 13] Permission denied '), text)
        self.assertIn('[at run-quickstart.py:1 in <module>]', text)
        self.assertNotIn(SECRET, text)
        self.assertIn(REDACTED, text)

    def test_key_errors_say_which_key(self):
        self.assertEqual(describe_failure(KeyError('data'), Redactor()), "KeyError: missing key 'data'")


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
        self.assertIn('probe failed with exit code 3\nboom detail', str(caught.exception))

    def test_script_failure_prints_type_message_and_line(self):
        result = subprocess.run([sys.executable, str(SCRIPT), 'neither'], env=dict(os.environ, **ENV),
                                capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 1)
        self.assertRegex(result.stderr.strip(),
                         r'^Agent quickstart journey failed: RuntimeError: Specify run or clean '
                         r'\[at run-quickstart\.py:\d+ in main\]$')


if __name__ == '__main__':
    unittest.main()
