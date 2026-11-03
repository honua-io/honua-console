"""Read the secrets a customer Compose quickstart install generated, for the agent-reflection harness.

honua-server #6888 (rc.2 on) keeps each generated secret in its own file, secrets/<lower-case key>
(the bare value; the server's own reader strips surrounding whitespace), and .env holds none of them.
An rc.1-layout install keeps them as KEY=value lines in .env. A secrets/ file wins; .env is the fallback.
Values are returned only to the caller; nothing here prints or logs them.
"""
from pathlib import Path

# The secrets run-quickstart.py uses: the server and manager admin passwords (to mint test API keys)
# and the Postgres password (to register the database connection).
REQUIRED_SECRETS = ('HONUA_ADMIN_PASSWORD', 'HONUA_MANAGER_ADMIN_PASSWORD', 'POSTGRES_PASSWORD')


def _env_values(install):
    env_file = Path(install) / '.env'
    if not env_file.is_file():
        return {}
    values = {}
    for line in env_file.read_text().splitlines():
        if line and not line.startswith('#') and '=' in line:
            key, value = line.split('=', 1)
            values[key] = value
    return values


def read_install_secrets(install, keys=REQUIRED_SECRETS):
    """Return {key: value} for every key; RuntimeError (naming only the key) when the install has none."""
    install = Path(install)
    legacy = _env_values(install)
    found = {}
    for key in keys:
        path = install / 'secrets' / key.lower()
        value = path.read_text().strip() if path.is_file() else legacy.get(key, '')
        if not value:
            raise RuntimeError(f'The quickstart did not generate {key}')
        found[key] = value
    return found
