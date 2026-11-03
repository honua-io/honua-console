"""Failure text for the agent-reflection harness: the exception type and message, with every secret removed.

The harness handles generated quickstart passwords, minted API keys and the test IdP password. A failure
must say what broke (type, message, the harness line that raised it, the tail of a failed command's stderr)
so one CI run is enough to diagnose it, but none of those values may reach the workflow log.
"""
import traceback
from pathlib import Path

REDACTED = '[redacted]'
# Short values (ports, booleans) would redact ordinary text; every real secret here is far longer.
MIN_SECRET_LENGTH = 8
STDERR_TAIL_LINES = 20
SECRET_KEY_WORDS = ('PASSWORD', 'SECRET', 'KEY', 'TOKEN')
STDERR_TAIL_CHARS = 4000


class Redactor:
    """Collects secret values as the harness learns them and removes them from any text."""

    def __init__(self):
        self._values = set()

    def add(self, *values):
        for value in values:
            if isinstance(value, str) and len(value.strip()) >= MIN_SECRET_LENGTH:
                self._values.add(value.strip())

    def add_install_secrets(self, install):
        """Best effort: learn every generated secret file and secret-named .env value of an install.

        A failure can happen before the harness reads the secrets it needs (in the product initializer,
        say), so the failure path calls this too. Unreadable or missing files are skipped.
        """
        install = Path(install)
        try:
            for path in (install / 'secrets').iterdir():
                try:
                    if path.is_file():
                        self.add(path.read_text(errors='ignore'))
                except OSError:
                    continue
        except OSError:
            pass
        try:
            for line in (install / '.env').read_text(errors='ignore').splitlines():
                key, _, value = line.partition('=')
                if any(word in key.upper() for word in SECRET_KEY_WORDS):
                    self.add(value.strip().strip('"\''))
        except OSError:
            pass

    def redact(self, text):
        text = str(text)
        # Longest first, so a secret that contains another is removed whole.
        for value in sorted(self._values, key=len, reverse=True):
            text = text.replace(value, REDACTED)
        return text


class CommandFailed(RuntimeError):
    """A harness subprocess exited non-zero; carries the command name, exit code and its stderr tail."""

    def __init__(self, command, returncode, stderr=b''):
        self.command = command
        self.returncode = returncode
        self.stderr_tail = stderr_tail(stderr)
        detail = f'\n{self.stderr_tail}' if self.stderr_tail else ''
        super().__init__(f'{command} failed with exit code {returncode}{detail}')


def stderr_tail(stderr):
    if isinstance(stderr, bytes):
        stderr = stderr.decode(errors='replace')
    lines = (stderr or '').strip().splitlines()[-STDERR_TAIL_LINES:]
    return '\n'.join(lines)[-STDERR_TAIL_CHARS:]


def describe_failure(error, redactor, script_name='run-quickstart.py'):
    """One redacted report: '<Type>: <message> [at <script>:<line> in <function>]'."""
    where = ''
    frames = [frame for frame in traceback.extract_tb(error.__traceback__) if frame.filename.endswith(script_name)]
    if frames:
        where = f' [at {script_name}:{frames[-1].lineno} in {frames[-1].name}]'
    message = str(error) if not isinstance(error, KeyError) else f'missing key {error}'
    return redactor.redact(f'{type(error).__name__}: {message}{where}')
