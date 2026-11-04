"""The harness reads quickstart secrets from both install layouts and never guesses one."""
import os
import tempfile
import unittest
from pathlib import Path

from quickstart_secrets import REQUIRED_SECRETS, read_install_secrets

VALUES = {'HONUA_ADMIN_PASSWORD': 'Aa1!' + 'a' * 64, 'HONUA_MANAGER_ADMIN_PASSWORD': 'Aa1!' + 'b' * 64,
          'POSTGRES_PASSWORD': 'c' * 64}
PORTS = 'HONUA_HTTP_PORT=18090\nHONUA_GRPC_PORT=18091\nHONUA_MANAGER_PORT=18092\n'


class QuickstartSecretsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.install = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write_files(self, values, newline=True):
        (self.install / 'secrets').mkdir(exist_ok=True)
        for key, value in values.items():
            path = self.install / 'secrets' / key.lower()
            path.write_text(value + ('\n' if newline else ''))
            os.chmod(path, 0o640)

    def test_files_layout_from_honua_server_6888(self):
        (self.install / '.env').write_text(PORTS)
        self.write_files(VALUES)
        self.assertEqual(read_install_secrets(self.install), VALUES)
        self.write_files(VALUES, newline=False)
        self.assertEqual(read_install_secrets(self.install), VALUES)

    def test_rc1_env_layout(self):
        (self.install / '.env').write_text(PORTS + ''.join(f'{k}={v}\n' for k, v in VALUES.items()) + '# comment\n')
        self.assertEqual(read_install_secrets(self.install), VALUES)

    def test_a_secrets_file_wins_over_a_stale_env_line(self):
        (self.install / '.env').write_text(PORTS + 'HONUA_ADMIN_PASSWORD=stale-value\n')
        self.write_files(VALUES)
        self.assertEqual(read_install_secrets(self.install)['HONUA_ADMIN_PASSWORD'], VALUES['HONUA_ADMIN_PASSWORD'])

    def test_a_missing_or_empty_secret_is_refused_naming_only_the_key(self):
        (self.install / '.env').write_text(PORTS)
        partial = dict(VALUES)
        partial.pop('POSTGRES_PASSWORD')
        self.write_files(partial)
        with self.assertRaises(RuntimeError) as raised:
            read_install_secrets(self.install)
        self.assertEqual(str(raised.exception), 'The quickstart did not generate POSTGRES_PASSWORD')
        self.write_files({'POSTGRES_PASSWORD': '  '})
        with self.assertRaisesRegex(RuntimeError, 'did not generate POSTGRES_PASSWORD'):
            read_install_secrets(self.install)
        for value in VALUES.values():
            self.assertNotIn(value, str(raised.exception))

    def test_an_install_with_neither_layout_is_refused(self):
        with self.assertRaisesRegex(RuntimeError, 'did not generate ' + REQUIRED_SECRETS[0]):
            read_install_secrets(self.install)


if __name__ == '__main__':
    unittest.main()
