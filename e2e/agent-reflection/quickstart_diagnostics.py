"""Failure text and published evidence for the agent-reflection harness, with every secret removed.

The harness handles generated quickstart passwords, minted API keys and the test IdP password. A failure
must say what broke (type, message, the harness line that raised it, a failed command's stderr, an admin
error body) so one CI run is enough to diagnose it, but none of those values may reach the workflow log
or an uploaded artifact. Two rules hold throughout:
- Redact complete text first and bound it afterwards, so a cut can never leave part of a secret behind.
- Anything the Playwright child prints or reports passes through the same redactor before it is shown
  or published: its stdout and stderr, its JSON report (nested strings and inline attachment bodies)
  and every file under its output directory.
"""
import base64
import json
import os
import traceback
from pathlib import Path

REDACTED = '[redacted]'
# Short values (ports, booleans) would redact ordinary text; every real secret here is far longer.
MIN_SECRET_LENGTH = 8
SECRET_KEY_WORDS = ('PASSWORD', 'SECRET', 'KEY', 'TOKEN')
DETAIL_TAIL_LINES = 20
DETAIL_TAIL_CHARS = 4000
# An admin error body larger than this is omitted whole, never cut: a cut could split a secret.
MAX_ERROR_BODY_BYTES = 64 * 1024


class Redactor:
    """Collects secret values as the harness learns them and removes them from any text."""

    def __init__(self):
        self._values = set()

    def add(self, *values):
        for value in values:
            if isinstance(value, str) and len(value.strip()) >= MIN_SECRET_LENGTH:
                value = value.strip()
                self._values.add(value)
                # The same value as it appears inside a JSON string.
                self._values.add(json.dumps(value)[1:-1])

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

    def contains_secret(self, data):
        return any(value.encode() in data for value in self._values)


class DetailedError(RuntimeError):
    """A fixed-text failure that also carries raw detail (stderr, a response body).

    The detail is never part of str(error); describe_failure redacts it whole and only then bounds it.
    """

    def __init__(self, message, detail=''):
        super().__init__(message)
        if isinstance(detail, bytes):
            detail = detail.decode(errors='replace')
        self.detail = (detail or '').strip()


class CommandFailed(DetailedError):
    """A harness subprocess exited non-zero; carries the command label, exit code and its stderr."""

    def __init__(self, command, returncode, stderr=b''):
        self.command = command
        self.returncode = returncode
        super().__init__(f'{command} failed with exit code {returncode}', stderr)


def read_error_body(response, limit=MAX_ERROR_BODY_BYTES):
    """The whole response body, or a placeholder when it is too large to read whole."""
    data = response.read(limit + 1)
    if len(data) > limit:
        return f'(body omitted: over {limit} bytes)'
    return data.decode(errors='replace')


def bounded_tail(text):
    lines = text.strip().splitlines()[-DETAIL_TAIL_LINES:]
    return '\n'.join(lines)[-DETAIL_TAIL_CHARS:]


def describe_failure(error, redactor, script_name='run-quickstart.py'):
    """One redacted report: '<Type>: <message> [at <script>:<line> in <function>]' and a bounded detail tail."""
    where = ''
    frames = [frame for frame in traceback.extract_tb(error.__traceback__) if frame.filename.endswith(script_name)]
    if frames:
        where = f' [at {script_name}:{frames[-1].lineno} in {frames[-1].name}]'
    message = str(error) if not isinstance(error, KeyError) else f'missing key {error}'
    text = redactor.redact(f'{type(error).__name__}: {message}{where}')
    detail = getattr(error, 'detail', '')
    if detail:
        # Redact the complete detail first; bounding it afterwards cannot expose part of a secret.
        text += '\n' + bounded_tail(redactor.redact(detail))
    return text


def redact_json(value, redactor):
    """Redact every string in a Playwright JSON report, including inline base64 attachment bodies."""
    if isinstance(value, dict):
        result = {redactor.redact(key): redact_json(item, redactor) for key, item in value.items()}
        body = value.get('body')
        if isinstance(body, str) and 'contentType' in value:
            try:
                raw = base64.b64decode(body, validate=True)
            except ValueError:
                raw = None
            if raw is not None and redactor.contains_secret(raw):
                try:
                    result['body'] = base64.b64encode(redactor.redact(raw.decode()).encode()).decode()
                except UnicodeDecodeError:
                    result['body'] = ''
        return result
    if isinstance(value, list):
        return [redact_json(item, redactor) for item in value]
    if isinstance(value, str):
        return redactor.redact(value)
    return value


def publish_report(raw_path, destination, redactor):
    """Write a redacted copy of a private Playwright JSON report to the uploaded path; remove the raw one."""
    raw_path, destination = Path(raw_path), Path(destination)
    if not raw_path.exists():
        return False
    try:
        try:
            report = redact_json(json.loads(raw_path.read_text(errors='replace')), redactor)
            text = json.dumps(report)
        except ValueError:
            # A truncated report (killed child) is still published as text, redacted whole.
            text = redactor.redact(raw_path.read_text(errors='replace'))
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(text)
    finally:
        raw_path.unlink(missing_ok=True)
    return True


def scrub_tree(directory, redactor):
    """Redact every text file under an uploaded directory that holds a secret; delete such binary files."""
    scrubbed = []
    for root, _, files in os.walk(directory):
        for name in files:
            path = Path(root, name)
            try:
                data = path.read_bytes()
            except OSError:
                continue
            if not redactor.contains_secret(data):
                continue
            try:
                path.write_text(redactor.redact(data.decode()))
            except UnicodeDecodeError:
                path.unlink()
            scrubbed.append(str(path))
    return scrubbed


def stream_redacted(stream, out, redactor):
    """Forward a child's merged output line by line, redacted, as it arrives."""
    for line in stream:
        out.write(redactor.redact(line))
        out.flush()
